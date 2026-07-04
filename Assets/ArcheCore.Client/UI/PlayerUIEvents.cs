using System;

namespace ArcheCore.Client.UI
{
    public static class PlayerUIEvents
    {
        public static event Action<int> OnLevelChanged;
        public static void RaiseLevelChanged(int level) => OnLevelChanged?.Invoke(level);

        public static event Action OnCharacterNotFound;
        public static event Action OnCharacterSpawned;

        public static void RaiseCharacterNotFound() => OnCharacterNotFound?.Invoke();
        public static void RaiseCharacterSpawned()  => OnCharacterSpawned?.Invoke();
    }
}