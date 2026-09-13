using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    public class C2WItemRequestDataPacketSender
    {
        public static void Send(NetPeer peer, int itemId)
        {
            ClientPacketSender.SendPacket(peer,Opcodes.ItemRequestData,
                new C2WItemRequestDataPacket{ItemId = itemId});
        }
    }
}