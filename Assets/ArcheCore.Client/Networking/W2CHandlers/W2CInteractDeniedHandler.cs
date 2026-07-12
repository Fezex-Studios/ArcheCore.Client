using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CInteractDeniedHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer
                .Deserialize<W2CInteractDeniedPacket>(reader.GetRemainingBytes());

            HudMessageDisplay.QueueOrShow(packet.Reason);
        }
    }
}