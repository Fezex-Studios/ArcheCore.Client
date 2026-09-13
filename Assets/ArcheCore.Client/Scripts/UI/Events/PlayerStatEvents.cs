using System;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.UI.Events
{
    public class PlayerStatEvents
    {
        public static event Action<CharacterData> OnCharacterDataChanged;
        public static void RaiseCharacterDataChanged(CharacterData data) => OnCharacterDataChanged?.Invoke(data);


        public static event Action<int> OnLevelChanged;
        public static void RaiseLevelChanged(int level) => OnLevelChanged?.Invoke(level);
    }
}