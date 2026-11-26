using System;
using System.Collections.Generic;
using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace HalfDuplexRadio;

/// <summary>
/// 服务端：无线电语音仲裁。
///
/// 收到某玩家的语音包时读它的 ForceLocal 标记：本地语音直接放行，无线电语音按
/// "队伍:频道" 抢占频道；频道已被别人占用时丢弃该包并回一条 BUSY 标记。
/// </summary>
public partial class Plugin
{
    /// <summary>频道占用表。key = "teamId:channel"，value = 占用截止时刻。</summary>
    private static readonly Dictionary<string, float> BusyUntil = new();

    /// <summary>频道当前占用者。</summary>
    private static readonly Dictionary<string, Client> BusyHolder = new();

    /// <summary>每个被拦玩家上一次收到 BUSY 的时刻，用于限流。</summary>
    private static readonly Dictionary<Client, float> LastBusySound = new();

    public partial void InitProjectSpecific()
    {
        PatchPrefix("Barotrauma.Networking.VoipServer", "Read", nameof(VoipServerReadPrefix),
            typeof(IReadMessage), typeof(Client));

        PatchPrefix("Barotrauma.Networking.VoipServer", "SendToClients", nameof(VoipServerSendToClientsPrefix),
            typeof(List<Client>));

        Log("[HalfDuplexRadio] 已加载（服务端）：无线电语音半双工");
    }

    public partial void ResetProjectSpecific()
    {
        BusyUntil.Clear();
        BusyHolder.Clear();
        LastBusySound.Clear();
    }

    // ============================================================
    //  语音拦截
    // ============================================================

    /// <summary>
    /// 服务端收到某玩家的语音包时调用。返回 false 表示丢弃该包（不写入队列，也就不会转发给任何人）。
    /// </summary>
    private static bool VoipServerReadPrefix(IReadMessage inc, Client? connectedClient)
    {
        try
        {
            if (connectedClient == null) return true;

            VoipQueue? queue = connectedClient.VoipQueue;
            if (queue == null) return true;

            Character? speaker = connectedClient.Character;
            if (speaker == null || speaker.IsDead) return true;

            // 只读取包头的 ForceLocal 标记，读完把读指针还原
            bool forceLocal;
            try
            {
                int savedPosition = inc.BitPosition;
                inc.ReadUInt16();               // LatestBufferID
                forceLocal = inc.ReadBoolean(); // ForceLocal
                inc.BitPosition = savedPosition;
            }
            catch
            {
                return true;
            }

            // 本地语音不参与半双工
            if (forceLocal) return true;

            // 没有可用电台 / 耳机（含被干扰、没电）时按本地语音处理
            if (!ChatMessage.CanUseRadio(speaker, out WifiComponent radio)) return true;

            string key = $"{(int)radio.TeamID}:{radio.Channel}";
            float now = (float)Timing.TotalTime;

            bool busy = BusyUntil.TryGetValue(key, out float until) && now < until;
            if (busy)
            {
                BusyHolder.TryGetValue(key, out Client? holder);
                if (holder != null && holder != connectedClient)
                {
                    queue.Read(inc, discardData: true);
                    PlayBusySound(connectedClient, now);
                    return false;
                }
            }

            // 抢占或续期
            BusyUntil[key] = now + ReleaseTimeout;
            BusyHolder[key] = connectedClient;

            // 之前被拦过、现在抢到了 → 通知客户端停掉占线循环音
            if (LastBusySound.TryGetValue(connectedClient, out float lastBusy) && now - lastBusy < 5f)
            {
                LastBusySound.Remove(connectedClient);
                SendMarker(connectedClient, MarkerClear);
            }
            return true;
        }
        catch (Exception e)
        {
            Log("[HalfDuplexRadio] 语音拦截异常：" + e.Message, Color.Red);
            return true;
        }
    }

    /// <summary>每帧转发前清理已经结束的占用。</summary>
    private static bool VoipServerSendToClientsPrefix(List<Client>? clients)
    {
        try { FlushFinishedTransmissions(); }
        catch (Exception e) { Log("[HalfDuplexRadio] 结束检测异常：" + e.Message, Color.Red); }
        return true;
    }

    private static void FlushFinishedTransmissions()
    {
        if (BusyUntil.Count == 0) return;

        float now = (float)Timing.TotalTime;
        List<string>? finished = null;
        foreach (var pair in BusyUntil)
        {
            if (now < pair.Value) continue;
            (finished ??= new List<string>()).Add(pair.Key);
        }
        if (finished == null) return;

        foreach (string key in finished)
        {
            BusyUntil.Remove(key);
            BusyHolder.Remove(key);
        }
    }

    // ============================================================
    //  标记消息
    // ============================================================

    /// <summary>向被拦玩家下发占线标记，受 <see cref="BusySoundCooldown"/> 限流。</summary>
    private static void PlayBusySound(Client blocked, float now)
    {
        if (LastBusySound.TryGetValue(blocked, out float last) && now - last < BusySoundCooldown) return;
        LastBusySound[blocked] = now;
        SendMarker(blocked, MarkerBusy);
    }

    /// <summary>
    /// 以 ChatMessageType.Console 定向发给单个客户端。
    /// 客户端收到后只会转交 DebugConsole，不会出现在聊天框里。
    /// </summary>
    private static void SendMarker(Client client, string kind)
    {
        GameServer? server = GameMain.Server;
        if (server == null) return;

        try { server.SendConsoleMessage(MarkerPrefix + kind, client); }
        catch (Exception e) { Log("[HalfDuplexRadio] 下发标记失败：" + e.Message, Color.Red); }
    }
}
