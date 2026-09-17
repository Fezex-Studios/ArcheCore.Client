// WorldDbAccess.cs
// Assets/Editor/World/
//
// Typed read/write for the WorldServer's live runtime database
// (Data/worldserver.db). Deliberately NOT built on the generic
// schema-driven DynamicSqlite layer in ArcheCoreDevTools: that exists to
// browse arbitrary unknown tables, whereas everything here is a table the
// world map editor understands the *meaning* of — a spawner has a
// position that maps to a Unity transform, a template has a name shown in
// a dropdown. Typed rows make that mapping explicit and catch schema
// drift at compile time rather than as a runtime string-key miss.
//
// Schema mirrors SQL/patches/006_npc_spawners_table.sql and the EF Core
// models in ArcheCore.Server.World/GameData. If you change either, change
// this too — there is no runtime check that keeps them honest.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ArcheCore.Editor.World
{
    // ─────────────────────────────────────────────────────────────────────
    //  Row types — mirror the server's EF models 1:1
    // ─────────────────────────────────────────────────────────────────────

    [SQLite.Table("NpcSpawners")]
    public class NpcSpawnerRow
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement, SQLite.Column("Id")]
        public int Id { get; set; }

        [SQLite.Column("TemplateId")] public int   TemplateId { get; set; }
        [SQLite.Column("X")]          public float X          { get; set; }
        [SQLite.Column("Y")]          public float Y          { get; set; }
        [SQLite.Column("Z")]          public float Z          { get; set; }
        [SQLite.Column("Count")]      public int   Count      { get; set; } = 1;
        [SQLite.Column("Radius")]     public float Radius     { get; set; } = 3f;
    }

    [SQLite.Table("NpcTemplates")]
    public class NpcTemplateRow
    {
        [SQLite.PrimaryKey, SQLite.Column("Id")]
        public int Id { get; set; }

        [SQLite.Column("Name")]          public string Name          { get; set; } = "";
        [SQLite.Column("Level")]         public int    Level         { get; set; }
        [SQLite.Column("ModelType")]     public string ModelType     { get; set; } = "";
        [SQLite.Column("InteractRange")] public float  InteractRange { get; set; } = 4f;
    }

    [SQLite.Table("SpawnPointTables")]
    public class SpawnPointRow
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement, SQLite.Column("Id")]
        public int Id { get; set; }

        [SQLite.Column("Name")]      public string Name      { get; set; } = "";
        [SQLite.Column("X")]         public float  X         { get; set; }
        [SQLite.Column("Y")]         public float  Y         { get; set; }
        [SQLite.Column("Z")]         public float  Z         { get; set; }
        [SQLite.Column("IsDefault")] public bool   IsDefault { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Access layer
    // ─────────────────────────────────────────────────────────────────────

    public static class WorldDbAccess
    {
        /// <summary>
        /// Opens a connection, runs the action, closes. Every call site here
        /// is short-lived and in-editor, so holding a persistent connection
        /// would only create a file lock that fights the running WorldServer
        /// for no benefit — the server holds worldserver.db open while it
        /// runs, and SQLite's default locking will surface that as a "database
        /// is locked" error rather than silent corruption. Callers should be
        /// prepared to surface that to the user (see TryDescribeLockError).
        /// </summary>
        private static T WithConnection<T>(string dbPath, Func<SQLite.SQLiteConnection, T> action)
        {
            if (string.IsNullOrEmpty(dbPath))
                throw new InvalidOperationException("World DB path is not set.");
            if (!File.Exists(dbPath))
                throw new FileNotFoundException($"World DB not found: {dbPath}");

            using var conn = new SQLite.SQLiteConnection(dbPath);
            return action(conn);
        }

        /// <summary>
        /// Turns SQLite's terse lock error into something that says what to
        /// actually do about it. Worth the special case: "database is locked"
        /// while the server is running is by far the most common failure here
        /// and the least self-explanatory.
        /// </summary>
        public static string TryDescribeLockError(Exception e)
        {
            if (e.Message.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Message.IndexOf("busy",   StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Database is locked — the WorldServer is probably running and " +
                       "holding worldserver.db open. Stop the server, or use " +
                       "'Export as SQL Patch' instead of writing directly.";
            }
            return e.Message;
        }

        public static bool TableExists(string dbPath, string table) =>
            WithConnection(dbPath, conn =>
                conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = ?",
                    table) > 0);

        // ── Reads ────────────────────────────────────────────────────────

        public static List<NpcSpawnerRow> LoadSpawners(string dbPath) =>
            WithConnection(dbPath, conn =>
                conn.Table<NpcSpawnerRow>().OrderBy(r => r.Id).ToList());

        public static List<NpcTemplateRow> LoadTemplates(string dbPath) =>
            WithConnection(dbPath, conn =>
                conn.Table<NpcTemplateRow>().OrderBy(r => r.Id).ToList());

        public static List<SpawnPointRow> LoadSpawnPoints(string dbPath) =>
            WithConnection(dbPath, conn =>
                conn.Table<SpawnPointRow>().OrderBy(r => r.Id).ToList());

        // ── Writes ───────────────────────────────────────────────────────
        //
        // All batched writes run inside a single transaction. A half-applied
        // sync would leave the world in a state that matches neither the
        // scene nor the previous DB contents, which is materially worse than
        // failing cleanly and letting the user retry.

        public static void ApplySpawnerChanges(
            string dbPath,
            IList<NpcSpawnerRow> inserts,
            IList<NpcSpawnerRow> updates,
            IList<int> deleteIds)
        {
            WithConnection<object>(dbPath, conn =>
            {
                conn.RunInTransaction(() =>
                {
                    foreach (var id in deleteIds)
                        conn.Execute("DELETE FROM \"NpcSpawners\" WHERE \"Id\" = ?", id);

                    foreach (var row in updates)
                        conn.Execute(
                            "UPDATE \"NpcSpawners\" SET \"TemplateId\" = ?, \"X\" = ?, \"Y\" = ?, " +
                            "\"Z\" = ?, \"Count\" = ?, \"Radius\" = ? WHERE \"Id\" = ?",
                            row.TemplateId, row.X, row.Y, row.Z, row.Count, row.Radius, row.Id);

                    foreach (var row in inserts)
                    {
                        conn.Execute(
                            "INSERT INTO \"NpcSpawners\" (\"TemplateId\", \"X\", \"Y\", \"Z\", " +
                            "\"Count\", \"Radius\") VALUES (?, ?, ?, ?, ?, ?)",
                            row.TemplateId, row.X, row.Y, row.Z, row.Count, row.Radius);

                        // Pull the assigned rowid straight back so the caller can
                        // stamp it onto the scene marker in the same pass. Without
                        // this the marker stays "new" and a second sync would
                        // insert a duplicate rather than update the row it created.
                        row.Id = (int)SQLite.SQLite3.LastInsertRowid(conn.Handle);
                    }
                });
                return null;
            });
        }

        public static void ApplySpawnPointChanges(
            string dbPath,
            IList<SpawnPointRow> inserts,
            IList<SpawnPointRow> updates,
            IList<int> deleteIds)
        {
            WithConnection<object>(dbPath, conn =>
            {
                conn.RunInTransaction(() =>
                {
                    foreach (var id in deleteIds)
                        conn.Execute("DELETE FROM \"SpawnPointTables\" WHERE \"Id\" = ?", id);

                    foreach (var row in updates)
                        conn.Execute(
                            "UPDATE \"SpawnPointTables\" SET \"Name\" = ?, \"X\" = ?, \"Y\" = ?, " +
                            "\"Z\" = ?, \"IsDefault\" = ? WHERE \"Id\" = ?",
                            row.Name, row.X, row.Y, row.Z, row.IsDefault ? 1 : 0, row.Id);

                    foreach (var row in inserts)
                    {
                        conn.Execute(
                            "INSERT INTO \"SpawnPointTables\" (\"Name\", \"X\", \"Y\", \"Z\", " +
                            "\"IsDefault\") VALUES (?, ?, ?, ?, ?)",
                            row.Name, row.X, row.Y, row.Z, row.IsDefault ? 1 : 0);

                        row.Id = (int)SQLite.SQLite3.LastInsertRowid(conn.Handle);
                    }
                });
                return null;
            });
        }

        /// <summary>
        /// Exactly one spawn point may be the default — the server's
        /// SpawnPointService picks it by that flag, and two defaults would
        /// make which one wins depend on row order. Clearing the others in
        /// the same transaction as the set keeps that invariant true even if
        /// the write is interrupted.
        /// </summary>
        public static void SetDefaultSpawnPoint(string dbPath, int spawnPointId)
        {
            WithConnection<object>(dbPath, conn =>
            {
                conn.RunInTransaction(() =>
                {
                    conn.Execute("UPDATE \"SpawnPointTables\" SET \"IsDefault\" = 0");
                    conn.Execute("UPDATE \"SpawnPointTables\" SET \"IsDefault\" = 1 WHERE \"Id\" = ?",
                        spawnPointId);
                });
                return null;
            });
        }
    }
}