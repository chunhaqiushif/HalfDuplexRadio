using System;
using System.Reflection;
using HarmonyLib;

namespace HalfDuplexRadio;

/// <summary>
/// 语音采集队列的反射访问。
/// VoipCapture 是 internal 类型，无法直接引用，只能按名解析。
/// </summary>
internal static class VoipCaptureAccessor
{
    private static Type? captureType;
    private static PropertyInfo? instanceProperty;
    private static FieldInfo? timerField;
    private static bool resolved;

    /// <summary>该队列是否就是本地采集队列。</summary>
    internal static bool IsCaptureQueue(object queue)
    {
        object? instance = GetInstance();
        return instance != null && ReferenceEquals(queue, instance);
    }

    /// <summary>把采集计时器抬到指定值，让下一帧进入发送分支。返回是否成功。</summary>
    internal static bool TryForceCapture(int timerValue)
    {
        object? instance = GetInstance();
        if (instance == null || timerField == null) return false;

        try
        {
            timerField.SetValue(instance, timerValue);
            return true;
        }
        catch { return false; }
    }

    /// <summary>每次调用都重新取实例：联机会话建立前 Instance 为 null，建立后即可用。</summary>
    private static object? GetInstance()
    {
        if (!resolved)
        {
            resolved = true;

            captureType = AccessTools.TypeByName("Barotrauma.Networking.VoipCapture");
            if (captureType == null)
            {
                Plugin.LogError("VoipCapture not found, push-to-talk will not force packets");
                return null;
            }

            instanceProperty = AccessTools.Property(captureType, "Instance");
            timerField = AccessTools.Field(captureType, "captureTimer");

            if (instanceProperty == null || timerField == null)
            {
                Plugin.LogError("VoipCapture members unresolved, push-to-talk may not force packets");
            }
        }

        try { return instanceProperty?.GetValue(null); }
        catch { return null; }
    }
}
