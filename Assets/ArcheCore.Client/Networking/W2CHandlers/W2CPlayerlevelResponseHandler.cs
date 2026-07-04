using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CPlayerlevelResponseHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CPlayerLevelResponsePacket>(reader.GetRemainingBytes());
            PlayerUIEvents.RaiseLevelChanged(packet.Level);

        }
    }
}