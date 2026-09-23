using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Opcode 61. An NPC's health changed with no hit involved - it healed
    /// after leashing home. Keeps nameplates and the target frame honest,
    /// which otherwise show it hurt until someone hits it again.
    /// </summary>
    public class W2CNpcHealthHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CNpcHealthPacket>(reader.GetRemainingBytes());

            WorldLoader.RunWhenReady(() =>
            {
                if (NpcRegistry.Instance != null &&
                    NpcRegistry.Instance.TryGetNpc(packet.NetworkId, out var npc) && npc != null)
                {
                    npc.Health = packet.Health;
                    npc.MaxHealth = packet.MaxHealth;
                }
            });
        }
    }
}
