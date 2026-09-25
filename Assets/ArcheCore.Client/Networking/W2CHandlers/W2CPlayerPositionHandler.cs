using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

using UnityEngine;
using ArcheCore.Client.World;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CPlayerPositionHandler
        : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CPlayerPositionPacket packet =
                MessagePackSerializer
                    .Deserialize<W2CPlayerPositionPacket>(
                        reader.GetRemainingBytes());

            if (packet.NetworkId ==
                ClientNetwork.Instance.LocalNetworkId)
            {
                return;
            }

            PlayerRegistry.Instance
                ?.UpdatePosition(
                    packet.NetworkId,
                    WorldOrigin.ToLocal(
                        packet.x,
                        packet.y,
                        packet.z));
        }
    }
}