using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    // Mirrors W2CPlayerLeaveHandler.
    public class W2CNpcDespawnHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CNpcDespawnPacket packet =
                MessagePackSerializer
                    .Deserialize<W2CNpcDespawnPacket>(
                        reader.GetRemainingBytes());

            int networkId = packet.NetworkId;

            // Same ordering guarantee as player leave: never runs before the
            // matching spawn that arrived earlier.
            WorldLoader.RunWhenReady(() =>
                NpcRegistry.Instance?.Despawn(networkId));
        }
    }
}