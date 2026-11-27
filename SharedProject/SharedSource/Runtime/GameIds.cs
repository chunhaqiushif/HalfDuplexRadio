namespace HalfDuplexRadio;

/// <summary>
/// 音效 tag 与标记字面量集中定义，便于与 Content/Sounds.xml、跨端协议对照。
/// </summary>
internal static partial class PluginCore
{
    // ---------------------------------------------------------------------
    // 音效 tag（对应 Content/Sounds.xml）
    // ---------------------------------------------------------------------
    /// <summary>自己按下按键。</summary>
    internal const string SoundPttBegin = "halfduplexpttbegin";

    /// <summary>自己按着但频道被占，循环播放。</summary>
    internal const string SoundPttBusy = "halfduplexppttbusy";

    /// <summary>开始收到别人的无线电。</summary>
    internal const string SoundListenBegin = "halfduplexlistenbegin";

    /// <summary>别人的无线电结束。</summary>
    internal const string SoundListenOver = "halfduplexlistenover";

    // ---------------------------------------------------------------------
    // 服务端 → 客户端标记（经 ChatMessageType.Console 下发）
    // ---------------------------------------------------------------------
    /// <summary>标记前缀。玩家无法在聊天框输入 \u0001，不会误触发。</summary>
    internal const string MarkerPrefix = "\u0001HDR:";

    /// <summary>频道被他人占用，按键无效。</summary>
    internal const string MarkerBusyToken = "BUSY";

    /// <summary>占用已解除。</summary>
    internal const string MarkerClearToken = "CLEAR";
}
