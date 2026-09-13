using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CItemDataResponseHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer
                .Deserialize<W2CItemDataResponsePacket>(reader.GetRemainingBytes());

            if (!packet.Found)
            {
                Debug.Log($"[ItemDataResponse] Server has no item {packet.ItemId}");
                return;
            }

            Debug.Log(
                $"[ItemDataResponse] #{packet.ItemId} {packet.Name} " +
                $"(category {packet.CategoryId}): {packet.Description}");
        }
    }
}