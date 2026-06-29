using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CPlayerLeaveHandler
        : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CPlayerLeavePacket packet =
                MessagePackSerializer
                    .Deserialize<W2CPlayerLeavePacket>(
                        reader.GetRemainingBytes());

            PlayerRegistry.Instance
                ?.Despawn(packet.NetworkId);
        }
    }
}