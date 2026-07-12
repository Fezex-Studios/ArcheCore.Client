using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    // NOTE: placeholder - see W2CInteractLootPacket. Once inventory exists,
    // this should call into an InventoryController instead of just showing
    // a message.
    public class W2CInteractLootHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer
                .Deserialize<W2CInteractLootPacket>(reader.GetRemainingBytes());

            HudMessageDisplay.QueueOrShow($"You found: {packet.ItemName}");
        }
    }
}