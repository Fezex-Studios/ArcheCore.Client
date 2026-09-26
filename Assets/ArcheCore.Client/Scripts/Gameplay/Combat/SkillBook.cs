using System;
using System.Collections.Generic;
using ArcheCore.Network.Shared.Packets.W2C;
using UnityEngine;

namespace ArcheCore.Client.Gameplay.Combat
{
    /// <summary>
    /// Your skills (W2CSkillCatalog) and their cooldowns. The cooldown each
    /// skill really has comes back in the W2CCombatEvent it caused (already
    /// shortened by haste), so the keys grey out exactly as long as the
    /// server will refuse them. Cosmetic: the server checks for itself.
    /// </summary>
    public static class SkillBook
    {
        public const int SlotCount = 5;

        private static SkillData[] _skills = Array.Empty<SkillData>();
        private static readonly Dictionary<int, (double readyAt, double length)> Cooldowns = new Dictionary<int, (double, double)>();

        public static event Action OnChanged;

        public static IReadOnlyList<SkillData> Skills => _skills;

        public static void SetCatalog(SkillData[] skills)
        {
            _skills = skills ?? Array.Empty<SkillData>();
            OnChanged?.Invoke();
        }

        /// <summary>The skill on key `slot` (1-based), or null.</summary>
        public static SkillData BySlot(int slot)
        {
            foreach (var s in _skills)
                if (s != null && s.HotbarSlot == slot) return s;
            return null;
        }

        public static SkillData ById(int id)
        {
            foreach (var s in _skills)
                if (s != null && s.Id == id) return s;
            return null;
        }

        public static void StartCooldown(int skillId, int ms)
        {
            if (ms <= 0) return;
            double length = ms / 1000.0;
            Cooldowns[skillId] = (Time.realtimeSinceStartupAsDouble + length, length);
        }

        public static bool IsOnCooldown(int skillId) =>
            Cooldowns.TryGetValue(skillId, out var cd) && Time.realtimeSinceStartupAsDouble < cd.readyAt;

        /// <summary>1 -> 0 over the cooldown.</summary>
        public static float Remaining01(int skillId)
        {
            if (!Cooldowns.TryGetValue(skillId, out var cd) || cd.length <= 0) return 0f;
            double left = cd.readyAt - Time.realtimeSinceStartupAsDouble;
            return left <= 0 ? 0f : (float)(left / cd.length);
        }

        public static double RemainingSeconds(int skillId) =>
            Cooldowns.TryGetValue(skillId, out var cd) ? Math.Max(0, cd.readyAt - Time.realtimeSinceStartupAsDouble) : 0;

        public static void Reset()
        {
            _skills = Array.Empty<SkillData>();
            Cooldowns.Clear();
        }
    }
}
