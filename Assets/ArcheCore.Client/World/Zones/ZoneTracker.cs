using System;
using System.IO;
using ArcheCore.Client.Networking;
using ArcheCore.Movement.World;
using UnityEngine;

namespace ArcheCore.Client.World
{
    /// <summary>
    /// Knows which zone the local player is standing in, and says so when it
    /// changes - the zone name banner, and later music, weather and the
    /// minimap label all hang off ZoneChanged.
    ///
    /// Reads the SAME zone map file the world server uses, shipped in
    /// StreamingAssets/World/zones.aczmap (Dev Tools > Zones saves it there
    /// and to the server). Looking the zone up locally means crossing a
    /// border costs no network traffic at all; the server tracks zones
    /// independently for anything with rules attached (PvP, quests).
    ///
    /// On entering the world the server sends its map's hash
    /// (WorldSettings.ServerZoneMapHash). A different hash means this build
    /// shipped a stale map - it still works, but may name the wrong zone
    /// near a border that moved, so it's logged as an error for you rather
    /// than shown to the player.
    ///
    /// No file is fine: the tracker idles and nothing is ever announced.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ArcheCore/World/Zone Tracker")]
    public sealed class ZoneTracker : MonoBehaviour
    {
        /// <summary>Path under StreamingAssets. Dev Tools > Zones writes here.</summary>
        public const string StreamingAssetsPath = "World/zones.aczmap";

        public static ZoneTracker Instance { get; private set; }

        [Tooltip("How often the player's zone is re-checked, in seconds.")]
        [SerializeField] private float checkInterval = 0.25f;

        [Tooltip("Show the zone name banner when entering a zone.")]
        [SerializeField] private bool showBanner = true;

        /// <summary>The loaded map; empty (never null) when there's no file.</summary>
        public ZoneMap Map { get; private set; } = new ZoneMap();

        /// <summary>Hash of the file this client loaded, "" if none.</summary>
        public string LocalHash { get; private set; } = "";

        /// <summary>Zone the local player is in, or null for unzoned ground.</summary>
        public ZoneDefinition Current { get; private set; }

        /// <summary>(previous, current) - either may be null for "no zone".</summary>
        public event Action<ZoneDefinition, ZoneDefinition> ZoneChanged;

        private ushort _currentId;
        private bool _hasPosition;
        private bool _hashChecked;
        private float _timer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            LoadMap();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void LoadMap()
        {
            string path = Path.Combine(Application.streamingAssetsPath, StreamingAssetsPath);

            if (!File.Exists(path))
            {
                Debug.Log($"[ZoneTracker] No zone map at {path} - no zones will be announced.");
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Map = ZoneMap.Load(bytes);
                LocalHash = ZoneMap.HashBytes(bytes);
                Debug.Log($"[ZoneTracker] Loaded {Map.ZoneCount} zone(s) (hash {LocalHash}).");
            }
            catch (Exception e)
            {
                Map = new ZoneMap();
                Debug.LogError($"[ZoneTracker] Could not read {path}: {e.Message}");
            }
        }

        private void Update()
        {
            CheckServerHash();

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = checkInterval;

            if (Map.ZoneCount == 0 || !TryGetFocusWorldPosition(out Vector3 world))
                return;

            ushort id = Map.ZoneIdAt(world.x, world.z);

            // The first position after spawning always "enters" its zone, so
            // logging in shows where you are.
            if (_hasPosition && id == _currentId)
                return;

            _hasPosition = true;

            var previous = Current;
            _currentId = id;
            Current = Map.GetZone(id);

            try { ZoneChanged?.Invoke(previous, Current); }
            catch (Exception e) { Debug.LogException(e); }

            if (showBanner && Current != null)
                ZoneBannerUI.Show(Current);
        }

        private void CheckServerHash()
        {
            if (_hashChecked || !WorldSettings.ReceivedFromServer)
                return;

            _hashChecked = true;
            string server = WorldSettings.ServerZoneMapHash ?? "";

            if (server == LocalHash)
                return;

            Debug.LogError(
                $"[ZoneTracker] This client's zone map ({(LocalHash == "" ? "none" : LocalHash)}) differs from shard " +
                $"'{WorldSettings.ShardName}' ({(server == "" ? "none" : server)}). Zone names near changed borders may be " +
                "wrong. Save the map again from Dev Tools > Zones so both copies match, and rebuild.");
        }

        /// <summary>
        /// Called when a new connection starts: the next position counts as
        /// arriving, so the banner shows again on login.
        /// </summary>
        public void ResetForNewSession()
        {
            _hasPosition = false;
            _hashChecked = false;
            _currentId = 0;
            Current = null;
        }

        private static bool TryGetFocusWorldPosition(out Vector3 world)
        {
            var network = ClientNetwork.Instance;
            if (network != null && network.LocalPlayer != null)
            {
                world = WorldOrigin.ToWorld(network.LocalPlayer.transform.position);
                return true;
            }

            // Offline in the Editor: follow the camera, so flying around a
            // world scene shows the banners.
            if (network == null || network.ServerPeer == null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    world = WorldOrigin.ToWorld(cam.transform.position);
                    return true;
                }
            }

            world = default;
            return false;
        }
    }
}
