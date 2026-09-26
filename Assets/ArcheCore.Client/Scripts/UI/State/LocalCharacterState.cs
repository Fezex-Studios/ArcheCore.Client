using System;
using System.Collections.Generic;
using ArcheCore.Client.UI.Events;
using ArcheCore.Network.Shared.Combat;
using ArcheCore.Network.Shared.Packets.W2C;
using UnityEngine;

namespace ArcheCore.Client.UI.State
{
    /// <summary>
    /// Client-side mirror of the local character: name, level, gold,
    /// inventory, item cooldowns. Packet handlers WRITE here; UI READS here.
    ///
    /// Exists because W2CEnterWorld arrives in the same burst as
    /// W2CSpawnPlayer - while main_world is still loading, before any HUD
    /// object exists to have subscribed. Caching here means a panel that
    /// wakes up later, or is toggled open ten minutes later, still reads
    /// the truth. Events are still raised for panels already alive.
    ///
    /// Static, not a MonoBehaviour: it has to survive the
    /// server_select -> main_world scene load.
    /// </summary>
    public static class LocalCharacterState
    {
        /// <summary>False until W2CEnterWorld lands. Gold == 0 is ambiguous
        /// (broke, or not loaded yet?) - this isn't.</summary>
        public static bool HasEnteredWorld { get; private set; }

        public static CharacterData Character { get; private set; }
        public static int Gold { get; private set; }
        public static InventorySlotData[] Inventory { get; private set; }

        public static int Level => Character?.Level ?? 0;

        /// <summary>Your health. Arrives in W2CEnterWorld, then W2CHealthUpdate.</summary>
        public static int Health { get; private set; }
        public static int MaxHealth { get; private set; }

        // ── Phase 3 ──

        /// <summary>Your mana (W2CHealthUpdate).</summary>
        public static int Mana { get; private set; }
        public static int MaxMana { get; private set; }

        private static float[] _stats = Array.Empty<float>();
        private static float[] _baseStats = Array.Empty<float>();

        /// <summary>A stat from the last W2CStats; 0 before it arrives.</summary>
        public static float Stat(StatId stat) => (int)stat < _stats.Length ? _stats[(int)stat] : 0f;

        /// <summary>The same stat without gear or buffs.</summary>
        public static float BaseStat(StatId stat) => (int)stat < _baseStats.Length ? _baseStats[(int)stat] : 0f;

        public static bool HasStats => _stats.Length > 0;

        private static readonly Dictionary<int, EquipmentSlotData> _equipment = new Dictionary<int, EquipmentSlotData>();

        /// <summary>What's in an equipment slot (EquipSlot), or null.</summary>
        public static EquipmentSlotData Worn(int slot) => _equipment.TryGetValue(slot, out var e) ? e : null;
        public static string Name => Character?.Name ?? string.Empty;

        // itemTemplateId -> when its cooldown ends / how long it was.
        // Realtime (not scaled game time) so pausing or slow-mo can't
        // desync the sweep from the server's clock.
        private static readonly Dictionary<int, (double endsAt, double duration)> _cooldowns = new();

        /// <summary>Called once from ClientNetwork.Awake.</summary>
        public static void Init() => Reset();

        /// <summary>Called on connect, disconnect and OnDestroy, so a relog
        /// never shows the previous character's state.</summary>
        public static void Reset()
        {
            Character = null;
            Gold = 0;
            Inventory = null;
            HasEnteredWorld = false;
            _cooldowns.Clear();
            Health = 0;
            MaxHealth = 0;
            Mana = 0;
            MaxMana = 0;
            _stats = Array.Empty<float>();
            _baseStats = Array.Empty<float>();
            _equipment.Clear();
            ArcheCore.Client.Gameplay.Combat.CombatClient.Reset();
            ArcheCore.Client.Gameplay.Combat.SkillBook.Reset();
            ArcheCore.Client.Gameplay.Statuses.StatusState.Reset();
            ArcheCore.Client.GameData.GearCatalog.Reset();
        }

        // ── Writes (packet handlers only) ─────────────────────────────

        /// <summary>W2CEnterWorld: the full initial picture.</summary>
        public static void ApplyEnterWorld(CharacterData character, int gold, InventorySlotData[] inventory,
                                           int health = 0, int maxHealth = 0)
        {
            Health = health;
            MaxHealth = maxHealth;
            PlayerStatEvents.RaiseHealthChanged(health, maxHealth);

            Character = character;
            Gold = gold;
            Inventory = inventory;
            HasEnteredWorld = true;

            PlayerStatEvents.RaiseCharacterDataChanged(character);
            PlayerStatEvents.RaiseGoldChanged(gold);
            PlayerInventoryEvents.RaiseInventorySnapshot(inventory);
        }

        /// <summary>W2CInventorySnapshot: manual resync, not sent at spawn.</summary>
        public static void ApplyInventorySnapshot(InventorySlotData[] inventory)
        {
            Inventory = inventory;
            PlayerInventoryEvents.RaiseInventorySnapshot(inventory);
        }

        /// <summary>
        /// W2CPlayerLevelResponse: the level changed after entering the
        /// world. Updates the cached CharacterData so a panel that opens
        /// later reads the new level, not the one from login.
        /// </summary>
        public static void ApplyLevel(int level)
        {
            if (Character != null)
                Character.Level = level;

            PlayerStatEvents.RaiseLevelChanged(level);
        }

        /// <summary>W2CHealthUpdate: absolute values, not a delta.</summary>
        public static void ApplyHealth(int health, int maxHealth)
        {
            Health = health;
            MaxHealth = maxHealth;
            PlayerStatEvents.RaiseHealthChanged(health, maxHealth);
        }

        /// <summary>W2CHealthUpdate's mana (0/0 from a server older than Phase 3 - ignored).</summary>
        public static void ApplyMana(int mana, int maxMana)
        {
            if (maxMana <= 0 && MaxMana <= 0)
                return;

            Mana = mana;
            MaxMana = maxMana;
            PlayerStatEvents.RaiseManaChanged(mana, maxMana);
        }

        /// <summary>W2CStats: the whole sheet.</summary>
        public static void ApplyStats(float[] values, float[] baseValues)
        {
            _stats = values ?? Array.Empty<float>();
            _baseStats = baseValues ?? Array.Empty<float>();
            PlayerStatEvents.RaiseStatsChanged();
        }

        /// <summary>W2CEquipment: everything worn, replacing what we had.</summary>
        public static void ApplyEquipment(EquipmentSlotData[] slots)
        {
            _equipment.Clear();
            if (slots != null)
                foreach (var s in slots)
                    if (s != null && s.ItemTemplateId > 0) _equipment[s.Slot] = s;
            PlayerStatEvents.RaiseEquipmentChanged();
        }

        /// <summary>W2CGoldUpdate: absolute balance, not a delta.</summary>
        public static void ApplyGold(int gold)
        {
            Gold = gold;
            PlayerStatEvents.RaiseGoldChanged(gold);
        }

        /// <summary>
        /// W2CInventorySlotChanged. Writes through to the cached array as
        /// well as raising the event, or the cache goes stale the first
        /// time a slot changes while the panel is closed.
        /// </summary>
        public static void ApplySlotChanged(int index, int itemTemplateId, int quantity, bool bound = false)
        {
            if (Inventory != null && index >= 0 && index < Inventory.Length)
            {
                Inventory[index] ??= new InventorySlotData();
                Inventory[index].Index = index;
                Inventory[index].ItemTemplateId = itemTemplateId;
                Inventory[index].Quantity = quantity;
                Inventory[index].Bound = bound;
            }

            PlayerInventoryEvents.RaiseSlotChanged(index, itemTemplateId, quantity);
        }

        /// <summary>W2CItemCooldown: every listed item is on cooldown now.</summary>
        public static void ApplyItemCooldown(int[] itemTemplateIds, int durationMs)
        {
            if (itemTemplateIds == null || durationMs <= 0)
                return;

            double duration = durationMs / 1000.0;
            double endsAt = Time.realtimeSinceStartupAsDouble + duration;

            foreach (int id in itemTemplateIds)
                _cooldowns[id] = (endsAt, duration);
        }

        // ── Reads (UI) ────────────────────────────────────────────────

        /// <summary>Is the item in this bag slot soulbound?</summary>
        public static bool IsBound(int index) =>
            Inventory != null && index >= 0 && index < Inventory.Length && Inventory[index] != null && Inventory[index].Bound;

        /// <summary>
        /// True while itemTemplateId is cooling down. remaining01 goes
        /// 1 -> 0 over the cooldown - feed it straight into a radial
        /// Image.fillAmount. Cosmetic only: the server rejects early use
        /// whatever this says.
        /// </summary>
        public static bool TryGetCooldown(int itemTemplateId, out float remaining01)
        {
            remaining01 = 0f;

            if (!_cooldowns.TryGetValue(itemTemplateId, out var cd))
                return false;

            double left = cd.endsAt - Time.realtimeSinceStartupAsDouble;
            if (left <= 0)
            {
                _cooldowns.Remove(itemTemplateId);
                return false;
            }

            remaining01 = (float)(left / cd.duration);
            return true;
        }
    }
}