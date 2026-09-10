using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2W
{
    public static class C2WChatMessagePacketSender
    {
        public static void Send(NetPeer peer, string message, ChatChannel channel, string targetName = null)
        {
            ClientPacketSender.SendPacket(
                peer,
                Opcodes.ChatMessage,
                new C2WChatMessagePacket
                {
                    Message    = message,
                    Channel    = channel,
                    TargetName = targetName
                });
        }
    }
}