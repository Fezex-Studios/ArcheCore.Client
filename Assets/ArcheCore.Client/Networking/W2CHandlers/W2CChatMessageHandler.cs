using System;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CChatMessageHandler : IClientPacketHandler
    {
        public static event Action<W2CChatMessagePacket> OnChatMessageReceived;

        public void Handle(NetPacketReader reader)
        {
            W2CChatMessagePacket packet = MessagePackSerializer
                .Deserialize<W2CChatMessagePacket>(reader.GetRemainingBytes());

            OnChatMessageReceived?.Invoke(packet);
        }
    }
}