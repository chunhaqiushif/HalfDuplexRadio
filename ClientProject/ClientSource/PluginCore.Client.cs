namespace HalfDuplexRadio;

/// <summary>
/// 客户端：每帧驱动按键路由与提示音，并处理状态复位。
/// </summary>
internal static partial class PluginCore
{
    static partial void ResetPlatform()
    {
        EmergencyController.Reset();
        PttState.Reset();
        BusyFeedback.Reset();
        ListenTracker.Reset();
    }

    static partial void TickPlatform(float now)
    {
        try
        {
            // 先更新紧急键状态，PttState 与气泡都依赖它
            EmergencyController.Tick();

            bool down = PttState.Refresh(now, out bool beganPress);

            // 按键期间强制发包，绕过噪音门限：没声音也发静音包，
            // 让同频道所有人收到包、画气泡、播监听提示音。
            if (down) VoipCaptureAccessor.TryForceCapture(ForceCaptureTimer);

            if (beganPress) VoiceSounds.Play(SoundPttBegin);

            BusyFeedback.Tick(down, now);

            if (ListenTracker.Tick(now)) VoiceSounds.Play(SoundListenOver);
        }
        catch { }
    }

    static partial void OnRoundStartedPlatform()
    {
        EmergencyController.Reset();
        PttState.Reset();
        BusyFeedback.Reset();
        ListenTracker.Reset();
    }
}
