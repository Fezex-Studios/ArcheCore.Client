using LiteNetLib;
using MessagePack;
using Shared.Packets;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CAnnouncementHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CAnnouncementPacket packet = MessagePackSerializer
                .Deserialize<W2CAnnouncementPacket>(reader.GetRemainingBytes());
            
            Debug.Log($"Received announcement packet: {packet.Message}");
        }
    }
}