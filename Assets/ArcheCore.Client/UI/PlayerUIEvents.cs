using System;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;

namespace ArcheCore.Client.UI
{
    public static class PlayerUIEvents
    {
        public static event Action<int> OnLevelChanged;
        public static void RaiseLevelChanged(int level) => OnLevelChanged?.Invoke(level);

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