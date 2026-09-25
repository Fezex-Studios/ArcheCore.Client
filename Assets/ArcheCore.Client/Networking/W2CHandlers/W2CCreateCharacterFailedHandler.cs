using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>The server refused a new character's name. The create screen stays up.</summary>
    public class W2CCreateCharacterFailedHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CCreateCharacterFailedPacket>(reader.GetRemainingBytes());
            CharacterFlowEvents.RaiseCreateCharacterFailed(packet.Reason ?? "");
        }
    }
}
