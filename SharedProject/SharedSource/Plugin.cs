using System;
using System.Reflection;
using Barotrauma;
using Barotrauma.Plugins;
using HarmonyLib;
using Microsoft.Xna.Framework;

namespace HalfDuplexRadio;

/// <summary>
/// 半双工无线电语音 —— 官方插件系统入口。
///
/// 同一无线电频道同一时刻只允许一个人说话：别人按住无线电键时语音包被服务端丢弃。
/// 按键路由：语音聊天键 / 无线电键 → 无线电，本地键 / 不按键 → 本地。
/// 提示音：按下、占线、开始收听、收听结束。
///
/// 本文件：生命周期、可调参数、共享常量、Harmony 装配辅助。
/// 平台实现：PluginServer.cs（服务端仲裁）、PluginClient.cs（客户端音效与按键路由）。
/// </summary>
public partial class Plugin : IBarotraumaPlugin
{
    private static readonly IDebugConsole PluginConsole = PluginServiceProvider.GetService<IDebugConsole>();

    private static Harmony? harmony;

    // ===== 可调参数 =====

    /// <summary>多久没收到语音包就认为对方说完了、释放频道（秒）。语音包间隔 20ms。</summary>
    internal const float ReleaseTimeout = 0.32f;

    /// <summary>同一个人的占线音效最短间隔（秒），防止 20ms 一包刷屏。</summary>
    internal const float BusySoundCooldown = 2.5f;

    // ===== 音效 tag（对应 Content/Sounds.xml） =====

    /// <summary>自己按下按键。</summary>
    internal const string PttBeginTag = "halfduplexpttbegin";

    /// <summary>自己按着但频道被占，循环播放。</summary>
    internal const string PttBusyTag = "halfduplexppttbusy";

    /// <summary>开始收到别人的无线电。</summary>
    internal const string ListenBeginTag = "halfduplexlistenbegin";

    /// <summary>别人的无线电结束。</summary>
    internal const string ListenOverTag = "halfduplexlistenover";

    // ===== 服务端 → 客户端的隐藏标记消息 =====

    /// <summary>标记前缀。玩家无法在聊天框输入 \u0001，不会误触发。</summary>
    internal const string MarkerPrefix = "\u0001HDR:";

    internal const string MarkerBusy = "BUSY";
    internal const string MarkerClear = "CLEAR";

    public void Init()
    {
        try
        {
            harmony = PluginServiceProvider.GetService<IHarmonyProvider>().GetHarmony();
        }
        catch (Exception e)
        {
            Log("[HalfDuplexRadio] 获取 Harmony 实例失败，语音改造不会生效：" + e.Message, Color.Red);
            return;
        }

        InitProjectSpecific();
    }

    public void OnContentLoaded() { }

    public void Dispose()
    {
        try { harmony?.UnpatchSelf(); } catch { /* 卸载阶段允许失败 */ }
        harmony = null;

        ResetProjectSpecific();
        Log("[HalfDuplexRadio] 已卸载");
    }

    /// <summary>由 ClientProject / ServerProject 分别实现，装配本端补丁。</summary>
    public partial void InitProjectSpecific();

    /// <summary>由 ClientProject / ServerProject 分别实现，清空本端静态状态。</summary>
    public partial void ResetProjectSpecific();

    // ============================================================
    //  补丁装配
    // ============================================================

    /// <summary>
    /// 按类型全名挂前缀补丁。目标类型在游戏程序集里可能是 internal（VoipServer / VoipClient /
    /// VoipCapture / DebugConsole），无法直接引用，因此统一按名字解析。
    /// </summary>
    internal static void PatchPrefix(string typeName, string methodName, string callbackName, params Type[] paramTypes)
    {
        MethodInfo? target = ResolveTarget(typeName, methodName, paramTypes);
        MethodInfo? callback = ResolveCallback(callbackName);
        if (target == null || callback == null) return;

        harmony!.Patch(target, prefix: new HarmonyMethod(callback));
        Log($"[HalfDuplexRadio] 已挂载 {typeName}.{methodName}");
    }

    internal static void PatchPostfix(string typeName, string methodName, string callbackName, params Type[] paramTypes)
    {
        MethodInfo? target = ResolveTarget(typeName, methodName, paramTypes);
        MethodInfo? callback = ResolveCallback(callbackName);
        if (target == null || callback == null) return;

        harmony!.Patch(target, postfix: new HarmonyMethod(callback));
        Log($"[HalfDuplexRadio] 已挂载 {typeName}.{methodName}（后缀）");
    }

    internal static void PatchPrefix(Type type, string methodName, string callbackName, params Type[] paramTypes)
        => PatchPrefix(type.FullName ?? type.Name, methodName, callbackName, paramTypes);

    internal static void PatchPostfix(Type type, string methodName, string callbackName, params Type[] paramTypes)
        => PatchPostfix(type.FullName ?? type.Name, methodName, callbackName, paramTypes);

    /// <summary>定位目标方法；找不到时记日志并跳过，不影响其余补丁。</summary>
    private static MethodInfo? ResolveTarget(string typeName, string methodName, Type[] paramTypes)
    {
        Type? type = AccessTools.TypeByName(typeName);
        if (type == null)
        {
            Log($"[HalfDuplexRadio] 当前端没有 {typeName}，跳过 {methodName}", Color.Yellow);
            return null;
        }

        MethodInfo? target = AccessTools.Method(type, methodName, paramTypes);
        if (target == null)
        {
            Log($"[HalfDuplexRadio] 未找到 {typeName}.{methodName}，该功能不生效", Color.Red);
        }
        return target;
    }

    private static MethodInfo? ResolveCallback(string callbackName)
    {
        MethodInfo? callback = typeof(Plugin).GetMethod(callbackName, BindingFlags.Static | BindingFlags.NonPublic);
        if (callback == null)
        {
            Log($"[HalfDuplexRadio] 内部回调 {callbackName} 缺失，该功能不生效", Color.Red);
        }
        return callback;
    }

    internal static void Log(object msg, Color? color = null)
    {
        try { PluginConsole.NewMessage($"{msg}", color ?? Color.Cyan); }
        catch { /* 插件卸载后控制台可能已失效 */ }
    }
}
