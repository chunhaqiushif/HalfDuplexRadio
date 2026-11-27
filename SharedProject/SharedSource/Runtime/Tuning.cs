namespace HalfDuplexRadio;

/// <summary>可调参数。</summary>
internal static partial class PluginCore
{
    /// <summary>多久没收到语音包就认为对方说完了、释放频道（秒）。语音包间隔约 16ms。</summary>
    internal const float ReleaseTimeout = 0.32f;

    /// <summary>同一个人的占线音效最短间隔（秒），防止每包一条刷屏。</summary>
    internal const float BusySoundCooldown = 2.5f;

    /// <summary>收到 BUSY 后保持"被挡下"状态的时长（秒）。客户端与服务端共用此窗口。</summary>
    internal const float BusyBlockedWindow = 5f;

    /// <summary>一直按着但连续这么久没再收到 BUSY，认为频道已空出（秒）。</summary>
    internal const float BusyLoopSilence = 3f;

    /// <summary>多久没收到某个人的语音包就认为他说完了（秒）。</summary>
    internal const float ListenTimeout = 0.35f;

    /// <summary>松开无线电键后继续按无线电计算的额外缓冲（秒）。</summary>
    internal const float RadioKeyTailPadding = 0.15f;

    /// <summary>语音气泡的固定时长（秒），用于识别语音气泡。</summary>
    internal const float VoiceBubbleDuration = 1.25f;

    /// <summary>语音气泡时长的匹配容差（秒）。</summary>
    internal const float VoiceBubbleTolerance = 0.02f;

    /// <summary>
    /// 按键期间写入 captureTimer 的值。采集线程每轮递减 20、轮间隔约 15.6ms，
    /// 而主线程约 16.7ms 写一次，取 40 覆盖两轮，抹平两者相位差造成的空槽。
    /// </summary>
    internal const int ForceCaptureTimer = 40;
}
