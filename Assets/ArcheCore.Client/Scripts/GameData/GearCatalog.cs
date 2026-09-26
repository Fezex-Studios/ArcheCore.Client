using System;
using System.Collections.Generic;
using ArcheCore.Network.Shared.Combat;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// Which items are gear, where they go, what they give, and how they
    /// bind (roadmap 3.2). Filled once per login from W2CItemGearCatalog -
    /// gamedata.bin predates gear, so the server sends it the same way it
    /// sends the quest catalogue. Tooltips and the character window read it.
    /// Static, like LocalCharacterState.
    /// </summary>
    public static class GearCatalog
    {
        private static readonly Dictionary<int, ItemGearData> Items = new Dictionary<int, ItemGearData>();

        public static event Action OnChanged;

        public static void Set(ItemGearData[] items)
        {
            Items.Clear();
            if (items != null)
                foreach (var item in items)
                    if (item != null) Items[item.ItemId] = item;
            OnChanged?.Invoke();
        }

        public static void Reset() => Items.Clear();

        public static bool TryGet(int itemId, out ItemGearData gear) => Items.TryGetValue(itemId, out gear);

        /// <summary>Wearable (right-click equips it).</summary>
        public static bool IsGear(int itemId) => Items.TryGetValue(itemId, out var g) && g.EquipSlot > 0;

        public static string SlotName(int slot)
        {
            switch ((EquipSlot)slot)
            {
                case EquipSlot.Head:     return "Head";
                case EquipSlot.Chest:    return "Chest";
                case EquipSlot.Legs:     return "Legs";
                case EquipSlot.Hands:    return "Hands";
                case EquipSlot.Feet:     return "Feet";
                case EquipSlot.MainHand: return "Main Hand";
                case EquipSlot.OffHand:  return "Off Hand";
                case EquipSlot.Neck:     return "Neck";
                case EquipSlot.Ring:     return "Ring";
                case EquipSlot.Trinket:  return "Trinket";
                default:                 return "";
            }
        }

        public static string BindText(byte bindType)
        {
            switch ((BindType)bindType)
            {
                case BindType.BindOnPickup: return "Binds when picked up";
                case BindType.BindOnEquip:  return "Binds when equipped";
                default:                    return null;
            }
        }
    }

    /// <summary>Names and number formats for StatId, shared by tooltips and the character window.</summary>
    public static class StatNames
    {
        public static string Name(StatId stat)
        {
            switch (stat)
            {
                case StatId.Strength:       return "Strength";
                case StatId.Agility:        return "Agility";
                case StatId.Vitality:       return "Vitality";
                case StatId.Intelligence:   return "Intelligence";
                case StatId.Spirit:         return "Spirit";
                case StatId.MaxHealth:      return "Health";
                case StatId.MaxMana:        return "Mana";
                case StatId.AttackPower:    return "Attack Power";
                case StatId.SpellPower:     return "Spell Power";
                case StatId.Armor:          return "Armor";
                case StatId.CritChance:     return "Critical Strike";
                case StatId.Haste:          return "Haste";
                case StatId.HealthRegen:    return "Health Regen";
                case StatId.ManaRegen:      return "Mana Regen";
                case StatId.ResistFire:     return "Fire Resistance";
                case StatId.ResistFrost:    return "Frost Resistance";
                case StatId.ResistNature:   return "Nature Resistance";
                case StatId.ResistArcane:   return "Arcane Resistance";
                case StatId.ResistHoly:     return "Holy Resistance";
                case StatId.ResistShadow:   return "Shadow Resistance";
                case StatId.DamageDonePct:  return "Damage Done";
                case StatId.DamageTakenPct: return "Damage Taken";
                case StatId.HealingDonePct: return "Healing Done";
                case StatId.ThreatPct:      return "Threat";
                default:                    return stat.ToString();
            }
        }

        /// <summary>Shown as a percentage (crit, haste, the multipliers).</summary>
        public static bool IsPercent(StatId stat) =>
            stat == StatId.CritChance || stat == StatId.Haste ||
            stat == StatId.DamageDonePct || stat == StatId.DamageTakenPct ||
            stat == StatId.HealingDonePct || stat == StatId.ThreatPct;

        public static string Value(StatId stat, float value) =>
            IsPercent(stat) ? $"{value:0.#}%" : $"{Math.Round(value):0}";

        /// <summary>"+5 Strength", "+3% Critical Strike" - for gear tooltips.</summary>
        public static string Bonus(StatId stat, float amount)
        {
            string sign = amount >= 0 ? "+" : "-";
            float a = Math.Abs(amount);
            return IsPercent(stat) ? $"{sign}{a:0.#}% {Name(stat)}" : $"{sign}{a:0.#} {Name(stat)}";
        }
    }
}
