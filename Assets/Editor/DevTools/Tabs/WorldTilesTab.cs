// WorldTilesTab.cs
// Assets/Editor/DevTools/Tabs/
//
// Authoring side of world partitioning. Cuts the world into the tile
// scenes WorldStreamer streams at runtime, and exports the heightmaps
// the world server stitches into one seamless ground.
//
// THE MODEL (see WorldGrid for the full reasoning)
//
//   main_world      persistent scene - camera, lighting, sky, registries,
//                   UI, spawn/NPC markers. Never streamed.
//   tile_{x}_{z}    one scene per 512m WorldGrid tile - terrain and static
//                   scenery at real world coordinates. Streamed in a 3x3
//                   block around the player.
//
// WORKFLOW
//
//   1. Create terrain tiles for the area you're building (aligned 512m
//      terrains that auto-connect at their seams), or Split an existing
//      scene's scenery into tile scenes.
//   2. Edit tiles by opening several additively (they're normal scenes).
//   3. Add tile scenes to Build Settings.
//   4. Export All Terrain for the server, after every sculpting pass.
//   5. Validate before committing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArcheCore.Client.Editor;
using ArcheCore.Movement.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcheCore.Editor.DevTools.Tabs
{
    public class WorldTilesTab : IDevToolsTab
    {
        public string Title => "🧱  World Tiles";
        public int    Order => 15;

        private const string Src = "WorldTiles";

        private const string ExportDirPref  = "ArcheCore.WorldTiles.ExportDir";

        private string _tileFolder;
        private string _exportDir;

        // Create
        private int _fromX, _fromZ, _toX, _toZ;
        private int _heightmapResolution = 513;
        private float _maxHeight = 600f;
        private bool _withTerrain = true;

        // Scene view overlay
        private bool _drawGrid = true;

        private Vector2 _scroll;
        private List<string> _tileScenePaths = new List<string>();
        private readonly List<string> _report = new List<string>();

        public void OnEnable(DevToolsContext ctx)
        {
            _tileFolder = TileSceneIndex.Folder;
            _exportDir = EditorPrefs.GetString(ExportDirPref, "");

            if (string.IsNullOrEmpty(_exportDir))
                _exportDir = GuessServerTerrainDir(ctx);

            RefreshTileScenes();
        }

        public void OnDisable(DevToolsContext ctx) { }

        // ─────────────────────────────────────────────────────────────────
        //  GUI
        // ─────────────────────────────────────────────────────────────────

        public void OnGUI(DevToolsContext ctx)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DevToolsContext.Section("📐  Partition", ctx.Styles.SubHeader, () =>
            {
                EditorGUILayout.LabelField(
                    $"Tile size {WorldGrid.TileSize:0}m (WorldGrid, shared with the server). " +
                    $"Streaming keeps the 3x3 block around the player loaded.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Tile scene folder", GUILayout.Width(190));
                string folder = EditorGUILayout.TextField(_tileFolder);
                if (folder != _tileFolder)
                {
                    _tileFolder = folder;
                    TileSceneIndex.Folder = _tileFolder;
                }
                if (GUILayout.Button("↻", EditorStyles.miniButton, GUILayout.Width(24)))
                    RefreshTileScenes();
                EditorGUILayout.EndHorizontal();

                int inBuild = _tileScenePaths.Count(IsInBuildSettings);
                EditorGUILayout.LabelField(
                    $"{_tileScenePaths.Count} tile scene(s) found, {inBuild} in Build Settings.",
                    inBuild == _tileScenePaths.Count ? ctx.Styles.Ok : ctx.Styles.Err);

                _drawGrid = EditorGUILayout.ToggleLeft("Draw tile grid in the Scene view (while this tab is open)", _drawGrid);
            });

            EditorGUILayout.Space(6);

            DevToolsContext.Section("➕  Create tiles", ctx.Styles.SubHeader, () =>
            {
                EditorGUILayout.LabelField(
                    "Creates tile scenes for a rectangle of tiles. With terrain, each gets a 512m terrain " +
                    "aligned exactly to its tile, auto-connected to its neighbours so seams match - the " +
                    "recommended starting point for new land. Existing tile scenes are left untouched.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("From tile (x, z)", GUILayout.Width(190));
                _fromX = EditorGUILayout.IntField(_fromX, GUILayout.Width(60));
                _fromZ = EditorGUILayout.IntField(_fromZ, GUILayout.Width(60));
                EditorGUILayout.LabelField("to", GUILayout.Width(20));
                _toX = EditorGUILayout.IntField(_toX, GUILayout.Width(60));
                _toZ = EditorGUILayout.IntField(_toZ, GUILayout.Width(60));
                if (GUILayout.Button("Tile under Scene camera", EditorStyles.miniButton))
                {
                    var t = TileUnderSceneCamera();
                    _fromX = _toX = t.X;
                    _fromZ = _toZ = t.Z;
                }
                EditorGUILayout.EndHorizontal();

                _withTerrain = EditorGUILayout.Toggle("Create terrain", _withTerrain);
                using (new EditorGUI.DisabledScope(!_withTerrain))
                {
                    _heightmapResolution = EditorGUILayout.IntPopup("Heightmap resolution", _heightmapResolution,
                        new[] { "257 (2m)", "513 (1m)", "1025 (0.5m)" }, new[] { 257, 513, 1025 });
                    _maxHeight = EditorGUILayout.FloatField("Max terrain height (m)", _maxHeight);
                }

                int count = (Math.Abs(_toX - _fromX) + 1) * (Math.Abs(_toZ - _fromZ) + 1);
                if (DevToolsContext.ActionButton($"Create {count} tile scene(s)", "", new Color(0.3f, 0.7f, 1f)))
                    CreateTiles(ctx);
            });

            EditorGUILayout.Space(6);

            DevToolsContext.Section("✂  Split a scene into tiles", ctx.Styles.SubHeader, () =>
            {
                EditorGUILayout.LabelField(
                    "Moves the ACTIVE scene's static scenery - every root object with a Terrain or any " +
                    "Static flag - into the tile scene its position falls in, creating tile scenes as needed. " +
                    "Cameras, lights, canvases, markers and anything not marked Static stay where they are. " +
                    "Commit to version control first: moving objects between scenes can't be undone.",
                    EditorStyles.wordWrappedMiniLabel);

                if (DevToolsContext.ActionButton("Preview split", "", new Color(0.6f, 0.6f, 0.6f), 24))
                    PreviewSplit(ctx, execute: false);

                if (DevToolsContext.ActionButton("Split active scene into tile scenes", "", new Color(0.95f, 0.6f, 0.2f)))
                    PreviewSplit(ctx, execute: true);
            });

            EditorGUILayout.Space(6);

            DevToolsContext.Section("🏗  Build Settings", ctx.Styles.SubHeader, () =>
            {
                EditorGUILayout.LabelField(
                    "WorldStreamer can only load scenes that are in Build Settings. A tile scene that isn't " +
                    "listed is treated as empty land at runtime - no error, just nothing there.",
                    EditorStyles.wordWrappedMiniLabel);

                if (DevToolsContext.ActionButton("Add all tile scenes to Build Settings", "", new Color(0.4f, 0.8f, 0.4f)))
                    AddTilesToBuildSettings(ctx);
            });

            EditorGUILayout.Space(6);

            DevToolsContext.Section("⛰  Export terrain for the server", ctx.Styles.SubHeader, () =>
            {
                EditorGUILayout.LabelField(
                    "Opens every tile scene in turn and exports each terrain to one .achtmap, into the world " +
                    "server's TerrainDirectory. The server stitches them into one seamless ground for movement " +
                    "validation and NPC walking. Re-run after every sculpting pass - a stale export is the " +
                    "\"server thinks you're underground\" bug.",
                    EditorStyles.wordWrappedMiniLabel);

                string dir = _exportDir;
                if (DevToolsContext.PathField("Server terrain folder", ref dir, true))
                {
                    _exportDir = dir;
                    EditorPrefs.SetString(ExportDirPref, _exportDir);
                }

                if (DevToolsContext.ActionButton("Export All Terrain (every tile scene + open scenes)", "", new Color(0.3f, 0.8f, 0.7f)))
                    ExportAllTerrain(ctx);
            });

            EditorGUILayout.Space(6);

            DevToolsContext.Section("🔍  Validate", ctx.Styles.SubHeader, () =>
            {
                if (DevToolsContext.ActionButton("Validate world partition", "", new Color(0.8f, 0.8f, 0.3f), 24))
                    Validate(ctx);

                foreach (var line in _report)
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            });

            EditorGUILayout.EndScrollView();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Scene view overlay
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Tile outlines DRAPED ON THE GROUND around the Scene camera: each
        /// edge is sampled every 16m against the loaded terrain, so the lines
        /// follow hills instead of floating at the camera's pivot height
        /// (which is what the first version did - they slid around as you
        /// orbited). Where no terrain is loaded the line sits at height 0.
        ///
        ///   bright blue  tile scene exists and is OPEN
        ///   dim blue     tile scene exists but isn't open (drag it in, or
        ///                use Zones > Open tiles) - that's why you can't see it
        ///   grey         no tile scene - empty land/sea
        /// </summary>
        public void OnSceneGUI(DevToolsContext ctx, SceneView sceneView)
        {
            if (!_drawGrid || Event.current.type != EventType.Repaint)
                return;

            var centre = TileUnderSceneCamera();

            var existing = new HashSet<TileCoord>();
            foreach (var path in _tileScenePaths)
                if (WorldGrid.TryParseSceneName(path, out var c)) existing.Add(c);

            var open = new HashSet<TileCoord>();
            foreach (var scene in TileSceneIndex.OpenTileScenes())
                if (WorldGrid.TryParseSceneName(scene.name, out var c)) open.Add(c);

            var terrains = Terrain.activeTerrains;
            const int Radius = 3;
            const float Step = 16f;
            const float Lift = 0.5f;
            float size = WorldGrid.TileSize;
            int steps = Mathf.CeilToInt(size / Step);
            var edge = new Vector3[steps + 1];

            for (int dx = -Radius; dx <= Radius; dx++)
            for (int dz = -Radius; dz <= Radius; dz++)
            {
                var tile = new TileCoord(centre.X + dx, centre.Z + dz);
                float x0 = WorldGrid.MinX(tile), z0 = WorldGrid.MinZ(tile);

                bool has = existing.Contains(tile);
                bool isOpen = open.Contains(tile);

                // Each edge is drawn once, by the tile north/east of it, in the
                // BRIGHTER of the two tiles' colours - so an open tile is
                // outlined bright on all four sides, whatever its neighbours are.
                int self = TileState(tile, existing, open);
                int south = TileState(new TileCoord(tile.X, tile.Z - 1), existing, open);
                int west = TileState(new TileCoord(tile.X - 1, tile.Z), existing, open);

                Handles.color = StateColor(Mathf.Max(self, south));
                DrawGroundEdge(terrains, edge, x0, z0, x0 + size, z0, steps, Lift);
                Handles.color = StateColor(Mathf.Max(self, west));
                DrawGroundEdge(terrains, edge, x0, z0, x0, z0 + size, steps, Lift);

                // The outer ring closes the block.
                if (dz == Radius)
                {
                    Handles.color = StateColor(Mathf.Max(self, TileState(new TileCoord(tile.X, tile.Z + 1), existing, open)));
                    DrawGroundEdge(terrains, edge, x0, z0 + size, x0 + size, z0 + size, steps, Lift);
                }
                if (dx == Radius)
                {
                    Handles.color = StateColor(Mathf.Max(self, TileState(new TileCoord(tile.X + 1, tile.Z), existing, open)));
                    DrawGroundEdge(terrains, edge, x0 + size, z0, x0 + size, z0 + size, steps, Lift);
                }

                if (has || (dx == 0 && dz == 0))
                {
                    float cx = x0 + size * 0.5f, cz = z0 + size * 0.5f;
                    float gy = TileSceneIndex.GroundHeight(terrains, cx, cz);
                    Handles.Label(new Vector3(cx, gy + 3f, cz),
                        has ? (isOpen ? tile.SceneName : tile.SceneName + "  (not open)") : tile.SceneName + "  (no scene)");
                }
            }
        }

        /// <summary>0 = no scene, 1 = scene exists but closed, 2 = open.</summary>
        private static int TileState(TileCoord tile, HashSet<TileCoord> existing, HashSet<TileCoord> open) =>
            open.Contains(tile) ? 2 : existing.Contains(tile) ? 1 : 0;

        private static Color StateColor(int state) =>
            state == 2 ? new Color(0.3f, 0.75f, 1f, 0.95f)
          : state == 1 ? new Color(0.3f, 0.75f, 1f, 0.35f)
                       : new Color(0.7f, 0.7f, 0.7f, 0.2f);

        private static void DrawGroundEdge(Terrain[] terrains, Vector3[] points,
            float ax, float az, float bx, float bz, int steps, float lift)
        {
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = ax + (bx - ax) * t, z = az + (bz - az) * t;
                points[i] = new Vector3(x, TileSceneIndex.GroundHeight(terrains, x, z) + lift, z);
            }

            Handles.DrawPolyLine(points);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Create
        // ─────────────────────────────────────────────────────────────────

        private void CreateTiles(DevToolsContext ctx)
        {
            if (!EnsureFolder(_tileFolder)) { ctx.Error($"Tile folder '{_tileFolder}' must be under Assets/.", Src); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            int minX = Math.Min(_fromX, _toX), maxX = Math.Max(_fromX, _toX);
            int minZ = Math.Min(_fromZ, _toZ), maxZ = Math.Max(_fromZ, _toZ);
            int created = 0, skipped = 0;

            try
            {
                int total = (maxX - minX + 1) * (maxZ - minZ + 1), i = 0;

                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    var tile = new TileCoord(x, z);
                    string path = ScenePathFor(tile);
                    EditorUtility.DisplayProgressBar("Create tiles", tile.SceneName, (float)i++ / total);

                    if (File.Exists(path)) { skipped++; continue; }

                    Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

                    if (_withTerrain)
                    {
                        var terrainGo = CreateTileTerrain(tile);
                        SceneManager.MoveGameObjectToScene(terrainGo, scene);
                    }

                    EditorSceneManager.SaveScene(scene, path);
                    EditorSceneManager.CloseScene(scene, true);
                    created++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            RefreshTileScenes();
            ctx.Success($"Created {created} tile scene(s), skipped {skipped} that already existed. " +
                        "Add them to Build Settings before playing.", Src);
        }

        /// <summary>
        /// A terrain covering exactly one tile. Resolution is set BEFORE size
        /// - Unity rescales size when resolution changes, the other order
        /// silently produces the wrong dimensions.
        /// </summary>
        private GameObject CreateTileTerrain(TileCoord tile)
        {
            string dataFolder = _tileFolder + "/TerrainData";
            EnsureFolder(dataFolder);

            var data = new TerrainData
            {
                heightmapResolution = _heightmapResolution
            };
            data.size = new Vector3(WorldGrid.TileSize, Mathf.Max(1f, _maxHeight), WorldGrid.TileSize);

            AssetDatabase.CreateAsset(data, $"{dataFolder}/{tile.SceneName}_TerrainData.asset");

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = $"Terrain_{tile.SceneName}";
            go.transform.position = new Vector3(WorldGrid.MinX(tile), 0f, WorldGrid.MinZ(tile));

            // Same grouping id + auto-connect = Unity stitches neighbouring
            // tiles' seams (heights and LOD) whenever both are loaded, in the
            // Editor and at runtime.
            var terrain = go.GetComponent<Terrain>();
            terrain.groupingID = 0;
            terrain.allowAutoConnect = true;

            return go;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Split
        // ─────────────────────────────────────────────────────────────────

        private void PreviewSplit(DevToolsContext ctx, bool execute)
        {
            Scene source = SceneManager.GetActiveScene();
            if (WorldGrid.TryParseSceneName(source.name, out _))
            {
                ctx.Error("The active scene is already a tile scene. Make the world scene you want to split active.", Src);
                return;
            }

            var byTile = new SortedDictionary<string, List<GameObject>>(StringComparer.Ordinal);
            var tooBig = new List<string>();

            foreach (var root in source.GetRootGameObjects())
            {
                if (!IsScenery(root)) continue;

                var terrain = root.GetComponent<Terrain>();
                if (terrain != null && terrain.terrainData != null &&
                    (terrain.terrainData.size.x > WorldGrid.TileSize + 0.01f || terrain.terrainData.size.z > WorldGrid.TileSize + 0.01f))
                {
                    tooBig.Add($"{root.name} ({terrain.terrainData.size.x:0}x{terrain.terrainData.size.z:0}m)");
                    continue;
                }

                Vector3 p = root.transform.position;
                string key = WorldGrid.TileOf(p.x, p.z).SceneName;
                if (!byTile.TryGetValue(key, out var list)) byTile[key] = list = new List<GameObject>();
                list.Add(root);
            }

            _report.Clear();
            _report.Add($"Split of '{source.name}': {byTile.Sum(k => k.Value.Count)} object(s) into {byTile.Count} tile scene(s).");
            foreach (var kv in byTile)
                _report.Add($"   {kv.Key}: {kv.Value.Count}  ({string.Join(", ", kv.Value.Take(4).Select(g => g.name))}{(kv.Value.Count > 4 ? ", …" : "")})");

            if (tooBig.Count > 0)
            {
                _report.Add("✗ Terrains larger than a tile are NOT moved (WorldGrid authoring rule):");
                foreach (var t in tooBig) _report.Add("   " + t);
                _report.Add("   Replace them with Create Tiles (aligned 512m terrains), or split them with Unity's " +
                            "Terrain Toolbox (Window > Terrain > Terrain Toolbox > Utilities > Split).");
            }

            if (!execute) { ctx.Info("Split preview written to the Validate section below.", Src); return; }
            if (byTile.Count == 0) { ctx.Warn("Nothing to split.", Src); return; }

            if (!EditorUtility.DisplayDialog("Split into tile scenes",
                    $"Move {byTile.Sum(k => k.Value.Count)} object(s) from '{source.name}' into {byTile.Count} tile scene(s)?\n\n" +
                    "This saves the affected scenes and cannot be undone with Ctrl+Z.", "Split", "Cancel"))
                return;

            if (!EnsureFolder(_tileFolder)) { ctx.Error($"Tile folder '{_tileFolder}' must be under Assets/.", Src); return; }

            try
            {
                int i = 0;
                foreach (var kv in byTile)
                {
                    EditorUtility.DisplayProgressBar("Split into tiles", kv.Key, (float)i++ / byTile.Count);
                    WorldGrid.TryParseSceneName(kv.Key, out var tile);

                    Scene target = OpenOrCreateTileScene(tile, out bool openedHere);
                    foreach (var go in kv.Value)
                        SceneManager.MoveGameObjectToScene(go, target);

                    EditorSceneManager.MarkSceneDirty(target);
                    EditorSceneManager.SaveScene(target);
                    if (openedHere) EditorSceneManager.CloseScene(target, true);
                }

                EditorSceneManager.MarkSceneDirty(source);
                EditorSceneManager.SaveScene(source);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            RefreshTileScenes();
            ctx.Success($"Split '{source.name}' into {byTile.Count} tile scene(s). Add them to Build Settings, then Export All Terrain.", Src);
        }

        /// <summary>
        /// Static world content: a terrain, or anything marked Static. The
        /// things that must stay in the persistent scene - camera, lights,
        /// UI, streaming/registry singletons, authoring markers - are
        /// excluded explicitly even if someone ticked Static on them.
        /// </summary>
        private static bool IsScenery(GameObject root)
        {
            if (root.GetComponentInChildren<Camera>(true) != null) return false;
            if (root.GetComponentInChildren<Canvas>(true) != null) return false;
            if (root.GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true) != null) return false;

            var light = root.GetComponent<Light>();
            if (light != null && light.type == LightType.Directional) return false;

            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string ns = mb.GetType().Namespace ?? "";
                // Every ArcheCore runtime component (registries, markers,
                // streaming) belongs to the persistent scene.
                if (ns.StartsWith("ArcheCore", StringComparison.Ordinal) || ns.StartsWith("ArchCore", StringComparison.Ordinal))
                    return false;
            }

            if (root.GetComponent<Terrain>() != null) return true;
            return GameObjectUtility.GetStaticEditorFlags(root) != 0;
        }

        private Scene OpenOrCreateTileScene(TileCoord tile, out bool openedHere)
        {
            string path = ScenePathFor(tile);
            openedHere = false;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == path) return s;
            }

            openedHere = true;

            if (File.Exists(path))
                return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, path);
            return scene;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Build settings
        // ─────────────────────────────────────────────────────────────────

        private void AddTilesToBuildSettings(DevToolsContext ctx)
        {
            RefreshTileScenes();

            var scenes = EditorBuildSettings.scenes.ToList();
            var present = new HashSet<string>(scenes.Select(s => s.path));
            int added = 0;

            foreach (var path in _tileScenePaths)
            {
                if (present.Contains(path))
                {
                    // Listed but disabled still can't be loaded - enable it.
                    int idx = scenes.FindIndex(s => s.path == path);
                    if (!scenes[idx].enabled) { scenes[idx] = new EditorBuildSettingsScene(path, true); added++; }
                    continue;
                }

                scenes.Add(new EditorBuildSettingsScene(path, true));
                added++;
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            ctx.Success($"Build Settings: {added} tile scene(s) added or enabled, {scenes.Count} scene(s) total.", Src);
        }

        private static bool IsInBuildSettings(string path) =>
            EditorBuildSettings.scenes.Any(s => s.enabled && s.path == path);

        // ─────────────────────────────────────────────────────────────────
        //  Export
        // ─────────────────────────────────────────────────────────────────

        private void ExportAllTerrain(DevToolsContext ctx)
        {
            if (string.IsNullOrWhiteSpace(_exportDir))
            {
                ctx.Error("Choose the server terrain folder first (the world server's Data/terrain_data).", Src);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            RefreshTileScenes();
            var written = new List<string>();
            var exportedTerrains = new HashSet<Terrain>();

            try
            {
                // Tile scenes, one at a time so a large world never has to
                // be open all at once.
                for (int i = 0; i < _tileScenePaths.Count; i++)
                {
                    string path = _tileScenePaths[i];
                    EditorUtility.DisplayProgressBar("Export terrain", Path.GetFileNameWithoutExtension(path), (float)i / Math.Max(1, _tileScenePaths.Count));

                    Scene scene = default;
                    bool openedHere = false;

                    for (int s = 0; s < SceneManager.sceneCount; s++)
                        if (SceneManager.GetSceneAt(s).path == path) scene = SceneManager.GetSceneAt(s);

                    if (!scene.IsValid())
                    {
                        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                        openedHere = true;
                    }

                    var terrains = TerrainsIn(scene);
                    written.AddRange(TerrainHeightmapExporter.ExportAll(terrains, _exportDir));
                    foreach (var t in terrains) exportedTerrains.Add(t);

                    if (openedHere) EditorSceneManager.CloseScene(scene, true);
                }

                // Plus any terrain in the other open scenes - the legacy
                // single-scene world, or a tile not in the folder yet.
                var loose = new List<Terrain>();
                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    var scene = SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;
                    foreach (var t in TerrainsIn(scene))
                        if (!exportedTerrains.Contains(t)) loose.Add(t);
                }
                written.AddRange(TerrainHeightmapExporter.ExportAll(loose, _exportDir));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // Anything in the folder we didn't just write is from a terrain
            // that no longer exists - it would keep validating against ground
            // that isn't there. Report it rather than silently deleting.
            var writtenSet = new HashSet<string>(written.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            var stale = Directory.GetFiles(_exportDir, "*.achtmap")
                .Where(f => !writtenSet.Contains(Path.GetFullPath(f)))
                .ToList();

            _report.Clear();
            _report.Add($"Exported {written.Count} heightmap(s) to {_exportDir}.");
            if (stale.Count > 0)
            {
                _report.Add($"⚠ {stale.Count} other .achtmap file(s) in that folder weren't produced by this export " +
                            "and will still be loaded by the server - delete them if their terrain is gone:");
                foreach (var f in stale) _report.Add("   " + Path.GetFileName(f));
            }

            ctx.Success($"Exported {written.Count} heightmap(s). Restart the world server (or copy to its output folder) to load them.", Src);
        }

        private static List<Terrain> TerrainsIn(Scene scene)
        {
            var list = new List<Terrain>();
            if (!scene.IsValid() || !scene.isLoaded) return list;
            foreach (var root in scene.GetRootGameObjects())
                list.AddRange(root.GetComponentsInChildren<Terrain>(true));
            return list;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Validate
        // ─────────────────────────────────────────────────────────────────

        private void Validate(DevToolsContext ctx)
        {
            RefreshTileScenes();
            _report.Clear();
            int errors = 0, warnings = 0;

            void Err(string m)  { _report.Add("✗ " + m); errors++; }
            void Warn(string m) { _report.Add("⚠ " + m); warnings++; }

            // Scene naming and build settings.
            foreach (var path in _tileScenePaths)
            {
                if (!IsInBuildSettings(path))
                    Warn($"{Path.GetFileName(path)} is not in Build Settings - it will be empty land at runtime.");
            }

            var buildTileNames = EditorBuildSettings.scenes
                .Where(s => s.enabled && WorldGrid.TryParseSceneName(s.path, out _))
                .GroupBy(s => Path.GetFileNameWithoutExtension(s.path))
                .Where(g => g.Count() > 1);
            foreach (var dup in buildTileNames)
                Err($"Two scenes in Build Settings are named {dup.Key} - the streamer loads by name and can't tell them apart.");

            // Open scenes: terrain sizes, objects outside their tile, ground
            // left in the persistent scene.
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                bool isTile = WorldGrid.TryParseSceneName(scene.name, out var tile);

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                    {
                        if (terrain.terrainData == null) continue;
                        var size = terrain.terrainData.size;
                        if (size.x > WorldGrid.TileSize + 0.01f || size.z > WorldGrid.TileSize + 0.01f)
                            Err($"{scene.name}/{terrain.name} is {size.x:0}x{size.z:0}m - no terrain may exceed one {WorldGrid.TileSize:0}m tile, " +
                                "or the ground under a player can be outside the streamed 3x3 block.");

                        if (!isTile && _tileScenePaths.Count > 0)
                            Warn($"{scene.name}/{terrain.name} is a terrain in a non-tile scene while tile scenes exist - " +
                                 "two grounds can overlap. Move it into its tile with Split.");
                    }

                    if (isTile)
                    {
                        Vector3 p = root.transform.position;
                        if (!WorldGrid.Contains(tile, p.x, p.z))
                            Warn($"{scene.name}/{root.name} is at ({p.x:0}, {p.z:0}), which is in {WorldGrid.TileOf(p.x, p.z).SceneName} - " +
                                 "it will pop in with the wrong tile.");
                    }
                }
            }

            if (_tileScenePaths.Count == 0)
                _report.Add("No tile scenes yet - the world is a single scene. Create Tiles or Split to partition it.");

            _report.Insert(0, errors == 0 && warnings == 0
                ? "✓ World partition looks good (checks cover open scenes + Build Settings)."
                : $"{errors} error(s), {warnings} warning(s) (checks cover open scenes + Build Settings):");

            if (errors > 0) ctx.Error($"World partition: {errors} error(s), {warnings} warning(s).", Src);
            else if (warnings > 0) ctx.Warn($"World partition: {warnings} warning(s).", Src);
            else ctx.Success("World partition looks good.", Src);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The tile's existing scene wherever it lives - including a zone
        /// subfolder after Zones > Organize - or the folder root for a new one.
        /// </summary>
        private string ScenePathFor(TileCoord tile) => TileSceneIndex.PathFor(tile, _tileFolder);

        private void RefreshTileScenes()
        {
            _tileScenePaths = new List<string>();
            if (string.IsNullOrEmpty(_tileFolder) || !AssetDatabase.IsValidFolder(_tileFolder))
                return;

            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { _tileFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (WorldGrid.TryParseSceneName(path, out _))
                    _tileScenePaths.Add(path);
            }

            _tileScenePaths.Sort(StringComparer.Ordinal);
        }

        private static TileCoord TileUnderSceneCamera()
        {
            var view = SceneView.lastActiveSceneView;
            Vector3 p = view != null ? view.pivot : Vector3.zero;
            return WorldGrid.TileOf(p.x, p.z);
        }

        private static bool EnsureFolder(string folder) => TileSceneIndex.EnsureFolder(folder);

        /// <summary>
        /// The world server's Data/terrain_data, found next to the world
        /// database Dev Tools already knows about - usually right.
        /// </summary>
        private static string GuessServerTerrainDir(DevToolsContext ctx)
        {
            string db = ctx.Settings.worldServerDbPath;
            if (string.IsNullOrEmpty(db)) return "";
            string dataDir = Path.GetDirectoryName(db);
            return string.IsNullOrEmpty(dataDir) ? "" : Path.Combine(dataDir, "terrain_data");
        }
    }
}
