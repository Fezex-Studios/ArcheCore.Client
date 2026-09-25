using System;
using System.Collections.Generic;
using ArcheCore.Client.Networking;
using ArcheCore.Movement.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcheCore.Client.World
{
    /// <summary>
    /// Streams the static world in and out around the local player, one
    /// tile scene at a time. The client half of world partitioning - the
    /// server's half is TiledHeightField.
    ///
    /// HOW THE WORLD IS SPLIT
    ///
    ///   main_world            the PERSISTENT scene: camera, lighting, sky,
    ///                         registries, UI. Loaded once, never unloaded.
    ///                         Players, NPCs and nodes spawned from packets
    ///                         go here too (it's the active scene), so they
    ///                         survive the tile under them unloading.
    ///   tile_{x}_{z}          one additive scene per WorldGrid tile holding
    ///                         that square's terrain and scenery, authored at
    ///                         real WORLD coordinates. Created and filled by
    ///                         Dev Tools > World Tiles.
    ///
    /// A tile that has no scene in Build Settings is simply empty - open
    /// sea, unbuilt land. Nothing waits for it and nothing errors.
    ///
    /// WHAT STAYS LOADED
    ///
    /// Everything within loadRadius tiles of the player's tile (1 = the 3x3
    /// block). A tile unloads only once it is further than unloadRadius -
    /// the gap is hysteresis, so walking back and forth across a boundary
    /// doesn't load and unload the same scene every few seconds. Loads are
    /// issued nearest-first, at most maxConcurrentOperations at a time, so
    /// the ground under the player never queues behind the horizon.
    ///
    /// THE GROUND GATE
    ///
    /// IsAreaReady answers "is every tile that could hold the ground under
    /// this point loaded?" LocalCharacterMotor won't simulate until it is -
    /// otherwise a player spawning, respawning or being corrected onto a
    /// tile that hasn't finished loading falls through the world before the
    /// terrain exists. The 3x3 check (not just the one tile) is what makes
    /// unaligned terrains safe: see WorldGrid's authoring rule.
    ///
    /// COORDINATES
    ///
    /// Tile scenes are authored in world space. When one finishes loading
    /// after the floating origin has moved, its roots are shifted into
    /// local space on arrival (sceneLoaded, before any Start runs).
    ///
    /// LEGACY WORLDS
    ///
    /// With no tile scenes at all - today's main_world, with its terrain
    /// inside it - every tile is "missing", IsAreaReady is always true, and
    /// the game behaves exactly as it did before this existed.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("ArcheCore/World/World Streamer")]
    public sealed class WorldStreamer : MonoBehaviour
    {
        public static WorldStreamer Instance { get; private set; }

        [Tooltip("Tiles kept loaded around the player's tile. 1 = the 3x3 block (1.5km across at 512m tiles).")]
        [SerializeField, Range(1, 4)] private int loadRadius = 1;

        [Tooltip("Tiles further than this unload. Must be more than loadRadius - the gap stops boundary thrashing.")]
        [SerializeField, Range(2, 6)] private int unloadRadius = 2;

        [Tooltip("Scene loads/unloads in flight at once. Higher streams faster and hitches harder.")]
        [SerializeField, Range(1, 4)] private int maxConcurrentOperations = 2;

        [Tooltip("How often the wanted set is recomputed, in seconds. Cheap, but there is no reason to do it every frame.")]
        [SerializeField] private float refreshInterval = 0.2f;

        [SerializeField] private bool logStreaming = true;

        private enum TileState { Unloaded, Loading, Loaded, Unloading }

        private sealed class Tile
        {
            public TileCoord Coord;
            public TileState State;
            public Scene Scene;
        }

        private readonly Dictionary<TileCoord, Tile> _tiles = new Dictionary<TileCoord, Tile>();
        private readonly Dictionary<TileCoord, bool> _exists = new Dictionary<TileCoord, bool>();
        private readonly List<TileCoord> _wanted = new List<TileCoord>(32);
        private readonly List<Tile> _toUnload = new List<Tile>(8);
        private readonly List<TileCoord> _areaScratch = new List<TileCoord>(9);

        private float _refreshTimer;
        private int _inFlight;
        private TileCoord? _focusTile;

        /// <summary>Raised when a tile scene has finished loading and been placed.</summary>
        public event Action<TileCoord, Scene> TileLoaded;

        /// <summary>Raised when a tile scene has been unloaded.</summary>
        public event Action<TileCoord> TileUnloaded;

        /// <summary>The tile the local player is standing in, once known.</summary>
        public TileCoord? FocusTile => _focusTile;

        public int LoadedTileCount
        {
            get
            {
                int n = 0;
                foreach (var t in _tiles.Values) if (t.State == TileState.Loaded) n++;
                return n;
            }
        }

        public int OperationsInFlight => _inFlight;

        /// <summary>
        /// True when streaming is switched off because this client and the
        /// server disagree about tile size (see WorldSettings). Nothing is
        /// loaded and IsAreaReady never blocks.
        /// </summary>
        public bool Disabled => !WorldSettings.TileSizeMatches;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            unloadRadius = Mathf.Max(unloadRadius, loadRadius + 1);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            // Adopt tile scenes that are already open - the usual case in the
            // Editor, where you press Play with a few tiles open for editing.
            // They were authored in world space and nothing has shifted yet.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !WorldGrid.TryParseSceneName(scene.name, out var coord))
                    continue;

                var tile = GetTile(coord);
                tile.State = TileState.Loaded;
                tile.Scene = scene;
                _exists[coord] = true;
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (Disabled)
                return;

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f)
                return;

            _refreshTimer = refreshInterval;

            if (!TryGetFocusWorldPosition(out Vector3 focusWorld))
                return;

            var centre = WorldGrid.TileOf(focusWorld.x, focusWorld.z);
            _focusTile = centre;

            // Loads, nearest first, until the concurrency budget is used.
            _wanted.Clear();
            WorldGrid.GetTilesInRadius(centre, loadRadius, _wanted);

            for (int i = 0; i < _wanted.Count && _inFlight < maxConcurrentOperations; i++)
            {
                var coord = _wanted[i];
                if (!TileExists(coord)) continue;

                var tile = GetTile(coord);
                if (tile.State == TileState.Unloaded)
                    BeginLoad(tile);
            }

            // Unloads, for anything beyond the hysteresis ring.
            _toUnload.Clear();
            foreach (var tile in _tiles.Values)
            {
                if (tile.State == TileState.Loaded && WorldGrid.Distance(tile.Coord, centre) > unloadRadius)
                    _toUnload.Add(tile);
            }

            foreach (var tile in _toUnload)
            {
                if (_inFlight >= maxConcurrentOperations) break;
                BeginUnload(tile);
            }
        }

        // ── The ground gate ──────────────────────────────────────────────

        /// <summary>
        /// True when every existing tile scene in the 3x3 block around this
        /// WORLD position is loaded - so whatever terrain covers the point
        /// is in memory. Also true when streaming is disabled or no tile
        /// scene exists anywhere near (legacy single-scene world, open sea).
        /// </summary>
        public bool IsAreaReady(Vector3 worldPosition)
        {
            if (Disabled)
                return true;

            _areaScratch.Clear();
            WorldGrid.GetTilesInRadius(WorldGrid.TileOf(worldPosition.x, worldPosition.z), 1, _areaScratch);

            foreach (var coord in _areaScratch)
            {
                if (!TileExists(coord)) continue;
                if (!_tiles.TryGetValue(coord, out var tile) || tile.State != TileState.Loaded)
                    return false;
            }

            return true;
        }

        public bool IsTileLoaded(TileCoord coord) =>
            _tiles.TryGetValue(coord, out var tile) && tile.State == TileState.Loaded;

        // ── Loading / unloading ──────────────────────────────────────────

        private void BeginLoad(Tile tile)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(tile.Coord.SceneName, LoadSceneMode.Additive);

            if (op == null)
            {
                // Listed as loadable a moment ago but refused now - treat the
                // tile as missing rather than retrying forever.
                Debug.LogWarning($"[WorldStreamer] Could not load '{tile.Coord.SceneName}' - treating the tile as empty.");
                _exists[tile.Coord] = false;
                return;
            }

            tile.State = TileState.Loading;
            _inFlight++;

            if (logStreaming)
                Debug.Log($"[WorldStreamer] Loading {tile.Coord.SceneName}");

            op.completed += _ =>
            {
                _inFlight = Mathf.Max(0, _inFlight - 1);

                // The streamer may have been destroyed (disconnect) while the
                // load was in flight - Unity still finishes it.
                if (this == null)
                    return;

                // OnSceneLoaded has already placed it and marked it Loaded;
                // this only balances the in-flight count. If sceneLoaded
                // somehow didn't match it, don't leave it stuck in Loading.
                if (tile.State == TileState.Loading)
                {
                    var scene = SceneManager.GetSceneByName(tile.Coord.SceneName);
                    if (scene.IsValid())
                    {
                        PlaceLoadedScene(tile, scene);
                    }
                    else
                    {
                        tile.State = TileState.Unloaded;
                    }
                }
            };
        }

        /// <summary>
        /// Runs after the scene's Awake/OnEnable and before any Start - the
        /// earliest point its objects exist, and early enough that no script
        /// in it has seen a world-space position it'll later be wrong about.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Additive || !WorldGrid.TryParseSceneName(scene.name, out var coord))
                return;

            if (!_tiles.TryGetValue(coord, out var tile) || tile.State != TileState.Loading)
                return; // loaded by something else (an editor tool, a test) - leave it alone

            PlaceLoadedScene(tile, scene);
        }

        private void PlaceLoadedScene(Tile tile, Scene scene)
        {
            tile.Scene = scene;
            tile.State = TileState.Loaded;

            // Authored in world space; bring it into local space.
            if (WorldOrigin.IsShifted)
            {
                Vector3 toLocal = WorldOrigin.ToLocal(Vector3.zero); // = -Offset on X/Z
                foreach (var root in scene.GetRootGameObjects())
                    root.transform.position += toLocal;

                Physics.SyncTransforms();
            }

            if (logStreaming)
                Debug.Log($"[WorldStreamer] Loaded {tile.Coord.SceneName} ({LoadedTileCount} resident)");

            TileLoaded?.Invoke(tile.Coord, scene);
        }

        private void BeginUnload(Tile tile)
        {
            if (!tile.Scene.IsValid() || !tile.Scene.isLoaded)
            {
                tile.State = TileState.Unloaded;
                return;
            }

            AsyncOperation op = SceneManager.UnloadSceneAsync(tile.Scene);
            if (op == null)
            {
                tile.State = TileState.Unloaded;
                return;
            }

            tile.State = TileState.Unloading;
            _inFlight++;

            if (logStreaming)
                Debug.Log($"[WorldStreamer] Unloading {tile.Coord.SceneName}");

            op.completed += _ =>
            {
                _inFlight = Mathf.Max(0, _inFlight - 1);
                if (this == null) return;

                tile.State = TileState.Unloaded;
                tile.Scene = default;
                TileUnloaded?.Invoke(tile.Coord);
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private Tile GetTile(TileCoord coord)
        {
            if (!_tiles.TryGetValue(coord, out var tile))
                _tiles[coord] = tile = new Tile { Coord = coord, State = TileState.Unloaded };
            return tile;
        }

        /// <summary>
        /// Whether a scene for this tile is in Build Settings. Cached - the
        /// answer can't change while the game runs.
        /// </summary>
        private bool TileExists(TileCoord coord)
        {
            if (!_exists.TryGetValue(coord, out bool exists))
                _exists[coord] = exists = Application.CanStreamedLevelBeLoaded(coord.SceneName);
            return exists;
        }

        /// <summary>
        /// Streams around the local player once they exist. With no
        /// connection at all it follows the main camera instead, so a world
        /// scene can be flown around in the Editor with no server. While
        /// connected but not yet spawned it waits: the camera is still
        /// wherever the scene left it, and loading tiles there would only
        /// delay the ones under the player.
        /// </summary>
        private static bool TryGetFocusWorldPosition(out Vector3 world)
        {
            var network = ClientNetwork.Instance;
            if (network != null && network.LocalPlayer != null)
            {
                world = WorldOrigin.ToWorld(network.LocalPlayer.transform.position);
                return true;
            }

            if (network != null && network.ServerPeer != null)
            {
                world = default;
                return false;
            }

            var cam = Camera.main;
            if (cam != null)
            {
                world = WorldOrigin.ToWorld(cam.transform.position);
                return true;
            }

            world = default;
            return false;
        }
    }
}
