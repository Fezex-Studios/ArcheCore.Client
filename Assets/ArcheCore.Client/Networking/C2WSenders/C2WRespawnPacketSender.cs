using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    public static class C2WRespawnPacketSender
    {
        public static void Send(NetPeer peer)
        {
            if (peer == null) return;
            ClientPacketSender.SendPacket(peer, Opcodes.C2WRespawn, new C2WRespawnPacket());
        }
    }
}
