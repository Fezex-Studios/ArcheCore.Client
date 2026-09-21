using ArcheCore.Client.UI.State;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Deserialize, hand to LocalCharacterState, done. No UI code here;
    /// PlayerGoldDisplay and anything else that cares subscribes to
    /// PlayerStatEvents.OnGoldChanged (which the state class raises)
    /// instead of this handler knowing about any of them.
    ///
    /// Goes through the state class rather than raising the event
    /// directly so that a panel opened later reads the current balance
    /// rather than whatever it last happened to be subscribed for.
    /// </summary>
    public class W2CGoldUpdatehandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CGoldUpdatePacket>(reader.GetRemainingBytes());
            LocalCharacterState.ApplyGold(packet.Gold);
        }
    }
}