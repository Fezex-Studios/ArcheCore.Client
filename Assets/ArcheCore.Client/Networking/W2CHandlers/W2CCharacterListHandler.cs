using ArcheCore.Client.UI;
using ArcheCore.Network.Client;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using ArcheCore.Network.Shared.Packets.W2C;
using LiteNetLib;
using MessagePack;

namespace ArcheCore.Client.Networking.W2C
{
    public class W2CCharacterListHandler : IClientPacketHandler
    {
        public void Handle(NetPacketReader reader)
        {
            W2CCharacterListPacket packet =
                MessagePackSerializer
                    .Deserialize<W2CCharacterListPacket>(
                        reader.GetRemainingBytes());

            UnityEngine.Debug.Log(
                $"[W2CCharacterList] Received {packet.Characters?.Length ?? 0} character(s)");

            CharacterFlowEvents.RaiseCharacterListReceived(
                packet.Characters ?? System.Array.Empty<CharacterSummary>());
        }
    }
}