using System;
using Barotrauma;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework.Input;

namespace HalfDuplexRadio;

/// <summary>
/// 紧急线路按键：按住时向服务端上报"紧急发言中"；松开后仍按防切断设置保持一段时间，
/// 尾巴结束才撤销。电台的实际频道不变，广播权由服务端按上报状态判定。
/// </summary>
internal static class EmergencyController
{
    private static IGameNetwork? network;
    private static KeyOrMouse boundKey = new KeyOrMouse(Keys.P);
    private static float lastHeldAt = float.MinValue;

    /// <summary>物理上是否按着紧急键，用于决定是否强制发包。</summary>
    internal static bool KeyHeld { get; private set; }

    /// <summary>是否处于紧急线路状态（含防切断尾巴），用于气泡颜色与上报服务端。</summary>
    internal static bool Active { get; private set; }

    internal static KeyOrMouse BoundKey => boundKey;

    /// <summary>当前绑定的按键显示名，用于设置界面。</summary>
    internal static string BoundKeyName
    {
        get
        {
            try { return boundKey.GetName().Value; }
            catch { return boundKey.ToString(); }
        }
    }

    internal static void Init(IGameNetwork gameNetwork, KeyOrMouse key)
    {
        network = gameNetwork;
        boundKey = key;

        KeyHeld = false;
        Active = false;
        lastHeldAt = float.MinValue;
    }

    /// <summary>换绑按键。若正处在紧急状态，先撤销上报。</summary>
    internal static void Rebind(KeyOrMouse key)
    {
        if (Active) Send(false);

        boundKey = key;
        KeyHeld = false;
        Active = false;
        lastHeldAt = float.MinValue;
    }

    internal static void Tick()
    {
        EmergencyKeySetting.SyncBinding();

        float now = (float)Timing.TotalTime;

        bool held;
        try { held = boundKey.IsDown(); }
        catch { held = false; }

        KeyHeld = held;
        if (held) lastHeldAt = now;

        bool active = held || now - lastHeldAt < TailDuration();
        if (active == Active) return;

        Active = active;
        Send(active);
    }

    internal static void Reset()
    {
        if (Active) Send(false);

        KeyHeld = false;
        Active = false;
        lastHeldAt = float.MinValue;
    }

    /// <summary>与 PttState.CurrentMode 的防切断尾巴保持一致；防截断设为 0 时不留尾巴。</summary>
    private static float TailDuration()
    {
        try
        {
            float hold = GameSettings.CurrentConfig.Audio.VoiceChatCutoffPrevention / 1000f;
            return hold > 0f ? hold + PluginCore.RadioKeyTailPadding : 0f;
        }
        catch { return 0f; }
    }

    private static void Send(bool active)
    {
        try
        {
            network?.Send(EmergencyHeader.SpeakerState, new EmergencySpeakerState { Active = active });
        }
        catch (Exception e)
        {
            Plugin.LogError("Failed to report emergency speaker state: " + e.Message);
        }
    }
}
