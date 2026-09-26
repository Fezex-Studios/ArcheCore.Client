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
        
        // Deliberately its own event, not folded into OnCharacterDataChanged.
        // Gold changes far more often than level or name - every shop
        // purchase, every loot pickup - and every listener that only cares
        // about the name label would otherwise re-run on every gold tick
        // for no reason. Separate events mean separate, minimal work per
        // packet type, matching how OnLevelChanged is already split out
        // from OnCharacterDataChanged for the same reason.
        public static event Action<int> OnGoldChanged;
        public static void RaiseGoldChanged(int gold) => OnGoldChanged?.Invoke(gold);

        /// <summary>(health, maxHealth) - your own, not your target's.</summary>
        public static event Action<int, int> OnHealthChanged;
        public static void RaiseHealthChanged(int health, int maxHealth) => OnHealthChanged?.Invoke(health, maxHealth);

        // ── Phase 3 ──

        /// <summary>(mana, maxMana) - your own.</summary>
        public static event Action<int, int> OnManaChanged;
        public static void RaiseManaChanged(int mana, int maxMana) => OnManaChanged?.Invoke(mana, maxMana);

        /// <summary>Your stat sheet changed (W2CStats). Read LocalCharacterState.Stat().</summary>
        public static event Action OnStatsChanged;
        public static void RaiseStatsChanged() => OnStatsChanged?.Invoke();

        /// <summary>What you're wearing changed (W2CEquipment).</summary>
        public static event Action OnEquipmentChanged;
        public static void RaiseEquipmentChanged() => OnEquipmentChanged?.Invoke();

    }
}