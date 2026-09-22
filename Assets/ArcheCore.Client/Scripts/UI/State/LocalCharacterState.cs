using System.Collections.Generic;
using ArcheCore.Client.UI.Events;
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
            ArcheCore.Client.Gameplay.Combat.CombatClient.Reset();
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
        public static void ApplySlotChanged(int index, int itemTemplateId, int quantity)
        {
            if (Inventory != null && index >= 0 && index < Inventory.Length)
            {
                Inventory[index] ??= new InventorySlotData();
                Inventory[index].Index = index;
                Inventory[index].ItemTemplateId = itemTemplateId;
                Inventory[index].Quantity = quantity;
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