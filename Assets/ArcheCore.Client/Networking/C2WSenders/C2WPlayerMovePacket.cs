using LiteNetLib;
using MMO.Shared.Packets;
using Shared;
using UnityEngine;

namespace ArcheCore.Client.Networking.C2W
{
    public static class C2WPlayerMovePacket
    {
        public static void Send(
            NetPeer peer,
            Vector3 position)
        {
            if (peer == null)
                return;

            PacketSender.SendPacket(
                peer,
                Opcode.PlayerMove,
                new MMO.Shared.Packets.C2WPlayerMovePacket
                {
                    x = position.x,
                    y = position.y,
                    z = position.z
                },
                DeliveryMethod.Unreliable);
        }
    }
}