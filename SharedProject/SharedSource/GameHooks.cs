using System;
using System.Reflection;
using Barotrauma;
using Barotrauma.Plugins;
using HarmonyLib;

namespace HalfDuplexRadio;

/// <summary>
/// Harmony 补丁装配：只负责「挂哪个方法、挂到哪个回调、失败怎么记日志」。
///
/// 目标类型在游戏程序集里可能是 internal（VoipServer / VoipClient / VoipCapture / DebugConsole），
/// 无法直接引用，因此统一按类型全名解析。回调方法统一声明在 Plugin 分部类中。
/// </summary>
internal static class GameHooks
{
    private static Harmony? harmony;

    internal static void Install(IHarmonyProvider provider)
    {
        try
        {
            harmony = provider.GetHarmony();
        }
        catch (Exception e)
        {
            Plugin.LogError("Failed to obtain Harmony instance: " + e.Message);
            return;
        }

        InstallRoundStartHook();
    }

    /// <summary>撤销本插件的全部补丁并释放实例。</summary>
    internal static void Reset()
    {
        try { harmony?.UnpatchSelf(); } catch { /* 卸载阶段允许失败 */ }
        harmony = null;
    }

    internal static void Prefix(string typeName, string methodName, string callbackName, params Type[] paramTypes)
        => Apply(typeName, methodName, callbackName, paramTypes, asPostfix: false);

    internal static void Postfix(string typeName, string methodName, string callbackName, params Type[] paramTypes)
        => Apply(typeName, methodName, callbackName, paramTypes, asPostfix: true);

    internal static void Prefix(Type type, string methodName, string callbackName, params Type[] paramTypes)
        => Prefix(type.FullName ?? type.Name, methodName, callbackName, paramTypes);

    internal static void Postfix(Type type, string methodName, string callbackName, params Type[] paramTypes)
        => Postfix(type.FullName ?? type.Name, methodName, callbackName, paramTypes);

    private static void Apply(string typeName, string methodName, string callbackName, Type[] paramTypes, bool asPostfix)
    {
        if (harmony == null)
        {
            Plugin.LogError($"Harmony not ready, skipped {typeName}.{methodName}");
            return;
        }

        Type? type = AccessTools.TypeByName(typeName);
        if (type == null)
        {
            Plugin.LogError($"Type {typeName} not present on this side, skipped {methodName}");
            return;
        }

        MethodInfo? target = AccessTools.Method(type, methodName, paramTypes);
        if (target == null)
        {
            Plugin.LogError($"Method {typeName}.{methodName} not found, feature disabled");
            return;
        }

        MethodInfo? callback = typeof(Plugin).GetMethod(callbackName, BindingFlags.Static | BindingFlags.NonPublic);
        if (callback == null)
        {
            Plugin.LogError($"Callback {callbackName} missing, feature disabled");
            return;
        }

        harmony.Patch(target,
            prefix: asPostfix ? null : new HarmonyMethod(callback),
            postfix: asPostfix ? new HarmonyMethod(callback) : null);

        string suffix = asPostfix ? " (postfix)" : string.Empty;
        Plugin.Log($"Patched {typeName}.{methodName}{suffix}");
    }

    /// <summary>
    /// 回合开始（新回合 / 读档 / 结束回合后重开）时清掉上一回合残留的语音状态。
    /// GameSession.StartRound 有多个重载，按方法名遍历逐个挂钩，postfix 不带参数，
    /// 因此不依赖具体重载的签名。
    /// </summary>
    private static void InstallRoundStartHook()
    {
        MethodInfo? callback = typeof(PluginCore).GetMethod(
            nameof(PluginCore.OnRoundStarted), BindingFlags.Static | BindingFlags.NonPublic);

        if (callback == null)
        {
            Plugin.LogError("Round-start callback missing, per-round state may persist");
            return;
        }

        int patched = 0;
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(GameSession)))
        {
            if (method.Name != nameof(GameSession.StartRound)) continue;

            harmony!.Patch(method, postfix: new HarmonyMethod(callback));
            patched++;
        }

        if (patched == 0)
        {
            Plugin.LogError("GameSession.StartRound not found, per-round state may persist");
        }
        else
        {
            Plugin.Log($"Patched GameSession.StartRound (overloads: {patched})");
        }
    }
}
