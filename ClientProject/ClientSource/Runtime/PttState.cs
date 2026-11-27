using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace HalfDuplexRadio;

/// <summary>按键状态与语音模式判定。</summary>
internal static class PttState
{
    /// <summary>CanUseRadio 结果缓存：主线程写，音频线程只读。</summary>
    private static volatile bool radioAvailable;

    /// <summary>上一帧无线电键是否按下，用于识别按下瞬间。</summary>
    private static bool wasDown;

    /// <summary>最后一次按下无线电键的时刻，用于 VoiceChatCutoffPrevention 尾巴。</summary>
    private static float lastRadioKeyAt = float.MinValue;

    /// <summary>无线电键（语音聊天键或无线电键）当前是否按下。</summary>
    internal static bool RadioKeyDown
    {
        get
        {
            try { return PlayerInput.KeyDown(InputType.Voice) || PlayerInput.KeyDown(InputType.RadioVoice); }
            catch { return false; }
        }
    }

    /// <summary>身上是否有可用电台 / 耳机（含被干扰、没电判定）。</summary>
    private static bool RadioUsable
    {
        get
        {
            try
            {
                Character? character = Character.Controlled;
                return character != null && ChatMessage.CanUseRadio(character, out WifiComponent _);
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// 主线程每帧刷新：更新可用性缓存与按键上升沿。
    /// 返回本帧是否处于"按住无线电键且电台可用"状态。
    /// </summary>
    internal static bool Refresh(float now, out bool beganPress)
    {
        radioAvailable = RadioUsable;

        bool keyDown = RadioKeyDown || EmergencyController.KeyHeld;
        if (keyDown) lastRadioKeyAt = now;

        bool down = keyDown && radioAvailable;
        beganPress = down && !wasDown;
        wasDown = down;
        return down;
    }

    /// <summary>当前应使用的语音模式。由主线程调用，只读缓存，不遍历物品栏。</summary>
    internal static ChatMode CurrentMode(float now)
    {
        try
        {
            if (RadioKeyDown || EmergencyController.KeyHeld)
            {
                lastRadioKeyAt = now;
                return radioAvailable ? ChatMode.Radio : ChatMode.Local;
            }

            // 防切断尾巴：松手后 VoiceChatCutoffPrevention 期间继续算无线电
            float hold = GameSettings.CurrentConfig.Audio.VoiceChatCutoffPrevention / 1000f;
            if (hold > 0f && now - lastRadioKeyAt < hold + PluginCore.RadioKeyTailPadding)
            {
                return radioAvailable ? ChatMode.Radio : ChatMode.Local;
            }

            return ChatMode.Local;
        }
        catch { return ChatMode.Local; }
    }

    internal static void Reset()
    {
        radioAvailable = false;
        wasDown = false;
        lastRadioKeyAt = float.MinValue;
    }
}
