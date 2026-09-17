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

            int networkId = packet.NetworkId;

            // Queued behind any pending spawn for the same player, so a
            // spawn-then-leave during loading can't leave a ghost behind.
            WorldLoader.RunWhenReady(() =>
                PlayerRegistry.Instance?.Despawn(networkId));
        }
    }
}