using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    /// <summary>The mailbox.</summary>
    public static class C2WMailPacketSender
    {
        /// <summary>mailId 0 = take everything that fits, negative = just show me my mailbox.</summary>
        public static void Claim(NetPeer peer, long mailId)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WMailClaim, new C2WMailClaimPacket { MailId = mailId });
        }
    }

    /// <summary>
    /// Auction requests. Nothing here decides an outcome: the server takes
    /// the listing first and answers with what actually happened.
    /// </summary>
    public static class C2WAuctionPacketSender
    {
        public static void Browse(NetPeer peer, string search, bool mineOnly)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WAuctionBrowse,
                new C2WAuctionBrowsePacket { Search = search ?? string.Empty, MineOnly = mineOnly });
        }

        public static void Create(NetPeer peer, int slot, int quantity, int price)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WAuctionCreate,
                new C2WAuctionCreatePacket { Slot = slot, Quantity = quantity, Price = price });
        }

        public static void Buy(NetPeer peer, long auctionId)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WAuctionBuy, new C2WAuctionBuyPacket { AuctionId = auctionId });
        }

        public static void Cancel(NetPeer peer, long auctionId)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WAuctionCancel, new C2WAuctionCancelPacket { AuctionId = auctionId });
        }
    }

    /// <summary>Cash shop. Balances and deliveries are the persistence server's business.</summary>
    public static class C2WCashShopPacketSender
    {
        public static void Browse(NetPeer peer)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WCashShopBrowse, new C2WCashShopBrowsePacket());
        }

        public static void Buy(NetPeer peer, int cashShopItemId)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WCashShopBuy,
                new C2WCashShopBuyPacket { CashShopItemId = cashShopItemId });
        }

        /// <summary>Buy it for another character. The server decides whether that item can be gifted.</summary>
        public static void Gift(NetPeer peer, int cashShopItemId, string recipientName)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WCashShopGift,
                new C2WCashShopGiftPacket { CashShopItemId = cashShopItemId, RecipientName = recipientName ?? string.Empty });
        }
    }
}
