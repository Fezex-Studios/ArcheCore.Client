using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>Opcode 48. You interacted with a merchant - open their window.</summary>
    public class W2CShopOpenHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CShopOpenPacket>(reader.GetRemainingBytes());

            if (ShopPanelUI.Instance == null)
            {
                Debug.LogWarning($"[Shop] No ShopPanelUI in the scene - can't show '{packet.ShopName}'.");
                return;
            }

            ShopPanelUI.Instance.Present(packet);
        }
    }

    /// <summary>
    /// Opcode 51. A buy or sell finished. Gold and items already updated
    /// through their own packets; this is only the message.
    /// </summary>
    public class W2CShopResultHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CShopResultPacket>(reader.GetRemainingBytes());

            if (!string.IsNullOrEmpty(packet.Message))
                HudMessageDisplay.QueueOrShow(packet.Message);
        }
    }
}
