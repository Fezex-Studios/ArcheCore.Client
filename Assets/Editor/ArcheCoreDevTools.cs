// ArcheCoreDevTools.cs
// Place this file in Assets/ArcheCore/Editor/
// (namespace ArcheCore.Editor mirrors this folder structure)
// Open via: ArcheCore → Dev Tools

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        public string plaintextDbPath  = "";   // _oggamedata.db  (client-facing content package)
        public string encryptedDbDir   = "";   // where to write gamedata.db copies
        public string authServerDbPath = "";   // AuthServer/src/gamedata.db
        public string worldServerDbPath = "";  // WorldServer's LIVE runtime db (Data/worldserver.db)
                                                // NOTE: this is a completely different file from
                                                // plaintextDbPath above. NpcTemplates/NpcSpawners
                                                // live here, not in the encrypted content package.

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
    //  WorldServer DB rows (mirror ArcheCore.Server.World.GameData.Npcs classes,
    //  which live in Data/worldserver.db via EF Core — NOT the encrypted
    //  gamedata.db content package).
    // ─────────────────────────────────────────────────────────────────────────
    [Serializable]
    [SQLite.Table("NpcTemplates")]
    public class NpcTemplateRow
    {
        [SQLite.PrimaryKey] public int Id { get; set; }
        public string Name          { get; set; } = string.Empty;
        public int    Level         { get; set; }
        public string ModelType     { get; set; } = string.Empty;
        public float  InteractRange { get; set; } = 4f;

        // Not a DB column — tracks whether this row exists yet, so Save
        // knows whether to Insert or Update. NpcTemplates.Id is a
        // deliberately hand-assigned design ID (not autoincrement), so
        // new rows still need a value typed in before saving.
        [SQLite.Ignore] public bool IsNew { get; set; }
    }

    [Serializable]
    [SQLite.Table("NpcSpawners")]
    public class NpcSpawnerRow
    {
        [SQLite.PrimaryKey, SQLite.AutoIncrement] public int Id { get; set; }
        public int   TemplateId { get; set; }
        public float X          { get; set; }
        public float Y          { get; set; }
        public float Z          { get; set; }
        public int   Count      { get; set; } = 1;
        public float Radius     { get; set; } = 1f;

        [SQLite.Ignore] public bool IsNew { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Client gamedata.db row (mirrors ArcheCore.Client.GameData.ItemRecord)
    // ─────────────────────────────────────────────────────────────────────────
    [Serializable]
    [SQLite.Table("items")]
    public class ItemDataRow
    {
        [SQLite.PrimaryKey, SQLite.Column("item_id")] public int ItemId { get; set; }
        [SQLite.Column("name")]        public string Name        { get; set; } = string.Empty;
        [SQLite.Column("description")] public string Description { get; set; } = string.Empty;
        [SQLite.Column("category")]    public int    Category    { get; set; }
        [SQLite.Column("icon_name")]   public string IconName    { get; set; } = string.Empty;

        [SQLite.Ignore] public bool IsNew { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Main window
    // ─────────────────────────────────────────────────────────────────────────
    public class ArcheCoreDevTools : EditorWindow
    {
        // ── Tabs ─────────────────────────────────────────────────────────────
        private enum Tab { GameData, SpawnMarkers, WorldDb, ClientData, Patches }
        private Tab _activeTab = Tab.GameData;

        // ── Shared log ───────────────────────────────────────────────────────
        private readonly List<LogEntry> _log = new();
        private Vector2 _logScroll;

        // ── Spawn Markers tab state (scene → SQL patch export) ───────────────
        private string  _npcPatchName   = "";
        private Vector2 _npcScroll;

        // ── World DB tab state ────────────────────────────────────────────────
        private List<NpcTemplateRow> _templates = new();
        private List<NpcSpawnerRow>  _spawners  = new();
        private Vector2 _templatesScroll;
        private Vector2 _spawnersScroll;
        private bool    _templatesDirty;
        private bool    _spawnersDirty;

        // ── Client Data tab state ────────────────────────────────────────────
        private List<ItemDataRow> _items = new();
        private Vector2 _itemsScroll;
        private bool    _itemsDirty;

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
            w.minSize = new Vector2(680, 640);
        }

        private void OnEnable()
        {
            RefreshPatches();
            AutoDetectPaths();
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

            if (string.IsNullOrEmpty(s.authServerDbPath))
            {
                string candidateAuth = Path.Combine(
                    root, "ArcheCore", "src", "ArcheCore.Server.Auth", "src");

                if (Directory.Exists(candidateAuth))
                    s.authServerDbPath = Path.Combine(candidateAuth, "gamedata.db");
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
                case Tab.WorldDb:      DrawWorldDbTab();      break;
                case Tab.ClientData:   DrawClientDataTab();   break;
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
            EditorGUI.LabelField(versionRect, "v1.1.0", EditorStyles.miniLabel);
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
            DrawTabButton(Tab.WorldDb,      "🗺  World DB");
            DrawTabButton(Tab.ClientData,   "🎒  Client Data");
            DrawTabButton(Tab.Patches,      "🗄  SQL Patches");

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
        //  TAB: GAME DATA  (encrypt/deploy pipeline for the client content
        //  package — _oggamedata.db → gamedata.db, copied to StreamingAssets
        //  and AuthServer. This is a DIFFERENT file from World DB below.)
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
                DrawPathField("Encrypted Output Dir",
                    ref s.encryptedDbDir, true);
                DrawPathField("AuthServer gamedata.db Path",
                    ref s.authServerDbPath, false);
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

            DrawFileStatus("Plaintext DB",   s.plaintextDbPath);
            DrawFileStatus("AuthServer DB",  s.authServerDbPath);

            string streamingDb = Path.Combine(
                Application.streamingAssetsPath, "GameData", "gamedata.db");
            DrawFileStatus("StreamingAssets DB", streamingDb);

            EditorGUILayout.EndVertical();

            // ── Actions ───────────────────────────────────────────────────────
            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("⚡  Actions", _subHeaderStyle);
            EditorGUILayout.Space(4);

            bool hasPlaintext = File.Exists(s.plaintextDbPath);
            bool hasOutputDir = !string.IsNullOrEmpty(s.encryptedDbDir);

            EditorGUI.BeginDisabledGroup(!hasPlaintext || !hasOutputDir);
            if (DrawActionButton(
                "Encrypt → StreamingAssets + AuthServer",
                "AES-256 encrypts _oggamedata.db and copies to both destinations.",
                new Color(0.2f, 0.55f, 0.9f)))
            {
                EncryptAndDeploy(s);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(2);

            EditorGUI.BeginDisabledGroup(!File.Exists(streamingDb));
            if (DrawActionButton(
                "Decrypt StreamingAssets → Plaintext",
                "Decrypts the current StreamingAssets copy back to _oggamedata.db for editing.",
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

            EditorGUILayout.EndVertical();

            if (GUI.changed) s.Save();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Encrypt + Deploy
        // ─────────────────────────────────────────────────────────────────────
        private void EncryptAndDeploy(ArcheCoreDevToolsSettings s)
        {
            try
            {
                byte[] plaintext = File.ReadAllBytes(s.plaintextDbPath);
                byte[] encrypted = EncryptAes(plaintext);

                string streamingDir = Path.Combine(
                    Application.streamingAssetsPath, "GameData");
                Directory.CreateDirectory(streamingDir);
                string streamingOut = Path.Combine(streamingDir, "gamedata.db");
                File.WriteAllBytes(streamingOut, encrypted);
                Log(LogLevel.Success,
                    $"Written to StreamingAssets/GameData/gamedata.db ({encrypted.Length:N0} bytes)");

                if (!string.IsNullOrEmpty(s.authServerDbPath))
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(s.authServerDbPath)!);
                    File.WriteAllBytes(s.authServerDbPath, encrypted);
                    Log(LogLevel.Success,
                        $"Written to AuthServer: {s.authServerDbPath}");
                }
                else
                {
                    Log(LogLevel.Warning,
                        "AuthServer path not set — skipped AuthServer copy.");
                }

                if (!string.IsNullOrEmpty(s.encryptedDbDir))
                {
                    string devOut = Path.Combine(s.encryptedDbDir, "gamedata.db");
                    File.WriteAllBytes(devOut, encrypted);
                    Log(LogLevel.Success, $"Written to DevTools dir: {devOut}");
                }

                AssetDatabase.Refresh();
                Log(LogLevel.Success, "✓ Encrypt + Deploy complete.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Encryption failed: {e.Message}");
            }
        }

        private void DecryptFromStreaming(ArcheCoreDevToolsSettings s,
                                          string streamingDb)
        {
            try
            {
                byte[] encrypted  = File.ReadAllBytes(streamingDb);
                byte[] decrypted  = DecryptAes(encrypted);
                string outputPath = s.plaintextDbPath;

                if (string.IsNullOrEmpty(outputPath))
                    outputPath = Path.Combine(
                        Path.GetDirectoryName(streamingDb)!, "_oggamedata.db");

                File.WriteAllBytes(outputPath, decrypted);
                Log(LogLevel.Success,
                    $"✓ Decrypted to: {outputPath} ({decrypted.Length:N0} bytes)");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Decryption failed: {e.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  TAB: SPAWN MARKERS  (place NPCs in-scene, export as a .sql patch —
        //  a separate workflow from the direct World DB editing below; useful
        //  when you want to visually place several spawners at once before
        //  committing anything to the live DB.)
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
        //  TAB: WORLD DB  (direct editing of Data/worldserver.db — the LIVE
        //  database WorldServer actually reads at runtime via EF Core.
        //  NpcTemplates and NpcSpawners both live here.)
        // ═════════════════════════════════════════════════════════════════════
        private void DrawWorldDbTab()
        {
            var s = ArcheCoreDevToolsSettings.instance;

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("📁  Source", _subHeaderStyle);
            EditorGUILayout.Space(4);
            try
            {
                DrawPathField("WorldServer DB (worldserver.db)",
                    ref s.worldServerDbPath, false);
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox($"Path error: {e.Message}", MessageType.Error);
            }

            bool hasDb = File.Exists(s.worldServerDbPath);
            if (!hasDb)
            {
                DrawHelpBox(
                    "worldserver.db not found. This is the live file WorldServer " +
                    "reads at runtime (Database:WorldDb in appsettings.json) — " +
                    "different from _oggamedata.db on the Game Data tab.",
                    MessageType.Warning);
            }

            DrawHelpBox(
                "Stop the WorldServer before editing — it holds this file open " +
                "and won't pick up changes until restarted anyway.",
                MessageType.Info);

            EditorGUILayout.EndVertical();

            DrawTemplatesSection(s.worldServerDbPath, hasDb);
            DrawSpawnersSection(s.worldServerDbPath, hasDb);

            if (GUI.changed) s.Save();
        }

        private void DrawTemplatesSection(string dbPath, bool hasDb)
        {
            EditorGUILayout.BeginVertical(_sectionBoxStyle);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"🐗  NpcTemplates  ({_templates.Count})", _subHeaderStyle,
                GUILayout.ExpandWidth(true));

            EditorGUI.BeginDisabledGroup(!hasDb);
            if (GUILayout.Button("Load", GUILayout.Width(70)))
                LoadTemplates(dbPath);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!hasDb);
            if (GUILayout.Button("+ Add", GUILayout.Width(60)))
                AddNewTemplate();
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!_templatesDirty);
            GUI.color = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("Save", GUILayout.Width(70)))
                SaveTemplates(dbPath);
            GUI.color = Color.white;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            if (_templatesDirty)
                DrawHelpBox("Unsaved template changes.", MessageType.Warning);

            if (_templates.Count == 0)
            {
                DrawHelpBox("No templates loaded. Click Load.", MessageType.Info);
            }
            else
            {
                DrawTemplateTableHeader();

                _templatesScroll = EditorGUILayout.BeginScrollView(
                    _templatesScroll, GUILayout.MaxHeight(220));

                NpcTemplateRow toDelete = null;
                foreach (var t in _templates)
                {
                    if (DrawTemplateRow(t))
                        toDelete = t;
                }

                if (toDelete != null)
                {
                    if (!toDelete.IsNew)
                        DeleteTemplate(dbPath, toDelete);
                    _templates.Remove(toDelete);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSpawnersSection(string dbPath, bool hasDb)
        {
            EditorGUILayout.BeginVertical(_sectionBoxStyle);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"📍  NpcSpawners  ({_spawners.Count})", _subHeaderStyle,
                GUILayout.ExpandWidth(true));

            EditorGUI.BeginDisabledGroup(!hasDb);
            if (GUILayout.Button("Load", GUILayout.Width(70)))
                LoadSpawners(dbPath);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!hasDb);
            if (GUILayout.Button("+ Add", GUILayout.Width(60)))
                AddNewSpawner();
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!_spawnersDirty);
            GUI.color = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("Save", GUILayout.Width(70)))
                SaveSpawners(dbPath);
            GUI.color = Color.white;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            if (_spawnersDirty)
                DrawHelpBox("Unsaved spawner changes.", MessageType.Warning);

            var knownTemplateIds = _templates.Select(t => t.Id).ToHashSet();
            bool templatesLoaded = _templates.Count > 0;

            if (_spawners.Count == 0)
            {
                DrawHelpBox("No spawners loaded. Click Load.", MessageType.Info);
            }
            else
            {
                DrawSpawnerTableHeader();

                _spawnersScroll = EditorGUILayout.BeginScrollView(
                    _spawnersScroll, GUILayout.MaxHeight(220));

                NpcSpawnerRow toDelete = null;
                foreach (var sp in _spawners)
                {
                    bool unknownTemplate = templatesLoaded &&
                                           !knownTemplateIds.Contains(sp.TemplateId);
                    if (DrawSpawnerRow(sp, unknownTemplate))
                        toDelete = sp;
                }

                if (toDelete != null)
                {
                    if (!toDelete.IsNew)
                        DeleteSpawner(dbPath, toDelete);
                    _spawners.Remove(toDelete);
                }

                EditorGUILayout.EndScrollView();
            }

            if (!templatesLoaded)
                DrawHelpBox(
                    "Load NpcTemplates above too, to catch spawners pointing at a " +
                    "TemplateId that doesn't exist.",
                    MessageType.Info);

            EditorGUILayout.EndVertical();
        }

        private void DrawTemplateTableHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Id",            EditorStyles.miniLabel, GUILayout.Width(30));
            GUILayout.Label("Name",          EditorStyles.miniLabel, GUILayout.Width(130));
            GUILayout.Label("Level",         EditorStyles.miniLabel, GUILayout.Width(45));
            GUILayout.Label("ModelType",     EditorStyles.miniLabel, GUILayout.Width(100));
            GUILayout.Label("InteractRange", EditorStyles.miniLabel, GUILayout.Width(85));
            GUILayout.Label("",              EditorStyles.miniLabel, GUILayout.Width(24));
            EditorGUILayout.EndHorizontal();

            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax, rect.width, 1),
                new Color(0.4f, 0.4f, 0.4f));
            EditorGUILayout.Space(2);
        }

        /// <returns>true if the row's delete button was clicked</returns>
        private bool DrawTemplateRow(NpcTemplateRow t)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();

            if (t.IsNew)
                t.Id = EditorGUILayout.IntField(t.Id, GUILayout.Width(30));
            else
                GUILayout.Label(t.Id.ToString(), EditorStyles.miniLabel, GUILayout.Width(30));

            t.Name          = EditorGUILayout.TextField(t.Name, GUILayout.Width(130));
            t.Level         = EditorGUILayout.IntField(t.Level, GUILayout.Width(45));
            t.ModelType     = EditorGUILayout.TextField(t.ModelType, GUILayout.Width(100));
            t.InteractRange = EditorGUILayout.FloatField(t.InteractRange, GUILayout.Width(85));

            bool delete = GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24));

            EditorGUILayout.EndHorizontal();

            if (t.IsNew)
            {
                GUI.color = new Color(0.95f, 0.75f, 0.2f);
                EditorGUILayout.LabelField("  new — not saved yet", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            if (EditorGUI.EndChangeCheck())
                _templatesDirty = true;

            return delete;
        }

        private void DrawSpawnerTableHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Id",         EditorStyles.miniLabel, GUILayout.Width(30));
            GUILayout.Label("TemplateId", EditorStyles.miniLabel, GUILayout.Width(70));
            GUILayout.Label("X",          EditorStyles.miniLabel, GUILayout.Width(55));
            GUILayout.Label("Y",          EditorStyles.miniLabel, GUILayout.Width(55));
            GUILayout.Label("Z",          EditorStyles.miniLabel, GUILayout.Width(55));
            GUILayout.Label("Count",      EditorStyles.miniLabel, GUILayout.Width(45));
            GUILayout.Label("Radius",     EditorStyles.miniLabel, GUILayout.Width(50));
            GUILayout.Label("",           EditorStyles.miniLabel, GUILayout.Width(24));
            EditorGUILayout.EndHorizontal();

            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax, rect.width, 1),
                new Color(0.4f, 0.4f, 0.4f));
            EditorGUILayout.Space(2);
        }

        /// <returns>true if the row's delete button was clicked</returns>
        private bool DrawSpawnerRow(NpcSpawnerRow sp, bool unknownTemplate)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();

            string idLabel = sp.IsNew ? "new" : sp.Id.ToString();
            GUILayout.Label(idLabel, EditorStyles.miniLabel, GUILayout.Width(30));

            if (unknownTemplate) GUI.color = new Color(1f, 0.5f, 0.5f);
            sp.TemplateId = EditorGUILayout.IntField(sp.TemplateId, GUILayout.Width(70));
            GUI.color = Color.white;

            sp.X      = EditorGUILayout.FloatField(sp.X, GUILayout.Width(55));
            sp.Y      = EditorGUILayout.FloatField(sp.Y, GUILayout.Width(55));
            sp.Z      = EditorGUILayout.FloatField(sp.Z, GUILayout.Width(55));
            sp.Count  = EditorGUILayout.IntField(sp.Count, GUILayout.Width(45));
            sp.Radius = EditorGUILayout.FloatField(sp.Radius, GUILayout.Width(50));

            bool delete = GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24));

            EditorGUILayout.EndHorizontal();

            if (unknownTemplate)
            {
                GUI.color = new Color(1f, 0.5f, 0.5f);
                EditorGUILayout.LabelField(
                    $"  ⚠ TemplateId {sp.TemplateId} not found in loaded NpcTemplates",
                    EditorStyles.miniLabel);
                GUI.color = Color.white;
            }
            else if (sp.IsNew)
            {
                GUI.color = new Color(0.95f, 0.75f, 0.2f);
                EditorGUILayout.LabelField("  new — not saved yet", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            if (EditorGUI.EndChangeCheck())
                _spawnersDirty = true;

            return delete;
        }

        private void AddNewTemplate()
        {
            int nextId = _templates.Count > 0 ? _templates.Max(t => t.Id) + 1 : 1;
            _templates.Add(new NpcTemplateRow
            {
                Id            = nextId,
                Name          = "New NPC",
                Level         = 1,
                ModelType     = "",
                InteractRange = 4f,
                IsNew         = true
            });
            _templatesDirty = true;
            Log(LogLevel.Info, $"Added new template row (Id {nextId} — edit before saving).");
        }

        private void AddNewSpawner()
        {
            _spawners.Add(new NpcSpawnerRow
            {
                Id         = 0,
                TemplateId = _templates.Count > 0 ? _templates[0].Id : 0,
                X = 0, Y = 0, Z = 0,
                Count  = 1,
                Radius = 1f,
                IsNew  = true
            });
            _spawnersDirty = true;
            Log(LogLevel.Info, "Added new spawner row — set TemplateId and position, then Save.");
        }

        private void LoadTemplates(string dbPath)
        {
            try
            {
                using (var conn = new SQLite.SQLiteConnection(dbPath))
                {
                    _templates = conn.Table<NpcTemplateRow>().OrderBy(r => r.Id).ToList();
                }
                _templatesDirty = false;
                Log(LogLevel.Success, $"Loaded {_templates.Count} NPC template(s).");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Failed to load templates: {e.Message}");
            }
        }

        private void SaveTemplates(string dbPath)
        {
            try
            {
                var templatesSnapshot = _templates;

                using (var conn = new SQLite.SQLiteConnection(dbPath))
                {
                    conn.RunInTransaction(() =>
                    {
                        foreach (var t in templatesSnapshot)
                        {
                            if (t.IsNew)
                            {
                                conn.Insert(t);
                                t.IsNew = false;
                            }
                            else
                            {
                                conn.Update(t);
                            }
                        }
                    });
                }

                _templatesDirty = false;
                Log(LogLevel.Success,
                    $"✓ Saved {_templates.Count} template(s) to {Path.GetFileName(dbPath)}.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Save failed: {e.Message}");
            }
        }

        private void DeleteTemplate(string dbPath, NpcTemplateRow t)
        {
            try
            {
                using (var conn = new SQLite.SQLiteConnection(dbPath))
                {
                    conn.Delete(t);
                }
                Log(LogLevel.Success, $"✓ Deleted template Id {t.Id}.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Delete failed: {e.Message}");
            }
        }

        private void LoadSpawners(string dbPath)
        {
            try
            {
                using (var conn = new SQLite.SQLiteConnection(dbPath))
                {
                    _spawners = conn.Table<NpcSpawnerRow>().OrderBy(r => r.Id).ToList();
                }
                _spawnersDirty = false;
                Log(LogLevel.Success, $"Loaded {_spawners.Count} NPC spawner(s).");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Failed to load spawners: {e.Message}");
            }
        }

        private void SaveSpawners(string dbPath)
        {
            try
            {
                var spawnersSnapshot = _spawners;

                using (var conn = new SQLite.SQLiteConnection(dbPath))
                {
                    conn.RunInTransaction(() =>
                    {
                        foreach (var sp in spawnersSnapshot)
                        {
                            if (sp.IsNew)
                            {
                                conn.Insert(sp); // AutoIncrement fills sp.Id here
                                sp.IsNew = false;
                            }
                            else
                            {
                                conn.Update(sp);
                            }
                        }
                    });
                }

                _spawnersDirty = false;
                Log(LogLevel.Success,
                    $"✓ Saved {_spawners.Count} spawner(s) to {Path.GetFileName(dbPath)}.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Save failed: {e.Message}");
            }
        }

        private void DeleteSpawner(string dbPath, NpcSpawnerRow sp)
        {
            try
            {
                using (var conn = new SQLite.SQLiteConnection(dbPath))
                {
                    conn.Delete(sp);
                }
                Log(LogLevel.Success, $"✓ Deleted spawner Id {sp.Id}.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Delete failed: {e.Message}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  TAB: CLIENT DATA  (edits the `items` table inside _oggamedata.db —
        //  the same plaintext file the Game Data tab encrypts/deploys. Editing
        //  here does NOT touch worldserver.db.)
        // ═════════════════════════════════════════════════════════════════════
        private void DrawClientDataTab()
        {
            var s = ArcheCoreDevToolsSettings.instance;
            bool hasDb = File.Exists(s.plaintextDbPath);

            EditorGUILayout.BeginVertical(_sectionBoxStyle);
            EditorGUILayout.LabelField("📁  Source", _subHeaderStyle);
            EditorGUILayout.Space(4);

            if (!hasDb)
            {
                DrawHelpBox(
                    "No plaintext DB found. Set the path on the Game Data tab, " +
                    "or use 'Decrypt StreamingAssets → Plaintext' there first.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(
                    $"Editing: {s.plaintextDbPath}", EditorStyles.miniLabel);
            }

            DrawHelpBox(
                "After saving, run 'Encrypt → StreamingAssets + AuthServer' on the " +
                "Game Data tab to push this to where the client/AuthServer actually read it.",
                MessageType.Info);

            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(_sectionBoxStyle);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"🎒  Items  ({_items.Count})", _subHeaderStyle,
                GUILayout.ExpandWidth(true));

            EditorGUI.BeginDisabledGroup(!hasDb);
            if (GUILayout.Button("Load", GUILayout.Width(70)))
                LoadItems(s.plaintextDbPath);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!hasDb);
            if (GUILayout.Button("+ Add", GUILayout.Width(60)))
                AddNewItem();
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!_itemsDirty);
            GUI.color = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button("Save", GUILayout.Width(70)))
                SaveItems(s.plaintextDbPath);
            GUI.color = Color.white;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            if (_itemsDirty)
                DrawHelpBox("Unsaved item changes.", MessageType.Warning);

            if (_items.Count == 0)
            {
                DrawHelpBox("No items loaded. Click Load.", MessageType.Info);
            }
            else
            {
                DrawItemTableHeader();

                _itemsScroll = EditorGUILayout.BeginScrollView(
                    _itemsScroll, GUILayout.MaxHeight(300));

                ItemDataRow toDelete = null;
                foreach (var item in _items)
                {
                    if (DrawItemRow(item))
                        toDelete = item;
                }

                if (toDelete != null)
                {
                    if (!toDelete.IsNew)
                        DeleteItem(s.plaintextDbPath, toDelete);
                    _items.Remove(toDelete);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawItemTableHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("item_id",     EditorStyles.miniLabel, GUILayout.Width(50));
            GUILayout.Label("name",        EditorStyles.miniLabel, GUILayout.Width(110));
            GUILayout.Label("description", EditorStyles.miniLabel, GUILayout.Width(160));
            GUILayout.Label("category",    EditorStyles.miniLabel, GUILayout.Width(60));
            GUILayout.Label("icon_name",   EditorStyles.miniLabel, GUILayout.Width(100));
            GUILayout.Label("",            EditorStyles.miniLabel, GUILayout.Width(24));
            EditorGUILayout.EndHorizontal();

            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(
                new Rect(rect.x, rect.yMax, rect.width, 1),
                new Color(0.4f, 0.4f, 0.4f));
            EditorGUILayout.Space(2);
        }

        /// <returns>true if the row's delete button was clicked</returns>
        private bool DrawItemRow(ItemDataRow item)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();

            if (item.IsNew)
                item.ItemId = EditorGUILayout.IntField(item.ItemId, GUILayout.Width(50));
            else
                GUILayout.Label(item.ItemId.ToString(), EditorStyles.miniLabel, GUILayout.Width(50));

            item.Name        = EditorGUILayout.TextField(item.Name, GUILayout.Width(110));
            item.Description = EditorGUILayout.TextField(item.Description, GUILayout.Width(160));
            item.Category    = EditorGUILayout.IntField(item.Category, GUILayout.Width(60));
            item.IconName    = EditorGUILayout.TextField(item.IconName, GUILayout.Width(100));

            bool delete = GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24));

            EditorGUILayout.EndHorizontal();

            if (item.IsNew)
            {
                GUI.color = new Color(0.95f, 0.75f, 0.2f);
                EditorGUILayout.LabelField("  new — not saved yet", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            if (EditorGUI.EndChangeCheck())
                _itemsDirty = true;

            return delete;
        }

        private void AddNewItem()
        {
            int nextId = _items.Count > 0 ? _items.Max(i => i.ItemId) + 1 : 1;
            _items.Add(new ItemDataRow
            {
                ItemId      = nextId,
                Name        = "New Item",
                Description = "",
                Category    = 0,
                IconName    = "",
                IsNew       = true
            });
            _itemsDirty = true;
            Log(LogLevel.Info, $"Added new item row (item_id {nextId} — edit before saving).");
        }

        private void LoadItems(string dbPath)
        {
            try
            {
                using (var conn = new SQLite.SQLiteConnection(dbPath))
                {
                    _items = conn.Table<ItemDataRow>().OrderBy(r => r.ItemId).ToList();
                }
                _itemsDirty = false;
                Log(LogLevel.Success, $"Loaded {_items.Count} item(s).");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Failed to load items: {e.Message}");
            }
        }

        private void SaveItems(string dbPath)
        {
            try
            {
                var itemsSnapshot = _items;

                using (var conn = new SQLite.SQLiteConnection(dbPath))
                {
                    conn.RunInTransaction(() =>
                    {
                        foreach (var item in itemsSnapshot)
                        {
                            if (item.IsNew)
                            {
                                conn.Insert(item);
                                item.IsNew = false;
                            }
                            else
                            {
                                conn.Update(item);
                            }
                        }
                    });
                }

                _itemsDirty = false;
                Log(LogLevel.Success,
                    $"✓ Saved {_items.Count} item(s) to {Path.GetFileName(dbPath)}.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Save failed: {e.Message}");
            }
        }

        private void DeleteItem(string dbPath, ItemDataRow item)
        {
            try
            {
                using (var conn = new SQLite.SQLiteConnection(dbPath))
                {
                    conn.Delete(item);
                }
                Log(LogLevel.Success, $"✓ Deleted item_id {item.ItemId}.");
            }
            catch (Exception e)
            {
                Log(LogLevel.Error, $"Delete failed: {e.Message}");
            }
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