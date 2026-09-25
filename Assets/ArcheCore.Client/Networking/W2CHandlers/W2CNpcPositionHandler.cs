using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;
using ArcheCore.Client.World;

namespace ArcheCore.Client.Networking.W2C
{
    // Mirrors W2CPlayerPositionHandler - no "is this me" check needed since
    // NPCs never have a local player to exclude.
    public class W2CNpcPositionHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CNpcPositionPacket packet =
                MessagePackSerializer
                    .Deserialize<W2CNpcPositionPacket>(
                        reader.GetRemainingBytes());

            NpcRegistry.Instance
                ?.UpdatePosition(
                    packet.NetworkId,
                    WorldOrigin.ToLocal(packet.x, packet.y, packet.z));
        }
    }
}