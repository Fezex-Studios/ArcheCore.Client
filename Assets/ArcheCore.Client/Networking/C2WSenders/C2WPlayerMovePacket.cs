using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using ArcheCore.Network.Worldserver;
using LiteNetLib;
using Shared;
using UnityEngine;

namespace ArcheCore.Client.Networking.C2WSenders
{
    public static class C2WPlayerMovePacketSender
    {
        public static void Send(
            NetPeer peer,
            Vector3 position)
        {
            if (peer == null)
                return;

            ClientPacketSender.SendPacket(
                peer,
                Opcodes.PlayerMove,
                new C2WPlayerMovePacket
                {
                    x = position.x,
                    y = position.y,
                    z = position.z
                },
                DeliveryMethod.Unreliable);
        }
    }
}