using System;

namespace HalfDuplexRadio;

/// <summary>频道占用标记的种类。</summary>
internal enum MarkerKind
{
    /// <summary>频道被他人占用，按键无效。</summary>
    Busy,

    /// <summary>占用已解除。</summary>
    Clear,
}

/// <summary>
/// 服务端 → 客户端的隐藏标记，经 ChatMessageType.Console 下发。
/// 客户端收到后只会转交 DebugConsole，不会出现在聊天框里。
/// </summary>
internal static class RadioMarker
{
    /// <summary>把标记种类编码成一条可以走聊天通道的文本。</summary>
    internal static string Encode(MarkerKind kind)
    {
        string token = kind switch
        {
            MarkerKind.Busy => PluginCore.MarkerBusyToken,
            MarkerKind.Clear => PluginCore.MarkerClearToken,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        return PluginCore.MarkerPrefix + token;
    }

    /// <summary>
    /// 解析标记。返回 false 表示这条文本不是标记。
    /// 返回 true 但 kind 为 null，说明前缀命中而种类未知（对端版本更新），
    /// 调用方应静默吞掉而不是当普通控制台消息输出。
    /// </summary>
    internal static bool TryDecode(string? text, out MarkerKind? kind)
    {
        kind = null;

        if (string.IsNullOrEmpty(text)) return false;
        if (!text.StartsWith(PluginCore.MarkerPrefix, StringComparison.Ordinal)) return false;

        kind = text.Substring(PluginCore.MarkerPrefix.Length) switch
        {
            PluginCore.MarkerBusyToken => MarkerKind.Busy,
            PluginCore.MarkerClearToken => MarkerKind.Clear,
            _ => null,
        };

        return true;
    }
}
