// SpawnPointsTab.cs + ValidateTab.cs
// Assets/Editor/DevTools/Tabs/
//
// Two tabs kept in one file because each is small and they're only ever
// touched together — splitting them would be file-count theatre rather
// than organisation.

using System;
using System.Collections.Generic;
using System.Linq;
using ArcheCore.Editor.World;
using UnityEditor;
using UnityEngine;

namespace ArcheCore.Editor.DevTools.Tabs
{
    // ═════════════════════════════════════════════════════════════════════
    //  Player spawn points — where players actually enter the world.
    //  Previously only DB rows, so the only way to know where "Starting
    //  Meadow" was involved reading coordinates out of a grid and mentally
    //  mapping them onto terrain.
    // ═════════════════════════════════════════════════════════════════════
    public class SpawnPointsTab : IDevToolsTab
    {
        public string Title => "⚑  Spawn Points";
        public int    Order => 20;

        private const string Src = "SpawnPoints";
        private const string RootName = "~WorldMap_SpawnPoints";

        private List<SpawnPointRow> _dbRows = new();
        private bool _loaded;
        private Vector2 _scroll;

        public bool HasUnsavedChanges =>
            _loaded && FindMarkers().Any(m => !m.IsInDatabase || m.HasLocalChanges);

        public void OnEnable(DevToolsContext ctx) { if (ctx.HasWorldDb) Load(ctx); }
        public void OnDisable(DevToolsContext ctx) { }

        public void OnGUI(DevToolsContext ctx)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!ctx.HasWorldDb);
            if (GUILayout.Button("⟳ Load from DB", GUILayout.Height(24))) Load(ctx);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (!_loaded)
            {
                EditorGUILayout.HelpBox("Load the world database first.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("🔄  Scene Sync", ctx.Styles.SubHeader);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("DB → Scene", GUILayout.Height(26))) ToScene(ctx);
            GUI.color = new Color(0.5f, 0.9f, 0.5f);
            if (GUILayout.Button("Scene → DB", GUILayout.Height(26))) ToDatabase(ctx);
            GUI.color = Color.white;
            if (GUILayout.Button("+ Add at Camera", GUILayout.Height(26))) AddAtCamera(ctx);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            var markers = FindMarkers();
            int defaults = markers.Count(m => m.IsDefault);

            // The server picks the default spawn by this flag. Zero means
            // players have nowhere to land; more than one means the winner
            // depends on row order, which is a genuinely confusing bug to
            // chase from the server side.
            if (defaults == 0)
                EditorGUILayout.HelpBox(
                    "No spawn point is marked default — players will have nowhere to spawn.",
                    MessageType.Error);
            else if (defaults > 1)
                EditorGUILayout.HelpBox(
                    $"{defaults} spawn points marked default; only one can apply.", MessageType.Warning);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"⚑  Spawn Points ({markers.Count})", ctx.Styles.SubHeader);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(200));

            foreach (var m in markers)
            {
                EditorGUILayout.BeginHorizontal();

                GUILayout.Label(!m.IsInDatabase ? "NEW" : m.HasLocalChanges ? "EDIT" : "  ok",
                    !m.IsInDatabase || m.HasLocalChanges ? ctx.Styles.Warn : ctx.Styles.Ok,
                    GUILayout.Width(36));
                GUILayout.Label(m.IsInDatabase ? $"#{m.DbId}" : "—",
                    EditorStyles.miniLabel, GUILayout.Width(40));

                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField(m.SpawnName, GUILayout.Width(190));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(m, "Rename Spawn Point");
                    m.SpawnName = newName;
                    EditorUtility.SetDirty(m);
                }

                bool isDef = GUILayout.Toggle(m.IsDefault, "Default",
                    EditorStyles.miniButton, GUILayout.Width(66));
                if (isDef != m.IsDefault)
                {
                    Undo.RecordObject(m, "Set Default Spawn");
                    if (isDef)
                        foreach (var o in markers) { o.IsDefault = false; EditorUtility.SetDirty(o); }
                    m.IsDefault = isDef;
                    EditorUtility.SetDirty(m);
                }

                var p = m.transform.position;
                GUILayout.Label($"({p.x:F0}, {p.y:F0}, {p.z:F0})",
                    EditorStyles.miniLabel, GUILayout.Width(125));

                if (GUILayout.Button("◎", EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    Selection.activeGameObject = m.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24)))
                    Undo.DestroyObjectImmediate(m.gameObject);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void Load(DevToolsContext ctx)
        {
            try
            {
                _dbRows = WorldDbAccess.LoadSpawnPoints(ctx.WorldDbPath);
                _loaded = true;
                ctx.Success($"Loaded {_dbRows.Count} spawn point(s).", Src);
            }
            catch (Exception e)
            {
                _loaded = false;
                ctx.Error(WorldDbAccess.TryDescribeLockError(e), Src);
            }
        }

        private void ToScene(DevToolsContext ctx)
        {
            var root = WorldMapTab.GetOrCreateRoot(RootName);
            var existing = FindMarkers().Where(m => m.IsInDatabase).ToDictionary(m => m.DbId, m => m);
            int created = 0, updated = 0;

            foreach (var row in _dbRows)
            {
                if (!existing.TryGetValue(row.Id, out var marker))
                {
                    var go = new GameObject($"SpawnPoint_{row.Id}");
                    go.transform.SetParent(root.transform);
                    Undo.RegisterCreatedObjectUndo(go, "Load Spawn Point");

                    marker = go.AddComponent<PlayerSpawnPointMarker>();
                    if (marker == null)
                    {
                        ctx.Error("PlayerSpawnPointMarker.cs is in an Editor folder — move it to a " +
                                  "runtime folder such as Assets/ArcheCore.Client/Scripts/World/.", Src);
                        UnityEngine.Object.DestroyImmediate(go);
                        return;
                    }

                    marker.DbId = row.Id;
                    created++;
                }
                else updated++;

                marker.transform.position = new Vector3(row.X, row.Y, row.Z);
                marker.SpawnName = row.Name;
                marker.IsDefault = row.IsDefault;
                marker.name = $"SpawnPoint_{row.Id}_{row.Name}";
                marker.MarkSynced();
                EditorUtility.SetDirty(marker);
            }

            ctx.Success($"DB → Scene: {created} created, {updated} updated.", Src);
            SceneView.RepaintAll();
        }

        private void ToDatabase(DevToolsContext ctx)
        {
            var markers = FindMarkers();
            var inserts = new List<SpawnPointRow>();
            var updates = new List<SpawnPointRow>();

            foreach (var m in markers)
            {
                var row = new SpawnPointRow
                {
                    Id = m.DbId,
                    Name = m.SpawnName,
                    X = m.transform.position.x,
                    Y = m.transform.position.y,
                    Z = m.transform.position.z,
                    IsDefault = m.IsDefault
                };
                if (!m.IsInDatabase) inserts.Add(row); else if (m.HasLocalChanges) updates.Add(row);
            }

            var sceneIds = new HashSet<int>(markers.Where(m => m.IsInDatabase).Select(m => m.DbId));
            var deletes = sceneIds.Count > 0
                ? _dbRows.Where(r => !sceneIds.Contains(r.Id)).Select(r => r.Id).ToList()
                : new List<int>();

            if (inserts.Count + updates.Count + deletes.Count == 0)
            {
                ctx.Info("Nothing to sync.", Src);
                return;
            }

            if (!EditorUtility.DisplayDialog("Write spawn points?",
                $"+{inserts.Count} new\n~{updates.Count} updated\n-{deletes.Count} deleted\n\n" +
                "The WorldServer must not be running.", "Write", "Cancel"))
                return;

            try
            {
                WorldDbAccess.ApplySpawnPointChanges(ctx.WorldDbPath, inserts, updates, deletes);

                var newMarkers = markers.Where(m => !m.IsInDatabase).ToList();
                for (int i = 0; i < newMarkers.Count && i < inserts.Count; i++)
                    newMarkers[i].DbId = inserts[i].Id;

                // Re-assert single-default at DB level: per-row writes can't
                // guarantee it alone, since two rows could each carry
                // IsDefault=1 from separate edits.
                var def = markers.FirstOrDefault(m => m.IsDefault);
                if (def != null && def.DbId != 0)
                    WorldDbAccess.SetDefaultSpawnPoint(ctx.WorldDbPath, def.DbId);

                foreach (var m in markers) { m.MarkSynced(); EditorUtility.SetDirty(m); }
                ctx.Success($"Wrote +{inserts.Count} ~{updates.Count} -{deletes.Count}", Src);
                Load(ctx);
            }
            catch (Exception e) { ctx.Error(WorldDbAccess.TryDescribeLockError(e), Src); }
        }

        private void AddAtCamera(DevToolsContext ctx)
        {
            var sv = SceneView.lastActiveSceneView;
            Vector3 pos = sv != null ? sv.pivot : Vector3.zero;
            if (Physics.Raycast(pos + Vector3.up * 500f, Vector3.down, out var hit, 2000f))
                pos = hit.point;

            var root = WorldMapTab.GetOrCreateRoot(RootName);
            var go = new GameObject("SpawnPoint_NEW");
            go.transform.SetParent(root.transform);
            go.transform.position = pos;

            if (go.AddComponent<PlayerSpawnPointMarker>() == null)
            {
                ctx.Error("PlayerSpawnPointMarker.cs is in an Editor folder — move it.", Src);
                UnityEngine.Object.DestroyImmediate(go);
                return;
            }

            Undo.RegisterCreatedObjectUndo(go, "Add Spawn Point");
            Selection.activeGameObject = go;
            ctx.Info($"Added spawn point at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}).", Src);
        }

        private static List<PlayerSpawnPointMarker> FindMarkers() =>
            UnityEngine.Object.FindObjectsByType<PlayerSpawnPointMarker>(FindObjectsSortMode.None)
                .OrderBy(m => m.DbId == 0 ? int.MaxValue : m.DbId).ToList();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Validation — the failure modes that are invisible in a table view
    //  and only surface at runtime as "why isn't this NPC spawning".
    // ═════════════════════════════════════════════════════════════════════
    public class ValidateTab : IDevToolsTab
    {
        public string Title => "🔍  Validate";
        public int    Order => 30;

        private const string Src = "Validate";
        private bool _checkGround = true;
        private bool _checkDuplicates = true;

        public void OnEnable(DevToolsContext ctx) { }
        public void OnDisable(DevToolsContext ctx) { }

        public void OnGUI(DevToolsContext ctx)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("🔍  World Data Validation", ctx.Styles.SubHeader);
            EditorGUILayout.Space(3);

            _checkGround = EditorGUILayout.ToggleLeft(
                new GUIContent("Ground check (needs markers loaded into the scene)",
                    "Raycasts down from each scene marker. Spawners with nothing beneath them " +
                    "produce NPCs that fall through the world."),
                _checkGround);

            _checkDuplicates = EditorGUILayout.ToggleLeft(
                new GUIContent("Stacked duplicate spawners",
                    "Spawners at identical rounded coordinates — usually an accidental " +
                    "double-export, and it silently doubles NPC count in that spot."),
                _checkDuplicates);

            EditorGUILayout.Space(4);
            EditorGUI.BeginDisabledGroup(!ctx.HasWorldDb);
            if (DevToolsContext.ActionButton("Run Checks",
                "Validates spawners, templates and spawn points against the world database.",
                new Color(0.2f, 0.55f, 0.9f)))
                Run(ctx);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();

            EditorGUILayout.HelpBox(
                "Checks:\n" +
                "• Spawners referencing a TemplateId with no NpcTemplates row\n" +
                "• Count ≤ 0 or negative Radius\n" +
                "• Spawners with no collider beneath them\n" +
                "• Stacked duplicates at the same coordinates\n" +
                "• Spawn point default-flag problems (zero or multiple)",
                MessageType.None);
        }

        private void Run(DevToolsContext ctx)
        {
            int problems = 0;

            try
            {
                var templates = WorldDbAccess.LoadTemplates(ctx.WorldDbPath).ToDictionary(t => t.Id);
                var spawners  = WorldDbAccess.LoadSpawners(ctx.WorldDbPath);
                var points    = WorldDbAccess.LoadSpawnPoints(ctx.WorldDbPath);

                foreach (var r in spawners)
                {
                    if (!templates.ContainsKey(r.TemplateId))
                    {
                        ctx.Warn($"Spawner #{r.Id} references TemplateId {r.TemplateId}, which has no " +
                                 "NpcTemplates row — the server will skip it.", Src);
                        problems++;
                    }
                    if (r.Count <= 0)
                    {
                        ctx.Warn($"Spawner #{r.Id} has Count = {r.Count}; it will never spawn.", Src);
                        problems++;
                    }
                    if (r.Radius < 0f)
                    {
                        ctx.Warn($"Spawner #{r.Id} has negative Radius ({r.Radius}).", Src);
                        problems++;
                    }
                }

                if (_checkDuplicates)
                {
                    var stacked = spawners
                        .GroupBy(r => (Mathf.Round(r.X), Mathf.Round(r.Y), Mathf.Round(r.Z)))
                        .Where(g => g.Count() > 1);

                    foreach (var g in stacked)
                    {
                        ctx.Warn($"{g.Count()} spawners stacked at ({g.Key.Item1}, {g.Key.Item2}, " +
                                 $"{g.Key.Item3}): {string.Join(", ", g.Select(r => "#" + r.Id))}", Src);
                        problems++;
                    }
                }

                if (_checkGround)
                {
                    var markers = WorldMapTab.FindSceneSpawners();
                    if (markers.Count == 0)
                        ctx.Info("Ground check skipped — no markers in the scene. " +
                                 "Run 'DB → Scene' on the World Map tab first.", Src);

                    foreach (var m in markers)
                    {
                        if (!Physics.Raycast(m.transform.position + Vector3.up * 2f, Vector3.down, 1000f))
                        {
                            ctx.Warn($"Spawner {(m.IsInDatabase ? "#" + m.DbId : m.name)} has no " +
                                     "collider beneath it — NPCs will fall.", Src);
                            problems++;
                        }
                    }
                }

                int defaults = points.Count(p => p.IsDefault);
                if (defaults == 0)
                {
                    ctx.Error("No default spawn point — players cannot enter the world.", Src);
                    problems++;
                }
                else if (defaults > 1)
                {
                    ctx.Warn($"{defaults} spawn points flagged default; only one applies.", Src);
                    problems++;
                }

                if (problems == 0) ctx.Success("Validation passed — no problems found.", Src);
                else ctx.Info($"Validation finished: {problems} problem(s).", Src);
            }
            catch (Exception e)
            {
                ctx.Error(WorldDbAccess.TryDescribeLockError(e), Src);
            }
        }
    }
}