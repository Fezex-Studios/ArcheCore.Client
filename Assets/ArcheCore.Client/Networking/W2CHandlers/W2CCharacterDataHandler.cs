using ArcheCore.Client.UI;
using ArcheCore.Client.UI.Events;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CCharacterDataHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var data = MessagePackSerializer.Deserialize<CharacterData>(reader.GetRemainingBytes());
            PlayerStatEvents.RaiseCharacterDataChanged(data);
        }
    }
}