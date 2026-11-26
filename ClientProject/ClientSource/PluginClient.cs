using System;
using System.Collections.Generic;
using System.Reflection;
using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Sounds;
using HarmonyLib;
using Microsoft.Xna.Framework;

namespace HalfDuplexRadio;

/// <summary>
/// 客户端：按键路由、语音气泡颜色、提示音。
///
/// 按键路由：按住语音聊天键 / 无线电键时语音走无线电并绕过噪音门限，其余情况走本地。
/// 提示音：按下、占线（服务端下发标记后循环）、开始收听、收听结束。
/// 无线电语音强制 2D，只保留 Near/Far 距离衰减。
/// </summary>
public partial class Plugin
{
    // ===== 发送状态 =====

    /// <summary>上一帧无线电键是否按下，用于识别按下瞬间。</summary>
    private static bool pttWasDown;

    /// <summary>占线循环音效通道。</summary>
    private static SoundChannel? busyChannel;

    /// <summary>最后一次收到 BUSY 标记的时刻，用于判断占线是否已解除。</summary>
    private static float busyLastTime = -999f;

    /// <summary>自己被频道占线挡下的时间窗口，期间头顶气泡为红色。</summary>
    private static float busyBlockedUntil = -999f;

    /// <summary>最后一次按下无线电键的时刻，用于 VoiceChatCutoffPrevention 尾巴。</summary>
    private static float lastRadioKeyTime = -999f;

    /// <summary>CanUseRadio 结果缓存：主线程写，音频线程只读。</summary>
    private static volatile bool canUseRadioCache;

    /// <summary>正在收听的客户端 → 最后一次收到其语音包的时刻。</summary>
    private static readonly Dictionary<Client, float> LastHeard = new();

    private static FieldInfo? captureTimerField;
    private static Type? voipCaptureType;
    private static PropertyInfo? voipCaptureInstanceProperty;
    private static bool voipCaptureResolved;

    public partial void InitProjectSpecific()
    {
        PatchPrefix("Barotrauma.DebugConsole", "NewMessage", nameof(DebugConsoleNewMessagePrefix),
            typeof(string), typeof(Color?), typeof(bool));

        PatchPrefix("Barotrauma.Networking.VoipClient", "SendToServer", nameof(VoipClientSendToServerPrefix));
        PatchPrefix("Barotrauma.Networking.VoipClient", "Read", nameof(VoipClientReadPrefix), typeof(IReadMessage));

        PatchPrefix(typeof(VoipQueue), nameof(VoipQueue.Write), nameof(VoipQueueWritePrefix), typeof(IWriteMessage));
        PatchPrefix(typeof(Character), nameof(Character.ShowTextlessSpeechBubble), nameof(ShowTextlessSpeechBubblePrefix),
            typeof(float), typeof(Color));

        PatchPostfix(typeof(Client), nameof(Client.UpdateVoipSound), nameof(ClientUpdateVoipSoundPostfix));

        Log("[HalfDuplexRadio] 已加载（客户端）：按键路由 + 提示音");
    }

    public partial void ResetProjectSpecific()
    {
        StopBusyLoop();
        LastHeard.Clear();
        pttWasDown = false;
        canUseRadioCache = false;
        busyBlockedUntil = -999f;
        busyLastTime = -999f;
        lastRadioKeyTime = -999f;
    }

    // ============================================================
    //  标记消息（服务端 → 客户端）
    // ============================================================

    /// <summary>拦截服务端下发的隐藏标记：播占线音效并吞掉，不写调试控制台。</summary>
    private static bool DebugConsoleNewMessagePrefix(string? msg)
    {
        try
        {
            if (string.IsNullOrEmpty(msg)) return true;
            if (!msg.StartsWith(MarkerPrefix, StringComparison.Ordinal)) return true;

            string kind = msg.Substring(MarkerPrefix.Length);
            if (kind == MarkerBusy)
            {
                busyBlockedUntil = (float)Timing.TotalTime + 5f;
                StartBusyLoop();
            }
            else if (kind == MarkerClear)
            {
                busyBlockedUntil = -999f;
                StopBusyLoop();
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    // ============================================================
    //  音效
    // ============================================================

    private static void PlaySoundSafe(string tag)
    {
        try { SoundPlayer.PlaySound(tag, 1f); }
        catch (Exception e) { Log("[HalfDuplexRadio] 播放音效失败：" + e.Message, Color.Red); }
    }

    private static void StartBusyLoop()
    {
        busyLastTime = (float)Timing.TotalTime;
        if (busyChannel != null && busyChannel.IsPlaying) return;

        try
        {
            busyChannel = SoundPlayer.PlaySound(PttBusyTag, 1f);
            if (busyChannel != null) busyChannel.Looping = true;
        }
        catch { busyChannel = null; }
    }

    private static void StopBusyLoop()
    {
        if (busyChannel == null) return;

        try { busyChannel.Dispose(); } catch { /* 已释放 */ }
        busyChannel = null;
    }

    // ============================================================
    //  每帧驱动（主线程）
    // ============================================================

    /// <summary>
    /// 主线程每帧调用：刷新 CanUseRadio 缓存、识别按键上升沿、松手停占线循环、检查收听超时，
    /// 按住按键期间强制发包。
    /// </summary>
    private static void VoipClientSendToServerPrefix()
    {
        try
        {
            float now = (float)Timing.TotalTime;
            canUseRadioCache = CanUseRadioNow();

            bool down = IsRadioKeyDown() && canUseRadioCache;

            // 按键期间强制发包，绕过噪音门限：没声音也发静音包，
            // 让同频道所有人收到包、画气泡、播监听提示音。
            if (down) ForceCapture();

            if (down && !pttWasDown) PlaySoundSafe(PttBeginTag);

            if (!down)
            {
                StopBusyLoop();
                busyBlockedUntil = -999f;
            }
            else if (busyChannel != null && now - busyLastTime > 3f)
            {
                StopBusyLoop();     // 一直按着但频道已空出来（不再收到 BUSY）
            }
            pttWasDown = down;

            CheckListenTimeouts(now);
        }
        catch { }
    }

    // ============================================================
    //  接收方
    // ============================================================

    /// <summary>收到语音包时刷新"正在收听"状态；只有无线电语音播提示音。</summary>
    private static void VoipClientReadPrefix(IReadMessage msg)
    {
        try
        {
            // 包结构：queueId(byte) + distanceFactor(ranged 8bit) + isRadio(bool) + 音频数据
            int saved = msg.BitPosition;
            byte queueId = msg.ReadByte();
            msg.ReadRangedSingle(0f, 1f, 8);
            bool isRadio = msg.ReadBoolean();
            msg.BitPosition = saved;

            if (!isRadio) return;

            Client? client = FindClientByQueueId(queueId);
            if (client == null) return;

            if (!LastHeard.ContainsKey(client)) PlaySoundSafe(ListenBeginTag);
            LastHeard[client] = (float)Timing.TotalTime;
        }
        catch { }
    }

    private static void CheckListenTimeouts(float now)
    {
        if (LastHeard.Count == 0) return;

        List<Client>? expired = null;
        foreach (var pair in LastHeard)
        {
            if (now - pair.Value > 0.35f) (expired ??= new List<Client>()).Add(pair.Key);
        }
        if (expired == null) return;

        foreach (Client client in expired) LastHeard.Remove(client);
        PlaySoundSafe(ListenOverTag);
    }

    private static Client? FindClientByQueueId(byte queueId)
    {
        var clients = GameMain.Client?.ConnectedClients;
        if (clients == null) return null;

        foreach (Client client in clients)
        {
            if (client?.VoipQueue != null && client.VoipQueue.QueueID == queueId) return client;
        }
        return null;
    }

    // ============================================================
    //  无线电语音强制 2D
    // ============================================================

    /// <summary>去掉无线电语音的左右声道方向感，只保留 Near/Far 距离衰减。</summary>
    private static void ClientUpdateVoipSoundPostfix(Client? __instance)
    {
        try
        {
            if (__instance?.VoipSound == null) return;
            if (!__instance.VoipSound.UsingRadio) return;
            if (!GameSettings.CurrentConfig.Audio.UseDirectionalVoiceChat) return;

            Character? character = __instance.Character;
            if (character == null) return;

            float distance = Vector3.Distance(
                new Vector3(character.WorldPosition.X, character.WorldPosition.Y, 0f),
                GameMain.SoundManager.ListenerPosition);

            float gain = 1f - MathUtils.InverseLerp(__instance.VoipSound.Near, __instance.VoipSound.Far, distance);
            gain = MathHelper.Clamp(gain, 0f, 1f);
            if (__instance.RadioNoise > 0f) gain *= 1f - __instance.RadioNoise;

            __instance.VoipSound.SetPosition(null);
            __instance.VoipSound.Gain = gain;
        }
        catch { }
    }

    // ============================================================
    //  按键路由
    // ============================================================

    /// <summary>发送前决定这一包走本地还是无线电。</summary>
    private static void VoipQueueWritePrefix(VoipQueue? __instance)
    {
        try
        {
            if (__instance == null) return;

            object? capture = GetVoipCaptureInstance();
            if (capture == null || !ReferenceEquals(__instance, capture)) return;

            __instance.ForceLocal = GetVoiceChatMode() == ChatMode.Local;
        }
        catch { }
    }

    /// <summary>
    /// 语音气泡固定用 1.25 秒，其他气泡（文字聊天 1 秒等）不动，只改自己头顶那个。
    /// 被占线挡下时红色；否则按当前语音模式取无线电 / 默认颜色。
    /// </summary>
    private static void ShowTextlessSpeechBubblePrefix(Character? __instance, float duration, ref Color color)
    {
        try
        {
            if (__instance == null || __instance != Character.Controlled) return;
            if (Math.Abs(duration - 1.25f) > 0.02f) return;

            if (busyBlockedUntil > (float)Timing.TotalTime)
            {
                color = Color.Red;
                return;
            }

            color = GetVoiceChatMode() == ChatMode.Radio
                ? ChatMessage.MessageColor[(int)ChatMessageType.Radio]
                : ChatMessage.MessageColor[(int)ChatMessageType.Default];
        }
        catch { }
    }

    private static bool IsRadioKeyDown()
    {
        try
        {
            return PlayerInput.KeyDown(InputType.Voice)
                || PlayerInput.KeyDown(InputType.RadioVoice);
        }
        catch { return false; }
    }

    /// <summary>身上没有可用电台 / 耳机（含被干扰、没电）就退回本地语音。</summary>
    private static bool CanUseRadioNow()
    {
        try
        {
            Character? character = Character.Controlled;
            return character != null && ChatMessage.CanUseRadio(character, out WifiComponent _);
        }
        catch { return false; }
    }

    /// <summary>
    /// 当前应使用的语音模式。
    /// 由音频线程调用，只读缓存，不遍历物品栏。
    /// </summary>
    private static ChatMode GetVoiceChatMode()
    {
        try
        {
            float now = (float)Timing.TotalTime;

            if (IsRadioKeyDown())
            {
                lastRadioKeyTime = now;
                return canUseRadioCache ? ChatMode.Radio : ChatMode.Local;
            }

            // 防切断尾巴：松手后 VoiceChatCutoffPrevention 期间继续算无线电
            float hold = GameSettings.CurrentConfig.Audio.VoiceChatCutoffPrevention / 1000f;
            if (hold > 0f && now - lastRadioKeyTime < hold + 0.15f)
            {
                return canUseRadioCache ? ChatMode.Radio : ChatMode.Local;
            }

            return ChatMode.Local;
        }
        catch { return ChatMode.Local; }
    }

    /// <summary>按键期间强制发包：captureTimer = 20 让 UpdateCapture 下一帧进入发送分支。</summary>
    private static void ForceCapture()
    {
        try
        {
            object? capture = GetVoipCaptureInstance();
            if (capture == null) return;

            captureTimerField ??= AccessTools.Field(voipCaptureType!, "captureTimer");
            captureTimerField?.SetValue(capture, 20);
        }
        catch { }
    }

    /// <summary>取当前的采集队列实例。VoipCapture 是 internal 类型，只能反射访问。</summary>
    private static object? GetVoipCaptureInstance()
    {
        if (!voipCaptureResolved)
        {
            voipCaptureResolved = true;
            voipCaptureType = AccessTools.TypeByName("Barotrauma.Networking.VoipCapture");
            if (voipCaptureType == null)
            {
                Log("[HalfDuplexRadio] 未找到 VoipCapture，按键期间不会强制发包", Color.Yellow);
            }
            else
            {
                voipCaptureInstanceProperty = AccessTools.Property(voipCaptureType, "Instance");
            }
        }

        try { return voipCaptureInstanceProperty?.GetValue(null); }
        catch { return null; }
    }

    // ============================================================
    //  以下为已废弃方案，保留备查
    // ============================================================
    //
    // 1) UpdateCapture 的 Transpiler 改写（语音模式 + 噪音门限）
    //    废弃原因：VoipCapture 用 ThreadStart 委托启动采集线程，补丁无法作用到已绑定的委托，
    //    且注入代码在音频线程执行会与主线程死锁。
    //    替代：主线程通过 VoipClientSendToServerPrefix → ForceCapture 设置 captureTimer，
    //    并在 VoipQueueWritePrefix 里直接改写 ForceLocal。
    //
    // 2) RebuildVoipCapture（重建采集线程以重新绑定补丁）
    //    废弃原因：依赖 1)，1) 不再使用后无需重建。
    //
    // private static IEnumerable<CodeInstruction> UpdateCaptureTranspiler(IEnumerable<CodeInstruction> instructions)
    // {
    //     MethodInfo chatModeReplacement = AccessTools.Method(typeof(Plugin), nameof(GetVoiceChatMode));
    //     MethodInfo thresholdReplacement = AccessTools.Method(typeof(Plugin), nameof(GetNoiseGateThreshold));
    //     if (chatModeReplacement == null || thresholdReplacement == null) return instructions;
    //
    //     var result = new List<CodeInstruction>();
    //     int chatModeHits = 0;
    //     int thresholdHits = 0;
    //
    //     foreach (CodeInstruction inst in instructions)
    //     {
    //         object operand = inst.operand;
    //
    //         if (operand is MethodInfo mi
    //             && mi.Name == "get_ActiveChatMode"
    //             && mi.DeclaringType != null && mi.DeclaringType.Name == "GameMain")
    //         {
    //             var call = new CodeInstruction(OpCodes.Call, chatModeReplacement);
    //             call.labels.AddRange(inst.labels);
    //             call.blocks.AddRange(inst.blocks);
    //             result.Add(call);
    //             chatModeHits++;
    //             continue;
    //         }
    //
    //         if (operand is FieldInfo fi && fi.Name == "NoiseGateThreshold")
    //         {
    //             result.Add(new CodeInstruction(OpCodes.Pop));
    //             result.Add(new CodeInstruction(OpCodes.Call, thresholdReplacement));
    //             thresholdHits++;
    //             continue;
    //         }
    //
    //         result.Add(inst);
    //     }
    //
    //     Log($"[HalfDuplexRadio] UpdateCapture transpiler: chatMode={chatModeHits}, threshold={thresholdHits}",
    //         chatModeHits > 0 && thresholdHits > 0 ? Color.Cyan : Color.Red);
    //     return result;
    // }
    //
    // /// <summary>按键说话不走噪音门限：给一个必然通过的值。</summary>
    // private static float GetNoiseGateThreshold()
    // {
    //     if (IsRadioKeyDown()) return -100f;
    //     return GameSettings.CurrentConfig.Audio.NoiseGateThreshold;
    // }
    //
    // private static void RebuildVoipCapture()
    // {
    //     try
    //     {
    //         VoipCapture capture = VoipCapture.Instance;
    //         if (capture == null) return;
    //
    //         ushort latest = capture.LatestBufferID;
    //         string device = GameSettings.CurrentConfig.Audio.VoiceCaptureDevice;
    //         capture.Dispose();
    //         VoipCapture.Create(device, latest);
    //         Log("[HalfDuplexRadio] 已重建 VoipCapture（应用 UpdateCapture 补丁）");
    //     }
    //     catch (Exception e)
    //     {
    //         Log("[HalfDuplexRadio] 重建 VoipCapture 失败：" + e.Message, Color.Yellow);
    //     }
    // }
}
