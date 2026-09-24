using ArcheCore.Client.Gameplay.Mounts;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Opcode 69. Someone got on or off a mount: swap the model, and if it's
    /// you, change how fast you move.
    /// </summary>
    public class W2CMountStateHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CMountStatePacket>(reader.GetRemainingBytes());

            WorldLoader.RunWhenReady(() =>
                MountVisuals.Apply(packet.PlayerNetworkId, packet.MountId == 0 ? null : packet.ModelType, packet.SpeedMultiplier));
        }
    }
}
