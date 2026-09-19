using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared;
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
        /// <param name="eulerDegrees">
        /// The character's full rotation in Unity's own units and order
        /// (x = pitch, y = yaw, z = roll, degrees). Converted to radians
        /// here because the wire format is radians throughout
        /// (EntityStateCodec), and doing it in one place beats three
        /// Mathf.Deg2Rad at the call site with one of them eventually
        /// missing.
        /// </param>
        public static void Send(
            NetPeer peer,
            Vector3 position,
            Vector3 velocity,
            Vector3 eulerDegrees,
            MovementState state)
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
                    yaw   = eulerDegrees.y * Mathf.Deg2Rad,
                    pitch = eulerDegrees.x * Mathf.Deg2Rad,
                    roll  = eulerDegrees.z * Mathf.Deg2Rad,
                    state = (byte)state
                },
                DeliveryMethod.Unreliable);
        }
    }
}