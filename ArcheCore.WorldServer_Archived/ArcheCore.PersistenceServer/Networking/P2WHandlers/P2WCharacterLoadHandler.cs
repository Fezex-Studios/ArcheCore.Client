using MessagePack;
using UnityEngine;
using Worldserver.ArcheCore.PersistenceServer.Scripts;

namespace ArcheCore.WorldServer.PersistenceServer.Networking.P2W
{
    public class P2WCharacterLoadHandler : IPersistencePacketHandler
    {
        private readonly PersistenceClient persistenceClient;

        public P2WCharacterLoadHandler(PersistenceClient persistenceClient)
        {
            this.persistenceClient = persistenceClient;
        }

        public void Handle(PersistencePacket persistencePacket)
        {
            var character =
                MessagePackSerializer
                    .Deserialize<P2WCharacterLoadResponse>(persistencePacket.Payload);

            Debug.Log($"[CharacterLoadHandler] Loaded CharacterId={character.CharacterId} Name={character.Name} Level={character.Level} Pos=({character.X}, {character.Y}, {character.Z})");

            persistenceClient.ResolveLoad(character);
        }
    }
}