// ArcheCoreDevTools.cs
// Place this file in Assets/ArcheCore/Editor/
// (namespace ArcheCore.Editor mirrors this folder structure)
// Open via: ArcheCore → Dev Tools
//
// v1.2.0 changes:
//  - Removed the AuthServer gamedata.bin path/auto-copy. The Game Data tab now
//    only writes to StreamingAssets + a configurable "Encrypted DB Output Dir";
//    copying that file onto the AuthServer is a manual, deliberate step.
//  - Replaced the hardcoded "World DB" and "Client Data" tabs (which listed
//    exactly two tables each via typed C# row classes) with a single generic
//    "Database" tab that reads the schema at runtime (sqlite_master +
//    PRAGMA table_info) and can browse/edit ANY table in either database,
//    which is the only approach that scales once you have hundreds of tables.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ArcheCore.Client.GameData;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcheCore.Editor
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Persistent settings (survive domain reloads / editor restarts)
    // ─────────────────────────────────────────────────────────────────────────
    [FilePath("ProjectSettings/ArcheCoreDevTools.asset",
              FilePathAttribute.Location.ProjectFolder)]
    public class ArcheCoreDevToolsSettings : ScriptableSingleton<ArcheCoreDevToolsSettings>
    {
        // Paths
        public string serverPatchDir   = "";
        public string plaintextDbPath  = "";   // _oggamedata.db (sqlite source; client-facing content, authored here)
        public string encryptedDbDir   = "";   // where Export writes the encrypted gamedata.bin — copy this to
                                                // AuthServer/src (or wherever it runs from) yourself when ready.
        public string decryptedDbOutputDir = ""; // where "Decrypt StreamingAssets → Inspect" writes its output.
                                                  // Deliberately separate from StreamingAssets, which should only
                                                  // ever hold the encrypted copy — never a decrypted one.
        public string worldServerDbPath = "";  // WorldServer's LIVE runtime db (Data/worldserver.db)
                                                // NOTE: this is a completely different file from
                                                // plaintextDbPath above.

        // Last-used state for the generic Database tab, so it doesn't forget
        // what you were looking at between domain reloads.
        public string lastDbTarget    = "ClientGameData";
        public string lastCustomDbPath = "";
        public string lastSelectedTable = "";

        public void Save() => Save(true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Log entry
    // ─────────────────────────────────────────────────────────────────────────
    internal enum LogLevel { Info, Success, Warning, Error }

    internal class LogEntry
    {
        public LogLevel Level;
        public string   Message;
        public string   Timestamp;

        public LogEntry(LogLevel level, string msg)
        {
            Level     = level;
            Message   = msg;
            Timestamp = DateTime.Now.ToString("HH:mm:ss");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Client gamedata.db row — kept ONLY because the Game Data tab's export
    //  pipeline needs a typed read of the `items` table to pack it into the
    //  fixed binary format (GameDataBinaryFormat.WriteItems). This is business
    //  logic tied to one specific known table, not a UI concern, so it's not
    //  part of the "hundreds of tables" problem the generic browser solves.
    // ─────────────────────────────────────────────────────────────────────────
    [Serializable]
    [SQLite.Table("items")]
    internal class ItemDataRow
    {
        [SQLite.PrimaryKey, SQLite.Column("item_id")] public int ItemId { get; set; }
        [SQLite.Column("name")]        public string Name        { get; set; } = string.Empty;
        [SQLite.Column("description")] public string Description { get; set; } = string.Empty;
        [SQLite.Column("category")]    public int    Category    { get; set; }
        [SQLite.Column("icon_name")]   public string IconName    { get; set; } = string.Empty;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Generic, schema-driven table access (the "Database" tab runs on this).
    //  Reads (table list / column list / row page) use sqlite-net-pcl's
    //  low-level SQLite3 stepping API because the column set is only known at
    //  runtime. Writes (insert/update/delete) deliberately use the normal
    //  public conn.Execute(...) API — the same one the rest of this file
    //  already relies on — so the riskier "figure out the shape of unknown
    //  data" code path never touches anything that mutates the database.
    // ═════════════════════════════════════════════════════════════════════════
    internal enum ColumnKind { Integer, Real, Text, Blob }

    internal class ColumnSchema
    {
        public string Name;
        public string DeclaredType;
        public bool   IsPrimaryKey;
        public bool   NotNull;
        public ColumnKind Kind => DynamicSqlite.ClassifyType(DeclaredType);
    }

    internal class DynamicRow
    {
        // Canonical typed values (long / double / string / byte[] / null).
        public Dictionary<string, object> Values   = new();
        // Snapshot as loaded from disk — used to build WHERE clauses for
        // UPDATE/DELETE (so editing the PK's on-screen value doesn't break
        // the lookup) and to know if a row actually changed.
        public Dictionary<string, object> Original = new();
        // Per-cell text the user is actively editing; parsed back into
        // Values on Save. Editing everything as text avoids a long tail of
        // numeric-field edge cases (nulls, overflow, blobs) for a tool meant
        // to work against an arbitrary, unknown schema.
        public Dictionary<string, string> EditText  = new();
        public bool IsNew;
        public bool IsDirty;
    }

    internal static class DynamicSqlite
    {
        private static readonly IntPtr TransientHint = new IntPtr(-1);

        public static ColumnKind ClassifyType(string declaredType)
        {
            if (string.IsNullOrEmpty(declaredType)) return ColumnKind.Text;
            string t = declaredType.ToUpperInvariant();
            if (t.Contains("INT")) return ColumnKind.Integer;
            if (t.Contains("BLOB")) return ColumnKind.Blob;
            if (t.Contains("REAL") || t.Contains("FLOA") || t.Contains("DOUB") ||
                t.Contains("NUM")  || t.Contains("DEC"))
                return ColumnKind.Real;
            return ColumnKind.Text; // CHAR / CLOB / TEXT / unknown → text, matching SQLite's own affinity rules
        }

        public static List<string> GetTableNames(SQLite.SQLiteConnection conn)
        {
            var names = new List<string>();
            dynamic stmt = SQLite.SQLite3.Prepare2(conn.Handle,
                "SELECT name FROM sqlite_master WHERE type='table' " +
                "AND name NOT LIKE 'sqlite_%' ORDER BY name COLLATE NOCASE");
            try
            {
                while (SQLite.SQLite3.Step(stmt) == SQLite.SQLite3.Result.Row)
                    names.Add(SQLite.SQLite3.ColumnString(stmt, 0));
            }
            finally { SQLite.SQLite3.Finalize(stmt); }
            return names;
        }

        public static List<ColumnSchema> GetSchema(SQLite.SQLiteConnection conn, string table)
        {
            var cols = new List<ColumnSchema>();
            dynamic stmt = SQLite.SQLite3.Prepare2(conn.Handle,
                $"PRAGMA table_info(\"{table}\")");
            try
            {
                // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk
                while (SQLite.SQLite3.Step(stmt) == SQLite.SQLite3.Result.Row)
                {
                    cols.Add(new ColumnSchema
                    {
                        Name         = SQLite.SQLite3.ColumnString(stmt, 1),
                        DeclaredType = SQLite.SQLite3.ColumnString(stmt, 2) ?? "",
                        NotNull      = SQLite.SQLite3.ColumnInt(stmt, 3) != 0,
                        IsPrimaryKey = SQLite.SQLite3.ColumnInt(stmt, 5) != 0
                    });
                }
            }
            finally { SQLite.SQLite3.Finalize(stmt); }
            return cols;
        }

        public static int GetRowCount(SQLite.SQLiteConnection conn, string table,
                                       List<ColumnSchema> schema, string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{table}\"");

            string where = BuildSearchWhere(schema);
            dynamic stmt = SQLite.SQLite3.Prepare2(conn.Handle,
                $"SELECT COUNT(*) FROM \"{table}\" {where}");
            try
            {
                BindSearchPattern(stmt, schema, searchTerm);
                SQLite.SQLite3.Step(stmt);
                return (int)SQLite.SQLite3.ColumnInt64(stmt, 0);
            }
            finally { SQLite.SQLite3.Finalize(stmt); }
        }

        public static List<DynamicRow> LoadRows(SQLite.SQLiteConnection conn, string table,
                                                 List<ColumnSchema> schema, string searchTerm,
                                                 int limit, int offset)
        {
            var rows = new List<DynamicRow>();
            string colList = string.Join(", ", schema.Select(c => $"\"{c.Name}\""));
            string where   = BuildSearchWhere(schema, searchTerm);
            string sql     = $"SELECT {colList} FROM \"{table}\" {where} LIMIT {limit} OFFSET {offset}";

            dynamic stmt = SQLite.SQLite3.Prepare2(conn.Handle, sql);
            try
            {
                BindSearchPattern(stmt, schema, searchTerm);

                while (SQLite.SQLite3.Step(stmt) == SQLite.SQLite3.Result.Row)
                {
                    var row = new DynamicRow();
                    for (int i = 0; i < schema.Count; i++)
                    {
                        object val = ReadColumn(stmt, i);
                        row.Values[schema[i].Name]   = val;
                        row.Original[schema[i].Name] = val;
                        row.EditText[schema[i].Name] = val?.ToString() ?? "";
                    }
                    rows.Add(row);
                }
            }
            finally { SQLite.SQLite3.Finalize(stmt); }
            return rows;
        }

        private static string BuildSearchWhere(List<ColumnSchema> schema, string searchTerm = null)
        {
            if (string.IsNullOrEmpty(searchTerm)) return "";
            var clauses = schema.Select(c => $"CAST(\"{c.Name}\" AS TEXT) LIKE ?");
            return "WHERE " + string.Join(" OR ", clauses);
        }

        private static void BindSearchPattern(dynamic stmt, List<ColumnSchema> schema, string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm)) return;
            string pattern = "%" + searchTerm + "%";
            for (int i = 0; i < schema.Count; i++)
                SQLite.SQLite3.BindText(stmt, i + 1, pattern, -1, TransientHint);
        }

        private static object ReadColumn(dynamic stmt, int index)
        {
            switch (SQLite.SQLite3.ColumnType(stmt, index))
            {
                case SQLite.SQLite3.ColType.Integer: return SQLite.SQLite3.ColumnInt64(stmt, index);
                case SQLite.SQLite3.ColType.Float:   return SQLite.SQLite3.ColumnDouble(stmt, index);
                case SQLite.SQLite3.ColType.Text:    return SQLite.SQLite3.ColumnString(stmt, index);
                case SQLite.SQLite3.ColType.Blob:    return SQLite.SQLite3.ColumnByteArray(stmt, index);
                default: return null;
            }
        }

        // ── Writes: plain public conn.Execute — same API the rest of this file uses ──

        public static void InsertRow(SQLite.SQLiteConnection conn, string table,
                                      List<ColumnSchema> schema, DynamicRow row)
        {
            var cols = schema.Where(c => row.Values.ContainsKey(c.Name)).ToList();
            string colList      = string.Join(", ", cols.Select(c => $"\"{c.Name}\""));
            string placeholders = string.Join(", ", cols.Select(_ => "?"));
            string sql = $"INSERT INTO \"{table}\" ({colList}) VALUES ({placeholders})";

            object[] args = cols.Select(c => row.Values[c.Name]).ToArray();
            conn.Execute(sql, args);

            row.IsNew = false;
            foreach (var kv in row.Values) row.Original[kv.Key] = kv.Value;
        }

        public static void UpdateRow(SQLite.SQLiteConnection conn, string table, ColumnSchema pk,
                                      List<ColumnSchema> schema, DynamicRow row)
        {
            var updateCols = schema.Where(c => !c.IsPrimaryKey).ToList();
            string setClause = string.Join(", ", updateCols.Select(c => $"\"{c.Name}\" = ?"));
            string sql = $"UPDATE \"{table}\" SET {setClause} WHERE \"{pk.Name}\" = ?";

            var args = updateCols.Select(c => row.Values[c.Name]).ToList();
            args.Add(row.Original[pk.Name]);
            conn.Execute(sql, args.ToArray());

            foreach (var kv in row.Values) row.Original[kv.Key] = kv.Value;
        }

        public static void DeleteRow(SQLite.SQLiteConnection conn, string table,
                                      ColumnSchema pk, DynamicRow row)
        {
            conn.Execute($"DELETE FROM \"{table}\" WHERE \"{pk.Name}\" = ?", row.Original[pk.Name]);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Main window
    // ─────────────────────────────────────────────────────────────────────────
    public class ArcheCoreDevTools : EditorWindow
    {
        // ── Tabs ─────────────────────────────────────────────────────────────
        private enum Tab { GameData, SpawnMarkers, Database, Patches }
        private Tab _activeTab = Tab.GameData;

        // ── Shared log ───────────────────────────────────────────────────────
        private readonly List<LogEntry> _log = new();
        private Vector2 _logScroll;

        // ── Spawn Markers tab state (scene → SQL patch export) ───────────────
        private string  _npcPatchName   = "";
        private Vector2 _npcScroll;

        // ── Database tab state (generic, schema-driven) ──────────────────────
        private enum DbTarget { ClientGameData, WorldServer, Custom }
        private DbTarget _dbTarget = DbTarget.ClientGameData;
        private string   _customDbPath = "";

        private List<string> _allTables = new();
        private string       _tableFilter = "";
        private string       _selectedTable = null;

        private List<ColumnSchema> _currentSchema = new();
        private List<DynamicRow>   _currentRows   = new();
        private string _rowSearch = "";
        private int    _rowPage   = 0;
        private const int PageSize = 50;
        private int    _totalRowCount = 0;
        private bool   _rowsDirty;

        private Vector2 _dbTableListScroll;
        private Vector2 _dbGridScroll;

        // ── Patch tab state ──────────────────────────────────────────────────
        private string  _customPatchName = "";
        private string  _customPatchSql  = "";
        private Vector2 _patchScroll;
        private List<string> _existingPatches = new();

        // ── Styles (built lazily) ─────────────────────────────────────────────
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _logStyle;
        private GUIStyle _tabActiveStyle;
        private GUIStyle _tabInactiveStyle;
        private GUIStyle _sectionBoxStyle;
        private GUIStyle _statusSuccessStyle;
        private GUIStyle _statusWarnStyle;
        private GUIStyle _statusErrorStyle;
        private bool     _stylesBuilt;

        // ── AES key (must match GameDataCrypto.cs) ───────────────────────────
        private static readonly byte[] CryptoKey =
        {
            0x4B, 0x1C, 0x9E, 0x7A, 0x2D, 0x88, 0x3F, 0x61,
            0xA5, 0x0E, 0xD2, 0x77, 0x9B, 0x44, 0x1A, 0xC3,
            0x6F, 0x52, 0xE8, 0x09, 0xB1, 0x3D, 0x95, 0x2C,
            0x70, 0xF4, 0x18, 0x8A, 0x5C, 0xDB, 0x21, 0x67
        };

        // ─────────────────────────────────────────────────────────────────────
        //  Open
        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("ArcheCore/Dev Tools #&d")]
        public static void Open()
        {
            var w = GetWindow<ArcheCoreDevTools>("ArcheCore Dev Tools");
            w.minSize = new Vector2(720, 660);
        }

        private void OnEnable()
        {
            RefreshPatches();
            AutoDetectPaths();

            var s = ArcheCoreDevToolsSettings.instance;
            Enum.TryParse(s.lastDbTarget, out _dbTarget);
            _customDbPath  = s.lastCustomDbPath;
            _selectedTable = string.IsNullOrEmpty(s.lastSelectedTable) ? null : s.lastSelectedTable;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Auto-detect default paths relative to the Unity project root
        // ─────────────────────────────────────────────────────────────────────────
        private void AutoDetectPaths()
        {
            var s = ArcheCoreDevToolsSettings.instance;

            string root = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../.."));

            if (string.IsNullOrEmpty(s.serverPatchDir))
            {
                string candidatePatch = Path.Combine(
                    root, "ArcheCore", "src",
                    "ArcheCore.Server.World", "SQL", "patches");

                if (Directory.Exists(candidatePatch))
                    s.serverPatchDir = candidatePatch;
            }

            if (string.IsNullOrEmpty(s.plaintextDbPath))
            {
                string candidateDevTools = Path.Combine(
                    root, "ArcheCore.DevTools", "ArcheCore.DevTools");

                if (Directory.Exists(candidateDevTools))
                {
                    s.plaintextDbPath = Path.Combine(candidateDevTools, "_oggamedata.db");
                    s.encryptedDbDir  = candidateDevTools;
                }
            }

            if (string.IsNullOrEmpty(s.decryptedDbOutputDir) && !string.IsNullOrEmpty(s.plaintextDbPath))
            {
                string dbFolder = Path.GetDirectoryName(s.plaintextDbPath);
                if (!string.IsNullOrEmpty(dbFolder))
                    s.decryptedDbOutputDir = Path.Combine(dbFolder, "decrypted_db_output");
            }

            if (string.IsNullOrEmpty(s.worldServerDbPath))
            {
                // Matches appsettings.json → Database:WorldDb = "Data/worldserver.db",
                // relative to the WorldServer project directory.
                string candidateWorldDb = Path.Combine(
                    root, "ArcheCore", "src",
                    "ArcheCore.Server.World", "Data", "worldserver.db");

                if (File.Exists(candidateWorldDb))
                    s.worldServerDbPath = candidateWorldDb;
            }

            s.Save();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Build styles
        // ─────────────────────────────────────────────────────────────────────
        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleLeft
            };
            _headerStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11
            };
            _subHeaderStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);

            _logStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap  = true,
                richText  = true,
                fontSize  = 10
            };

            var tabBase = new GUIStyle(EditorStyles.toolbarButton)
            {
                fontSize    = 11,
                fixedHeight = 28
            };
            _tabActiveStyle   = new GUIStyle(tabBase);
            _tabInactiveStyle = new GUIStyle(tabBase);
            _tabActiveStyle.normal.textColor   = new Color(0.95f, 0.85f, 0.4f);
            _tabInactiveStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);

            _sectionBoxStyle = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin  = new RectOffset(0, 0, 4, 4)
            };

            _statusSuccessStyle = new GUIStyle(EditorStyles.miniLabel);
            _statusSuccessStyle.normal.textColor = new Color(0.4f, 0.9f, 0.4f);

            _statusWarnStyle = new GUIStyle(EditorStyles.miniLabel);
            _statusWarnStyle.normal.textColor = new Color(0.95f, 0.75f, 0.2f);

            _statusErrorStyle = new GUIStyle(EditorStyles.miniLabel);
            _statusErrorStyle.normal.textColor = new Color(0.95f, 0.3f, 0.3f);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  OnGUI
        // ─────────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            BuildStyles();
            DrawHeader();
            DrawTabs();

            EditorGUILayout.Space(4);

            switch (_activeTab)
            {
                case Tab.GameData:     DrawGameDataTab();     break;
                case Tab.SpawnMarkers: DrawSpawnMarkersTab(); break;
                case Tab.Database:     DrawDatabaseTab();     break;
                case Tab.Patches:      DrawPatchesTab();      break;
            }

            EditorGUILayout.Space(4);
            DrawLog();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Header
        // ─────────────────────────────────────────────────────────────────────
        private void DrawHeader()
        {
            var rect = EditorGUILayout.GetControlRect(false, 36);
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.16f));

            var labelRect = new Rect(rect.x + 12, rect.y + 6, rect.width, rect.height);
            EditorGUI.LabelField(labelRect, "⚔  ArcheCore Dev Tools", _headerStyle);

            var versionRect = new Rect(rect.xMax - 80, rect.y + 10, 72, rect.height);
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            EditorGUI.LabelField(versionRect, "v1.2.0", EditorStyles.miniLabel);
            GUI.color = Color.white;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Tab bar
        // ─────────────────────────────────────────────────────────────────────
        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            DrawTabButton(Tab.GameData,     "📦  Game Data");
            DrawTabButton(Tab.SpawnMarkers, "📍  Spawn Markers");
            DrawTabButton(Tab.Database,     "🗄  Database");
            DrawTabButton(Tab.Patches,      "🧩  SQL Patches");

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTabButton(Tab tab, string label)
        {
            bool active = _activeTab == tab;
            var  style  = active ? _tabActiveStyle : _tabInactiveStyle;

            if (GUILayout.Button(label, style, GUILayout.Width(150)))
                _activeTab = tab;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  TAB: GAME DATA  (export/encrypt pipeline for the client content
        //  package — _oggamedata.db (sqlite, authored) is exported to the
        //  custom binary format and encrypted as gamedata.bin, then copied to
        //  StreamingAssets and to a local "encrypted output" folder. Getting
        //  that file onto the AuthServer is now a manual step — copy it over
        //  yourself once you're happy with a build, rather than this tool
        //  reaching into the AuthServer's folder automatically.)
        // ═════════════════════════════════════════════════════════════════════
        private void DrawGameDataTab()
        {
            var s = ArcheCoreDevToolsSettings.instance;

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("📁  Paths", _subHeaderStyle);
            EditorGUILayout.Space(4);
            try
            {
                DrawPathField("Plaintext DB (_oggamedata.db)",
                    ref s.plaintextDbPath, false);
                DrawPathField("Encrypted DB Output Dir",
                    ref s.encryptedDbDir, true);
                DrawPathField("Decrypted DB Output Dir",
                    ref s.decryptedDbOutputDir, true);
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox($"Path error: {e.Message}", MessageType.Error);
            }
            EditorGUILayout.EndVertical();

            // ── Status ───────────────────────────────────────────────────────
            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("📊  Status", _subHeaderStyle);
            EditorGUILayout.Space(2);

            DrawFileStatus("Plaintext DB (source)", s.plaintextDbPath);

            string streamingDb = Path.Combine(
                Application.streamingAssetsPath, "GameData", "gamedata.bin");
            DrawFileStatus("StreamingAssets .bin", streamingDb);

            string devOutDb = string.IsNullOrEmpty(s.encryptedDbDir)
                ? "" : Path.Combine(s.encryptedDbDir, "gamedata.bin");
            DrawFileStatus("Encrypted output .bin", devOutDb);

            EditorGUILayout.EndVertical();

            // ── Actions ───────────────────────────────────────────────────────
            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("⚡  Actions", _subHeaderStyle);
            EditorGUILayout.Space(4);

            bool hasPlaintext = File.Exists(s.plaintextDbPath);
            bool hasOutputDir = !string.IsNullOrEmpty(s.encryptedDbDir);

            EditorGUI.BeginDisabledGroup(!hasPlaintext || !hasOutputDir);
            if (DrawActionButton(
                "Export Binary + Encrypt → StreamingAssets + Output Dir",
                "Reads the items table from _oggamedata.db, packs it into the " +
                "custom binary format the client reads, AES-256 encrypts it, " +
                "and writes the result to StreamingAssets and to the Encrypted " +
                "DB Output Dir above. It does NOT touch the AuthServer — copy " +
                "gamedata.bin over yourself once you're ready to deploy it.",
                new Color(0.2f, 0.55f, 0.9f)))
            {
                ExportEncryptAndDeploy(s);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(2);

            EditorGUI.BeginDisabledGroup(!File.Exists(streamingDb) || string.IsNullOrEmpty(s.decryptedDbOutputDir));
            if (DrawActionButton(
                "Decrypt StreamingAssets → Inspect (.bin)",
                "Decrypts the current StreamingAssets .bin into the Decrypted DB " +
                "Output Dir above, for debugging. StreamingAssets itself is left " +
                "untouched — it should only ever hold the encrypted copy. This is " +
                "the binary format, NOT sqlite — it can't be opened in DB Browser " +
                "and won't overwrite _oggamedata.db, which remains the editable " +
                "source of truth; re-run 'Export Binary + Encrypt' after making " +
                "changes there.",
                new Color(0.55f, 0.35f, 0.75f)))
            {
                DecryptFromStreaming(s, streamingDb);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(2);

            EditorGUI.BeginDisabledGroup(!hasPlaintext);
            if (DrawActionButton(
                "Open Plaintext DB in Explorer",
                "Opens the folder containing _oggamedata.db.",
                new Color(0.3f, 0.3f, 0.3f)))
            {
                if (File.Exists(s.plaintextDbPath))
                    EditorUtility.RevealInFinder(s.plaintextDbPath);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(2);

            EditorGUI.BeginDisabledGroup(!hasOutputDir || !Directory.Exists(s.encryptedDbDir));
            if (DrawActionButton(
                "Open Encrypted Output Folder",
                "Opens the Encrypted DB Output Dir so you can copy gamedata.bin to the AuthServer.",
                new Color(0.3f, 0.3f, 0.3f)))
            {
                EditorUtility.RevealInFinder(s.encryptedDbDir);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();

            if (GUI.changed) s.Save();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Export Binary + Encrypt + Deploy
        //
        //  _oggamedata.db (sqlite, hand-edited via DB Browser or the Database
        //  tab) is still the source of truth for content authoring. This step
        //  reads its `items` table, packs it into the shared binary format
        //  from GameDataBinaryFormat.cs (the same class the client runtime
        //  uses to read it back), AES-encrypts that binary blob, and writes
        //  it to StreamingAssets + the Encrypted DB Output Dir. Deploying to
        //  the AuthServer is a manual copy from that output dir.
        // ─────────────────────────────────────────────────────────────────────
        private void ExportEncryptAndDeploy(ArcheCoreDevToolsSettings s)
        {
            try
            {
                byte[] binary    = ExportItemsToBinary(s.plaintextDbPath);
                byte[] encrypted = EncryptAes(binary);

                string streamingDir = Path.Combine(
                    Application.streamingAssetsPath, "GameData");
                Directory.CreateDirectory(streamingDir);
                string streamingOut = Path.Combine(streamingDir, "gamedata.bin");
                File.WriteAllBytes(streamingOut, encrypted);
                Log(LogLevel.Success,
                    $"Written to StreamingAssets/GameData/gamedata.bin ({encrypted.Length:N0} bytes)");

                string devOut = Path.Combine(s.encryptedDbDir, "gamedata.bin");
                File.WriteAllBytes(devOut, encrypted);
                Log(LogLevel.Success, $"Written to Encrypted DB Output Dir: {devOut}");
                Log(LogLevel.Info,
                    "Reminder: this was NOT copied to the AuthServer. Copy " +
                    $"{devOut} over manually when you're ready to deploy it.");

                AssetDatabase.Refresh();
                Log(LogLevel.Success, "✓ Export + Encrypt complete.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Export/encryption failed: {e.Message}");
            }
        }

        /// <summary>
        /// Reads the `items` table straight from the sqlite plaintext DB
        /// (not from any in-editor cache, so this always reflects what's
        /// actually saved on disk) and packs it via GameDataBinaryFormat.
        /// </summary>
        private byte[] ExportItemsToBinary(string plaintextDbPath)
        {
            List<ItemDataRow> rows;
            using (var conn = new SQLite.SQLiteConnection(plaintextDbPath))
            {
                rows = conn.Table<ItemDataRow>().OrderBy(r => r.ItemId).ToList();
            }

            var records = rows.Select(r => new ItemRecord
            {
                ItemId      = r.ItemId,
                Name        = r.Name,
                Description = r.Description,
                Category    = r.Category,
                IconName    = r.IconName
            }).ToList();

            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                GameDataBinaryFormat.WriteItems(writer, records);
            }

            Log(LogLevel.Info,
                $"Packed {records.Count} item(s) into binary format v{GameDataBinaryFormat.CurrentVersion}.");

            return ms.ToArray();
        }

        private void DecryptFromStreaming(ArcheCoreDevToolsSettings s,
                                          string streamingDb)
        {
            try
            {
                byte[] encrypted  = File.ReadAllBytes(streamingDb);
                byte[] decrypted  = DecryptAes(encrypted);

                // Written to the dedicated Decrypted DB Output Dir for inspection
                // only — deliberately NOT back into StreamingAssets (which should
                // only ever hold the encrypted copy) and NOT to s.plaintextDbPath,
                // since this is the packed binary format, not a valid sqlite file,
                // and overwriting the real editable source with it would break it.
                Directory.CreateDirectory(s.decryptedDbOutputDir);
                string outputPath = Path.Combine(s.decryptedDbOutputDir, "gamedata.decrypted.bin");

                File.WriteAllBytes(outputPath, decrypted);
                Log(LogLevel.Success,
                    $"✓ Decrypted to: {outputPath} ({decrypted.Length:N0} bytes). " +
                    "This is the binary payload for inspection — _oggamedata.db " +
                    "is unaffected.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Decryption failed: {e.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  TAB: SPAWN MARKERS  (place NPCs in-scene, export as a .sql patch —
        //  a separate workflow from editing NpcSpawners directly on the
        //  Database tab; useful when you want to visually place several
        //  spawners at once before committing anything to the live DB.)
        // ═════════════════════════════════════════════════════════════════════
        private void DrawSpawnMarkersTab()
        {
            var s = ArcheCoreDevToolsSettings.instance;

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("📁  Output", _subHeaderStyle);
            EditorGUILayout.Space(4);
            try
            {
                DrawPathField("Server Patch Dir", ref s.serverPatchDir, true);
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox($"Path error: {e.Message}", MessageType.Error);
            }
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Patch Name", GUILayout.Width(90));
            _npcPatchName = EditorGUILayout.TextField(_npcPatchName);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            var markers = FindNpcMarkers();

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField(
                $"👾  Scene Markers  ({markers.Count} found)", _subHeaderStyle);
            EditorGUILayout.Space(4);

            if (markers.Count == 0)
            {
                DrawHelpBox(
                    "No NpcSpawnerMarker objects found in the scene.\n" +
                    "Tag a GameObject 'NpcSpawner' and add an NpcSpawnerMarker component.",
                    MessageType.Info);
            }
            else
            {
                _npcScroll = EditorGUILayout.BeginScrollView(
                    _npcScroll, GUILayout.MaxHeight(160));

                DrawMarkerTableHeader();
                foreach (var obj in markers)
                {
                    var m = obj.GetComponent<NpcSpawnerMarker>();
                    if (m == null) continue;
                    DrawMarkerRow(obj, m);
                }

                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("➕  Place New Marker", _subHeaderStyle);
            EditorGUILayout.Space(4);

            if (DrawActionButton(
                "Place NPC Spawner Marker at Scene Origin",
                "Creates a tagged, Gizmo-visible marker at (0,0,0). Move it in the scene.",
                new Color(0.25f, 0.55f, 0.3f)))
            {
                PlaceNewMarker();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("📤  Export", _subHeaderStyle);
            EditorGUILayout.Space(4);

            bool canExport = markers.Count > 0 &&
                             !string.IsNullOrEmpty(_npcPatchName) &&
                             !string.IsNullOrEmpty(s.serverPatchDir);

            if (!canExport)
            {
                DrawHelpBox(
                    "Set a patch name and server patch dir, and place at least one marker.",
                    MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(!canExport);

            if (DrawActionButton(
                "Export SQL Patch",
                "Writes INSERT statements for all markers to a .sql patch file. " +
                "Run this patch against Data/worldserver.db to apply it.",
                new Color(0.2f, 0.55f, 0.9f)))
            {
                ExportNpcPatch(markers, s.serverPatchDir, false);
            }

            EditorGUILayout.Space(2);

            GUI.color = new Color(1f, 0.85f, 0.85f);
            if (DrawActionButton(
                "Export + Delete Markers From Scene",
                "Exports the SQL patch then removes all markers from the scene.",
                new Color(0.75f, 0.25f, 0.25f)))
            {
                ExportNpcPatch(markers, s.serverPatchDir, true);
            }
            GUI.color = Color.white;

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();

            if (GUI.changed) s.Save();
        }

        private void DrawMarkerTableHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Template ID",  EditorStyles.miniLabel, GUILayout.Width(80));
            GUILayout.Label("Name",         EditorStyles.miniLabel, GUILayout.Width(130));
            GUILayout.Label("Count",        EditorStyles.miniLabel, GUILayout.Width(45));
            GUILayout.Label("Radius",       EditorStyles.miniLabel, GUILayout.Width(50));
            GUILayout.Label("Position",     EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax, rect.width, 1),
                new Color(0.4f, 0.4f, 0.4f));
            EditorGUILayout.Space(2);
        }

        private void DrawMarkerRow(GameObject obj, NpcSpawnerMarker m)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(m.TemplateId.ToString(),
                EditorStyles.miniLabel, GUILayout.Width(80));
            GUILayout.Label(m.NpcName,
                EditorStyles.miniLabel, GUILayout.Width(130));
            GUILayout.Label(m.Count.ToString(),
                EditorStyles.miniLabel, GUILayout.Width(45));
            GUILayout.Label($"{m.Radius:F1}",
                EditorStyles.miniLabel, GUILayout.Width(50));

            var p = obj.transform.position;
            GUILayout.Label(
                $"({p.x:F1}, {p.y:F1}, {p.z:F1})",
                EditorStyles.miniLabel);

            if (GUILayout.Button("Select", EditorStyles.miniButton,
                                 GUILayout.Width(48)))
            {
                Selection.activeGameObject = obj;
                SceneView.FrameLastActiveSceneView();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void PlaceNewMarker()
        {
            var go = new GameObject("NpcSpawnerMarker");

            if (!IsTagDefined("NpcSpawner"))
                Log(LogLevel.Warning,
                    "Tag 'NpcSpawner' not found. Create it in Edit → Project Settings → Tags.");
            else
                go.tag = "NpcSpawner";

            var marker = go.AddComponent<NpcSpawnerMarker>();
            marker.NpcName = "New NPC";

            Undo.RegisterCreatedObjectUndo(go, "Place NPC Spawner Marker");
            Selection.activeGameObject = go;

            Log(LogLevel.Success,
                "Placed NpcSpawnerMarker. Select it and move to the desired position.");
        }

        private void ExportNpcPatch(List<GameObject> markers,
                                    string patchDir, bool deleteAfter)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("-- Auto-generated by ArcheCore Dev Tools");
                sb.AppendLine(
                    $"-- Scene: {SceneManager.GetActiveScene().name}");
                sb.AppendLine(
                    $"-- Date:  {DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine();

                int exported = 0;
                foreach (var obj in markers)
                {
                    var m = obj.GetComponent<NpcSpawnerMarker>();
                    if (m == null)
                    {
                        Log(LogLevel.Warning,
                            $"'{obj.name}' has no NpcSpawnerMarker — skipped.");
                        continue;
                    }

                    var p = obj.transform.position;
                    sb.AppendLine(
                        $"-- {m.NpcName} x{m.Count} (TemplateId={m.TemplateId})");
                    sb.AppendLine(
                        $"INSERT INTO \"NpcSpawners\" " +
                        $"(TemplateId, X, Y, Z, Count, Radius) VALUES " +
                        $"({m.TemplateId}, " +
                        $"{p.x:F4}, {p.y:F4}, {p.z:F4}, " +
                        $"{m.Count}, {m.Radius:F1});");
                    sb.AppendLine();
                    exported++;

                    Log(LogLevel.Info,
                        $"  → [{m.TemplateId}] {m.NpcName} x{m.Count} " +
                        $"at ({p.x:F1}, {p.y:F1}, {p.z:F1})");
                }

                string fileName = $"{_npcPatchName}.sql";
                Directory.CreateDirectory(patchDir);
                string fullPath = Path.Combine(patchDir, fileName);
                File.WriteAllText(fullPath, sb.ToString());
                Log(LogLevel.Success,
                    $"✓ Exported {exported} spawner(s) → {fileName}");

                if (deleteAfter)
                {
                    foreach (var obj in markers)
                        DestroyImmediate(obj);
                    Log(LogLevel.Success, "✓ Markers removed from scene.");
                }

                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Export failed: {e.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  TAB: DATABASE  (generic — works against ANY table in either the
        //  client plaintext DB or the WorldServer DB, or an arbitrary custom
        //  .db path. Table list and columns are read from the schema at
        //  runtime, so nothing here needs updating as tables are added.)
        // ═════════════════════════════════════════════════════════════════════
        private void DrawDatabaseTab()
        {
            var s = ArcheCoreDevToolsSettings.instance;

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("🎯  Target Database", _subHeaderStyle);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            DrawDbTargetButton(DbTarget.ClientGameData, "Client DB");
            DrawDbTargetButton(DbTarget.WorldServer,    "WorldServer DB");
            DrawDbTargetButton(DbTarget.Custom,         "Custom Path…");
            EditorGUILayout.EndHorizontal();

            switch (_dbTarget)
            {
                case DbTarget.ClientGameData:
                    DrawPathField("Client DB Path", ref s.plaintextDbPath, false);
                    break;
                case DbTarget.WorldServer:
                    DrawPathField("WorldServer DB Path", ref s.worldServerDbPath, false);
                    break;
                case DbTarget.Custom:
                    DrawPathField("Custom DB Path", ref _customDbPath, false);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                ConfirmDiscardIfDirtyThen(() =>
                {
                    _allTables.Clear();
                    _selectedTable = null;
                    _currentSchema.Clear();
                    _currentRows.Clear();
                });
            }

            string activeDbPath = ResolveActiveDbPath();
            bool hasDb = !string.IsNullOrEmpty(activeDbPath) && File.Exists(activeDbPath);

            EditorGUILayout.Space(2);
            if (!hasDb)
            {
                DrawHelpBox(
                    string.IsNullOrEmpty(activeDbPath)
                        ? "No path set for this target yet."
                        : $"File not found: {activeDbPath}",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField($"Connected: {activeDbPath}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();

            // ── Table list ───────────────────────────────────────────────────
            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"📋  Tables  ({_allTables.Count})", _subHeaderStyle, GUILayout.ExpandWidth(true));

            EditorGUI.BeginDisabledGroup(!hasDb);
            if (GUILayout.Button("Load Tables", GUILayout.Width(90)))
                LoadTableList(activeDbPath);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_allTables.Count > 0)
            {
                EditorGUILayout.Space(2);
                _tableFilter = EditorGUILayout.TextField("Filter", _tableFilter);

                var filtered = string.IsNullOrEmpty(_tableFilter)
                    ? _allTables
                    : _allTables.Where(t => t.IndexOf(_tableFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                _dbTableListScroll = EditorGUILayout.BeginScrollView(
                    _dbTableListScroll, GUILayout.MaxHeight(120));

                foreach (var table in filtered)
                {
                    bool selected = table == _selectedTable;
                    GUI.color = selected ? new Color(0.95f, 0.85f, 0.4f) : Color.white;
                    if (GUILayout.Button(table, EditorStyles.miniButton))
                    {
                        string chosen = table;
                        ConfirmDiscardIfDirtyThen(() => SelectTable(activeDbPath, chosen));
                    }
                }
                GUI.color = Color.white;

                EditorGUILayout.EndScrollView();

                if (filtered.Count == 0)
                    DrawHelpBox("No tables match that filter.", MessageType.Info);
            }
            else
            {
                DrawHelpBox(hasDb
                    ? "Click 'Load Tables' to read the schema."
                    : "Set a valid database path above first.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();

            // ── Row grid ─────────────────────────────────────────────────────
            if (_selectedTable != null)
                DrawTableGrid(activeDbPath);

            if (GUI.changed)
            {
                s.lastDbTarget      = _dbTarget.ToString();
                s.lastCustomDbPath  = _customDbPath;
                s.lastSelectedTable = _selectedTable ?? "";
                s.Save();
            }
        }

        private void DrawDbTargetButton(DbTarget target, string label)
        {
            bool active = _dbTarget == target;
            GUI.color = active ? new Color(0.95f, 0.85f, 0.4f) : Color.white;
            if (GUILayout.Button(label, EditorStyles.miniButton))
                _dbTarget = target;
            GUI.color = Color.white;
        }

        private string ResolveActiveDbPath()
        {
            var s = ArcheCoreDevToolsSettings.instance;
            return _dbTarget switch
            {
                DbTarget.ClientGameData => s.plaintextDbPath,
                DbTarget.WorldServer    => s.worldServerDbPath,
                DbTarget.Custom         => _customDbPath,
                _                       => null
            };
        }

        private void ConfirmDiscardIfDirtyThen(Action action)
        {
            if (_rowsDirty)
            {
                bool discard = EditorUtility.DisplayDialog(
                    "Discard unsaved changes?",
                    $"'{_selectedTable}' has unsaved edits. Switching now will discard them.",
                    "Discard", "Cancel");
                if (!discard) return;
            }
            _rowsDirty = false;
            action();
        }

        private void LoadTableList(string dbPath)
        {
            try
            {
                using var conn = new SQLite.SQLiteConnection(dbPath);
                _allTables = DynamicSqlite.GetTableNames(conn);
                Log(LogLevel.Success, $"Found {_allTables.Count} table(s) in {Path.GetFileName(dbPath)}.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Failed to read schema: {e.Message}");
            }
        }

        private void SelectTable(string dbPath, string table)
        {
            _selectedTable = table;
            _rowPage       = 0;
            _rowSearch     = "";
            LoadTableSchemaAndRows(dbPath);
        }

        private void LoadTableSchemaAndRows(string dbPath)
        {
            try
            {
                using var conn = new SQLite.SQLiteConnection(dbPath);
                _currentSchema = DynamicSqlite.GetSchema(conn, _selectedTable);

                if (_currentSchema.Count == 0)
                {
                    Log(LogLevel.Warning, $"'{_selectedTable}' has no columns (or doesn't exist).");
                    _currentRows.Clear();
                    return;
                }

                if (!_currentSchema.Any(c => c.IsPrimaryKey))
                    Log(LogLevel.Warning,
                        $"'{_selectedTable}' has no single-column PRIMARY KEY — existing rows can be " +
                        "viewed but not edited safely here. New rows can still be inserted.");

                _totalRowCount = DynamicSqlite.GetRowCount(conn, _selectedTable, _currentSchema, _rowSearch);
                _currentRows   = DynamicSqlite.LoadRows(conn, _selectedTable, _currentSchema,
                                                         _rowSearch, PageSize, _rowPage * PageSize);
                _rowsDirty = false;

                Log(LogLevel.Success,
                    $"Loaded {_currentRows.Count} of {_totalRowCount} row(s) from '{_selectedTable}'.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Failed to load '{_selectedTable}': {e.Message}");
            }
        }

        private void DrawTableGrid(string dbPath)
        {
            var pk = _currentSchema.FirstOrDefault(c => c.IsPrimaryKey);

            EditorGUILayout.BeginVertical(_sectionBoxStyle);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"🧾  {_selectedTable}  ({_currentRows.Count} of {_totalRowCount})",
                _subHeaderStyle, GUILayout.ExpandWidth(true));

            if (GUILayout.Button("+ Add Row", GUILayout.Width(80)))
                AddNewRow();

            EditorGUI.BeginDisabledGroup(!_rowsDirty);
            GUI.color = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("Save Changes", GUILayout.Width(100)))
                SaveTableChanges(dbPath, pk);
            GUI.color = Color.white;
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Reload", GUILayout.Width(70)))
                ConfirmDiscardIfDirtyThen(() => LoadTableSchemaAndRows(dbPath));

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search", GUILayout.Width(50));
            string newSearch = EditorGUILayout.TextField(_rowSearch);
            if (GUILayout.Button("Go", GUILayout.Width(30)) || newSearch != _rowSearch)
            {
                _rowSearch = newSearch;
                _rowPage   = 0;
                LoadTableSchemaAndRows(dbPath);
            }
            EditorGUILayout.EndHorizontal();

            if (_rowsDirty)
                DrawHelpBox("Unsaved changes in this table.", MessageType.Warning);

            if (_currentSchema.Count == 0)
            {
                DrawHelpBox("No schema loaded for this table.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // Header
            EditorGUILayout.BeginHorizontal();
            foreach (var c in _currentSchema)
            {
                string label = c.Name + (c.IsPrimaryKey ? " 🔑" : "");
                GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(140));
            }
            GUILayout.Label("", EditorStyles.miniLabel, GUILayout.Width(24));
            EditorGUILayout.EndHorizontal();
            var headerRect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax, headerRect.width, 1),
                new Color(0.4f, 0.4f, 0.4f));
            EditorGUILayout.Space(2);

            // Rows
            _dbGridScroll = EditorGUILayout.BeginScrollView(_dbGridScroll, GUILayout.MaxHeight(320));

            DynamicRow toDelete = null;
            foreach (var row in _currentRows)
            {
                if (DrawGenericRow(row, pk))
                    toDelete = row;
            }

            if (toDelete != null)
            {
                if (toDelete.IsNew)
                {
                    _currentRows.Remove(toDelete);
                }
                else if (pk == null)
                {
                    Log(LogLevel.Error,
                        $"Can't delete — '{_selectedTable}' has no single-column primary key.");
                }
                else
                {
                    try
                    {
                        using var conn = new SQLite.SQLiteConnection(dbPath);
                        DynamicSqlite.DeleteRow(conn, _selectedTable, pk, toDelete);
                        _currentRows.Remove(toDelete);
                        _totalRowCount--;
                        Log(LogLevel.Success, $"✓ Deleted row where {pk.Name} = {toDelete.Original[pk.Name]}.");
                    }
                    catch (Exception e)
                    {
                        Log(LogLevel.Error, $"Delete failed: {e.Message}");
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            // Pagination
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_rowPage == 0);
            if (GUILayout.Button("◀ Prev", GUILayout.Width(70)))
            {
                _rowPage--;
                ConfirmDiscardIfDirtyThen(() => LoadTableSchemaAndRows(dbPath));
            }
            EditorGUI.EndDisabledGroup();

            int totalPages = Math.Max(1, (int)Math.Ceiling(_totalRowCount / (double)PageSize));
            GUILayout.Label($"Page {_rowPage + 1} / {totalPages}", EditorStyles.miniLabel,
                GUILayout.Width(90));

            EditorGUI.BeginDisabledGroup((_rowPage + 1) * PageSize >= _totalRowCount);
            if (GUILayout.Button("Next ▶", GUILayout.Width(70)))
            {
                _rowPage++;
                ConfirmDiscardIfDirtyThen(() => LoadTableSchemaAndRows(dbPath));
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        /// <returns>true if the row's delete button was clicked</returns>
        private bool DrawGenericRow(DynamicRow row, ColumnSchema pk)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();

            foreach (var c in _currentSchema)
            {
                bool lockField = c.IsPrimaryKey && !row.IsNew; // don't let PK edits break the WHERE lookup
                EditorGUI.BeginDisabledGroup(lockField || c.Kind == ColumnKind.Blob);

                string current = row.EditText.TryGetValue(c.Name, out var v) ? v : "";
                string display = c.Kind == ColumnKind.Blob
                    ? $"<{(row.Values[c.Name] as byte[])?.Length ?? 0} bytes>"
                    : current;

                string edited = EditorGUILayout.TextField(display, GUILayout.Width(140));
                if (c.Kind != ColumnKind.Blob) row.EditText[c.Name] = edited;

                EditorGUI.EndDisabledGroup();
            }

            bool delete = GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24));
            EditorGUILayout.EndHorizontal();

            if (row.IsNew)
            {
                GUI.color = new Color(0.95f, 0.75f, 0.2f);
                EditorGUILayout.LabelField("  new — not saved yet", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            if (EditorGUI.EndChangeCheck())
            {
                row.IsDirty = true;
                _rowsDirty  = true;
            }

            return delete;
        }

        private void AddNewRow()
        {
            var row = new DynamicRow { IsNew = true, IsDirty = true };
            foreach (var c in _currentSchema)
            {
                row.Values[c.Name]   = null;
                row.EditText[c.Name] = "";
            }
            _currentRows.Add(row);
            _rowsDirty = true;
            Log(LogLevel.Info, $"Added new row to '{_selectedTable}' — fill in values, then Save.");
        }

        private void SaveTableChanges(string dbPath, ColumnSchema pk)
        {
            var dirtyRows = _currentRows.Where(r => r.IsDirty).ToList();
            if (dirtyRows.Count == 0)
            {
                _rowsDirty = false;
                return;
            }

            int saved = 0, failed = 0;

            try
            {
                using var conn = new SQLite.SQLiteConnection(dbPath);
                conn.RunInTransaction(() =>
                {
                    foreach (var row in dirtyRows)
                    {
                        try
                        {
                            if (!ParseEditTextIntoValues(row))
                            {
                                failed++;
                                continue;
                            }

                            if (row.IsNew)
                            {
                                DynamicSqlite.InsertRow(conn, _selectedTable, _currentSchema, row);
                            }
                            else
                            {
                                if (pk == null)
                                {
                                    Log(LogLevel.Error,
                                        $"Can't save — '{_selectedTable}' has no single-column primary key.");
                                    failed++;
                                    continue;
                                }
                                DynamicSqlite.UpdateRow(conn, _selectedTable, pk, _currentSchema, row);
                            }

                            row.IsDirty = false;
                            saved++;
                        }
                        catch (Exception rowEx)
                        {
                            failed++;
                            Log(LogLevel.Error, $"Row save failed: {rowEx.Message}");
                        }
                    }
                });
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Save failed: {e.Message}");
                return;
            }

            _rowsDirty = _currentRows.Any(r => r.IsDirty);
            _totalRowCount = DynamicSqlite.GetRowCount(
                new SQLite.SQLiteConnection(dbPath), _selectedTable, _currentSchema, _rowSearch);

            if (failed == 0)
                Log(LogLevel.Success, $"✓ Saved {saved} row(s) to '{_selectedTable}'.");
            else
                Log(LogLevel.Warning, $"Saved {saved} row(s), {failed} failed — see errors above.");
        }

        /// <summary>Parses each column's EditText back into a typed value. Returns false (and logs) on a bad parse.</summary>
        private bool ParseEditTextIntoValues(DynamicRow row)
        {
            foreach (var c in _currentSchema)
            {
                if (c.Kind == ColumnKind.Blob) continue; // not editable here — leave as-is

                string text = row.EditText.TryGetValue(c.Name, out var t) ? t : "";

                if (string.IsNullOrEmpty(text))
                {
                    if (c.NotNull && !(c.IsPrimaryKey && row.IsNew))
                    {
                        Log(LogLevel.Error, $"'{c.Name}' can't be empty (NOT NULL).");
                        return false;
                    }
                    row.Values[c.Name] = null;
                    continue;
                }

                switch (c.Kind)
                {
                    case ColumnKind.Integer:
                        if (!long.TryParse(text, out long lv))
                        {
                            Log(LogLevel.Error, $"'{c.Name}' expects a whole number, got '{text}'.");
                            return false;
                        }
                        row.Values[c.Name] = lv;
                        break;

                    case ColumnKind.Real:
                        if (!double.TryParse(text, out double dv))
                        {
                            Log(LogLevel.Error, $"'{c.Name}' expects a number, got '{text}'.");
                            return false;
                        }
                        row.Values[c.Name] = dv;
                        break;

                    default: // Text
                        row.Values[c.Name] = text;
                        break;
                }
            }
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  TAB: SQL PATCHES
        // ═════════════════════════════════════════════════════════════════════
        private void DrawPatchesTab()
        {
            var s = ArcheCoreDevToolsSettings.instance;

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("📁  Server Patch Directory", _subHeaderStyle);
            EditorGUILayout.Space(4);
            try
            {
                DrawPathField("", ref s.serverPatchDir, true);
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox($"Path error: {e.Message}", MessageType.Error);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻ Refresh", EditorStyles.miniButton,
                                 GUILayout.Width(70)))
                RefreshPatches();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField(
                $"📋  Existing Patches  ({_existingPatches.Count})",
                _subHeaderStyle);
            EditorGUILayout.Space(4);

            if (_existingPatches.Count == 0)
            {
                DrawHelpBox("No .sql patches found in the patch directory.",
                            MessageType.Info);
            }
            else
            {
                _patchScroll = EditorGUILayout.BeginScrollView(
                    _patchScroll, GUILayout.MaxHeight(140));

                foreach (var patch in _existingPatches)
                {
                    EditorGUILayout.BeginHorizontal();
                    string patchFileName = Path.GetFileName(patch);
                    GUILayout.Label(patchFileName, EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Open", EditorStyles.miniButton,
                                         GUILayout.Width(42)))
                        System.Diagnostics.Process.Start(patch);
                    if (GUILayout.Button("📂", EditorStyles.miniButton,
                                         GUILayout.Width(24)))
                        EditorUtility.RevealInFinder(patch);
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("✏️  Write New Patch", _subHeaderStyle);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Patch Name", GUILayout.Width(80));
            _customPatchName = EditorGUILayout.TextField(_customPatchName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("SQL", EditorStyles.miniLabel);
            _customPatchSql = EditorGUILayout.TextArea(
                _customPatchSql,
                GUILayout.Height(90));

            EditorGUILayout.Space(4);

            bool canWrite = !string.IsNullOrEmpty(_customPatchName) &&
                            !string.IsNullOrEmpty(_customPatchSql) &&
                            !string.IsNullOrEmpty(s.serverPatchDir);

            EditorGUI.BeginDisabledGroup(!canWrite);
            if (DrawActionButton(
                "Write Patch File",
                "Saves the SQL above as a new .sql file in the patch directory.",
                new Color(0.2f, 0.55f, 0.9f)))
            {
                WriteCustomPatch(s.serverPatchDir);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();

            EditorGUI.BeginDisabledGroup(
                string.IsNullOrEmpty(s.serverPatchDir) ||
                !Directory.Exists(s.serverPatchDir));

            if (DrawActionButton(
                "Open Patch Folder in Explorer",
                "Opens the SQL patches directory in your file explorer.",
                new Color(0.3f, 0.3f, 0.3f)))
            {
                EditorUtility.RevealInFinder(s.serverPatchDir);
            }
            EditorGUI.EndDisabledGroup();

            if (GUI.changed) s.Save();
        }

        private void RefreshPatches()
        {
            _existingPatches.Clear();
            var s = ArcheCoreDevToolsSettings.instance;
            if (string.IsNullOrEmpty(s.serverPatchDir) ||
                !Directory.Exists(s.serverPatchDir)) return;

            _existingPatches = Directory
                .GetFiles(s.serverPatchDir, "*.sql")
                .OrderBy(f => f)
                .ToList();
        }

        private void WriteCustomPatch(string patchDir)
        {
            try
            {
                string fileName = _customPatchName.EndsWith(".sql")
                    ? _customPatchName
                    : $"{_customPatchName}.sql";

                Directory.CreateDirectory(patchDir);
                string fullPath = Path.Combine(patchDir, fileName);

                if (File.Exists(fullPath))
                {
                    if (!EditorUtility.DisplayDialog(
                        "Overwrite Patch?",
                        $"{fileName} already exists. Overwrite it?",
                        "Overwrite", "Cancel"))
                        return;
                }

                var header = new StringBuilder();
                header.AppendLine($"-- Written by ArcheCore Dev Tools");
                header.AppendLine($"-- Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
                header.AppendLine();
                header.AppendLine(_customPatchSql);

                File.WriteAllText(fullPath, header.ToString());
                Log(LogLevel.Success, $"✓ Patch written: {fileName}");

                _customPatchName = "";
                _customPatchSql  = "";
                RefreshPatches();
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Write failed: {e.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Shared log panel
        // ─────────────────────────────────────────────────────────────────────
        private void DrawLog()
        {
            EditorGUILayout.BeginVertical(_sectionBoxStyle);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("📋  Log", _subHeaderStyle,
                                       GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Clear", EditorStyles.miniButton,
                                  GUILayout.Width(44)))
                _log.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            _logScroll = EditorGUILayout.BeginScrollView(
                _logScroll, GUILayout.Height(100));

            foreach (var entry in _log)
            {
                var style = entry.Level switch
                {
                    LogLevel.Success => _statusSuccessStyle,
                    LogLevel.Warning => _statusWarnStyle,
                    LogLevel.Error   => _statusErrorStyle,
                    _                => _logStyle
                };

                EditorGUILayout.LabelField(
                    $"[{entry.Timestamp}]  {entry.Message}", style);
            }

            if (_log.Count > 0)
            {
                _logScroll.y = float.MaxValue;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI helpers
        // ─────────────────────────────────────────────────────────────────────
        private void DrawPathField(string label, ref string value, bool isDir)
        {
            EditorGUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label))
                EditorGUILayout.LabelField(label, GUILayout.Width(190));
            value = EditorGUILayout.TextField(value);
            if (GUILayout.Button("…", EditorStyles.miniButton, GUILayout.Width(24)))
            {
                string picked;
                if (isDir)
                {
                    picked = EditorUtility.OpenFolderPanel(
                        "Select Folder",
                        !string.IsNullOrEmpty(value) && Directory.Exists(value) ? value : "",
                        "");
                }
                else
                {
                    string startDir = "";
                    if (!string.IsNullOrEmpty(value) && File.Exists(value))
                    {
                        try   { startDir = Path.GetDirectoryName(value) ?? ""; }
                        catch { startDir = ""; }
                    }

                    picked = EditorUtility.OpenFilePanel("Select File", startDir, "db");
                }

                if (!string.IsNullOrEmpty(picked))
                    value = picked;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFileStatus(string label, string path)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel,
                                       GUILayout.Width(160));

            bool exists = !string.IsNullOrEmpty(path) && File.Exists(path);
            var  style  = exists ? _statusSuccessStyle : _statusErrorStyle;
            string text = exists
                ? $"✓  {Path.GetFileName(path)}  " +
                  $"({new FileInfo(path).Length / 1024:N0} KB)"
                : "✗  Not found";

            EditorGUILayout.LabelField(text, style);
            EditorGUILayout.EndHorizontal();
        }

        private bool DrawActionButton(string label, string tooltip, Color accent)
        {
            var rect = EditorGUILayout.GetControlRect(false, 30);
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.y, 3, rect.height), accent);
            var btnRect = new Rect(rect.x + 6, rect.y, rect.width - 6, rect.height);
            return GUI.Button(btnRect, new GUIContent(label, tooltip));
        }

        private void DrawHelpBox(string msg, MessageType type)
        {
            EditorGUILayout.HelpBox(msg, type);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Crypto
        // ─────────────────────────────────────────────────────────────────────
        private static byte[] EncryptAes(byte[] plaintext)
        {
            using var aes = Aes.Create();
            aes.Key     = CryptoKey;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            using var ms        = new MemoryStream();
            using var encryptor = aes.CreateEncryptor();

            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                cs.Write(plaintext, 0, plaintext.Length);

            return ms.ToArray();
        }

        private static byte[] DecryptAes(byte[] data)
        {
            const int ivLen = 16;
            if (data.Length <= ivLen)
                throw new InvalidDataException("Data too short to contain IV.");

            byte[] iv         = new byte[ivLen];
            byte[] cipherText = new byte[data.Length - ivLen];
            Buffer.BlockCopy(data, 0,     iv,         0, ivLen);
            Buffer.BlockCopy(data, ivLen, cipherText, 0, cipherText.Length);

            using var aes = Aes.Create();
            aes.Key     = CryptoKey;
            aes.IV      = iv;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            using var ms        = new MemoryStream();
            using (var cs = new CryptoStream(
                new MemoryStream(cipherText), decryptor, CryptoStreamMode.Read))
                cs.CopyTo(ms);

            return ms.ToArray();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Utilities
        // ─────────────────────────────────────────────────────────────────────
        private void Log(LogLevel level, string msg)
        {
            _log.Add(new LogEntry(level, msg));
            Repaint();
        }

        private List<GameObject> FindNpcMarkers()
        {
            var result = new List<GameObject>();
            foreach (var obj in FindObjectsByType<GameObject>(
                         FindObjectsSortMode.None))
            {
                if (obj.CompareTag("NpcSpawner"))
                    result.Add(obj);
            }
            return result;
        }

        private static bool IsTagDefined(string tag)
        {
            try
            {
                GameObject.FindWithTag(tag);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}