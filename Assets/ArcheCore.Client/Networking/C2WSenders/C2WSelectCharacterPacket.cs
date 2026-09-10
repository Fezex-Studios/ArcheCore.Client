using ArcheCore.Library.Net.Worldserver;
using ArcheCore.Network.Shared.Packets.C2W;
using LiteNetLib;
using Shared;

namespace ArcheCore.Client.Networking.C2WSenders
{
    public class C2WSelectCharacterPacket
    {
        public static void Send(NetPeer peer, long characterId)
        {
            ClientPacketSender.SendPacket(peer, Opcodes.C2WSelectCharacter,
                new C2WSelectCharacterRequest { CharacterId = characterId });
        }
    }
}