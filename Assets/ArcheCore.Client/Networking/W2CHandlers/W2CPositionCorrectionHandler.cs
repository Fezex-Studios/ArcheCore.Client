using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;
using UnityEngine;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// The server rejected where we said we were and is putting us back.
    ///
    /// This is the only packet that moves the LOCAL player. Everything else
    /// on the receive path deliberately refuses to (the snapshot handler
    /// skips our own network id), because the local character is driven by
    /// local input and having the network fight it produces constant
    /// jitter. A correction is the exception and it is a hard snap, not a
    /// slide - if the server has stopped believing us, interpolating
    /// smoothly toward the truth over half a second just means half a
    /// second more of being somewhere the server says we aren't.
    ///
    /// If players see this fire during normal play, the validator is
    /// mistuned, not the client - MovementValidator's limits assume
    /// PlayerController's moveSpeed and jump height, and adding a mount, a
    /// sprint or a knockback without raising them turns every user of that
    /// feature into a cheater.
    /// </summary>
    public class W2CPositionCorrectionHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CPositionCorrectionPacket packet =
                MessagePackSerializer
                    .Deserialize<W2CPositionCorrectionPacket>(
                        reader.GetRemainingBytes());

            var local = ClientNetwork.Instance?.LocalPlayer;
            if (local == null)
                return;

            local.ForcePosition(new Vector3(packet.x, packet.y, packet.z));
        }
    }
}