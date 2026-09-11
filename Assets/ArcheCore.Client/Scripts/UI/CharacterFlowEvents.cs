using System;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;

namespace ArcheCore.Client.UI
{
    // Character-flow-only events: roster arriving, character chosen and
    // spawned into the world. Subscribed to by LoginScreenManager.
    public static class CharacterFlowEvents
    {
        public static event Action OnCharacterSpawned;
        public static void RaiseCharacterSpawned() => OnCharacterSpawned?.Invoke();

        // Fired once per authentication, with the account's full roster.
        // Empty array means "no characters" — LoginScreenManager decides
        // whether that means show Create or show Select.
        public static event Action<CharacterSummary[]> OnCharacterListReceived;
        public static void RaiseCharacterListReceived(CharacterSummary[] characters) =>
            OnCharacterListReceived?.Invoke(characters);
    }
}