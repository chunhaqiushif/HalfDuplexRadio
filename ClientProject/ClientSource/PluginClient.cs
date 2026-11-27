using System;
using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;

namespace HalfDuplexRadio;

/// <summary>
/// 客户端：补丁装配与回调。
///
/// 按键路由：按住语音聊天键 / 无线电键时语音走无线电，其余情况走本地。
/// 紧急线路：按住紧急键时走无线电并上报服务端，气泡显示绿色。
/// 提示音：按下、占线（服务端下发标记后循环）、开始收听、收听结束。
/// 无线电语音强制 2D，只保留 Near/Far 距离衰减。
///
/// 每帧驱动与状态复位在 PluginCore.Client.cs，状态与音效分别在
/// PttState / BusyFeedback / ListenTracker / VoiceSounds，反射访问在 VoipCaptureAccessor。
/// </summary>
public partial class Plugin
{
    public partial void InitProjectSpecific()
    {
        GameHooks.Prefix("Barotrauma.DebugConsole", "NewMessage", nameof(DebugConsoleNewMessagePrefix),
            typeof(string), typeof(Color?), typeof(bool));

        GameHooks.Prefix("Barotrauma.Networking.VoipClient", "Read", nameof(VoipClientReadPrefix), typeof(IReadMessage));
        GameHooks.Prefix(typeof(VoipQueue), nameof(VoipQueue.Write), nameof(VoipQueueWritePrefix), typeof(IWriteMessage));
        GameHooks.Prefix(typeof(Character), nameof(Character.ShowTextlessSpeechBubble), nameof(ShowTextlessSpeechBubblePrefix),
            typeof(float), typeof(Color));

        GameHooks.Postfix(typeof(Client), nameof(Client.UpdateVoipSound), nameof(ClientUpdateVoipSoundPostfix));

        InitEmergencyKey();
    }

    /// <summary>注册紧急线路按键设置，并启动按键状态上报。</summary>
    private static void InitEmergencyKey()
    {
        try
        {
            EmergencyKeySetting.Init(PluginServiceProvider.GetService<ISettingsService>());
            EmergencyController.Init(PluginServiceProvider.GetService<IGameNetwork>(), EmergencyKeySetting.Resolve());
        }
        catch (Exception e)
        {
            Plugin.LogError("Failed to initialise emergency key: " + e.Message);
        }
    }

    // ============================================================
    //  标记消息（服务端 → 客户端）
    // ============================================================

    /// <summary>拦截服务端下发的隐藏标记：驱动占线反馈并吞掉，不写调试控制台。</summary>
    private static bool DebugConsoleNewMessagePrefix(string? msg)
    {
        try
        {
            if (!RadioMarker.TryDecode(msg, out MarkerKind? kind)) return true;

            float now = (float)Timing.TotalTime;
            switch (kind)
            {
                case MarkerKind.Busy: BusyFeedback.OnBusy(now); break;
                case MarkerKind.Clear: BusyFeedback.OnClear(); break;
            }
            return false;
        }
        catch
        {
            return true;
        }
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

            // 只对无线电语音播提示音，本地语音不处理
            if (!isRadio) return;

            Client? client = FindClientByQueueId(queueId);
            if (client == null) return;

            if (ListenTracker.OnRadioPacket(client, (float)Timing.TotalTime))
            {
                VoiceSounds.Play(PluginCore.SoundListenBegin);
            }
        }
        catch { }
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
            if (!VoipCaptureAccessor.IsCaptureQueue(__instance)) return;

            __instance.ForceLocal = PttState.CurrentMode((float)Timing.TotalTime) == ChatMode.Local;
        }
        catch { }
    }

    /// <summary>
    /// 自己头顶的语音气泡按当前语音模式着色：紧急线路绿色，被占线挡下红色，
    /// 其余按无线电 / 默认色。其他气泡（文字聊天等）时长不同，不受影响。
    /// </summary>
    private static void ShowTextlessSpeechBubblePrefix(Character? __instance, float duration, ref Color color)
    {
        try
        {
            if (__instance == null || __instance != Character.Controlled) return;
            if (Math.Abs(duration - PluginCore.VoiceBubbleDuration) > PluginCore.VoiceBubbleTolerance) return;

            if (BusyFeedback.Blocked)
            {
                color = Color.Red;
                return;
            }

            ChatMode mode = PttState.CurrentMode((float)Timing.TotalTime);

            if (mode == ChatMode.Radio && EmergencyController.Active)
            {
                color = Color.Lime;
                return;
            }

            color = mode == ChatMode.Radio
                ? ChatMessage.MessageColor[(int)ChatMessageType.Radio]
                : ChatMessage.MessageColor[(int)ChatMessageType.Default];
        }
        catch { }
    }
}
