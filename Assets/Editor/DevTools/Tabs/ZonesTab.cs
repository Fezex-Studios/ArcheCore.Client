// ZonesTab.cs
// Assets/Editor/DevTools/Tabs/
//
// Define the shard's zones (Solzreed, Gweonid Forest...), paint where each
// one is, and work on the world zone-by-zone.
//
// ZONES ARE NOT TILES. Tiles are 512m streaming squares; zones are design
// regions of any shape, painted onto 32m cells. A zone spans many tiles
// and a border tile holds parts of two zones. See ZoneMap.
//
// WHAT THIS TAB DOES
//
//   Zones      the list - key, display name, subtitle, level range, PvP
//              mode, editor colour. Stored in the zone map file itself.
//   Map        a top-down map of the world: paint zones with a round brush,
//              pan/zoom, optional background image (your world map art).
//   Scene view show zones as a coloured overlay, and optionally paint on
//              the actual terrain.
//   Open zone  opens every tile scene the zone touches alongside
//              main_world - editing "Solzreed" in one click.
//   Organize   moves each tile scene into a folder named after the zone
//              that covers most of it. Purely for people; the streamer
//              loads tiles by name.
//   Save       writes StreamingAssets/World/zones.aczmap (shipped with the
//              client) AND the server's copy. The two must match - the
//              server sends its hash on entering the world and the client
//              logs an error if they differ.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArcheCore.Movement.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcheCore.Editor.DevTools.Tabs
{
    public class ZonesTab : IDevToolsTab
    {
        public string Title => "🧭  Zones";
        public int    Order => 16;

        private const string Src = "Zones";

        /// <summary>The client's copy, shipped with the build.</summary>
        private const string ClientMapPath = "Assets/StreamingAssets/World/zones.aczmap";

        private const string ServerPathPref = "ArcheCore.Zones.ServerPath";
        private const string BackgroundPref = "ArcheCore.Zones.Background";
        private const string BackgroundRectPref = "ArcheCore.Zones.BackgroundRect";

        private enum Tool { Paint, Erase, Pick }

        private ZoneMap _map = new ZoneMap();
        private bool _dirty;
        private ushort _selected;
        private Tool _tool = Tool.Paint;
        private float _brushRadius = 48f;

        private string _serverPath = "";

        // Map view
        private Vector2 _viewCenter;          // world X, Z at the panel centre
        private float _metersPerPixel = 8f;
        private bool _viewInitialised;
        private Texture2D _zoneTexture;
        private bool _textureStale = true;
        private int _texOriginCx, _texOriginCz, _texCellsX, _texCellsZ;
        private Texture2D _background;
        private Rect _backgroundWorld = new Rect(-2048, -2048, 4096, 4096); // x=minX, y=minZ, w, h
        private bool _strokeActive;
        private string _hoverInfo = "";

        // Scene view
        private bool _showInScene = true;
        private bool _paintInScene;

        // Scene overlay: a mesh draped over the terrain, rebuilt only when
        // the map, the selection, or the tile under the camera changes (plus
        // every couple of seconds, to follow sculpting).
        private Mesh _overlayMesh;
        private Material _overlayMaterial;
        private bool _overlayStale = true;
        private TileCoord _overlayCentre;
        private ushort _overlaySelected;
        private double _overlayBuiltAt;
        private const int OverlayRadiusTiles = 2;
        private const float OverlayLift = 0.4f;

        // Undo: whole-map snapshots, one per brush stroke.
        private readonly List<byte[]> _undo = new List<byte[]>();
        private const int MaxUndo = 30;

        private Vector2 _scroll;
        private ushort _pendingDelete;
        private readonly List<string> _report = new List<string>();
        private Dictionary<TileCoord, string> _tileScenes = new Dictionary<TileCoord, string>();

        public bool HasUnsavedChanges => _dirty;

        // ─────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────

        public void OnEnable(DevToolsContext ctx)
        {
            _serverPath = EditorPrefs.GetString(ServerPathPref, "");
            if (string.IsNullOrEmpty(_serverPath))
                _serverPath = GuessServerPath(ctx);

            string bgPath = EditorPrefs.GetString(BackgroundPref, "");
            if (!string.IsNullOrEmpty(bgPath))
                _background = AssetDatabase.LoadAssetAtPath<Texture2D>(bgPath);

            var r = EditorPrefs.GetString(BackgroundRectPref, "");
            var parts = r.Split(',');
            if (parts.Length == 4 &&
                float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float bx) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float bz) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float bw) &&
                float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float bh))
                _backgroundWorld = new Rect(bx, bz, bw, bh);

            LoadFromDisk(ctx, quiet: true);
            _tileScenes = TileSceneIndex.FindAll();
        }

        public void OnDisable(DevToolsContext ctx)
        {
            if (_overlayMesh != null) UnityEngine.Object.DestroyImmediate(_overlayMesh);
            if (_overlayMaterial != null) UnityEngine.Object.DestroyImmediate(_overlayMaterial);
            _overlayMesh = null;
            _overlayMaterial = null;

            if (_zoneTexture != null) UnityEngine.Object.DestroyImmediate(_zoneTexture);
            _zoneTexture = null;
        }

        // ─────────────────────────────────────────────────────────────────
        //  GUI
        // ─────────────────────────────────────────────────────────────────

        public void OnGUI(DevToolsContext ctx)
        {
            // Deletions are applied at the start of the NEXT layout pass, so the
            // list never changes length between Layout and Repaint of one frame.
            if (_pendingDelete != 0 && Event.current.type == EventType.Layout)
            {
                PushUndo();
                _map.RemoveZone(_pendingDelete);
                if (_selected == _pendingDelete) _selected = 0;
                _pendingDelete = 0;
                MarkDirty();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawFileSection(ctx);
            EditorGUILayout.Space(6);
            DrawZoneList(ctx);
            EditorGUILayout.Space(6);
            DrawMapSection(ctx);
            EditorGUILayout.Space(6);
            DrawTileTools(ctx);
            EditorGUILayout.Space(6);

            DevToolsContext.Section("🔍  Report", ctx.Styles.SubHeader, () =>
            {
                if (DevToolsContext.ActionButton("Validate zones", "", new Color(0.8f, 0.8f, 0.3f), 24))
                    Validate(ctx);

                foreach (var line in _report)
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            });

            EditorGUILayout.EndScrollView();
        }

        private void DrawFileSection(DevToolsContext ctx)
        {
            DevToolsContext.Section("💾  Zone map file", ctx.Styles.SubHeader, () =>
            {
                EditorGUILayout.LabelField(
                    "One file holds the zone list and the painted borders. Save writes the client's copy " +
                    $"({ClientMapPath}, shipped in the build) and the server's copy. Keep them identical - " +
                    "the server sends its hash on entering the world and the client complains if they differ.",
                    EditorStyles.wordWrappedMiniLabel);

                string path = _serverPath;
                if (DevToolsContext.PathField("Server copy (zones.aczmap)", ref path, false, "aczmap"))
                {
                    _serverPath = path;
                    EditorPrefs.SetString(ServerPathPref, _serverPath);
                }

                EditorGUILayout.LabelField(
                    $"{_map.ZoneCount} zone(s), {_map.PaintedTiles.Count()} painted tile(s)" + (_dirty ? "  -  UNSAVED CHANGES" : ""),
                    _dirty ? ctx.Styles.Err : ctx.Styles.Ok);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save (client + server)", GUILayout.Height(26))) Save(ctx);
                if (GUILayout.Button("Reload from disk", GUILayout.Height(26), GUILayout.Width(140)))
                {
                    if (!_dirty || EditorUtility.DisplayDialog("Reload zones", "Discard unsaved zone changes?", "Discard", "Cancel"))
                        LoadFromDisk(ctx, quiet: false);
                }
                using (new EditorGUI.DisabledScope(_undo.Count == 0))
                {
                    if (GUILayout.Button($"Undo stroke ({_undo.Count})", GUILayout.Height(26), GUILayout.Width(130)))
                        Undo();
                }
                EditorGUILayout.EndHorizontal();
            });
        }

        private void DrawZoneList(DevToolsContext ctx)
        {
            DevToolsContext.Section("🏷  Zones", ctx.Styles.SubHeader, () =>
            {
                var counts = _map.CountCells();
                var zones = _map.Zones.OrderBy(z => z.Id).ToList();

                if (zones.Count == 0)
                    EditorGUILayout.LabelField("No zones yet. Add one, then paint it on the map below.", EditorStyles.wordWrappedMiniLabel);

                foreach (var zone in zones)
                {
                    bool isSelected = zone.Id == _selected;

                    EditorGUILayout.BeginHorizontal();

                    var color = ToColor(zone.ColorRgba);
                    var newColor = EditorGUILayout.ColorField(GUIContent.none, color, false, false, false, GUILayout.Width(40));
                    if (newColor != color) { zone.ColorRgba = FromColor(newColor); MarkDirty(); }

                    string label = $"{zone.Id}  {(string.IsNullOrEmpty(zone.DisplayName) ? zone.Key : zone.DisplayName)}";
                    if (GUILayout.Toggle(isSelected, label, EditorStyles.miniButton, GUILayout.MinWidth(180)) && !isSelected)
                    {
                        _selected = zone.Id;
                        if (_tool == Tool.Erase) _tool = Tool.Paint;
                    }

                    int cells = counts.TryGetValue(zone.Id, out int n) ? n : 0;
                    EditorGUILayout.LabelField($"{cells * ZoneMap.CellSize * ZoneMap.CellSize / 1_000_000f:0.00} km²",
                        EditorStyles.miniLabel, GUILayout.Width(70));

                    if (GUILayout.Button("Open tiles", EditorStyles.miniButton, GUILayout.Width(72)))
                        OpenZoneTiles(ctx, zone, closeOthers: false);
                    if (GUILayout.Button("Only these", EditorStyles.miniButton, GUILayout.Width(72)))
                        OpenZoneTiles(ctx, zone, closeOthers: true);
                    if (GUILayout.Button("Focus", EditorStyles.miniButton, GUILayout.Width(46)))
                        FocusZone(zone);
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)) &&
                        EditorUtility.DisplayDialog("Delete zone",
                            $"Delete '{zone.Key}' and erase all {cells} cell(s) painted with it?", "Delete", "Cancel"))
                    {
                        _pendingDelete = zone.Id;
                        ctx.Repaint();
                    }

                    EditorGUILayout.EndHorizontal();

                    if (isSelected)
                        DrawZoneDetails(zone);
                }

                EditorGUILayout.Space(4);
                if (DevToolsContext.ActionButton("➕  Add zone", "", new Color(0.4f, 0.8f, 0.4f), 24))
                {
                    ushort id = _map.NextFreeId();
                    var zone = new ZoneDefinition
                    {
                        Id = id,
                        Key = $"zone_{id}",
                        DisplayName = $"New Zone {id}",
                        PvpMode = ZonePvpMode.Peaceful,
                        ColorRgba = FromColor(DistinctColor(id))
                    };
                    _map.SetZone(zone);
                    _selected = id;
                    _tool = Tool.Paint;
                    MarkDirty();
                }
            });
        }

        private void DrawZoneDetails(ZoneDefinition zone)
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();

            string key = EditorGUILayout.TextField(new GUIContent("Key", "Stable id for code, Lua and folder names. lowercase_with_underscores."), zone.Key);
            zone.DisplayName = EditorGUILayout.TextField(new GUIContent("Display name", "What the zone banner says."), zone.DisplayName);
            zone.Subtitle = EditorGUILayout.TextField(new GUIContent("Subtitle", "Smaller line under the name - region, continent. Optional."), zone.Subtitle);
            zone.PvpMode = (ZonePvpMode)EditorGUILayout.EnumPopup(new GUIContent("PvP", "Safe/Peaceful: no player attacks. Contested/War: open PvP."), zone.PvpMode);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Level range");
            zone.MinLevel = (ushort)Mathf.Clamp(EditorGUILayout.IntField(zone.MinLevel, GUILayout.Width(50)), 0, 999);
            EditorGUILayout.LabelField("to", GUILayout.Width(20));
            zone.MaxLevel = (ushort)Mathf.Clamp(EditorGUILayout.IntField(zone.MaxLevel, GUILayout.Width(50)), 0, 999);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                zone.Key = SanitizeKey(key);
                MarkDirty();
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Map
        // ─────────────────────────────────────────────────────────────────

        private void DrawMapSection(DevToolsContext ctx)
        {
            DevToolsContext.Section("🖌  Paint", ctx.Styles.SubHeader, () =>
            {
                EditorGUILayout.BeginHorizontal();
                _tool = (Tool)GUILayout.Toolbar((int)_tool, new[] { "Paint", "Erase", "Pick" }, GUILayout.Width(210));
                GUILayout.Space(10);
                EditorGUILayout.LabelField("Brush", GUILayout.Width(40));
                _brushRadius = EditorGUILayout.Slider(_brushRadius, 0f, 512f);
                EditorGUILayout.EndHorizontal();

                var sel = _map.GetZone(_selected);
                EditorGUILayout.LabelField(
                    _tool == Tool.Paint
                        ? (sel != null ? $"Painting: {sel.DisplayName} ({sel.Key})" : "Select a zone in the list to paint it.")
                        : _tool == Tool.Erase ? "Erasing - painted cells go back to 'no zone'." : "Click the map to select the zone under the cursor.",
                    EditorStyles.miniBoldLabel);

                EditorGUILayout.LabelField(
                    "Left-drag paints. Ctrl+click fills the whole 512m tile. Right/middle-drag pans, scroll zooms. " +
                    "Brush 0 = one 32m cell. Blue outlines are tiles that have a scene.",
                    EditorStyles.wordWrappedMiniLabel);

                Rect rect = GUILayoutUtility.GetRect(100, 460, GUILayout.ExpandWidth(true));
                DrawMap(ctx, rect);

                EditorGUILayout.LabelField(_hoverInfo, EditorStyles.miniLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Fit painted area", EditorStyles.miniButton)) FitView(rect);
                if (GUILayout.Button("Centre on Scene view", EditorStyles.miniButton))
                {
                    var view = SceneView.lastActiveSceneView;
                    if (view != null) { _viewCenter = new Vector2(view.pivot.x, view.pivot.z); _textureStale = true; }
                }
                _showInScene = GUILayout.Toggle(_showInScene, "Show in Scene view", EditorStyles.miniButton);
                _paintInScene = GUILayout.Toggle(_paintInScene, "Paint in Scene view", EditorStyles.miniButton);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Background image (optional - your world map art, aligned to world coordinates)", EditorStyles.miniBoldLabel);
                EditorGUI.BeginChangeCheck();
                _background = (Texture2D)EditorGUILayout.ObjectField("Image", _background, typeof(Texture2D), false);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("World min X / min Z");
                float bx = EditorGUILayout.FloatField(_backgroundWorld.x);
                float bz = EditorGUILayout.FloatField(_backgroundWorld.y);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("World width / height");
                float bw = Mathf.Max(1f, EditorGUILayout.FloatField(_backgroundWorld.width));
                float bh = Mathf.Max(1f, EditorGUILayout.FloatField(_backgroundWorld.height));
                EditorGUILayout.EndHorizontal();
                if (EditorGUI.EndChangeCheck())
                {
                    _backgroundWorld = new Rect(bx, bz, bw, bh);
                    EditorPrefs.SetString(BackgroundPref, _background != null ? AssetDatabase.GetAssetPath(_background) : "");
                    EditorPrefs.SetString(BackgroundRectPref, string.Join(",",
                        new[] { bx, bz, bw, bh }.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                }
            });
        }

        private void DrawMap(DevToolsContext ctx, Rect rect)
        {
            if (!_viewInitialised && rect.width > 10)
            {
                FitView(rect);
                _viewInitialised = true;
            }

            var e = Event.current;
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.09f, 0.11f));

            // The brush preview and hover readout need mouse-move events,
            // which an editor window only gets when it asks for them.
            var window = EditorWindow.mouseOverWindow;
            if (window != null && !window.wantsMouseMove && rect.Contains(e.mousePosition))
                window.wantsMouseMove = true;

            GUI.BeginClip(rect);
            var local = new Rect(0, 0, rect.width, rect.height);

            if (e.type == EventType.Repaint)
            {
                // Background art.
                if (_background != null)
                {
                    Rect bg = WorldRectToLocal(local, _backgroundWorld.xMin, _backgroundWorld.yMin, _backgroundWorld.xMax, _backgroundWorld.yMax);
                    GUI.DrawTexture(bg, _background, ScaleMode.StretchToFill, true);
                }

                // Zones.
                RebuildTextureIfNeeded(local);
                if (_zoneTexture != null)
                {
                    Rect tr = WorldRectToLocal(local,
                        _texOriginCx * ZoneMap.CellSize, _texOriginCz * ZoneMap.CellSize,
                        (_texOriginCx + _texCellsX) * ZoneMap.CellSize, (_texOriginCz + _texCellsZ) * ZoneMap.CellSize);
                    GUI.DrawTexture(tr, _zoneTexture, ScaleMode.StretchToFill, true);
                }

                DrawTileGrid(local);
                DrawBrushPreview(local, e.mousePosition);
                DrawCursorLabel(local, e.mousePosition);
            }

            HandleMapInput(ctx, local, e);

            GUI.EndClip();
        }

        private void DrawTileGrid(Rect local)
        {
            WorldBounds(local, out float minX, out float minZ, out float maxX, out float maxZ);

            var first = WorldGrid.TileOf(minX, minZ);
            var last = WorldGrid.TileOf(maxX, maxZ);

            int tilesAcross = last.X - first.X + 1;
            if (tilesAcross > 200) return; // zoomed far out - grid would be noise

            var gridColor = new Color(1f, 1f, 1f, 0.12f);
            for (int tx = first.X; tx <= last.X + 1; tx++)
            {
                float x = WorldToLocal(local, tx * WorldGrid.TileSize, 0).x;
                EditorGUI.DrawRect(new Rect(x, 0, 1, local.height), gridColor);
            }
            for (int tz = first.Z; tz <= last.Z + 1; tz++)
            {
                float y = WorldToLocal(local, 0, tz * WorldGrid.TileSize).y;
                EditorGUI.DrawRect(new Rect(0, y, local.width, 1), gridColor);
            }

            // Tiles that have a scene.
            var sceneColor = new Color(0.3f, 0.75f, 1f, 0.9f);
            foreach (var tile in _tileScenes.Keys)
            {
                if (tile.X < first.X || tile.X > last.X || tile.Z < first.Z || tile.Z > last.Z) continue;
                Rect r = WorldRectToLocal(local, WorldGrid.MinX(tile), WorldGrid.MinZ(tile),
                    WorldGrid.MinX(tile) + WorldGrid.TileSize, WorldGrid.MinZ(tile) + WorldGrid.TileSize);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), sceneColor);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), sceneColor);
                EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), sceneColor);
                EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), sceneColor);
            }

            // Origin cross.
            Vector2 o = WorldToLocal(local, 0, 0);
            EditorGUI.DrawRect(new Rect(o.x - 6, o.y, 13, 1), new Color(1f, 1f, 1f, 0.5f));
            EditorGUI.DrawRect(new Rect(o.x, o.y - 6, 1, 13), new Color(1f, 1f, 1f, 0.5f));
        }

        private void DrawBrushPreview(Rect local, Vector2 mouse)
        {
            if (!local.Contains(mouse) || _tool == Tool.Pick) return;

            float radiusPx = Mathf.Max(ZoneMap.CellSize * 0.5f, _brushRadius) / _metersPerPixel;

            // In an editor window, Handles draw in GUI space (inside the
            // current clip), which is exactly what the map is.
            Handles.color = _tool == Tool.Erase ? new Color(1f, 0.4f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.9f);
            Handles.DrawWireDisc(new Vector3(mouse.x, mouse.y, 0f), Vector3.forward, radiusPx);
        }

        private GUIStyle _cursorLabelStyle;

        /// <summary>
        /// Zone name and tile under the cursor, drawn right next to it on the
        /// map - the same readout as the line under the map, where you're
        /// actually looking.
        /// </summary>
        private void DrawCursorLabel(Rect local, Vector2 mouse)
        {
            if (!local.Contains(mouse)) return;

            if (_cursorLabelStyle == null)
            {
                _cursorLabelStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 11,
                    richText = false,
                    padding = new RectOffset(6, 6, 3, 3)
                };
            }

            Vector2 world = LocalToWorld(local, mouse);
            var zone = _map.ZoneAt(world.x, world.y);
            var tile = WorldGrid.TileOf(world.x, world.y);

            string text = (zone != null ? $"{zone.DisplayName}  [{zone.Key}]" : "no zone") + "\n" + tile.SceneName;
            var content = new GUIContent(text);
            Vector2 size = _cursorLabelStyle.CalcSize(content);

            // Beside the cursor, flipped to the other side near the edges.
            float x = mouse.x + 16f, y = mouse.y + 12f;
            if (x + size.x > local.width) x = mouse.x - size.x - 8f;
            if (y + size.y > local.height) y = mouse.y - size.y - 8f;

            GUI.Label(new Rect(x, y, size.x, size.y), content, _cursorLabelStyle);
        }

        private void HandleMapInput(DevToolsContext ctx, Rect local, Event e)
        {
            if (!local.Contains(e.mousePosition) && e.type != EventType.MouseUp)
                return;

            Vector2 world = LocalToWorld(local, e.mousePosition);

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                var tile = WorldGrid.TileOf(world.x, world.y);
                var zone = _map.ZoneAt(world.x, world.y);
                _hoverInfo = $"({world.x:0}, {world.y:0})   {tile.SceneName}{(_tileScenes.ContainsKey(tile) ? "" : " (no scene)")}   " +
                             $"zone: {(zone != null ? $"{zone.DisplayName} [{zone.Key}]" : "none")}";
                ctx.Repaint();
            }

            switch (e.type)
            {
                case EventType.ScrollWheel:
                {
                    // Zoom around the cursor, so what's under it stays put.
                    float factor = e.delta.y > 0 ? 1.15f : 1f / 1.15f;
                    _metersPerPixel = Mathf.Clamp(_metersPerPixel * factor, 0.5f, 256f);
                    Vector2 after = LocalToWorld(local, e.mousePosition);
                    _viewCenter += world - after;
                    _textureStale = true;
                    e.Use();
                    ctx.Repaint();
                    break;
                }

                case EventType.MouseDrag when e.button == 1 || e.button == 2:
                    _viewCenter += new Vector2(-e.delta.x, e.delta.y) * _metersPerPixel;
                    _textureStale = true;
                    e.Use();
                    ctx.Repaint();
                    break;

                case EventType.MouseDown when e.button == 0:
                    if (_tool == Tool.Pick)
                    {
                        ushort id = _map.ZoneIdAt(world.x, world.y);
                        if (id != 0) { _selected = id; _tool = Tool.Paint; }
                        e.Use();
                        ctx.Repaint();
                        break;
                    }

                    if (!CanPaint(ctx)) { e.Use(); break; }

                    PushUndo();
                    _strokeActive = true;

                    if (e.control || e.command)
                        _map.FillTile(WorldGrid.TileOf(world.x, world.y), PaintId());
                    else
                        _map.PaintCircle(world.x, world.y, _brushRadius, PaintId());

                    MarkDirty();
                    e.Use();
                    ctx.Repaint();
                    break;

                case EventType.MouseDrag when e.button == 0 && _strokeActive:
                    if (_map.PaintCircle(world.x, world.y, _brushRadius, PaintId()) > 0)
                        MarkDirty();
                    e.Use();
                    ctx.Repaint();
                    break;

                case EventType.MouseUp when e.button == 0:
                    _strokeActive = false;
                    break;
            }
        }

        private bool CanPaint(DevToolsContext ctx)
        {
            if (_tool == Tool.Erase) return true;
            if (_map.GetZone(_selected) != null) return true;
            ctx.Warn("Select (or add) a zone before painting.", Src);
            return false;
        }

        private ushort PaintId() => _tool == Tool.Erase ? (ushort)0 : _selected;

        /// <summary>
        /// One pixel per 32m cell over the visible area. Rebuilt only when
        /// the view moves or the map changes - painting a stroke marks it
        /// stale, a plain repaint doesn't.
        /// </summary>
        private void RebuildTextureIfNeeded(Rect local)
        {
            if (!_textureStale && _zoneTexture != null) return;
            _textureStale = false;

            WorldBounds(local, out float minX, out float minZ, out float maxX, out float maxZ);

            int cx0 = Mathf.FloorToInt(minX / ZoneMap.CellSize);
            int cz0 = Mathf.FloorToInt(minZ / ZoneMap.CellSize);
            int cx1 = Mathf.CeilToInt(maxX / ZoneMap.CellSize);
            int cz1 = Mathf.CeilToInt(maxZ / ZoneMap.CellSize);

            int w = Mathf.Clamp(cx1 - cx0, 1, 2048);
            int h = Mathf.Clamp(cz1 - cz0, 1, 2048);

            if (_zoneTexture == null || _zoneTexture.width != w || _zoneTexture.height != h)
            {
                if (_zoneTexture != null) UnityEngine.Object.DestroyImmediate(_zoneTexture);
                _zoneTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            _texOriginCx = cx0;
            _texOriginCz = cz0;
            _texCellsX = w;
            _texCellsZ = h;

            var colors = new Dictionary<ushort, Color32>();
            foreach (var z in _map.Zones)
            {
                var c = ToColor(z.ColorRgba);
                c.a = z.Id == _selected ? 0.75f : 0.5f;
                colors[z.Id] = c;
            }

            var clear = new Color32(0, 0, 0, 0);
            var pixels = new Color32[w * h];

            for (int z = 0; z < h; z++)
            {
                float worldZ = (cz0 + z + 0.5f) * ZoneMap.CellSize;
                for (int x = 0; x < w; x++)
                {
                    float worldX = (cx0 + x + 0.5f) * ZoneMap.CellSize;
                    ushort id = _map.ZoneIdAt(worldX, worldZ);
                    pixels[z * w + x] = id != 0 && colors.TryGetValue(id, out var c) ? c : clear;
                }
            }

            _zoneTexture.SetPixels32(pixels);
            _zoneTexture.Apply(false);
        }

        // Coordinate helpers. Local = pixels inside the clipped map rect,
        // y down. World = (x, z) metres, z up.

        private Vector2 WorldToLocal(Rect local, float x, float z) => new Vector2(
            local.width * 0.5f + (x - _viewCenter.x) / _metersPerPixel,
            local.height * 0.5f - (z - _viewCenter.y) / _metersPerPixel);

        private Vector2 LocalToWorld(Rect local, Vector2 p) => new Vector2(
            _viewCenter.x + (p.x - local.width * 0.5f) * _metersPerPixel,
            _viewCenter.y - (p.y - local.height * 0.5f) * _metersPerPixel);

        private Rect WorldRectToLocal(Rect local, float minX, float minZ, float maxX, float maxZ)
        {
            Vector2 a = WorldToLocal(local, minX, maxZ); // top-left
            Vector2 b = WorldToLocal(local, maxX, minZ); // bottom-right
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        private void WorldBounds(Rect local, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            Vector2 tl = LocalToWorld(local, Vector2.zero);
            Vector2 br = LocalToWorld(local, new Vector2(local.width, local.height));
            minX = tl.x; maxX = br.x;
            minZ = br.y; maxZ = tl.y;
        }

        private void FitView(Rect rect)
        {
            var tiles = _map.PaintedTiles.Concat(_tileScenes.Keys).ToList();
            if (tiles.Count == 0)
            {
                _viewCenter = Vector2.zero;
                _metersPerPixel = 8f;
            }
            else
            {
                float minX = tiles.Min(t => WorldGrid.MinX(t));
                float minZ = tiles.Min(t => WorldGrid.MinZ(t));
                float maxX = tiles.Max(t => WorldGrid.MinX(t)) + WorldGrid.TileSize;
                float maxZ = tiles.Max(t => WorldGrid.MinZ(t)) + WorldGrid.TileSize;

                _viewCenter = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
                float w = Mathf.Max(rect.width, 100f), h = Mathf.Max(rect.height, 100f);
                _metersPerPixel = Mathf.Clamp(Mathf.Max((maxX - minX) / w, (maxZ - minZ) / h) * 1.15f, 0.5f, 256f);
            }

            _textureStale = true;
        }

        private void FocusZone(ZoneDefinition zone)
        {
            var tiles = _map.TilesContaining(zone.Id);
            if (tiles.Count == 0) return;

            float cx = tiles.Average(t => WorldGrid.MinX(t) + WorldGrid.TileSize * 0.5f);
            float cz = tiles.Average(t => WorldGrid.MinZ(t) + WorldGrid.TileSize * 0.5f);
            _viewCenter = new Vector2(cx, cz);
            _textureStale = true;

            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.pivot = new Vector3(cx, view.pivot.y, cz);
                view.Repaint();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Scene view
        // ─────────────────────────────────────────────────────────────────

        public void OnSceneGUI(DevToolsContext ctx, SceneView sceneView)
        {
            var e = Event.current;

            if (_showInScene && e.type == EventType.Repaint)
                DrawSceneOverlay(sceneView);

            if (!_paintInScene)
                return;

            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(control);

            if (!TryGetScenePoint(e.mousePosition, sceneView, out Vector3 point))
                return;

            if (e.type == EventType.Repaint)
            {
                Handles.color = _tool == Tool.Erase ? new Color(1f, 0.4f, 0.4f, 1f) : Color.white;
                Handles.DrawWireDisc(point, Vector3.up, Mathf.Max(ZoneMap.CellSize * 0.5f, _brushRadius));
                var zone = _map.ZoneAt(point.x, point.z);
                Handles.Label(point + Vector3.up * 2f, zone != null ? zone.DisplayName : "no zone");
            }

            if (e.alt) return; // alt-drag orbits the camera

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (_tool == Tool.Pick)
                {
                    ushort id = _map.ZoneIdAt(point.x, point.z);
                    if (id != 0) { _selected = id; _tool = Tool.Paint; ctx.Repaint(); }
                    e.Use();
                    return;
                }

                if (!CanPaint(ctx)) { e.Use(); return; }

                PushUndo();
                _strokeActive = true;
                _map.PaintCircle(point.x, point.z, _brushRadius, PaintId());
                MarkDirty();
                e.Use();
                ctx.Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _strokeActive)
            {
                if (_map.PaintCircle(point.x, point.z, _brushRadius, PaintId()) > 0) MarkDirty();
                e.Use();
                ctx.Repaint();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                _strokeActive = false;
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                sceneView.Repaint();
        }

        /// <summary>The ground under the cursor: terrain/colliders first, else the pivot's height plane.</summary>
        private static bool TryGetScenePoint(Vector2 mouse, SceneView view, out Vector3 point)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);

            if (Physics.Raycast(ray, out RaycastHit hit, 100000f))
            {
                point = hit.point;
                return true;
            }

            var plane = new Plane(Vector3.up, new Vector3(0f, view.pivot.y, 0f));
            if (plane.Raycast(ray, out float d))
            {
                point = ray.GetPoint(d);
                return true;
            }

            point = default;
            return false;
        }

        /// <summary>
        /// Zones as a tinted sheet DRAPED OVER THE TERRAIN: one quad per 32m
        /// cell, each corner at the ground height, lifted a little so it
        /// doesn't z-fight. Depth-tested, so hills in front hide it like real
        /// geometry. Covers the 5x5 tiles around the Scene camera's pivot.
        ///
        /// (The first version drew flat rectangles at the pivot's height on
        /// top of everything - they slid through the terrain as the camera
        /// moved.)
        /// </summary>
        private void DrawSceneOverlay(SceneView view)
        {
            var centre = WorldGrid.TileOf(view.pivot.x, view.pivot.z);

            if (_overlayMesh == null || _overlayStale || centre != _overlayCentre || _selected != _overlaySelected ||
                EditorApplication.timeSinceStartup - _overlayBuiltAt > 2.0)
                RebuildOverlay(centre);

            if (_overlayMesh == null || _overlayMesh.vertexCount == 0)
                return;

            if (_overlayMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return;

                _overlayMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _overlayMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _overlayMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _overlayMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _overlayMaterial.SetInt("_ZWrite", 0);
                _overlayMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            }

            _overlayMaterial.SetPass(0);
            Graphics.DrawMeshNow(_overlayMesh, Matrix4x4.identity);
        }

        private void RebuildOverlay(TileCoord centre)
        {
            _overlayStale = false;
            _overlayCentre = centre;
            _overlaySelected = _selected;
            _overlayBuiltAt = EditorApplication.timeSinceStartup;

            if (_overlayMesh == null)
                _overlayMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _overlayMesh.Clear();

            var colors = new Dictionary<ushort, Color32>();
            foreach (var z in _map.Zones)
            {
                var c = ToColor(z.ColorRgba);
                c.a = z.Id == _selected ? 0.45f : 0.3f;
                colors[z.Id] = c;
            }

            var terrains = Terrain.activeTerrains;
            var verts = new List<Vector3>(4096);
            var cols = new List<Color32>(4096);
            var tris = new List<int>(6144);
            float cell = ZoneMap.CellSize;

            for (int tx = centre.X - OverlayRadiusTiles; tx <= centre.X + OverlayRadiusTiles; tx++)
            for (int tz = centre.Z - OverlayRadiusTiles; tz <= centre.Z + OverlayRadiusTiles; tz++)
            {
                var tile = new TileCoord(tx, tz);
                float x0 = WorldGrid.MinX(tile), z0 = WorldGrid.MinZ(tile);

                for (int cz = 0; cz < ZoneMap.CellsPerTile; cz++)
                for (int cx = 0; cx < ZoneMap.CellsPerTile; cx++)
                {
                    ushort id = _map.GetCell(tile, cx, cz);
                    if (id == 0 || !colors.TryGetValue(id, out var col)) continue;

                    float ax = x0 + cx * cell, az = z0 + cz * cell;
                    int b = verts.Count;

                    verts.Add(new Vector3(ax,        GroundY(terrains, ax,        az       ) + OverlayLift, az));
                    verts.Add(new Vector3(ax + cell, GroundY(terrains, ax + cell, az       ) + OverlayLift, az));
                    verts.Add(new Vector3(ax + cell, GroundY(terrains, ax + cell, az + cell) + OverlayLift, az + cell));
                    verts.Add(new Vector3(ax,        GroundY(terrains, ax,        az + cell) + OverlayLift, az + cell));

                    cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);

                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
            }

            if (verts.Count == 0) return;

            _overlayMesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _overlayMesh.SetVertices(verts);
            _overlayMesh.SetColors(cols);
            _overlayMesh.SetTriangles(tris, 0);
            _overlayMesh.RecalculateBounds();
        }

        /// <summary>
        /// Terrain height at a point from whichever loaded terrain covers it.
        /// With no terrain there (sea, an unopened tile), 0 - the overlay
        /// still shows the zone, lying at sea-floor level.
        /// </summary>
        private static float GroundY(Terrain[] terrains, float x, float z)
        {
            foreach (var t in terrains)
            {
                if (t == null || t.terrainData == null) continue;
                Vector3 p = t.transform.position;
                Vector3 size = t.terrainData.size;
                if (x < p.x || x > p.x + size.x || z < p.z || z > p.z + size.z) continue;
                return t.SampleHeight(new Vector3(x, 0f, z)) + p.y;
            }
            return 0f;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Tile tools
        // ─────────────────────────────────────────────────────────────────

        private void DrawTileTools(DevToolsContext ctx)
        {
            DevToolsContext.Section("🧱  Tiles by zone", ctx.Styles.SubHeader, () =>
            {
                EditorGUILayout.LabelField(
                    "Open tiles (in the zone list) opens every tile scene a zone touches alongside main_world. " +
                    "Organize moves each tile scene into a folder named after the zone covering most of it " +
                    "(Tiles/solzreed/tile_-1_-2.unity). Folders are only for you - the game loads tiles by name, " +
                    "and Build Settings are updated to the new paths.",
                    EditorStyles.wordWrappedMiniLabel);

                if (DevToolsContext.ActionButton("Organize tile scenes into zone folders", "", new Color(0.3f, 0.7f, 1f)))
                    OrganizeFolders(ctx);

                if (GUILayout.Button("Refresh tile list", EditorStyles.miniButton, GUILayout.Width(120)))
                {
                    _tileScenes = TileSceneIndex.FindAll();
                    ctx.Info($"{_tileScenes.Count} tile scene(s) found.", Src);
                }
            });
        }

        private void OpenZoneTiles(DevToolsContext ctx, ZoneDefinition zone, bool closeOthers)
        {
            _tileScenes = TileSceneIndex.FindAll();
            var tiles = _map.TilesContaining(zone.Id);

            if (tiles.Count == 0)
            {
                ctx.Warn($"'{zone.Key}' isn't painted anywhere yet.", Src);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var wanted = new HashSet<string>(tiles.Where(t => _tileScenes.ContainsKey(t)).Select(t => _tileScenes[t]));
            int missing = tiles.Count(t => !_tileScenes.ContainsKey(t));

            if (closeOthers)
            {
                foreach (var open in TileSceneIndex.OpenTileScenes())
                {
                    if (wanted.Contains(open.path)) continue;
                    if (SceneManager.sceneCount <= 1) break;
                    EditorSceneManager.CloseScene(open, true);
                }
            }

            int opened = 0;
            try
            {
                int i = 0;
                foreach (var path in wanted)
                {
                    EditorUtility.DisplayProgressBar($"Open {zone.DisplayName}", Path.GetFileNameWithoutExtension(path), (float)i++ / wanted.Count);
                    TileSceneIndex.OpenAdditive(path, out bool openedHere);
                    if (openedHere) opened++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            FocusZone(zone);
            ctx.Success($"{zone.DisplayName}: {wanted.Count} tile scene(s) open ({opened} newly)" +
                        (missing > 0 ? $", {missing} painted tile(s) have no scene yet - create them in World Tiles." : "."), Src);
        }

        private void OrganizeFolders(DevToolsContext ctx)
        {
            if (_dirty)
            {
                ctx.Warn("Save the zone map first - organizing uses the saved borders.", Src);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string root = TileSceneIndex.Folder.TrimEnd('/');
            _tileScenes = TileSceneIndex.FindAll();

            var moves = new Dictionary<string, string>();
            foreach (var kv in _tileScenes)
            {
                var zone = _map.GetZone(_map.DominantZone(kv.Key));
                string folder = zone != null && !string.IsNullOrEmpty(zone.Key) ? $"{root}/{zone.Key}" : $"{root}/_unzoned";
                string target = $"{folder}/{Path.GetFileName(kv.Value)}";
                if (target != kv.Value) moves[kv.Value] = target;
            }

            if (moves.Count == 0)
            {
                ctx.Success("Every tile scene is already in its zone's folder.", Src);
                return;
            }

            if (!EditorUtility.DisplayDialog("Organize tile scenes",
                    $"Move {moves.Count} tile scene(s) into zone folders under {root}? Build Settings will be updated.",
                    "Move", "Cancel"))
                return;

            // Folders first, as their own step: a folder created inside a
            // batched asset edit doesn't exist yet for the moves that need it.
            foreach (var folder in moves.Values.Select(v => Path.GetDirectoryName(v)?.Replace('\\', '/')).Distinct())
                TileSceneIndex.EnsureFolder(folder);

            var done = new Dictionary<string, string>();
            foreach (var kv in moves)
            {
                string error = AssetDatabase.MoveAsset(kv.Key, kv.Value);
                if (string.IsNullOrEmpty(error)) done[kv.Key] = kv.Value;
                else ctx.Error($"Could not move {kv.Key}: {error}", Src);
            }

            TileSceneIndex.RemapBuildSettings(done);
            AssetDatabase.Refresh();
            _tileScenes = TileSceneIndex.FindAll();
            ctx.Success($"Moved {done.Count} tile scene(s) into zone folders.", Src);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Save / load / undo / validate
        // ─────────────────────────────────────────────────────────────────

        private void LoadFromDisk(DevToolsContext ctx, bool quiet)
        {
            _undo.Clear();
            _dirty = false;
            _textureStale = true;

            string full = Path.GetFullPath(ClientMapPath);
            if (!File.Exists(full))
            {
                _map = new ZoneMap();
                if (!quiet) ctx.Info("No zone map yet - add a zone to start one.", Src);
                return;
            }

            try
            {
                _map = ZoneMap.Load(full);
                if (_map.GetZone(_selected) == null)
                    _selected = _map.Zones.Select(z => z.Id).DefaultIfEmpty((ushort)0).Min();
                if (!quiet) ctx.Success($"Loaded {_map.ZoneCount} zone(s).", Src);
            }
            catch (Exception e)
            {
                _map = new ZoneMap();
                ctx.Error($"Could not read {ClientMapPath}: {e.Message}", Src);
            }
        }

        private void Save(DevToolsContext ctx)
        {
            var problems = KeyProblems();
            if (problems.Count > 0)
            {
                _report.Clear();
                _report.AddRange(problems.Select(p => "✗ " + p));
                ctx.Error("Fix the zone keys listed in the report before saving.", Src);
                return;
            }

            byte[] bytes = _map.ToBytes();
            string hash = ZoneMap.HashBytes(bytes);

            string clientFull = Path.GetFullPath(ClientMapPath);
            Directory.CreateDirectory(Path.GetDirectoryName(clientFull) ?? ".");
            File.WriteAllBytes(clientFull, bytes);
            AssetDatabase.ImportAsset(ClientMapPath);

            string serverNote;
            if (!string.IsNullOrWhiteSpace(_serverPath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_serverPath) ?? ".");
                    File.WriteAllBytes(_serverPath, bytes);
                    serverNote = $"and {_serverPath}";
                }
                catch (Exception e)
                {
                    serverNote = $"but NOT the server copy ({e.Message})";
                    ctx.Error($"Server copy not written: {e.Message}", Src);
                }
            }
            else
            {
                serverNote = "but no server path is set - the server won't see these zones";
                ctx.Warn("Set the server copy path so the world server gets the same zones.", Src);
            }

            _dirty = false;
            ctx.Success($"Saved {_map.ZoneCount} zone(s) (hash {hash}) to {ClientMapPath} {serverNote}. " +
                        "Rebuild/restart the world server to load them.", Src);
        }

        private void PushUndo()
        {
            _undo.Add(_map.ToBytes());
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            _map = ZoneMap.Load(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _textureStale = true;
            _overlayStale = true;
            SceneView.RepaintAll();
        }

        private List<string> KeyProblems()
        {
            var problems = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var z in _map.Zones)
            {
                if (string.IsNullOrWhiteSpace(z.Key)) problems.Add($"Zone {z.Id} has no key.");
                else if (!seen.Add(z.Key)) problems.Add($"Key '{z.Key}' is used by more than one zone.");
                if (string.IsNullOrWhiteSpace(z.DisplayName)) problems.Add($"Zone '{z.Key}' has no display name.");
                if (z.MaxLevel != 0 && z.MaxLevel < z.MinLevel) problems.Add($"Zone '{z.Key}' has max level below min level.");
            }

            return problems;
        }

        private void Validate(DevToolsContext ctx)
        {
            _report.Clear();
            _tileScenes = TileSceneIndex.FindAll();

            foreach (var p in KeyProblems()) _report.Add("✗ " + p);

            var counts = _map.CountCells();
            foreach (var z in _map.Zones.OrderBy(z => z.Id))
                if (!counts.ContainsKey(z.Id))
                    _report.Add($"⚠ '{z.Key}' is defined but not painted anywhere.");

            // Ground that has a scene but no zone - players would get no banner there.
            int unzonedTiles = 0;
            foreach (var tile in _tileScenes.Keys)
            {
                bool anyUnzoned = false;
                for (int cz = 0; cz < ZoneMap.CellsPerTile && !anyUnzoned; cz++)
                for (int cx = 0; cx < ZoneMap.CellsPerTile && !anyUnzoned; cx++)
                    if (_map.GetCell(tile, cx, cz) == 0) anyUnzoned = true;
                if (anyUnzoned) unzonedTiles++;
            }
            if (unzonedTiles > 0)
                _report.Add($"⚠ {unzonedTiles} tile scene(s) contain ground with no zone painted.");

            // Paint over ocean is fine; flagged as info only.
            int paintedWithoutScene = _map.PaintedTiles.Count(t => !_tileScenes.ContainsKey(t));
            if (paintedWithoutScene > 0)
                _report.Add($"ℹ {paintedWithoutScene} painted tile(s) have no tile scene (fine for sea or planned land).");

            // The server's copy.
            if (string.IsNullOrWhiteSpace(_serverPath) || !File.Exists(_serverPath))
                _report.Add("⚠ No server copy on disk - the world server will run with no zones.");
            else if (File.Exists(Path.GetFullPath(ClientMapPath)) &&
                     ZoneMap.HashBytes(File.ReadAllBytes(_serverPath)) != ZoneMap.HashBytes(File.ReadAllBytes(Path.GetFullPath(ClientMapPath))))
                _report.Add("✗ The client and server copies differ - Save again.");

            if (_dirty) _report.Add("⚠ Unsaved changes.");

            int errors = _report.Count(l => l.StartsWith("✗"));
            _report.Insert(0, errors == 0 ? $"✓ {_map.ZoneCount} zone(s) look good." : $"{errors} error(s):");

            if (errors > 0) ctx.Error($"Zones: {errors} error(s).", Src);
            else ctx.Success("Zones validated.", Src);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Small helpers
        // ─────────────────────────────────────────────────────────────────

        private static string SanitizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var chars = key.Trim().ToLowerInvariant().Select(c =>
                (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' ? c : c == ' ' || c == '-' ? '_' : '\0');
            return new string(chars.Where(c => c != '\0').ToArray());
        }

        private static Color ToColor(uint rgba) => new Color32(
            (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);

        private static uint FromColor(Color c)
        {
            Color32 c32 = c;
            return ((uint)c32.r << 24) | ((uint)c32.g << 16) | ((uint)c32.b << 8) | 0xFF;
        }

        /// <summary>Golden-ratio hue steps, so consecutive new zones never get similar colours.</summary>
        private static Color DistinctColor(int id)
        {
            float hue = (id * 0.618034f) % 1f;
            return Color.HSVToRGB(hue, 0.65f, 0.95f);
        }

        /// <summary>The world server's Data/world/zones.aczmap, next to the world DB Dev Tools knows about.</summary>
        private static string GuessServerPath(DevToolsContext ctx)
        {
            string db = ctx.Settings.worldServerDbPath;
            if (string.IsNullOrEmpty(db)) return "";
            string dataDir = Path.GetDirectoryName(db);
            return string.IsNullOrEmpty(dataDir) ? "" : Path.Combine(dataDir, "world", "zones.aczmap");
        }
    }
}