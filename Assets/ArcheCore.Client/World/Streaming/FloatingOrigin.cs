using System.Collections.Generic;
using ArcheCore.Client.Networking;
using ArcheCore.Movement.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcheCore.Client.World
{
    /// <summary>
    /// Keeps the local player near Unity's origin by moving the WORLD when
    /// they wander too far from it. See WorldOrigin for why.
    ///
    /// WHAT A SHIFT DOES, IN ORDER
    ///
    ///   1. Picks a new origin: the player's world position snapped to a
    ///      tile boundary. Whole tiles only, so every shift is an exact
    ///      float and tile scenes loaded later land on the same positions
    ///      they would have without any shifts at all.
    ///   2. Moves every root GameObject in every loaded scene by the delta -
    ///      terrain tiles, props, players, NPCs, the camera. Children follow
    ///      their roots. DontDestroyOnLoad objects are included.
    ///   3. Shifts world-space particles and trails, which live outside the
    ///      transform hierarchy and would otherwise be left behind.
    ///   4. Physics.SyncTransforms, so the very next raycast sees the new
    ///      collider positions.
    ///   5. Raises WorldOrigin.Shifted so caches outside transforms (the
    ///      local motor's simulation state, remote interpolation buffers, the
    ///      camera's last-target position, floating damage numbers) move too.
    ///
    /// All in one frame, between Updates, so nothing ever observes the world
    /// half-moved.
    ///
    /// THE THRESHOLD
    ///
    /// Defaults to 6144m (12 tiles). Well before float jitter becomes
    /// visible, well beyond anything today's world reaches - so right now
    /// this is plumbing that never fires in normal play. Lower it, or use
    /// the "Shift Origin To Player Now" context menu, to exercise it.
    ///
    /// KNOWN LIMITS - WORTH KNOWING BEFORE LOWERING THE THRESHOLD
    ///
    ///   - Static batching: Unity bakes static-batched meshes at their
    ///     authored positions and they can't be moved afterwards. Turn it
    ///     off (Project Settings > Player > Other Settings > Static
    ///     Batching) - Unity 6's GPU Resident Drawer and the SRP Batcher are
    ///     the replacement anyway.
    ///   - Baked light probes / APV stay where they were baked. Dynamic
    ///     objects sample the wrong probes after a shift. Outdoor lighting
    ///     from the directional light is unaffected.
    ///   - Anything that stores world positions in a component field (a
    ///     patrol path, a cached target point) must subscribe to
    ///     WorldOrigin.Shifted. Transforms are handled; plain Vector3 fields
    ///     are not, and can't be found generically.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ArcheCore/World/Floating Origin")]
    public sealed class FloatingOrigin : MonoBehaviour
    {
        public static FloatingOrigin Instance { get; private set; }

        [Tooltip("Shift the world once the player is this far from Unity's origin (metres, ground plane).")]
        [SerializeField] private float shiftThreshold = 6144f;

        [Tooltip("Master switch. Off keeps Offset at zero forever - world space and local space stay identical.")]
        [SerializeField] private bool shiftingEnabled = true;

        private readonly List<GameObject> _roots = new List<GameObject>(256);
        private ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[256];

        public float ShiftThreshold
        {
            get => shiftThreshold;
            set => shiftThreshold = Mathf.Max(WorldGrid.TileSize, value);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// LateUpdate, after the motor and interpolators have written this
        /// frame's positions and before rendering - so the frame that shifts
        /// renders the shifted world, never a mix.
        /// </summary>
        private void LateUpdate()
        {
            if (!shiftingEnabled)
                return;

            Transform focus = FocusTransform();
            if (focus == null)
                return;

            Vector3 p = focus.position;
            if (p.x * p.x + p.z * p.z < shiftThreshold * shiftThreshold)
                return;

            ShiftTo(WorldOrigin.ToWorld(p));
        }

        [ContextMenu("Shift Origin To Player Now")]
        private void ShiftNowForTesting()
        {
            Transform focus = FocusTransform();
            if (focus != null)
                ShiftTo(WorldOrigin.ToWorld(focus.position));
        }

        private static Transform FocusTransform()
        {
            var network = ClientNetwork.Instance;
            if (network != null && network.LocalPlayer != null)
                return network.LocalPlayer.transform;

            return null;
        }

        /// <summary>
        /// Moves the origin to the tile boundary nearest this world position.
        /// Public so a long-distance teleport can re-centre immediately
        /// rather than waiting for the threshold.
        /// </summary>
        public void ShiftTo(Vector3 worldPosition)
        {
            var newOffset = new Vector3(
                WorldGrid.SnapToTileBoundary(worldPosition.x),
                0f,
                WorldGrid.SnapToTileBoundary(worldPosition.z));

            if (newOffset == WorldOrigin.Offset)
                return;

            Vector3 delta = WorldOrigin.MoveOrigin(newOffset);

            MoveAllRoots(delta);
            ShiftParticles(delta);
            ShiftTrails(delta);
            Physics.SyncTransforms();

            WorldOrigin.RaiseShifted(delta);

            Debug.Log($"[FloatingOrigin] Shifted world by {delta} - origin is now world {newOffset}.");
        }

        private void MoveAllRoots(Vector3 delta)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                MoveRoots(SceneManager.GetSceneAt(i), delta);

            // The DontDestroyOnLoad scene isn't in SceneManager's list. Reach
            // it through ClientNetwork, which lives there.
            var network = ClientNetwork.Instance;
            if (network != null)
            {
                Scene persistent = network.gameObject.scene;
                if (persistent.IsValid() && persistent.name == "DontDestroyOnLoad")
                    MoveRoots(persistent, delta);
            }
        }

        private void MoveRoots(Scene scene, Vector3 delta)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            _roots.Clear();
            scene.GetRootGameObjects(_roots);

            foreach (var root in _roots)
                root.transform.position += delta;
        }

        /// <summary>
        /// Particles simulated in world space keep their own positions,
        /// outside any transform. Without this every smoke plume and spell
        /// trail visibly jumps by the shift distance.
        /// </summary>
        private void ShiftParticles(Vector3 delta)
        {
            var systems = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);

            foreach (var ps in systems)
            {
                if (ps.main.simulationSpace != ParticleSystemSimulationSpace.World)
                    continue;

                int count = ps.particleCount;
                if (count == 0) continue;

                if (_particles.Length < count)
                    _particles = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];

                int n = ps.GetParticles(_particles);
                for (int i = 0; i < n; i++)
                    _particles[i].position += delta;

                ps.SetParticles(_particles, n);
            }
        }

        private static void ShiftTrails(Vector3 delta)
        {
            var trails = FindObjectsByType<TrailRenderer>(FindObjectsSortMode.None);

            foreach (var trail in trails)
            {
                int count = trail.positionCount;
                if (count == 0) continue;

                // Exact-size array: SetPositions takes the whole array as the
                // trail. Shifts are rare, so the allocation doesn't matter.
                var points = new Vector3[count];
                trail.GetPositions(points);

                for (int i = 0; i < count; i++)
                    points[i] += delta;

                trail.SetPositions(points);
            }
        }
    }
}
