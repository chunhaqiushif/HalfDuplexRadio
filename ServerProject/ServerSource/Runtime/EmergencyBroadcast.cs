using System.Collections.Generic;
using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace HalfDuplexRadio;

/// <summary>
/// 服务端：紧急线路广播。
///
/// 客户端按住紧急键时上报 Active，服务端记入集合；紧急发言者的语音跳过频道与队伍判定，
/// 其余限制（静音、干扰、距离）保留。
/// </summary>
internal static partial class PluginCore
{
    /// <summary>处于紧急发言中的客户端。</summary>
    private static readonly Dictionary<Client, float> EmergencySpeakers = new();

    private static void ClearEmergencySpeakers() => EmergencySpeakers.Clear();

    /// <summary>客户端上报紧急发言状态。Active 为 false 时立即撤销。</summary>
    internal static void SetEmergencySpeaker(Client? client, bool active)
    {
        if (client == null) return;

        if (active) EmergencySpeakers[client] = 0f;
        else EmergencySpeakers.Remove(client);
    }

    /// <summary>该客户端当前是否在紧急发言。已断线的客户端由 PruneEmergencySpeakers 清掉。</summary>
    private static bool IsEmergencySpeaker(Client client)
        => EmergencySpeakers.ContainsKey(client);

    /// <summary>清理已经断线、来不及上报撤销的客户端。</summary>
    private static void PruneEmergencySpeakers()
    {
        if (EmergencySpeakers.Count == 0) return;

        IReadOnlyList<Client>? connected = GameMain.Server?.ConnectedClients;
        if (connected == null)
        {
            EmergencySpeakers.Clear();
            return;
        }

        List<Client>? stale = null;
        foreach (var pair in EmergencySpeakers)
        {
            if (connected.Contains(pair.Key)) continue;
            (stale ??= new List<Client>()).Add(pair.Key);
        }
        if (stale == null) return;

        foreach (Client client in stale) EmergencySpeakers.Remove(client);
    }

    /// <summary>
    /// 紧急发言者的语音能否被某个接收者听到。
    /// 返回 null 表示"不是紧急发言者，交由原版逻辑"；否则为最终判定，并给出距离系数与是否无线电。
    ///
    /// 跳过：频道、队伍。
    /// 保留：静音、语言障碍、接收方电台被干扰或缺电、距离。
    /// </summary>
    internal static bool? EvaluateEmergencyReceive(Client sender, Client recipient, out float distanceFactor, out bool isRadio)
    {
        distanceFactor = 0f;
        isRadio = false;

        if (!IsEmergencySpeaker(sender)) return null;

        if (sender.Muted) return false;

        Character? senderCharacter = sender.Character;
        if (senderCharacter == null || senderCharacter.IsDead) return null;

        if (senderCharacter.SpeechImpediment >= 100f) return false;

        Character? recipientCharacter = recipient.Character;
        if (recipientCharacter == null || recipientCharacter.IsDead)
        {
            isRadio = true;
            return true;
        }

        // 接收方必须有可用的语音电台；电台被干扰或缺电时收不到
        if (!ChatMessage.CanUseRadio(recipientCharacter, out WifiComponent recipientRadio)) return false;
        if (!recipientRadio.CanTransmit(false)) return false;

        if (!ChatMessage.CanUseRadio(senderCharacter, out WifiComponent senderRadio)) return false;

        float distance = Vector2.Distance(recipientRadio.Item.WorldPosition, senderRadio.Item.WorldPosition);
        if (distance > senderRadio.Range) return false;

        distanceFactor = MathHelper.Clamp(distance / senderRadio.Range, 0f, 1f);
        isRadio = true;
        return true;
    }
}
