using System;
using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Plugins;

namespace HalfDuplexRadio;

public partial class Plugin
{
    /// <summary>
    /// 服务端：语音包拦截 + 紧急广播的接收判定。
    /// 每帧清理已交由 PluginOnPreUpdateDelegate 驱动。
    /// </summary>
    public partial void InitProjectSpecific()
    {
        GameHooks.Prefix("Barotrauma.Networking.VoipServer", "Read", nameof(VoipServerReadPrefix),
            typeof(IReadMessage), typeof(Client));

        GameHooks.Prefix("Barotrauma.Networking.VoipServer", "CanReceive", nameof(CanReceivePrefix),
            typeof(Client), typeof(Client), typeof(float).MakeByRefType(), typeof(bool).MakeByRefType());

        RegisterEmergencyHandler();
    }

    /// <summary>接收客户端上报的紧急发言状态。</summary>
    private static void RegisterEmergencyHandler()
    {
        try
        {
            PluginServiceProvider.GetService<IGameNetwork>()
                .RegisterHandler<EmergencyHeader, EmergencySpeakerState>(
                    EmergencyHeader.SpeakerState,
                    (data, client) => PluginCore.SetEmergencySpeaker(client, data.Active));
        }
        catch (Exception e)
        {
            Plugin.LogError("Failed to register emergency speaker handler: " + e.Message);
        }
    }

    private static bool VoipServerReadPrefix(IReadMessage inc, Client? connectedClient)
        => PluginCore.TryArbitrate(inc, connectedClient);

    /// <summary>紧急发言者的语音绕过原版的频道与队伍判定，其余限制保留。</summary>
    private static bool CanReceivePrefix(Client? sender, Client? recipient, ref float distanceFactor, ref bool isRadio)
    {
        if (sender == null || recipient == null) return true;

        bool? decision = PluginCore.EvaluateEmergencyReceive(sender, recipient, out float factor, out bool radio);
        if (decision == null) return true;

        distanceFactor = factor;
        isRadio = radio;
        return decision.Value;
    }
}
