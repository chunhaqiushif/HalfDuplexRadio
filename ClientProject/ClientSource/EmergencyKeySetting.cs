using System;
using System.Linq;
using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace HalfDuplexRadio;

/// <summary>
/// 紧急线路按键设置：存储用官方 StringSetting（值为 KeyOrMouse.ToString()），
/// 界面用 OverrideUI 自绘，复刻游戏原生 controls 页的按键捕获方式。
/// </summary>
internal static class EmergencyKeySetting
{
    private const string SettingIdentifier = "emergencykey";
    private const string DefaultKeyName = "P";

    private static StringSetting? setting;
    private static ISettingsService? service;
    private static GUIButton? captureButton;
    private static bool capturing;
    private static bool skipNextFrame;
    private static string? lastRaw;

    internal static void Init(ISettingsService settings)
    {
        service = settings;
        setting = new StringSetting(SettingIdentifier.ToIdentifier(), DefaultKeyName,
            "Emergency channel key")
        {
            ShowInUI = false,
            SyncMode = SettingSyncMode.NoSync,
            ToolTip = "Hold this key to talk on the emergency channel.",
        };

        settings.RegisterSetting(setting);

        // 插件重载（回主菜单、切换 mod）会创建全新的 SettingsService，
        // 不主动加载就永远是构造时的默认值。
        try { settings.Load(); }
        catch (Exception e) { Plugin.LogError("Failed to load settings: " + e.Message); }

        settings.OverrideUI = BuildUI;
        lastRaw = setting.Value;
    }

    /// <summary>设置值被外部改动（重置为默认、重新加载）时同步到运行时绑定。</summary>
    internal static void SyncBinding()
    {
        string? raw = setting?.Value;
        if (raw == lastRaw) return;

        lastRaw = raw;
        EmergencyController.Rebind(Resolve());
        RefreshButtonText();
    }

    /// <summary>解析设置里存的按键，非法值回退到 P。</summary>
    internal static KeyOrMouse Resolve()
    {
        if (setting == null) return new KeyOrMouse(Keys.P);

        string raw = setting.Value ?? DefaultKeyName;
        if (TryParse(raw, out KeyOrMouse key)) return key;

        Plugin.LogError($"Invalid emergency key binding \"{raw}\", falling back to {DefaultKeyName}");
        return new KeyOrMouse(Keys.P);
    }

    // ============================================================
    //  设置界面
    // ============================================================

    private static void BuildUI(GUIFrame content)
    {
        capturing = false;

        var layout = new GUILayoutGroup(new RectTransform(Vector2.One, content.RectTransform));

        var row = new GUIFrame(new RectTransform(new Vector2(1f, 0.1f), layout.RectTransform), null);

        new GUITextBlock(new RectTransform(new Vector2(1f, 0f), row.RectTransform, Anchor.TopLeft, Pivot.TopLeft),
            TextManager.Get("halfduplex.emergency.description"), null, null, Alignment.CenterLeft);

        var row2 = new GUIFrame(new RectTransform(new Vector2(1f, 3f), layout.RectTransform), null);

        captureButton = new GUIButton(
            new RectTransform(new Vector2(1f, 1f), row2.RectTransform, Anchor.TopCenter, Pivot.TopCenter),
            EmergencyController.BoundKeyName, Alignment.Center, "GUITextBoxNoIcon")
        {
            OnClicked = (button, _) =>
            {
                capturing = true;
                skipNextFrame = true;
                button.Text = TextManager.Get("halfduplex.emergency.awaiting");
                return true;
            },
        };

        new GUICustomComponent(new RectTransform(Vector2.Zero, layout.RectTransform), null, OnUpdate);
    }

    /// <summary>捕获态下轮询键盘与鼠标，Escape 取消。</summary>
    private static void OnUpdate(float deltaTime, GUICustomComponent component)
    {
        if (!capturing) return;

        // 点击"进入捕获"的那一帧不参与判定，避免把这次鼠标点击当成绑定
        if (skipNextFrame)
        {
            skipNextFrame = false;
            return;
        }

        Keys[] pressed = PlayerInput.GetKeyboardState.GetPressedKeys();
        if (pressed != null && pressed.Length > 0)
        {
            if (pressed.Contains(Keys.Escape)) CancelCapture();
            else ApplyKey(new KeyOrMouse(pressed.First()));
            return;
        }

        if (PlayerInput.PrimaryMouseButtonClicked()) ApplyKey(new KeyOrMouse(MouseButton.PrimaryMouse));
        else if (PlayerInput.SecondaryMouseButtonClicked()) ApplyKey(new KeyOrMouse(MouseButton.SecondaryMouse));
        else if (PlayerInput.MidButtonClicked()) ApplyKey(new KeyOrMouse(MouseButton.MiddleMouse));
        else if (PlayerInput.Mouse4ButtonClicked()) ApplyKey(new KeyOrMouse(MouseButton.MouseButton4));
        else if (PlayerInput.Mouse5ButtonClicked()) ApplyKey(new KeyOrMouse(MouseButton.MouseButton5));
        else if (PlayerInput.MouseWheelUpClicked()) ApplyKey(new KeyOrMouse(MouseButton.MouseWheelUp));
        else if (PlayerInput.MouseWheelDownClicked()) ApplyKey(new KeyOrMouse(MouseButton.MouseWheelDown));
    }

    private static void ApplyKey(KeyOrMouse key)
    {
        if (key.Key == Keys.None && key.MouseButton == MouseButton.None)
        {
            CancelCapture();
            return;
        }

        setting?.Set(key.ToString());
        SaveSettings();
        EmergencyController.Rebind(key);

        capturing = false;
        RefreshButtonText();
    }

    /// <summary>立即写盘。游戏的设置不会自动保存，不落盘时重开设置页会被文件里的旧值覆盖。</summary>
    private static void SaveSettings()
    {
        try { service?.Save(); }
        catch (Exception e) { Plugin.LogError("Failed to save emergency key setting: " + e.Message); }
    }

    private static void CancelCapture()
    {
        capturing = false;
        RefreshButtonText();
    }

    private static void RefreshButtonText()
    {
        if (captureButton != null) captureButton.Text = EmergencyController.BoundKeyName;
    }

    // ============================================================
    //  解析
    // ============================================================

    private static bool TryParse(string? raw, out KeyOrMouse key)
    {
        key = new KeyOrMouse(Keys.P);

        if (string.IsNullOrWhiteSpace(raw)) return false;

        string text = raw.Trim();

        if (Enum.TryParse(text, ignoreCase: true, out Keys parsedKey) && parsedKey != Keys.None)
        {
            key = new KeyOrMouse(parsedKey);
            return true;
        }

        if (Enum.TryParse(text, ignoreCase: true, out MouseButton parsedButton) && parsedButton != MouseButton.None)
        {
            key = new KeyOrMouse(parsedButton);
            return true;
        }

        return false;
    }
}
