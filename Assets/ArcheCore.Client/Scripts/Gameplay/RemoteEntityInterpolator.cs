using UnityEngine;

namespace ArcheCore.Client.Gameplay
{
    /// <summary>
    /// Renders a server-controlled entity smoothly between the discrete
    /// position updates that actually arrive. Used by both remote players
    /// and NPCs - the problem is identical for both, and having one
    /// implementation means a fix to remote-player smoothness is
    /// automatically a fix to NPC smoothness.
    ///
    /// REPLACES the old pattern, which was this, in every entity's Update:
    ///
    ///     transform.position = Vector3.Lerp(
    ///         transform.position, _targetPosition, Time.deltaTime * 10f);
    ///
    /// That is not interpolation. It's exponential decay toward a moving
    /// point, and it's wrong in three separate ways that compound:
    ///
    ///   1. It never arrives. Each frame it covers a fraction of the
    ///      REMAINING distance, so the character is always behind, and
    ///      visibly decelerates as it approaches each sample. A character
    ///      running at constant speed should not look like it's easing into
    ///      every waypoint.
    ///   2. It's framerate dependent. `Time.deltaTime * 10f` is used as a
    ///      lerp factor, but lerp factors aren't rates - at 144fps the
    ///      character converges faster than at 30fps, so two players
    ///      watching the same movement see it happen at different speeds.
    ///   3. It has no idea when the next update is due, so it can't pace
    ///      itself to arrive just as the next one lands, and it has nothing
    ///      to do when one goes missing except keep decaying toward a stale
    ///      point - which reads as the character stopping dead.
    ///
    /// What this does instead is the standard approach: render the entity
    /// one update-interval in the PAST, moving at constant speed between
    /// the last two known positions. Being slightly behind live is what
    /// buys the smoothness; you trade a fixed ~50-100ms of latency on other
    /// people's positions for motion with no stutter in it. Every MMO makes
    /// this trade, including ArcheAge.
    ///
    /// The interval isn't a constant, because the server doesn't send at a
    /// constant rate: SnapshotDispatcher's LOD tiers mean a nearby player
    /// arrives every tick, a mid-range one every 3rd, a distant one every
    /// 10th. So the interval is MEASURED per entity and smoothed. A player
    /// walking toward you gets faster updates, this notices, and the
    /// interpolation tightens automatically.
    ///
    /// When an update is late or lost - unavoidable, these are unreliable
    /// packets - it extrapolates along the last known velocity rather than
    /// freezing. That's bounded by MaxExtrapolation, because extrapolating
    /// a character who actually stopped, or turned, for a full second
    /// produces a visible rubber-band snap when the truth arrives. A short
    /// extrapolation hides ordinary packet loss entirely; a long one trades
    /// a freeze for a teleport, which is worse.
    /// </summary>
    public class RemoteEntityInterpolator : MonoBehaviour
    {
        [Header("Interval learning")]

        /// <summary>
        /// Floor on the learned update interval. Below this the entity is
        /// effectively live and there's nothing to interpolate across;
        /// mostly this guards against a burst of updates arriving in the
        /// same frame collapsing the interval to near zero, which would
        /// make the next real gap look like a jump.
        /// </summary>
        [SerializeField] private float minInterval = 0.03f;

        /// <summary>
        /// Ceiling on the learned interval. Also the cutoff for what counts
        /// as a usable sample at all - a gap longer than this means the
        /// entity was standing still (the server stops sending for
        /// stationary entities) or we had a dropout, and neither is
        /// evidence that its update RATE changed. Feeding those into the
        /// average would make a player who idled for ten seconds
        /// interpolate their next step over half a second.
        /// </summary>
        [SerializeField] private float maxInterval = 0.5f;

        /// <summary>
        /// How fast the learned interval chases a new sample. Low, because
        /// the rate genuinely does change (LOD tier crossings) but network
        /// jitter makes individual samples noisy, and overreacting to one
        /// early packet makes the next gap look like a stall.
        /// </summary>
        [SerializeField] private float intervalSmoothing = 0.25f;

        [Header("Extrapolation")]

        /// <summary>
        /// How far past the expected arrival time to keep predicting.
        /// ~5 ticks at 20Hz. Long enough to cover ordinary loss invisibly,
        /// short enough that the correction when the real position lands is
        /// a nudge rather than a teleport.
        /// </summary>
        [SerializeField] private float maxExtrapolation = 0.25f;

        [Header("Correction")]

        /// <summary>
        /// Past this distance, don't interpolate - teleport. Mounts,
        /// respawns, GM teleports and long dropouts all produce legitimate
        /// jumps, and sliding a character 200 units across the map at walk
        /// speed looks far worse than a hard cut. Set this comfortably
        /// above the furthest a character can plausibly travel in one
        /// update interval.
        /// </summary>
        [SerializeField] private float snapDistance = 10f;

        [SerializeField] private float yawTurnSpeed = 12f;

        /// <summary>
        /// When true, facing is derived from the direction of travel rather
        /// than taken from the wire. Correct for NPCs, whose server-side
        /// wander AI has no concept of facing and reports yaw as zero -
        /// without this every NPC in the world stares due north while
        /// walking sideways. Wrong for players, who send real yaw and can
        /// face somewhere other than where they're going.
        /// </summary>
        public bool DeriveYawFromMotion { get; set; }

        private Vector3 _from;
        private Vector3 _to;
        private Vector3 _velocity;
        private float   _targetYawDegrees;

        private float _timer;
        private float _interval = 0.1f;
        private float _lastArrivalTime = -1f;
        private bool  _initialized;

        public Vector3 TargetPosition => _to;
        public Vector3 Velocity       => _velocity;

        /// <summary>
        /// Place the entity with no interpolation. Call on spawn, so the
        /// first real update interpolates from where it actually is rather
        /// than sliding in from the world origin.
        /// </summary>
        public void Initialize(Vector3 position, float yawDegrees)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

            _from = _to = position;
            _velocity = Vector3.zero;
            _targetYawDegrees = yawDegrees;
            _timer = 0f;
            _lastArrivalTime = -1f;
            _initialized = true;
        }

        /// <param name="yawDegrees">
        /// Facing in DEGREES. The wire format is radians (see
        /// EntityStateCodec); callers convert, because everything else in
        /// Unity is degrees and doing it here would mean one more place to
        /// get the unit wrong.
        /// </param>
        public void ApplyUpdate(Vector3 position, Vector3 velocity, float yawDegrees)
        {
            if (!_initialized)
            {
                Initialize(position, yawDegrees);
                _velocity = velocity;
                _lastArrivalTime = Time.time;
                return;
            }

            float now = Time.time;

            // Legitimate discontinuity - teleport rather than slide.
            if ((position - transform.position).sqrMagnitude > snapDistance * snapDistance)
            {
                transform.position = position;
                _from = _to = position;
                _velocity = velocity;
                _targetYawDegrees = yawDegrees;
                _timer = 0f;
                _lastArrivalTime = now;
                return;
            }

            if (_lastArrivalTime >= 0f)
            {
                float sample = now - _lastArrivalTime;

                // Only samples inside the plausible range teach us
                // anything. A longer gap means the entity went quiet
                // (stationary, or dropped packets), which says nothing
                // about its send rate - keep what we already learned.
                if (sample <= maxInterval)
                {
                    sample = Mathf.Max(sample, minInterval);
                    _interval = Mathf.Lerp(_interval, sample, intervalSmoothing);
                }
            }

            _lastArrivalTime = now;

            // Start from where the entity is being RENDERED, not from the
            // previous target. If we were extrapolating we're already past
            // the old target, and snapping back to it before interpolating
            // forward again is precisely the visible hitch this is all
            // meant to remove. Continuity beats correctness here - the
            // error is sub-centimetre and gone within one interval.
            _from = transform.position;
            _to = position;
            _velocity = velocity;
            _targetYawDegrees = yawDegrees;
            _timer = 0f;
        }

        private void Update()
        {
            if (!_initialized) return;

            _timer += Time.deltaTime;

            if (_timer <= _interval || _interval <= 0f)
            {
                // Constant-speed interpolation across the known gap. This
                // is the part the old exponential lerp got wrong.
                float t = _interval <= 0f ? 1f : _timer / _interval;
                transform.position = Vector3.Lerp(_from, _to, t);
            }
            else
            {
                // Overdue. Predict forward along the last known velocity,
                // bounded - see maxExtrapolation. A stopped entity reports
                // zero velocity, so this correctly holds it still rather
                // than drifting it onward, which is exactly why the client
                // has to send that final zero-velocity packet when it
                // stops moving.
                float overtime = Mathf.Min(_timer - _interval, maxExtrapolation);
                transform.position = _to + _velocity * overtime;
            }

            ApplyRotation();
        }

        private void ApplyRotation()
        {
            float targetYaw = _targetYawDegrees;

            if (DeriveYawFromMotion)
            {
                // Prefer velocity; fall back to the interpolation segment
                // for entities that report position but no velocity (mid
                // and far LOD tiers don't carry velocity bytes).
                Vector3 direction = _velocity.sqrMagnitude > 0.04f
                    ? _velocity
                    : _to - _from;

                direction.y = 0f;

                if (direction.sqrMagnitude < 0.0004f)
                    return; // standing still - keep current facing

                targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            }

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.Euler(0f, targetYaw, 0f),
                Time.deltaTime * yawTurnSpeed);
        }
    }
}