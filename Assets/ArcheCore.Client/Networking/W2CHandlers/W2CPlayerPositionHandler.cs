using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

using UnityEngine;

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
                    new Vector3(
                        packet.x,
                        packet.y,
                        packet.z));
        }
    }
}