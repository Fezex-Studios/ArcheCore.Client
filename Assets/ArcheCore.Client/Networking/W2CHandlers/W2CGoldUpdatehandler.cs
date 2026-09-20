using ArcheCore.Client.UI.Events;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Mirrors W2CPlayerlevelResponseHandler exactly - deserialize, raise
    /// the event, done. No UI code here; PlayerGoldDisplay and anything
    /// else that cares subscribes to PlayerStatEvents.OnGoldChanged
    /// instead of this handler knowing about any of them.
    /// </summary>
    public class W2CGoldUpdatehandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CGoldUpdatePacket>(reader.GetRemainingBytes());
            PlayerStatEvents.RaiseGoldChanged(packet.Gold);
        }
    }
}