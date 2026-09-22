using ArcheCore.Client.UI.State;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    /// <summary>
    /// Level changed (level-up, or the admin panel's level request). Goes
    /// through LocalCharacterState so the cached level stays current -
    /// raising the event directly left the cache on the login level, so any
    /// panel opened after a level-up showed the old number.
    /// </summary>
    public class W2CPlayerlevelResponseHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            var packet = MessagePackSerializer.Deserialize<W2CPlayerLevelResponsePacket>(reader.GetRemainingBytes());
            LocalCharacterState.ApplyLevel(packet.Level);
        }
    }
}