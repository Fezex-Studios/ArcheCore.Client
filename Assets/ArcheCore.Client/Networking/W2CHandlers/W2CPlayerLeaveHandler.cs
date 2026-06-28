using ArcheCore.Client.Gameplay;
using LiteNetLib;
using MessagePack;
using Shared.Components;

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