using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    public static class C2WInteractPacketSender
    {
        /// <summary>actionType 0 = the target's first (F) action.</summary>
        public static void Send(NetPeer peer, int targetNetworkId, int actionType = 0)
        {
            if (peer == null)
                return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.Interact,
                new C2WInteractPacket
                {
                    TargetNetworkId = targetNetworkId,
                    ActionType = actionType
                },
                DeliveryMethod.ReliableOrdered);
        }
    }
}