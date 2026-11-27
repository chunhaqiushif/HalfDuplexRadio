namespace HalfDuplexRadio;

/// <summary>
/// 服务端：PluginCore 的平台分部，只负责把生命周期钩子接到各模块的清理与每帧维护。
/// 具体实现在 Runtime/ChannelArbitration.cs 与 Runtime/EmergencyBroadcast.cs。
/// </summary>
internal static partial class PluginCore
{
    static partial void ResetPlatform() => ClearPlatformState();

    static partial void TickPlatform(float now)
    {
        FlushFinishedTransmissions(now);
        PruneEmergencySpeakers();
    }

    static partial void OnRoundStartedPlatform() => ClearPlatformState();

    private static void ClearPlatformState()
    {
        ClearArbitration();
        ClearEmergencySpeakers();
    }
}
