using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    public static class C2WInteractPacketSender
    {
        public static void Send(NetPeer peer, int targetNetworkId)
        {
            if (peer == null)
                return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.Interact,
                new C2WInteractPacket
                {
                    TargetNetworkId = targetNetworkId
                },
                DeliveryMethod.ReliableOrdered);
        }
    }
}