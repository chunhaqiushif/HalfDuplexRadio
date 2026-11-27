using System;
using System.Collections.Generic;
using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace HalfDuplexRadio;

/// <summary>
/// 服务端：无线电频道占用仲裁。
///
/// 收到某玩家的语音包时读它的 ForceLocal 标记：本地语音直接放行，无线电语音按
/// "队伍:频道" 抢占频道；频道已被别人占用时丢弃该包并回一条 BUSY 标记。
/// </summary>
internal static partial class PluginCore
{
    /// <summary>一条频道占用记录。</summary>
    private readonly record struct ChannelOccupancy(Client? Holder, float Until);

    /// <summary>频道占用表。key = "teamId:channel"。</summary>
    private static readonly Dictionary<string, ChannelOccupancy> Occupancy = new();

    /// <summary>每个被拦玩家上一次收到 BUSY 的时刻，用于限流。</summary>
    private static readonly Dictionary<Client, float> LastBusyAt = new();

    private static void ClearArbitration()
    {
        Occupancy.Clear();
        LastBusyAt.Clear();
    }

    // ============================================================
    //  语音拦截
    // ============================================================

    /// <summary>
    /// 服务端收到某玩家的语音包时调用。返回 false 表示丢弃该包（不写入队列，也就不会转发给任何人）。
    /// </summary>
    internal static bool TryArbitrate(IReadMessage inc, Client? connectedClient)
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

            float now = (float)Timing.TotalTime;

            // 紧急发言者无视占线，也不占用任何频道
            if (IsEmergencySpeaker(connectedClient)) return true;

            string key = $"{(int)radio.TeamID}:{radio.Channel}";

            if (IsHeldByOther(key, connectedClient, now))
            {
                queue.Read(inc, discardData: true);
                NotifyBusy(connectedClient, now);
                return false;
            }

            // 抢占或续期
            Occupancy[key] = new ChannelOccupancy(connectedClient, now + ReleaseTimeout);

            // 之前被拦过、现在抢到了 → 通知客户端停掉占线循环音
            if (LastBusyAt.TryGetValue(connectedClient, out float lastBusy)
                && now - lastBusy < BusyBlockedWindow)
            {
                LastBusyAt.Remove(connectedClient);
                SendMarker(connectedClient, MarkerKind.Clear);
            }
            return true;
        }
        catch (Exception e)
        {
            Plugin.LogError("Voice arbitration failed: " + e.Message);
            return true;
        }
    }

    /// <summary>该频道当前是否被其他人占用。</summary>
    private static bool IsHeldByOther(string key, Client claimant, float now)
    {
        return Occupancy.TryGetValue(key, out ChannelOccupancy occupancy)
            && now < occupancy.Until
            && occupancy.Holder != null
            && occupancy.Holder != claimant;
    }

    /// <summary>清理已经结束的占用。</summary>
    private static void FlushFinishedTransmissions(float now)
    {
        if (Occupancy.Count == 0) return;

        List<string>? finished = null;
        foreach (var pair in Occupancy)
        {
            if (now < pair.Value.Until) continue;
            (finished ??= new List<string>()).Add(pair.Key);
        }
        if (finished == null) return;

        foreach (string key in finished) Occupancy.Remove(key);
    }

    // ============================================================
    //  标记消息
    // ============================================================

    /// <summary>向被拦玩家下发占线标记，受 BusySoundCooldown 限流。</summary>
    private static void NotifyBusy(Client blocked, float now)
    {
        if (LastBusyAt.TryGetValue(blocked, out float last) && now - last < BusySoundCooldown) return;

        LastBusyAt[blocked] = now;
        SendMarker(blocked, MarkerKind.Busy);
    }

    /// <summary>
    /// 以 ChatMessageType.Console 定向发给单个客户端。
    /// 客户端收到后只会转交 DebugConsole，不会出现在聊天框里。
    /// </summary>
    private static void SendMarker(Client client, MarkerKind kind)
    {
        GameServer? server = GameMain.Server;
        if (server == null) return;

        try { server.SendConsoleMessage(RadioMarker.Encode(kind), client); }
        catch (Exception e) { Plugin.LogError("Failed to send voice marker: " + e.Message); }
    }
}
