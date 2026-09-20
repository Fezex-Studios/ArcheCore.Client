using ArchCore.Client;
using ArcheCore.Client.Gameplay;
using UnityEngine;

namespace ArcheCore.Client.Debugging
{
    /// <summary>
    /// TEMPORARY. Prints what RemoteEntityInterpolator actually believes,
    /// once a second, for one entity.
    ///
    /// WHY THIS EXISTS
    ///
    /// The stutter has now survived two fixes that were both reasoned from
    /// how the motion LOOKED rather than from any measurement. Neither the
    /// learned interval nor the derived velocity is visible from outside,
    /// so every diagnosis so far has been a guess about values nobody has
    /// read. This reads them.
    ///
    /// HOW TO USE
    ///
    /// Add to the same GameObject as a REMOTE player (not your own), either
    /// in the Inspector while playing or by dropping it on the player
    /// prefab. Walk the other client in a straight line at various
    /// distances and watch the console.
    ///
    /// WHAT THE NUMBERS SHOULD SAY
    ///
    ///   interval  - near tier ~0.05, mid ~0.15, far ~0.50. If it reads
    ///               0.15 while the two characters are standing next to
    ///               each other, interval learning is broken.
    ///
    ///   rawGap    - if this is consistently much larger than the accepted
    ///               sample, updates are being discarded as dropouts.
    ///
    ///   updates   - should be about 20/sec near, 6-7 mid, 2 far. A count
    ///               well below that means packets aren't arriving, and the
    ///               problem is upstream of this component entirely.
    ///
    ///   velZero   - TRUE means the wire carried no velocity (mid and far
    ///               tiers don't), so extrapolation adds nothing and the
    ///               entity freezes at the end of each segment.
    ///
    ///   segLen    - should be roughly vel * interval. If it's much larger,
    ///               updates are arriving in bursts rather than evenly.
    ///
    ///   fps       - the observing client's own framerate. Stutter at 45fps
    ///               in an unfocused background build is not a netcode
    ///               problem, and this rules that in or out.
    ///
    /// Delete this file once the cause is found.
    /// </summary>
    public class InterpolationDebugLogger : MonoBehaviour
    {
        [SerializeField] private float logIntervalSeconds = 1f;

        /// <summary>
        /// Optional label so two logged entities can be told apart in the
        /// console. Defaults to the GameObject's name.
        /// </summary>
        [SerializeField] private string label;

        private RemoteEntityInterpolator _interp;
        private Transform _localPlayer;
        private float _timer;

        // Framerate over the logging window rather than one frame's
        // deltaTime, which is noisy enough to be misleading.
        private int   _frames;
        private float _frameTime;

        private void Awake()
        {
            _interp = GetComponent<RemoteEntityInterpolator>();

            if (string.IsNullOrEmpty(label))
                label = gameObject.name;
        }

        private void Update()
        {
            if (_interp == null) return;

            _frames++;
            _frameTime += Time.unscaledDeltaTime;
            _timer += Time.unscaledDeltaTime;

            if (_timer < logIntervalSeconds) return;

            float fps = _frames / Mathf.Max(_frameTime, 0.0001f);
            float distance = DistanceToLocalPlayer();

            Debug.Log(
                $"[Interp:{label}] dist={distance:F1} " +
                $"delay={_interp.CurrentDelay:F3} " +
                $"gap={_interp.ObservedGap:F3} " +
                $"buffered={_interp.BufferedSamples} " +
                $"extrapolating={_interp.IsExtrapolating} " +
                $"vel={_interp.Velocity.magnitude:F2} " +
                $"jumping={_interp.IsSimulatingJump} " +
                $"fps={fps:F0}");

            _timer = 0f;
            _frames = 0;
            _frameTime = 0f;
        }

        /// <summary>
        /// Distance to the local player, so the log line says which LOD
        /// tier this entity was in when the numbers were taken. Looked up
        /// lazily because the local player spawns after remotes sometimes.
        /// </summary>
        private float DistanceToLocalPlayer()
        {
            if (_localPlayer == null)
            {
                var registry = PlayerRegistry.Instance;
                if (registry == null) return -1f;

                foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                {
                    if (pc.isLocalPlayer)
                    {
                        _localPlayer = pc.transform;
                        break;
                    }
                }
            }

            if (_localPlayer == null) return -1f;

            return Vector3.Distance(_localPlayer.position, transform.position);
        }
    }
}