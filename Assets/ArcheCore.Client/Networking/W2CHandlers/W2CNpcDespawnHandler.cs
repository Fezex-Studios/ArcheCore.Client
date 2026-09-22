using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// "This non-player entity left your view." The server sends it for
    /// ANY non-player id - NPCs and harvest nodes share one id range and
    /// one interest grid - so check every registry. An id belongs to at
    /// most one, and a miss everywhere is harmless.
    /// </summary>
    public class W2CNpcDespawnHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CNpcDespawnPacket packet =
                MessagePackSerializer.Deserialize<W2CNpcDespawnPacket>(reader.GetRemainingBytes());

            int networkId = packet.NetworkId;

            // Same ordering guarantee as player leave: never runs before the
            // matching spawn that arrived earlier.
            WorldLoader.RunWhenReady(() =>
            {
                if (HarvestNodeRegistry.Despawn(networkId))
                    return;

                NpcRegistry.Instance?.Despawn(networkId);
            });
        }
    }
}
