using System;

namespace ArcheCore.Client.UI
{
    public static class PlayerUIEvents
    {
        public static event Action<int> OnLevelChanged;
        public static void RaiseLevelChanged(int level) => OnLevelChanged?.Invoke(level);

        // future stats slot in the same way, no manager growth needed:
        // public static event Action<int> OnHealthChanged;
        // public static event Action<int> OnGoldChanged;
    }
}