using System;
using Barotrauma;
using Barotrauma.Sounds;

namespace HalfDuplexRadio;

/// <summary>
/// 提示音播放。音效 tag 定义在 PluginCore.Sound*（见 Runtime/GameIds.cs）。
/// </summary>
internal static class VoiceSounds
{
    internal static void Play(string tag)
    {
        try { SoundPlayer.PlaySound(tag, 1f); }
        catch (Exception e) { Plugin.LogError("Failed to play voice cue: " + e.Message); }
    }

    /// <summary>播放循环音效，生命周期由调用方负责。</summary>
    internal static SoundChannel? PlayLoop(string tag)
    {
        try
        {
            SoundChannel? channel = SoundPlayer.PlaySound(tag, 1f);
            if (channel != null) channel.Looping = true;
            return channel;
        }
        catch (Exception e)
        {
            Plugin.LogError("Failed to play looping voice cue: " + e.Message);
            return null;
        }
    }
}
