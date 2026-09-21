using ArcheCore.Client.UI.State;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Opcode 35. No longer sent at spawn - W2CEnterWorld carries the
    /// initial inventory now - but kept registered for a manual resync
    /// (a "/refresh" command, or a future bag-size change) where the
    /// server wants to overwrite the client's whole picture at once.
    /// </summary>
    public class W2CInventorySnapshotHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CInventorySnapshotPacket>(reader.GetRemainingBytes());
            LocalCharacterState.ApplyInventorySnapshot(packet.Slots);
        }
    }

    /// <summary>
    /// Opcode 36. One slot. Goes through LocalCharacterState so the
    /// cached array is written through as well as the event raised -
    /// otherwise the cache would go stale the first time a slot changed
    /// while the panel was closed, and the next open would show
    /// pre-change contents.
    /// </summary>
    public class W2CInventorySlotChangedHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CInventorySlotChangedPacket>(reader.GetRemainingBytes());
            LocalCharacterState.ApplySlotChanged(packet.Index, packet.ItemTemplateId, packet.Quantity);
        }
    }
}