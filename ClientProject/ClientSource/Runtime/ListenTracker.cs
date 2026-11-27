using System.Collections.Generic;
using Barotrauma.Networking;

namespace HalfDuplexRadio;

/// <summary>跟踪正在通过无线电说话的玩家，用于收听开始 / 结束提示音。</summary>
internal static class ListenTracker
{
    /// <summary>客户端 → 最后一次收到其语音包的时刻。</summary>
    private static readonly Dictionary<Client, float> Heard = new();

    /// <summary>收到无线电语音包。返回 true 表示这是本次说话的第一个包。</summary>
    internal static bool OnRadioPacket(Client client, float now)
    {
        bool begins = !Heard.ContainsKey(client);
        Heard[client] = now;
        return begins;
    }

    /// <summary>清理已经停止说话的玩家。返回 true 表示有人结束（播一次结束音）。</summary>
    internal static bool Tick(float now)
    {
        if (Heard.Count == 0) return false;

        List<Client>? expired = null;
        foreach (var pair in Heard)
        {
            if (now - pair.Value > PluginCore.ListenTimeout) (expired ??= new List<Client>()).Add(pair.Key);
        }
        if (expired == null) return false;

        foreach (Client client in expired) Heard.Remove(client);
        return true;
    }

    internal static void Reset() => Heard.Clear();
}
