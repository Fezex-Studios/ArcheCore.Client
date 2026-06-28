using LiteNetLib;
using MMO.Shared;
using MMO.Shared.Packets;
using Shared;

namespace ArcheCore.Client.Networking.C2W
{
    public static class C2WAuthenticatePacket
    {
        public static void Send(
            NetPeer peer,
            string token)
        {
            PacketSender.SendPacket(
                peer,
                Opcode.Authenticate,
                new C2WAuthenticateRequest
                {
                    Token = token
                });
        }
    }
}