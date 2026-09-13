using System;

namespace ArcheCore.Client.UI
{
    // Player-stat events (level, and future stats like HP/mana/XP).
    // Subscribed to by PlayerLevelDisplay and similar HUD readouts.
    public static class AdminEvents
    {
        public static event Action<int> OnLevelChanged;
        public static void RaiseLevelChanged(int level) => OnLevelChanged?.Invoke(level);
    }
}