using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.PersistenceServer;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>Opcode 70. Everything waiting in your mailbox.</summary>
    public class W2CMailListHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CMailListPacket>(reader.GetRemainingBytes());
            WorldLoader.RunWhenReady(() => MailWindowUI.Present(packet));
        }
    }

    /// <summary>Opcode 72. Auction listings.</summary>
    public class W2CAuctionListHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CAuctionListPacket>(reader.GetRemainingBytes());
            WorldLoader.RunWhenReady(() => AuctionWindowUI.Present(packet));
        }
    }

    /// <summary>Opcode 79. The cash shop, and what this account can spend.</summary>
    public class W2CCashShopListHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CCashShopListPacket>(reader.GetRemainingBytes());
            WorldLoader.RunWhenReady(() => CashShopWindowUI.Present(packet));
        }
    }

    /// <summary>
    /// Opcode 77. What happened, in words, for all three windows. After a
    /// change it asks the right window to reload rather than patching its
    /// list locally - which can't drift.
    /// </summary>
    public class W2CMarketResultHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CMarketResultPacket>(reader.GetRemainingBytes());

            WorldLoader.RunWhenReady(() =>
            {
                if (!string.IsNullOrEmpty(packet.Message))
                    HudMessageDisplay.QueueOrShow(packet.Message);

                switch (packet.Refresh)
                {
                    case 1: AuctionWindowUI.RefreshIfOpen(); break;
                    case 2: CashShopWindowUI.RefreshIfOpen(); break;
                    case 3: MailWindowUI.RefreshIfOpen(); break;
                }
            });
        }
    }
}
