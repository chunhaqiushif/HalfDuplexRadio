using Barotrauma;

namespace HalfDuplexRadio;

/// <summary>
/// 平台无关的编排层：生命周期复位、每帧驱动、回合切换清场。
///
/// 端专属实现在 ClientProject / ServerProject 的同名分部文件中；
/// 某端未实现的分部调用会被编译器直接移除，因此不需要条件编译。
/// </summary>
internal static partial class PluginCore
{
    /// <summary>插件卸载时清空全部静态状态。</summary>
    internal static void Reset()
    {
        ResetPlatform();
    }

    /// <summary>每帧驱动入口，由 PluginOnPreUpdateDelegate 钩子调用。</summary>
    internal static void Tick(float deltaTime)
    {
        TickPlatform((float)Timing.TotalTime);
    }

    /// <summary>回合开始（新回合 / 读档 / 结束回合后重开）时清掉上一回合的残留状态。</summary>
    internal static void OnRoundStarted()
    {
        OnRoundStartedPlatform();
    }

    static partial void ResetPlatform();

    static partial void TickPlatform(float now);

    static partial void OnRoundStartedPlatform();
}
