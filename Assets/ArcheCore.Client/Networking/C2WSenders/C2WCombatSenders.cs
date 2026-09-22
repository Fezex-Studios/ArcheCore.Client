using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    /// <summary>No damage or position is sent - the server decides everything.</summary>
    public static class C2WAttackPacketSender
    {
        public static void Send(NetPeer peer, int targetNetworkId, int skillId)
        {
            if (peer == null) return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WAttack,
                new C2WAttackPacket { TargetNetworkId = targetNetworkId, SkillId = skillId });
        }
    }
}
