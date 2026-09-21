using ArcheCore.Client.UI.State;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Opcode 42. Caches the cooldown in LocalCharacterState; every
    /// InventorySlotUI holding one of the listed item ids reads it each
    /// frame to draw its sweep. No event - slots poll, so a slot that
    /// didn't exist when this arrived still shows the right remaining time.
    /// </summary>
    public class W2CItemCooldownHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CItemCooldownPacket>(reader.GetRemainingBytes());
            LocalCharacterState.ApplyItemCooldown(packet.ItemTemplateIds, packet.DurationMs);
        }
    }
}