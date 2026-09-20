using ArcheCore.Client.Gameplay;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Starts a locally simulated jump arc for a remote entity.
    ///
    /// Mirrors W2CNpcDespawnHandler's shape, including RunWhenReady: this
    /// packet is RELIABLE and can therefore land before the world is loaded
    /// or before the spawn it refers to has been processed. Running it
    /// early would look up a network id that doesn't exist yet and silently
    /// drop the jump.
    ///
    /// Tries players first, then NPCs. NPCs can't jump today - nothing in
    /// NpcAiManager sets the Jumping bit - but the interest set contains
    /// both and the packet carries no type flag, so handling both is one
    /// line and avoids a surprise the first time an NPC does.
    /// </summary>
    public class W2CJumpEventHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CJumpEventPacket packet =
                MessagePackSerializer
                    .Deserialize<W2CJumpEventPacket>(
                        reader.GetRemainingBytes());

            var origin = new Vector3(packet.OriginX, packet.OriginY, packet.OriginZ);
            var horizontal = new Vector3(packet.VelocityX, 0f, packet.VelocityZ);
            float vertical = packet.VelocityY;
            int networkId = packet.NetworkId;

            WorldLoader.RunWhenReady(() =>
            {
                var players = PlayerRegistry.Instance;

                if (players != null && players.BeginJump(networkId, origin, horizontal, vertical))
                    return;

                NpcRegistry.Instance?.BeginJump(networkId, origin, horizontal, vertical);
            });
        }
    }
}