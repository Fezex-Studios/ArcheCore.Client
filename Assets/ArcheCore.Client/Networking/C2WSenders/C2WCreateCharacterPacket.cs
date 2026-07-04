using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2W
{
    public static class C2WCreateCharacterPacket
    {
        public static void Send(NetPeer peer, string name)
        {
            ClientPacketSender.SendPacket(
                peer,
                Opcodes.C2WCreateCharacterRequest,
                new C2WCreateCharacterRequest { Name = name });
        }
    }
}