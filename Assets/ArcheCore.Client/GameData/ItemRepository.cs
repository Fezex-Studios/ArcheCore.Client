using System.Collections.Generic;
using SQLite;
using UnityEngine;

namespace ArcheCore.Client.GameData
{
    /// <summary>
    /// All item lookups against gamedata.db.
    /// </summary>
    public static class ItemRepository
    {
        private static SQLiteConnection DB => GameDataDatabase.Connection;

        // ── Single lookups ────────────────────────────────────────────────────

        /// <summary>Returns the item with this ID, or null if not found.</summary>
        public static ItemRecord GetById(int itemId)
        {
            if (!Ready()) return null;

            return DB.Find<ItemRecord>(itemId);
        }

        /// <summary>Returns the first item whose name matches exactly (case-insensitive).</summary>
        public static ItemRecord GetByName(string name)
        {
            if (!Ready()) return null;

            return DB.FindWithQuery<ItemRecord>(
                "SELECT * FROM items WHERE name = ? COLLATE NOCASE LIMIT 1",
                name);
        }

        // ── Collection lookups ────────────────────────────────────────────────

        /// <summary>Returns all items whose name contains the search term (case-insensitive).</summary>
        public static List<ItemRecord> Search(string term)
        {
            if (!Ready()) return new List<ItemRecord>();

            return DB.Query<ItemRecord>(
                "SELECT * FROM items WHERE name LIKE ? COLLATE NOCASE",
                $"%{term}%");
        }

        /// <summary>Returns all items in a given category.</summary>
        public static List<ItemRecord> GetByCategory(int category)
        {
            if (!Ready()) return new List<ItemRecord>();

            return DB.Query<ItemRecord>(
                "SELECT * FROM items WHERE category = ?",
                category);
        }

        /// <summary>Returns every item in the database. Use sparingly.</summary>
        public static List<ItemRecord> GetAll()
        {
            if (!Ready()) return new List<ItemRecord>();

            return DB.Table<ItemRecord>().ToList();
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