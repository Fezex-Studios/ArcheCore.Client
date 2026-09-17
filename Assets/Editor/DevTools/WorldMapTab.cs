// WorldMapTab.cs
// Assets/Editor/DevTools/Tabs/
//
// NPC spawner placement with bidirectional worldserver.db sync.
//
// Replaces the old "Spawn Markers" tab, which was one-way: place markers,
// export SQL, and the scene object then had no idea whether the row it
// created still existed, had moved, or had been edited elsewhere. Here
// every marker carries its DbId, so the tab can tell "row 42, moved 3
// units" apart from "brand new spawner" and compute a real diff.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ArcheCore.Editor.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcheCore.Editor.DevTools.Tabs
{
    public class WorldMapTab : IDevToolsTab
    {
        public string Title => "🗺  World Map";
        public int    Order => 10;

        private const string Src = "WorldMap";
        private const string SpawnerRootName = "~WorldMap_NpcSpawners";

        private List<NpcTemplateRow> _templates = new();
        private List<NpcSpawnerRow>  _dbSpawners = new();
        private Dictionary<int, NpcTemplateRow> _templateById = new();
        private string[] _templateLabels = Array.Empty<string>();
        private int[]    _templateIds    = Array.Empty<int>();

        private bool     _loaded;
        private string   _loadedFrom = "";
        private DateTime _loadedAt;

        private string _search = "";
        private int    _filterTemplateId = -1;
        private bool   _onlyChanged;
        private Vector2 _listScroll;

        public bool HasUnsavedChanges
        {
            get
            {
                if (!_loaded) return false;
                var markers = FindSceneSpawners();
                return markers.Any(m => !m.IsInDatabase || m.HasLocalChanges);
            }
        }

        public void OnEnable(DevToolsContext ctx)
        {
            if (ctx.HasWorldDb) LoadFromDatabase(ctx);
        }

        public void OnDisable(DevToolsContext ctx) { }

        // ─────────────────────────────────────────────────────────────────
        public void OnGUI(DevToolsContext ctx)
        {
            DrawConnectionBar(ctx);

            if (!_loaded)
            {
                EditorGUILayout.HelpBox(
                    "Load the world database to begin. Spawner rows become movable scene objects.",
                    MessageType.Info);
                return;
            }

            DrawSyncActions(ctx);

            var (inserts, updates, deletes) = ComputeDiff();
            DrawDiffSummary(ctx, inserts.Count, updates.Count, deletes.Count);

            DrawFilters();
            DrawMarkerList(ctx);
        }

        private void DrawConnectionBar(DevToolsContext ctx)
        {
            EditorGUILayout.BeginVertical("box");
            var s = ctx.Settings;

            EditorGUILayout.BeginHorizontal();
            string path = s.worldServerDbPath;
            if (DevToolsContext.PathField("World DB", ref path, false, "db", 70))
            {
                s.worldServerDbPath = path;
                s.Save();
            }

            EditorGUI.BeginDisabledGroup(!ctx.HasWorldDb);
            GUI.color = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("⟳ Load", GUILayout.Width(70)))
                LoadFromDatabase(ctx);
            GUI.color = Color.white;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_loaded)
                EditorGUILayout.LabelField(
                    $"{_dbSpawners.Count} spawner(s), {_templates.Count} template(s) — loaded {_loadedAt:HH:mm:ss}",
                    ctx.Styles.Ok);

            EditorGUILayout.EndVertical();
        }

        private void DrawSyncActions(DevToolsContext ctx)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("🔄  Scene Sync", ctx.Styles.SubHeader);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("DB → Scene",
                "Creates a movable GameObject for every spawner row, under one root object. " +
                "Markers already linked to a row are updated in place, not duplicated."),
                GUILayout.Height(26)))
                SpawnersToScene(ctx);

            GUI.color = new Color(0.5f, 0.9f, 0.5f);
            if (GUILayout.Button(new GUIContent("Scene → DB",
                "Writes inserts/updates/deletes into worldserver.db in one transaction. " +
                "Fails while the WorldServer is running and holding the file lock."),
                GUILayout.Height(26)))
                SyncToDatabase(ctx);
            GUI.color = Color.white;

            if (GUILayout.Button(new GUIContent("Export Patch",
                "Same changes as a reviewable .sql file. Use for anything leaving your machine."),
                GUILayout.Height(26)))
                ExportPatch(ctx);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add at Camera")) AddAtCamera(ctx);
            if (GUILayout.Button("Clear Scene Markers")) ClearMarkers(ctx);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawFilters()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search", GUILayout.Width(50));
            _search = EditorGUILayout.TextField(_search);

            int idx = Array.IndexOf(_templateIds, _filterTemplateId);
            if (idx < 0) idx = 0;
            idx = EditorGUILayout.Popup(idx, _templateLabels, GUILayout.Width(180));
            _filterTemplateId = _templateIds.Length > idx ? _templateIds[idx] : -1;

            _onlyChanged = GUILayout.Toggle(_onlyChanged, "Changed only",
                EditorStyles.miniButton, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawMarkerList(DevToolsContext ctx)
        {
            var markers = FindSceneSpawners().Where(PassesFilter).ToList();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"👾  Scene Markers ({markers.Count} shown)", ctx.Styles.SubHeader);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.MinHeight(200));

            if (markers.Count == 0)
                EditorGUILayout.HelpBox("No markers match. Try 'DB → Scene' or clear the filter.",
                    MessageType.Info);

            foreach (var m in markers) DrawMarkerRow(ctx, m);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private bool PassesFilter(NpcSpawnerMarker m)
        {
            if (_onlyChanged && m.IsInDatabase && !m.HasLocalChanges) return false;
            if (_filterTemplateId >= 0 && m.TemplateId != _filterTemplateId) return false;
            if (!string.IsNullOrEmpty(_search))
            {
                bool hit = (m.NpcName ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                           || m.TemplateId.ToString() == _search
                           || m.DbId.ToString() == _search;
                if (!hit) return false;
            }
            return true;
        }

        private void DrawMarkerRow(DevToolsContext ctx, NpcSpawnerMarker m)
        {
            EditorGUILayout.BeginHorizontal();

            string state = !m.IsInDatabase ? "NEW" : m.HasLocalChanges ? "EDIT" : "  ok";
            GUILayout.Label(state,
                !m.IsInDatabase || m.HasLocalChanges ? ctx.Styles.Warn : ctx.Styles.Ok,
                GUILayout.Width(36));

            GUILayout.Label(m.IsInDatabase ? $"#{m.DbId}" : "—", EditorStyles.miniLabel, GUILayout.Width(40));

            int idx = Array.IndexOf(_templateIds, m.TemplateId);
            if (idx < 0)
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                GUILayout.Label($"⚠ bad template {m.TemplateId}", EditorStyles.miniLabel, GUILayout.Width(180));
                GUI.color = Color.white;
            }
            else
            {
                int newIdx = EditorGUILayout.Popup(idx, _templateLabels, GUILayout.Width(180));
                if (newIdx != idx && newIdx > 0)
                {
                    Undo.RecordObject(m, "Change Template");
                    m.TemplateId = _templateIds[newIdx];
                    m.NpcName = _templateById[m.TemplateId].Name;
                    EditorUtility.SetDirty(m);
                }
            }

            EditorGUI.BeginChangeCheck();
            int count = EditorGUILayout.IntField(m.Count, GUILayout.Width(38));
            float radius = EditorGUILayout.FloatField(m.Radius, GUILayout.Width(46));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(m, "Edit Spawner");
                m.Count = Mathf.Max(1, count);
                m.Radius = Mathf.Max(0f, radius);
                EditorUtility.SetDirty(m);
            }

            var p = m.transform.position;
            GUILayout.Label($"({p.x:F0}, {p.y:F0}, {p.z:F0})", EditorStyles.miniLabel, GUILayout.Width(125));

            if (GUILayout.Button("◎", EditorStyles.miniButton, GUILayout.Width(24)))
            {
                Selection.activeGameObject = m.gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24)))
                Undo.DestroyObjectImmediate(m.gameObject);

            EditorGUILayout.EndHorizontal();
        }

        // ─────────────────────────────────────────────────────────────────
        private void LoadFromDatabase(DevToolsContext ctx)
        {
            try
            {
                _templates  = WorldDbAccess.LoadTemplates(ctx.WorldDbPath);
                _dbSpawners = WorldDbAccess.LoadSpawners(ctx.WorldDbPath);
                _templateById = _templates.ToDictionary(t => t.Id);

                _templateLabels = new[] { "(All templates)" }
                    .Concat(_templates.Select(t => $"[{t.Id}] {t.Name}")).ToArray();
                _templateIds = new[] { -1 }.Concat(_templates.Select(t => t.Id)).ToArray();

                _loaded = true;
                _loadedFrom = ctx.WorldDbPath;
                _loadedAt = DateTime.Now;

                ctx.Success($"Loaded {_dbSpawners.Count} spawner(s), {_templates.Count} template(s).", Src);
            }
            catch (Exception e)
            {
                _loaded = false;
                ctx.Error(WorldDbAccess.TryDescribeLockError(e), Src);
            }
        }

        private void SpawnersToScene(DevToolsContext ctx)
        {
            var root = GetOrCreateRoot(SpawnerRootName);
            var existing = FindSceneSpawners().Where(m => m.IsInDatabase)
                .ToDictionary(m => m.DbId, m => m);

            int created = 0, updated = 0;

            foreach (var row in _dbSpawners)
            {
                if (!existing.TryGetValue(row.Id, out var marker))
                {
                    var go = new GameObject($"Spawner_{row.Id}");
                    go.transform.SetParent(root.transform);
                    Undo.RegisterCreatedObjectUndo(go, "Load Spawner");
                    if (IsTagDefined("NpcSpawner")) go.tag = "NpcSpawner";

                    marker = go.AddComponent<NpcSpawnerMarker>();

                    // AddComponent returns null if the marker script sits in an
                    // Editor folder — Unity refuses to attach editor scripts to
                    // GameObjects. Without this check that surfaces as a bare
                    // NullReferenceException three lines later with no hint at
                    // the real cause, which is a genuinely confusing first-run
                    // experience.
                    if (marker == null)
                    {
                        ctx.Error(
                            "Couldn't add NpcSpawnerMarker — the script is in an 'Editor' folder. " +
                            "Move NpcSpawnerMarker.cs and PlayerSpawnPointMarker.cs to a runtime " +
                            "folder such as Assets/ArcheCore.Client/Scripts/World/.", Src);
                        UnityEngine.Object.DestroyImmediate(go);
                        return;
                    }

                    marker.DbId = row.Id;
                    created++;
                }
                else updated++;

                marker.transform.position = new Vector3(row.X, row.Y, row.Z);
                marker.TemplateId = row.TemplateId;
                marker.Count      = row.Count;
                marker.Radius     = row.Radius;
                marker.NpcName    = _templateById.TryGetValue(row.TemplateId, out var t)
                    ? t.Name : $"(unknown template {row.TemplateId})";
                marker.name = $"Spawner_{row.Id}_{marker.NpcName}";
                marker.MarkSynced();
                EditorUtility.SetDirty(marker);
            }

            if (created + updated == 0)
                ctx.Warn("No spawner rows in the database — nothing to place.", Src);
            else
                ctx.Success($"DB → Scene: {created} created, {updated} updated.", Src);

            SceneView.RepaintAll();
        }

        private (List<NpcSpawnerRow>, List<NpcSpawnerRow>, List<int>) ComputeDiff()
        {
            var inserts = new List<NpcSpawnerRow>();
            var updates = new List<NpcSpawnerRow>();
            var markers = FindSceneSpawners();
            var sceneIds = new HashSet<int>(markers.Where(m => m.IsInDatabase).Select(m => m.DbId));

            foreach (var m in markers)
            {
                var row = new NpcSpawnerRow
                {
                    Id = m.DbId,
                    TemplateId = m.TemplateId,
                    X = m.transform.position.x,
                    Y = m.transform.position.y,
                    Z = m.transform.position.z,
                    Count = m.Count,
                    Radius = m.Radius
                };
                if (!m.IsInDatabase) inserts.Add(row);
                else if (m.HasLocalChanges) updates.Add(row);
            }

            // Deletions require the scene to actually have been populated from
            // this DB. Treating an empty scene as "delete every spawner" is
            // trivially easy to trigger by opening the window in a fresh scene
            // and unrecoverable once written.
            var deletes = new List<int>();
            if (sceneIds.Count > 0)
                deletes.AddRange(_dbSpawners.Where(r => !sceneIds.Contains(r.Id)).Select(r => r.Id));

            return (inserts, updates, deletes);
        }

        private void DrawDiffSummary(DevToolsContext ctx, int ins, int upd, int del)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("📊  Pending", ctx.Styles.SubHeader, GUILayout.Width(80));

            GUI.color = ins > 0 ? new Color(0.5f, 0.9f, 0.5f) : Color.gray;
            GUILayout.Label($"+{ins} new", EditorStyles.miniLabel, GUILayout.Width(66));
            GUI.color = upd > 0 ? new Color(1f, 0.8f, 0.3f) : Color.gray;
            GUILayout.Label($"~{upd} edited", EditorStyles.miniLabel, GUILayout.Width(76));
            GUI.color = del > 0 ? new Color(1f, 0.45f, 0.45f) : Color.gray;
            GUILayout.Label($"-{del} removed", EditorStyles.miniLabel, GUILayout.Width(86));
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (del > 0)
                EditorGUILayout.HelpBox(
                    $"{del} row(s) exist in the DB with no scene marker and will be DELETED. " +
                    "If you only meant to hide markers, reload from DB first.", MessageType.Warning);

            EditorGUILayout.EndVertical();
        }

        private void SyncToDatabase(DevToolsContext ctx)
        {
            var (inserts, updates, deletes) = ComputeDiff();
            if (inserts.Count + updates.Count + deletes.Count == 0)
            {
                ctx.Info("Nothing to sync — scene matches the database.", Src);
                return;
            }

            if (!EditorUtility.DisplayDialog("Write to worldserver.db?",
                $"+{inserts.Count} new\n~{updates.Count} updated\n-{deletes.Count} deleted\n\n" +
                "The WorldServer must not be running.", "Write", "Cancel"))
                return;

            try
            {
                WorldDbAccess.ApplySpawnerChanges(ctx.WorldDbPath, inserts, updates, deletes);

                var newMarkers = FindSceneSpawners().Where(m => !m.IsInDatabase).ToList();
                for (int i = 0; i < newMarkers.Count && i < inserts.Count; i++)
                {
                    newMarkers[i].DbId = inserts[i].Id;
                    newMarkers[i].name = $"Spawner_{inserts[i].Id}_{newMarkers[i].NpcName}";
                }
                foreach (var m in FindSceneSpawners()) { m.MarkSynced(); EditorUtility.SetDirty(m); }

                ctx.Success($"Wrote +{inserts.Count} ~{updates.Count} -{deletes.Count}", Src);
                LoadFromDatabase(ctx);
            }
            catch (Exception e)
            {
                ctx.Error(WorldDbAccess.TryDescribeLockError(e), Src);
            }
        }

        private void ExportPatch(DevToolsContext ctx)
        {
            var (inserts, updates, deletes) = ComputeDiff();
            if (inserts.Count + updates.Count + deletes.Count == 0)
            {
                ctx.Info("Nothing to export.", Src);
                return;
            }
            if (string.IsNullOrEmpty(ctx.PatchDir))
            {
                ctx.Error("Server patch directory not set (SQL Patches tab).", Src);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("-- Auto-generated by ArcheCore Dev Tools (World Map)");
            sb.AppendLine($"-- Scene: {SceneManager.GetActiveScene().name}");
            sb.AppendLine($"-- Date:  {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"-- Base:  {Path.GetFileName(_loadedFrom)} as loaded {_loadedAt:HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine();

            foreach (var id in deletes)
                sb.AppendLine($"DELETE FROM \"NpcSpawners\" WHERE \"Id\" = {id};");
            if (deletes.Count > 0) sb.AppendLine();

            foreach (var r in updates)
                sb.AppendLine($"UPDATE \"NpcSpawners\" SET \"TemplateId\" = {r.TemplateId}, " +
                              $"\"X\" = {r.X:F4}, \"Y\" = {r.Y:F4}, \"Z\" = {r.Z:F4}, " +
                              $"\"Count\" = {r.Count}, \"Radius\" = {r.Radius:F2} WHERE \"Id\" = {r.Id};");
            if (updates.Count > 0) sb.AppendLine();

            foreach (var r in inserts)
            {
                string n = _templateById.TryGetValue(r.TemplateId, out var t) ? t.Name : "?";
                sb.AppendLine($"-- {n} x{r.Count}");
                sb.AppendLine($"INSERT INTO \"NpcSpawners\" (\"TemplateId\", \"X\", \"Y\", \"Z\", \"Count\", \"Radius\") " +
                              $"VALUES ({r.TemplateId}, {r.X:F4}, {r.Y:F4}, {r.Z:F4}, {r.Count}, {r.Radius:F2});");
            }

            sb.AppendLine();
            sb.AppendLine("COMMIT;");

            try
            {
                Directory.CreateDirectory(ctx.PatchDir);
                string file = Path.Combine(ctx.PatchDir, $"{DateTime.Now:yyyyMMdd_HHmm}_worldmap_spawners.sql");
                File.WriteAllText(file, sb.ToString());
                AssetDatabase.Refresh();
                ctx.Success($"Patch written: {Path.GetFileName(file)}", Src);
                EditorUtility.RevealInFinder(file);
            }
            catch (Exception e) { ctx.Error($"Patch export failed: {e.Message}", Src); }
        }

        private void AddAtCamera(DevToolsContext ctx)
        {
            var sv = SceneView.lastActiveSceneView;
            Vector3 pos = sv != null ? sv.pivot : Vector3.zero;
            if (Physics.Raycast(pos + Vector3.up * 500f, Vector3.down, out var hit, 2000f))
                pos = hit.point;

            var root = GetOrCreateRoot(SpawnerRootName);
            var go = new GameObject("Spawner_NEW");
            go.transform.SetParent(root.transform);
            go.transform.position = pos;
            if (IsTagDefined("NpcSpawner")) go.tag = "NpcSpawner";

            var m = go.AddComponent<NpcSpawnerMarker>();
            if (m == null)
            {
                ctx.Error("NpcSpawnerMarker.cs is in an Editor folder — move it to a runtime folder.", Src);
                UnityEngine.Object.DestroyImmediate(go);
                return;
            }

            if (_templates.Count > 0) { m.TemplateId = _templates[0].Id; m.NpcName = _templates[0].Name; }

            Undo.RegisterCreatedObjectUndo(go, "Add Spawner");
            Selection.activeGameObject = go;
            ctx.Info($"Added spawner at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}).", Src);
        }

        private void ClearMarkers(DevToolsContext ctx)
        {
            var markers = FindSceneSpawners();
            int unsaved = markers.Count(m => !m.IsInDatabase || m.HasLocalChanges);

            if (unsaved > 0 && !EditorUtility.DisplayDialog("Discard unsaved markers?",
                $"{unsaved} marker(s) have changes not in the database. " +
                "Clearing removes them from the scene; the database is untouched.",
                "Clear anyway", "Cancel"))
                return;

            foreach (var m in markers) Undo.DestroyObjectImmediate(m.gameObject);
            ctx.Info($"Cleared {markers.Count} marker(s).", Src);
        }

        // ─────────────────────────────────────────────────────────────────
        public void OnSceneGUI(DevToolsContext ctx, SceneView sv)
        {
            if (!_loaded) return;

            Handles.BeginGUI();
            var r = new Rect(10, 10, 200, 62);
            GUI.Box(r, GUIContent.none);
            GUILayout.BeginArea(new Rect(r.x + 8, r.y + 5, r.width - 16, r.height - 10));

            GUILayout.Label("World Map", EditorStyles.boldLabel);
            var markers = FindSceneSpawners();
            int changed = markers.Count(m => !m.IsInDatabase || m.HasLocalChanges);

            GUILayout.Label($"{markers.Count} spawner(s)", EditorStyles.miniLabel);
            GUI.color = changed > 0 ? new Color(1f, 0.8f, 0.3f) : new Color(0.5f, 0.9f, 0.5f);
            GUILayout.Label(changed > 0 ? $"{changed} unsaved" : "in sync", EditorStyles.miniLabel);
            GUI.color = Color.white;

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        // ─────────────────────────────────────────────────────────────────
        internal static GameObject GetOrCreateRoot(string name)
        {
            var existing = GameObject.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create World Map Root");
            return go;
        }

        internal static List<NpcSpawnerMarker> FindSceneSpawners() =>
            UnityEngine.Object.FindObjectsByType<NpcSpawnerMarker>(FindObjectsSortMode.None)
                .OrderBy(m => m.DbId == 0 ? int.MaxValue : m.DbId).ToList();

        internal static bool IsTagDefined(string tag)
        {
            try { GameObject.FindWithTag(tag); return true; }
            catch { return false; }
        }
    }
}