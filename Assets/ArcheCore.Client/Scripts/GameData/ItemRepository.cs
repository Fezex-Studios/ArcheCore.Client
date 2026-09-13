using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// All item lookups against the in-memory gamedata loaded by
    /// GameDataDatabase. Same public API as before the SQLite → binary
    /// migration, so callers elsewhere in the client don't need to change.
    /// </summary>
    public static class ItemRepository
    {
        private static IReadOnlyDictionary<int, ItemRecord> Items => GameDataDatabase.Items;

        // ── Single lookups ────────────────────────────────────────────────────

        /// <summary>Returns the item with this ID, or null if not found.</summary>
        public static ItemRecord GetById(int itemId)
        {
            if (!Ready()) return null;

            return Items.TryGetValue(itemId, out var item) ? item : null;
        }

        /// <summary>Returns the first item whose name matches exactly (case-insensitive).</summary>
        public static ItemRecord GetByName(string name)
        {
            if (!Ready()) return null;

            foreach (var item in Items.Values)
            {
                if (string.Equals(item.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return null;
        }

        // ── Collection lookups ────────────────────────────────────────────────

        /// <summary>Returns all items whose name contains the search term (case-insensitive).</summary>
        public static List<ItemRecord> Search(string term)
        {
            if (!Ready()) return new List<ItemRecord>();

            return Items.Values
                .Where(i => i.Name != null &&
                            i.Name.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        /// <summary>Returns all items in a given category.</summary>
        public static List<ItemRecord> GetByCategory(int category)
        {
            if (!Ready()) return new List<ItemRecord>();

            return Items.Values.Where(i => i.Category == category).ToList();
        }

        /// <summary>Returns every item in the database. Use sparingly.</summary>
        public static List<ItemRecord> GetAll()
        {
            if (!Ready()) return new List<ItemRecord>();

            return Items.Values.ToList();
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static bool Ready()
        {
            if (GameDataDatabase.IsReady)
                return true;

            Debug.LogWarning("[ItemRepository] Database is not ready.");
            return false;
        }
    }
}