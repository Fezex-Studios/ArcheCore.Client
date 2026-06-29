

using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using LiteNetLib;
using Shared;


namespace ArcheCore.Client.Networking.C2W
{
    public static class C2WAuthenticatePacket
    {
        public static void Send(
            NetPeer peer,
            string token)
        {
            ClientPacketSender.SendPacket(
                peer,
                Opcodes.Authenticate,
                new C2WAuthenticateRequest
                {
                    Token = token
                });
        }
    }
}