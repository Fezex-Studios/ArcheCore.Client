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
        /// <param name="velocity">
        /// World units/second, as actually applied (CharacterController.velocity),
        /// not the requested input vector. Zero means stopped, and the
        /// server and other clients treat it as a positive statement to
        /// that effect rather than a missing value - so it matters that the
        /// caller sends a real zero when the character stops rather than
        /// simply going quiet.
        /// </param>
        /// <param name="yawRadians">
        /// Facing, in RADIANS. Unity is degrees everywhere else; the
        /// conversion happens at the call site because the wire format
        /// (EntityStateCodec.QuantizeYaw) is radians and doing it here
        /// would hide the unit change from the caller.
        /// </param>
        public static void Send(
            NetPeer peer,
            Vector3 position,
            Vector3 velocity,
            float yawRadians)
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
                    z = position.z,
                    vx = velocity.x,
                    vy = velocity.y,
                    vz = velocity.z,
                    yaw = yawRadians
                },
                DeliveryMethod.Unreliable);
        }
    }
}