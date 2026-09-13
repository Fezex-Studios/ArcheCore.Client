using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2W
{
    public static class C2WLevelUpPacketSender
    {
        public static void Send(NetPeer peer)
        {
            ClientPacketSender.SendPacket(
                peer,
                Opcodes.LevelUp,
                new AdminC2WLevelUpPacket());
        }
    }
}