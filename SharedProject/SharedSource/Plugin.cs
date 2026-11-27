using System;
using Barotrauma;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;

namespace HalfDuplexRadio;

/// <summary>
/// 半双工无线电语音 —— 官方插件系统入口。
///
/// 同一无线电频道同一时刻只允许一个人说话：别人按住无线电键时语音包被服务端丢弃。
/// 按键路由：语音聊天键 / 无线电键 → 无线电，本地键 / 不按键 → 本地。
/// 提示音：按下、占线、开始收听、收听结束。
///
/// 本文件只负责生命周期编排，其余职责分别在：
///   GameHooks    —— Harmony 补丁装配
///   PluginCore   —— 复位 / 每帧驱动 / 回合清场（端专属实现见同名分部）
///   PluginClient / PluginServer —— 端专属补丁与回调
/// </summary>
public partial class Plugin : IBarotraumaPlugin
{
    /// <summary>错误日志上限，避免语音拦截异常每包一条刷屏。</summary>
    private const int MaxErrorMessages = 5;

    private static readonly IDebugConsole DebugConsole = PluginServiceProvider.GetService<IDebugConsole>();

    private static int errorCount;

    public void Init()
    {
        IHarmonyProvider harmonyProvider;
        try
        {
            harmonyProvider = PluginServiceProvider.GetService<IHarmonyProvider>();
        }
        catch (Exception e)
        {
            LogError("Harmony provider unavailable, voice rework will not apply: " + e.Message);
            return;
        }

        GameHooks.Install(harmonyProvider);
        RegisterNetworkHeaders();
        RegisterTickHook();

        InitProjectSpecific();
        Log("Half Duplex Radio loaded.");
    }

    public void OnContentLoaded() { }

    public void Dispose()
    {
        GameHooks.Reset();
        PluginCore.Reset();

        errorCount = 0;
        Log("Half Duplex Radio unloaded.");
    }

    /// <summary>注册本插件的网络消息头。两端都要注册，服务端读包时需要 opcode 区间。</summary>
    private static void RegisterNetworkHeaders()
    {
        try
        {
            PluginServiceProvider.GetService<IGameNetwork>().RegisterNetworkHeaders<EmergencyHeader>();
        }
        catch (Exception e)
        {
            LogError("Failed to register network headers: " + e.Message);
        }
    }

    /// <summary>注册每帧驱动。官方钩子在两端主循环的最前调用，先于语音发包。</summary>
    private static void RegisterTickHook()
    {
        try
        {
            PluginServiceProvider.GetService<ISimpleHookService>()
                .RegisterHook<PluginOnPreUpdateDelegate>(PluginCore.Tick);
        }
        catch (Exception e)
        {
            LogError("Failed to register tick hook, key routing will not update: " + e.Message);
        }
    }

    internal static void Log(string message) => DebugConsole.NewMessage(message, Color.Lime);

    internal static void LogError(string message)
    {
        if (errorCount >= MaxErrorMessages) return;

        errorCount++;
        DebugConsole.NewMessage(message, Color.Red);
    }

    /// <summary>由 ClientProject / ServerProject 分别实现，装配本端补丁。</summary>
    public partial void InitProjectSpecific();
}
