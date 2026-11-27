using Barotrauma;

namespace HalfDuplexRadio;

/// <summary>
/// 紧急线路的网络消息头。值必须从 0 开始连续，游戏用最小/最大值界定该插件的 opcode 区间。
/// </summary>
public enum EmergencyHeader
{
    /// <summary>客户端上报紧急发言状态。</summary>
    SpeakerState = 0,
}

/// <summary>紧急发言状态：按住紧急键为 true，松开为 false。</summary>
[NetworkSerialize]
public struct EmergencySpeakerState : INetSerializableStruct
{
    public bool Active;
}
