using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    /// <summary>No price is sent - the server prices every purchase itself.</summary>
    public static class C2WShopBuyPacketSender
    {
        public static void Send(NetPeer peer, int npcNetworkId, int itemTemplateId, int quantity)
        {
            if (peer == null) return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WShopBuy,
                new C2WShopBuyPacket
                {
                    NpcNetworkId   = npcNetworkId,
                    ItemTemplateId = itemTemplateId,
                    Quantity       = quantity
                });
        }
    }

    /// <summary>quantity &lt;= 0 sells the whole stack.</summary>
    public static class C2WShopSellPacketSender
    {
        public static void Send(NetPeer peer, int npcNetworkId, int slot, int quantity = 0)
        {
            if (peer == null) return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WShopSell,
                new C2WShopSellPacket { NpcNetworkId = npcNetworkId, Slot = slot, Quantity = quantity });
        }
    }
}
