using Barotrauma;
using Barotrauma.Sounds;

namespace HalfDuplexRadio;

/// <summary>占线反馈：循环音效 + 被挡下的时间窗口（期间头顶气泡为红色）。</summary>
internal static class BusyFeedback
{
    private static SoundChannel? channel;
    private static float lastBusyAt = float.MinValue;
    private static float blockedUntil = float.MinValue;

    /// <summary>当前是否处于被占线挡下的时间窗口。</summary>
    internal static bool Blocked => blockedUntil > (float)Timing.TotalTime;

    /// <summary>收到 BUSY 标记。</summary>
    internal static void OnBusy(float now)
    {
        blockedUntil = now + PluginCore.BusyBlockedWindow;
        StartLoop(now);
    }

    /// <summary>收到 CLEAR 标记。</summary>
    internal static void OnClear()
    {
        blockedUntil = float.MinValue;
        StopLoop();
    }

    /// <summary>每帧：松手停循环；一直按着但久未再收到 BUSY 也停。</summary>
    internal static void Tick(bool keyDown, float now)
    {
        if (!keyDown)
        {
            StopLoop();
            blockedUntil = float.MinValue;
            return;
        }

        if (channel != null && now - lastBusyAt > PluginCore.BusyLoopSilence) StopLoop();
    }

    internal static void Reset()
    {
        StopLoop();
        blockedUntil = float.MinValue;
        lastBusyAt = float.MinValue;
    }

    private static void StartLoop(float now)
    {
        lastBusyAt = now;
        if (channel != null && channel.IsPlaying) return;

        channel = VoiceSounds.PlayLoop(PluginCore.SoundPttBusy);
    }

    private static void StopLoop()
    {
        if (channel == null) return;

        try { channel.Dispose(); } catch { /* 已释放 */ }
        channel = null;
    }
}
