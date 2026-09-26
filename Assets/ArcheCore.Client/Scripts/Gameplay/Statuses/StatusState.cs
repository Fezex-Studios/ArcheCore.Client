using System;
using System.Collections.Generic;
using ArcheCore.Network.Shared.Packets.W2C;
using UnityEngine;

namespace ArcheCore.Client.Gameplay.Statuses
{
    /// <summary>One buff or debuff on one entity, as the client shows it.</summary>
    public sealed class StatusView
    {
        public int StatusId;
        public int CasterId;
        public int Stacks;
        public double EndsAt;      // Time.realtimeSinceStartupAsDouble
        public double Duration;    // seconds
        public StatusDefinitionData Definition;

        public string Name => Definition?.Name ?? $"Status {StatusId}";
        public bool IsDebuff => Definition != null && Definition.IsDebuff;

        public double RemainingSeconds => Math.Max(0, EndsAt - Time.realtimeSinceStartupAsDouble);

        /// <summary>1 -> 0 over its duration, for a sweep.</summary>
        public float Remaining01 => Duration > 0 ? (float)Math.Min(1, RemainingSeconds / Duration) : 0f;
    }

    /// <summary>
    /// Buffs and debuffs on everything you can see (roadmap 3.3): yourself,
    /// your target, anyone. The catalogue arrives once per login; changes
    /// arrive as W2CStatusUpdate; a spawn packet brings what an entity
    /// already has. The server decides everything - this only displays it,
    /// and the timers here are cosmetic.
    /// </summary>
    public static class StatusState
    {
        private static readonly Dictionary<int, StatusDefinitionData> Catalog = new Dictionary<int, StatusDefinitionData>();
        private static readonly Dictionary<int, List<StatusView>> ByEntity = new Dictionary<int, List<StatusView>>();
        private static readonly List<StatusView> Empty = new List<StatusView>();

        /// <summary>An entity's statuses changed (network id).</summary>
        public static event Action<int> OnChanged;

        public static void SetCatalog(StatusDefinitionData[] statuses)
        {
            Catalog.Clear();
            if (statuses != null)
                foreach (var s in statuses)
                    if (s != null) Catalog[s.Id] = s;
        }

        public static bool TryGetDefinition(int id, out StatusDefinitionData def) => Catalog.TryGetValue(id, out def);

        public static IReadOnlyList<StatusView> Get(int entityId) =>
            ByEntity.TryGetValue(entityId, out var list) ? list : Empty;

        /// <summary>W2CStatusUpdate.</summary>
        public static void Apply(int entityId, StatusStateData data, bool removed)
        {
            if (data == null) return;

            if (!ByEntity.TryGetValue(entityId, out var list))
                ByEntity[entityId] = list = new List<StatusView>();

            int index = Find(list, data);

            if (removed)
            {
                if (index >= 0) list.RemoveAt(index);
            }
            else
            {
                var view = index >= 0 ? list[index] : new StatusView();
                Fill(view, data);
                if (index < 0) list.Add(view);
            }

            OnChanged?.Invoke(entityId);
        }

        /// <summary>A spawn packet: everything it has right now, replacing whatever we had.</summary>
        public static void SetAll(int entityId, StatusStateData[] statuses)
        {
            if (statuses == null || statuses.Length == 0)
            {
                if (ByEntity.Remove(entityId)) OnChanged?.Invoke(entityId);
                return;
            }

            var list = new List<StatusView>(statuses.Length);
            foreach (var s in statuses)
            {
                if (s == null) continue;
                var view = new StatusView();
                Fill(view, s);
                list.Add(view);
            }

            ByEntity[entityId] = list;
            OnChanged?.Invoke(entityId);
        }

        /// <summary>It left view or died.</summary>
        public static void Clear(int entityId)
        {
            if (ByEntity.Remove(entityId)) OnChanged?.Invoke(entityId);
        }

        public static void Reset()
        {
            ByEntity.Clear();
            Catalog.Clear();
        }

        private static int Find(List<StatusView> list, StatusStateData data)
        {
            bool perCaster = Catalog.TryGetValue(data.StatusId, out var def) && def.PerCaster;
            for (int i = 0; i < list.Count; i++)
                if (list[i].StatusId == data.StatusId && (!perCaster || list[i].CasterId == data.CasterId))
                    return i;
            return -1;
        }

        private static void Fill(StatusView view, StatusStateData data)
        {
            view.StatusId = data.StatusId;
            view.CasterId = data.CasterId;
            view.Stacks = Math.Max(1, data.Stacks);
            view.Duration = Math.Max(0.001, data.DurationMs / 1000.0);
            view.EndsAt = Time.realtimeSinceStartupAsDouble + Math.Max(0, data.RemainingMs) / 1000.0;
            Catalog.TryGetValue(data.StatusId, out view.Definition);
        }
    }
}
