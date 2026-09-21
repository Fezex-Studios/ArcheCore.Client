using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    public static class C2WMoveItemPacketSender
    {
        public static void Send(NetPeer peer, int fromSlot, int toSlot)
        {
            if (peer == null) return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WMoveItem,
                new C2WMoveItemPacket { FromSlot = fromSlot, ToSlot = toSlot });
        }
    }

    /// <summary>
    /// quantity &lt;= 0 destroys the whole stack. Only ever called after the
    /// player confirms ConfirmDialog - see InventoryPanelUI.AskDestroy.
    /// </summary>
    public static class C2WDropItemPacketSender
    {
        public static void Send(NetPeer peer, int slot, int quantity = 0)
        {
            if (peer == null) return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WDropItem,
                new C2WDropItemPacket { Slot = slot, Quantity = quantity });
        }
    }

    public static class C2WUseItemPacketSender
    {
        public static void Send(NetPeer peer, int slot)
        {
            if (peer == null) return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WUseItem,
                new C2WUseItemPacket { Slot = slot });
        }
    }

    /// <summary>DEV ONLY - see C2WDebugAddItemHandler server-side.</summary>
    public static class C2WDebugAddItemPacketSender
    {
        public static void Send(NetPeer peer, int itemTemplateId, int quantity)
        {
            if (peer == null) return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WDebugAddItem,
                new C2WDebugAddItemPacket { ItemTemplateId = itemTemplateId, Quantity = quantity });
        }
    }
}