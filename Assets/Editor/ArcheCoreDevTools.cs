// ArcheCoreDevTools.cs
// Place this file in Assets/Editor/
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
        public string plaintextDbPath  = "";   // _oggamedata.db
        public string encryptedDbDir   = "";   // where to write gamedata.db copies
        public string authServerDbPath = "";   // AuthServer/src/gamedata.db

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
    //  Main window
    // ─────────────────────────────────────────────────────────────────────────
    public class ArcheCoreDevTools : EditorWindow
    {
        // ── Tabs ─────────────────────────────────────────────────────────────
        private enum Tab { GameData, NPC, Patches }
        private Tab _activeTab = Tab.GameData;

        // ── Shared log ───────────────────────────────────────────────────────
        private readonly List<LogEntry> _log = new();
        private Vector2 _logScroll;

        // ── NPC tab state ────────────────────────────────────────────────────
        private string  _npcPatchName   = "";
        private Vector2 _npcScroll;

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
            w.minSize = new Vector2(620, 600);
        }

        private void OnEnable()
        {
            RefreshPatches();
            AutoDetectPaths();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Auto-detect default paths relative to the Unity project root
        // ─────────────────────────────────────────────────────────────────────
        private void AutoDetectPaths()
        {
            var s = ArcheCoreDevToolsSettings.instance;
            if (!string.IsNullOrEmpty(s.serverPatchDir)) return;

            string root = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../.."));

            string candidatePatch = Path.Combine(
                root, "ArcheCore", "src",
                "ArcheCore.Server.World", "SQL", "patches");

            if (Directory.Exists(candidatePatch))
                s.serverPatchDir = candidatePatch;

            string candidateDevTools = Path.Combine(
                root, "ArcheCore.DevTools", "ArcheCore.DevTools");

            if (Directory.Exists(candidateDevTools))
            {
                s.plaintextDbPath = Path.Combine(candidateDevTools, "_oggamedata.db");
                s.encryptedDbDir  = candidateDevTools;
            }

            string candidateAuth = Path.Combine(
                root, "ArcheCore", "src", "ArcheCore.Server.Auth", "src");

            if (Directory.Exists(candidateAuth))
                s.authServerDbPath = Path.Combine(candidateAuth, "gamedata.db");

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
                case Tab.GameData: DrawGameDataTab(); break;
                case Tab.NPC:      DrawNpcTab();      break;
                case Tab.Patches:  DrawPatchesTab();  break;
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
            EditorGUI.LabelField(versionRect, "v1.0.0", EditorStyles.miniLabel);
            GUI.color = Color.white;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Tab bar
        // ─────────────────────────────────────────────────────────────────────
        private void DrawTabs()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            DrawTabButton(Tab.GameData, "📦  Game Data");
            DrawTabButton(Tab.NPC,      "👾  NPC Spawners");
            DrawTabButton(Tab.Patches,  "🗄  SQL Patches");

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
        //  TAB: GAME DATA
        // ═════════════════════════════════════════════════════════════════════
        private void DrawGameDataTab()
        {
            var s = ArcheCoreDevToolsSettings.instance;

            // ── Paths section ─────────────────────────────────────────────────
            // FIX: wrap DrawPathField calls in try/catch so an ArgumentException
            //      from Path.GetDirectoryName never leaves BeginVertical unmatched.
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

                // StreamingAssets copy
                string streamingDir = Path.Combine(
                    Application.streamingAssetsPath, "GameData");
                Directory.CreateDirectory(streamingDir);
                string streamingOut = Path.Combine(streamingDir, "gamedata.db");
                File.WriteAllBytes(streamingOut, encrypted);
                Log(LogLevel.Success,
                    $"Written to StreamingAssets/GameData/gamedata.db ({encrypted.Length:N0} bytes)");

                // AuthServer copy
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

                // DevTools encrypted copy
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
        //  TAB: NPC SPAWNERS
        // ═════════════════════════════════════════════════════════════════════
        private void DrawNpcTab()
        {
            var s = ArcheCoreDevToolsSettings.instance;

            // ── Patch output settings ─────────────────────────────────────────
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

            // ── Scene markers preview ─────────────────────────────────────────
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

            // ── Place new marker ──────────────────────────────────────────────
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

            // ── Export ────────────────────────────────────────────────────────
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
                "Writes INSERT statements for all markers to a .sql patch file.",
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

            // Ensure the NpcSpawner tag exists
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
        //  TAB: SQL PATCHES
        // ═════════════════════════════════════════════════════════════════════
        private void DrawPatchesTab()
        {
            var s = ArcheCoreDevToolsSettings.instance;

            // ── Patch dir path ────────────────────────────────────────────────
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

            // ── Existing patches ──────────────────────────────────────────────
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
                    string name = Path.GetFileName(patch);
                    GUILayout.Label(name, EditorStyles.miniLabel);
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

            // ── Write new patch ───────────────────────────────────────────────
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

            // ── Open patch folder ─────────────────────────────────────────────
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
                // Auto-scroll to bottom
                _logScroll.y = float.MaxValue;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI helpers
        // ─────────────────────────────────────────────────────────────────────

        // FIX: Guard Path.GetDirectoryName against empty/invalid strings.
        //      Passing "" or a string with illegal chars to GetDirectoryName
        //      throws ArgumentException, which previously left BeginVertical
        //      blocks unmatched and triggered the GUILayout state error.
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
                    // Only call GetDirectoryName when value is a non-empty path
                    // that actually exists — avoids ArgumentException on "" or
                    // strings containing illegal characters.
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