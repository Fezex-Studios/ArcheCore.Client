using System.Collections.Generic;
using ArcheCore.Network.Shared;
using UnityEngine;

namespace ArcheCore.Client.Gameplay
{
    /// <summary>
    /// Renders a server-controlled entity smoothly between the discrete
    /// updates that actually arrive. Used by remote players and NPCs both.
    ///
    /// SNAPSHOT BUFFER WITH FIXED DELAY
    ///
    /// This is the approach Valve documented for Source and the one most
    /// networked games use. It replaces a "measure the send rate, learn it,
    /// extrapolate when overdue" design that failed for a specific and
    /// instructive reason.
    ///
    /// WHY THE OLD ONE FAILED
    ///
    /// It timed updates as they ARRIVED and averaged that into a predicted
    /// interval. Measured on a live two-client test, arrivals looked like:
    ///
    ///     150ms 154ms 147ms 159ms 389ms 152ms 601ms 149ms 469ms ...
    ///
    /// The server sends on a fixed schedule. That spread is pure network
    /// jitter, and averaging it produces a number that is wrong in both
    /// directions. Predict 150ms and a 600ms gap leaves the character with
    /// 450ms of nothing to do: it extrapolates, runs out of budget, and
    /// then eases BACKWARDS toward its last known position - a visible
    /// lurch - before the real update arrives and throws it forward again.
    ///
    /// No constant fixes that. Raise the extrapolation budget and a
    /// character who genuinely stopped overshoots instead. The design was
    /// wrong, not the tuning.
    ///
    /// WHAT THIS DOES INSTEAD
    ///
    /// Every update is stored with its SERVER TICK, not its arrival time.
    /// Ticks are evenly spaced by definition - the server emits them on a
    /// fixed clock - so jitter disappears from the timeline entirely. A
    /// packet that took 600ms to arrive still carries the tick it was
    /// created on, and slots into its correct place in the buffer.
    ///
    /// Rendering then happens at (newest tick - delay), interpolating
    /// between the two buffered samples that bracket that moment. Both are
    /// real positions the server actually reported. Nothing is predicted,
    /// so nothing needs correcting, so there is nothing to snap back from.
    ///
    /// THE TRADE
    ///
    /// You see other players a fixed ~200ms in the past. That is not a
    /// defect, it's the payment: a CONSTANT delay is imperceptible, while
    /// variable error is exactly what reads as lag. Every game making this
    /// trade makes it for the same reason.
    ///
    /// The delay must exceed the gap between updates, or the buffer runs
    /// dry and you are extrapolating again. Since the gap changes with LOD
    /// tier - near every tick, mid every 3rd, far every 10th - the delay
    /// ADAPTS: it tracks the largest recent tick gap for this entity and
    /// keeps a margin above it. A player walking toward you tightens
    /// automatically as their tier changes.
    ///
    /// EXTRAPOLATION STILL EXISTS, as a fallback only, for when the buffer
    /// genuinely runs dry. In normal operation it never runs.
    /// </summary>
    public class RemoteEntityInterpolator : MonoBehaviour
    {
        [Header("Buffer")]

        /// <summary>
        /// How far behind the newest received tick to render, in seconds,
        /// as a FLOOR. The actual delay is this or (largest recent gap x
        /// DelayGapMultiplier), whichever is larger.
        ///
        /// 0.1 is about two ticks at 20Hz. Enough to absorb a single
        /// dropped near-tier packet without the buffer emptying.
        /// </summary>
        [SerializeField] private float minDelaySeconds = 0.1f;

        /// <summary>
        /// Ceiling on the adaptive delay. A far-tier entity updating every
        /// 10th tick would otherwise push the delay past a second, and at
        /// that point you are watching a noticeably stale character. Past
        /// this, accept occasional extrapolation instead.
        ///
        /// CHANGED from 0.35 to 0.75. Two-client testing showed mid-tier
        /// (30-80 units, every 3rd tick = ~150ms nominal) gaps regularly
        /// reaching 250-800ms once phase spreading and the "stop packet"
        /// gating in SnapshotDispatcher are accounted for - real gaps run
        /// well above the nominal tier rate, not just occasionally but
        /// routinely. At 0.35 the delay was pinned at the ceiling and
        /// IsExtrapolating was true on a large fraction of frames even
        /// during ordinary walking, which is exactly the "lurch and
        /// correct" pattern this class exists to avoid. 0.75 comfortably
        /// covers the measured mid-tier gap with the multiplier's margin
        /// intact. This costs staleness (you see a mid-tier entity up to
        /// ~0.75s behind instead of ~0.35s), which is the correct trade -
        /// see the class remarks on why constant delay beats variable
        /// error. Once the far tier (see SnapshotDispatcher.MidRange) is
        /// re-enabled, re-measure; a real far tier may need this even
        /// higher or a third, coarser delay band per LOD tier.
        /// </summary>
        [SerializeField] private float maxDelaySeconds = 0.75f;

        /// <summary>
        /// Safety margin over the observed gap. 1.5 means "render far
        /// enough back that one update can go missing entirely and the
        /// buffer still has something to interpolate toward".
        /// </summary>
        [SerializeField] private float delayGapMultiplier = 1.5f;

        /// <summary>
        /// Ring buffer size. 32 samples covers 1.6s of near-tier updates
        /// or 16s of far-tier ones - far more than the delay will ever
        /// reach back for, which is the point: the buffer should never be
        /// the reason a sample is unavailable.
        /// </summary>
        private const int BufferCapacity = 32;

        [Header("Fallback extrapolation")]

        /// <summary>
        /// Used ONLY when the buffer has run dry - render time is past the
        /// newest sample. Short, because this path means the prediction is
        /// unverified and every extra millisecond of it is more to correct.
        /// </summary>
        [SerializeField] private float maxExtrapolation = 0.25f;

        [Header("Correction")]

        /// <summary>
        /// Past this distance, teleport rather than slide. Mounts,
        /// respawns and GM teleports all produce legitimate jumps, and
        /// sliding a character 200 units at walk speed looks far worse
        /// than a hard cut.
        /// </summary>
        [SerializeField] private float snapDistance = 10f;

        [SerializeField] private float yawTurnSpeed = 12f;

        [Header("Jump simulation")]

        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundProbeDistance = 50f;

        /// <summary>
        /// When true, facing is derived from direction of travel rather
        /// than taken from the wire. Correct for NPCs whose server AI
        /// reports yaw as zero; wrong for players, who send real yaw and
        /// can face somewhere other than where they're going.
        /// </summary>
        public bool DeriveYawFromMotion { get; set; }

        /// <summary>
        /// One received update. Position and rotation as reported, stamped
        /// with the SERVER tick it describes - never the arrival time.
        /// </summary>
        private struct Sample
        {
            public uint          Tick;
            public Vector3       Position;
            public Vector3       Velocity;
            public float         Yaw;
            public float         Pitch;
            public float         Roll;
            public MovementState State;
        }

        private readonly List<Sample> _buffer = new(BufferCapacity);

        /// <summary>
        /// Unity time at which the newest buffered tick was received. The
        /// bridge between server time and local time: render time is
        /// derived as (newest tick) + (seconds elapsed locally since it
        /// arrived) - delay.
        /// </summary>
        private float _newestArrivalRealtime;
        private uint  _newestTick;
        private bool  _hasSamples;

        /// <summary>
        /// Largest gap, in seconds, between consecutive samples recently.
        /// Drives the adaptive delay. Decays slowly so a one-off hitch
        /// doesn't permanently inflate the delay, but a genuine LOD tier
        /// change is picked up within a couple of updates.
        /// </summary>
        private float _observedGap = 0.1f;

        private float _targetYawDegrees;
        private float _targetPitchDegrees;
        private float _targetRollDegrees;
        private Vector3 _renderVelocity;

        private bool _initialized;

        // ── Ballistic mode (jumps) ───────────────────────────────────────
        //
        // While a jump is in flight the VERTICAL axis is computed locally
        // and the horizontal axis keeps following the buffer. Vertical is
        // perfectly derivable from the takeoff instant and is the part a
        // slow update rate describes worst; horizontal isn't derivable at
        // all, since a jumping player can be pushed or blocked mid-air.

        private bool  _jumping;
        private float _jumpTime;
        private float _jumpOriginY;
        private float _jumpVelocityY;
        private float _jumpGroundY;
        private bool  _jumpGroundKnown;
        private float _jumpRegroundTimer;

        /// <summary>
        /// Hard cap for a jump whose ground was never found, in seconds.
        /// Deliberately far below MovementConstants.MaxJumpSimulationSeconds
        /// (3s, meant for a landing packet that's merely late). A takeoff
        /// raycast miss means we are ALREADY not tracking real ground, so
        /// riding that out for a full 3s of blind ballistic freefall is
        /// what produced "falls through the floor" - visually correct
        /// physics simulating a hole in the world that isn't there. 1s is
        /// comfortably past the ~0.77s round trip of a default jump; past
        /// that, trust the snapshot buffer instead of the guess.
        /// </summary>
        [SerializeField] private float maxUngroundedJumpSeconds = 1f;

        public Vector3 TargetPosition => _hasSamples ? _buffer[_buffer.Count - 1].Position : transform.position;
        public Vector3 Velocity       => _renderVelocity;
        public bool    IsSimulatingJump => _jumping;

        /// <summary>
        /// What the server says this entity is doing. Exposed for an
        /// Animator driver to read - that's why the byte is on the wire.
        /// </summary>
        public MovementState State { get; private set; }

        public event System.Action<MovementState, MovementState> StateChanged;

        // ── Diagnostics ──────────────────────────────────────────────────

        /// <summary>Current adaptive delay, in seconds.</summary>
        public float CurrentDelay => Mathf.Clamp(
            _observedGap * delayGapMultiplier, minDelaySeconds, maxDelaySeconds);

        /// <summary>Samples currently buffered. Should stay comfortably
        /// above 2; hitting 1 means the buffer is starving.</summary>
        public int BufferedSamples => _buffer.Count;

        /// <summary>Largest recent gap between samples, in seconds.</summary>
        public float ObservedGap => _observedGap;

        /// <summary>True when render time has passed the newest sample and
        /// the fallback extrapolation is running. Should be rare.</summary>
        public bool IsExtrapolating { get; private set; }

        public void Initialize(Vector3 position, float yawDegrees)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

            _buffer.Clear();
            _hasSamples = false;
            _targetYawDegrees = yawDegrees;
            _targetPitchDegrees = 0f;
            _targetRollDegrees = 0f;
            _renderVelocity = Vector3.zero;
            _observedGap = 0.1f;
            _initialized = true;

            _jumping = false;
            _jumpGroundKnown = false;
        }

        /// <summary>
        /// Legacy path for W2CPlayerPosition, which predates the snapshot
        /// packet and carries no tick. Falls back to synthesising one from
        /// arrival time, which reintroduces jitter - use the tick overload
        /// for anything coming from a world snapshot.
        /// </summary>
        public void ApplyUpdate(Vector3 position, Vector3 velocity, float yawDegrees) =>
            ApplyUpdate(position, velocity, yawDegrees, 0f, 0f, State, 0);

        public void ApplyUpdate(
            Vector3 position, Vector3 velocity, float yawDegrees,
            float pitchDegrees, float rollDegrees, MovementState state) =>
            ApplyUpdate(position, velocity, yawDegrees, pitchDegrees, rollDegrees, state, 0);

        /// <param name="serverTick">
        /// The tick this state describes. THE IMPORTANT ARGUMENT. Pass 0
        /// only if genuinely unavailable; a synthesised tick puts network
        /// jitter back into the timeline, which is exactly what this class
        /// exists to remove.
        /// </param>
        public void ApplyUpdate(
            Vector3 position,
            Vector3 velocity,
            float yawDegrees,
            float pitchDegrees,
            float rollDegrees,
            MovementState state,
            uint serverTick)
        {
            if (state != State)
            {
                var previous = State;
                State = state;
                StateChanged?.Invoke(previous, state);
            }

            if (!_initialized)
            {
                Initialize(position, yawDegrees);
            }

            // No tick available (legacy opcode): synthesise one from the
            // local clock so the buffer still works, accepting the jitter.
            if (serverTick == 0)
            {
                serverTick = _hasSamples
                    ? _newestTick + 1
                    : 1;
            }

            // Out of order or duplicate - snapshots are unreliable and can
            // overtake each other. The buffer is ordered by tick, so a late
            // arrival is dropped rather than corrupting the timeline.
            if (_hasSamples && serverTick <= _newestTick)
                return;

            // Legitimate discontinuity. Not suppressed during a jump -
            // vertical divergence during an arc is expected, but a jump
            // never moves a character snapDistance horizontally in one
            // update, so the check is done on the horizontal plane only.
            if (_hasSamples)
            {
                var previousPos = _buffer[_buffer.Count - 1].Position;
                var horizontalDelta = new Vector3(
                    position.x - previousPos.x, 0f, position.z - previousPos.z);

                if (horizontalDelta.sqrMagnitude > snapDistance * snapDistance)
                {
                    transform.position = position;
                    _buffer.Clear();
                    _hasSamples = false;
                    _jumping = false;
                }
            }

            float gapSeconds = 0f;

            if (_hasSamples)
            {
                gapSeconds = (serverTick - _newestTick) / MovementConstants.ServerTickRate;

                // Track the largest recent gap, decaying slowly. Rising
                // fast matters (a tier change must widen the delay before
                // the buffer starves); falling slowly is fine, because an
                // over-wide delay only costs a little staleness while an
                // under-wide one costs a visible stall.
                // REVERTED to 0.05 (was briefly 0.15). Decaying the
                // observed-gap ceiling three times faster meant the safety
                // margin collapsed back toward the CURRENT gap well before
                // the next routine gap of similar size arrived, so the
                // delay kept shrinking just in time to be too small again -
                // a self-inflicted extrapolation cycle on ordinary mid-tier
                // traffic, not just real LOD-tier changes. Falling slowly
                // is supposed to be cheap (a little staleness) precisely so
                // it can stay wide enough to survive normal jitter; 0.05
                // restores that.
                _observedGap = gapSeconds > _observedGap
                    ? gapSeconds
                    : Mathf.Lerp(_observedGap, gapSeconds, 0.05f);
            }

            _buffer.Add(new Sample
            {
                Tick     = serverTick,
                Position = position,
                Velocity = velocity,
                Yaw      = yawDegrees,
                Pitch    = pitchDegrees,
                Roll     = rollDegrees,
                State    = state
            });

            if (_buffer.Count > BufferCapacity)
                _buffer.RemoveAt(0);

            _newestTick = serverTick;
            _newestArrivalRealtime = Time.time;
            _hasSamples = true;
        }

        public void BeginJump(Vector3 origin, Vector3 horizontalVelocity, float verticalVelocity)
        {
            if (!_initialized)
                Initialize(origin, _targetYawDegrees);

            _jumping       = true;
            _jumpTime      = 0f;
            _jumpOriginY   = origin.y;
            _jumpVelocityY = verticalVelocity;
            _jumpGroundKnown = false;
            _jumpRegroundTimer = 0f;

            // Raycast ONCE, at takeoff. A per-frame ray finds whatever is
            // under the character mid-flight, including the lip of the
            // ledge it just left, which ends the jump early and drops it
            // through the floor.
            if (Physics.Raycast(
                    origin + Vector3.up * 0.5f, Vector3.down,
                    out RaycastHit hit, groundProbeDistance, groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                _jumpGroundY = hit.point.y;
                _jumpGroundKnown = true;
            }

            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, origin.y, p.z);
        }

        private void EndJump()
        {
            _jumping = false;
            _jumpGroundKnown = false;
        }

        private void Update()
        {
            if (!_initialized || !_hasSamples) return;

            // Where in SERVER TIME to render, expressed in ticks.
            //
            // Local elapsed time since the newest sample arrived, converted
            // to ticks, added to that sample's tick, minus the delay. Using
            // the newest sample's arrival as the anchor means a burst of
            // packets or a long silence shifts the anchor but never the
            // spacing, because the spacing comes from the ticks themselves.
            float elapsed = Time.time - _newestArrivalRealtime;
            float renderTick = _newestTick
                             + elapsed * MovementConstants.ServerTickRate
                             - CurrentDelay * MovementConstants.ServerTickRate;

            SampleBufferAt(renderTick, out Vector3 position, out Vector3 velocity,
                           out float yaw, out float pitch, out float roll);

            _renderVelocity = velocity;
            _targetYawDegrees = yaw;
            _targetPitchDegrees = pitch;
            _targetRollDegrees = roll;

            if (_jumping)
            {
                UpdateJump(position);
            }
            else
            {
                transform.position = position;
            }

            ApplyRotation();
        }

        /// <summary>
        /// Find the two buffered samples that bracket the requested tick
        /// and interpolate between them. Both are real positions the server
        /// reported, which is the entire point - there is nothing predicted
        /// here to later be corrected.
        /// </summary>
        private void SampleBufferAt(
            float renderTick,
            out Vector3 position, out Vector3 velocity,
            out float yaw, out float pitch, out float roll)
        {
            IsExtrapolating = false;

            // Before the oldest sample: the buffer hasn't filled yet, or
            // the delay just widened. Hold at the oldest rather than
            // inventing anything.
            var oldest = _buffer[0];

            if (renderTick <= oldest.Tick || _buffer.Count == 1)
            {
                position = oldest.Position;
                velocity = oldest.Velocity;
                yaw = oldest.Yaw; pitch = oldest.Pitch; roll = oldest.Roll;
                return;
            }

            var newest = _buffer[_buffer.Count - 1];

            // Past the newest: buffer has run dry. This is the ONLY place
            // extrapolation happens, and in normal operation it doesn't -
            // the adaptive delay exists to keep render time behind the
            // newest sample. Seeing IsExtrapolating true regularly means
            // the delay is too short for this entity's update rate.
            if (renderTick >= newest.Tick)
            {
                IsExtrapolating = true;

                float overtimeSeconds = Mathf.Min(
                    (renderTick - newest.Tick) / MovementConstants.ServerTickRate,
                    maxExtrapolation);

                position = newest.Position + newest.Velocity * overtimeSeconds;
                velocity = newest.Velocity;
                yaw = newest.Yaw; pitch = newest.Pitch; roll = newest.Roll;
                return;
            }

            // The normal case. Walk back to find the bracketing pair -
            // from the end, because render time is usually near the newest
            // couple of samples.
            for (int i = _buffer.Count - 2; i >= 0; i--)
            {
                var a = _buffer[i];
                var b = _buffer[i + 1];

                if (renderTick < a.Tick)
                    continue;

                float span = b.Tick - a.Tick;
                float t = span <= 0f ? 1f : (renderTick - a.Tick) / span;

                position = Vector3.Lerp(a.Position, b.Position, t);

                // Velocity from the SEGMENT rather than the reported value.
                // It's what the character is actually doing on screen right
                // now, which is what a yaw-from-motion or animation driver
                // wants. The reported velocity describes the instant the
                // sample was taken, which is already in the past.
                float segmentSeconds = span / MovementConstants.ServerTickRate;
                velocity = segmentSeconds > 0f
                    ? (b.Position - a.Position) / segmentSeconds
                    : Vector3.zero;

                yaw   = Mathf.LerpAngle(a.Yaw, b.Yaw, t);
                pitch = Mathf.LerpAngle(a.Pitch, b.Pitch, t);
                roll  = Mathf.LerpAngle(a.Roll, b.Roll, t);

                // Everything older than the pair we just used will never be
                // needed again - render time only moves forward.
                if (i > 0)
                    _buffer.RemoveRange(0, i);

                return;
            }

            position = oldest.Position;
            velocity = oldest.Velocity;
            yaw = oldest.Yaw; pitch = oldest.Pitch; roll = oldest.Roll;
        }

        /// <summary>
        /// Vertical from the ballistic arc, horizontal from the buffer.
        /// </summary>
        private void UpdateJump(Vector3 bufferedPosition)
        {
            _jumpTime += Time.deltaTime;

            // Closed form, not incremental. Accumulating velocity per frame
            // makes apex height depend on framerate.
            float y = _jumpOriginY
                    + _jumpVelocityY * _jumpTime
                    + 0.5f * MovementConstants.Gravity * _jumpTime * _jumpTime;

            transform.position = new Vector3(bufferedPosition.x, y, bufferedPosition.z);

            bool descending = _jumpVelocityY + MovementConstants.Gravity * _jumpTime < 0f;

            if (descending && _jumpGroundKnown && y <= _jumpGroundY)
            {
                transform.position = new Vector3(
                    transform.position.x, _jumpGroundY, transform.position.z);
                EndJump();
                return;
            }

            // The takeoff-only raycast deliberately skips the ledge just
            // left, but that blind spot only matters AT takeoff. Once
            // descending, re-probe periodically from the current simulated
            // position - this is what a takeoff-only raycast misses
            // (terrain the takeoff ray's origin didn't see, a moving
            // platform, geometry that streamed in after takeoff) without
            // reintroducing the ledge problem, since by now we've moved
            // past it.
            if (descending && !_jumpGroundKnown)
            {
                _jumpRegroundTimer -= Time.deltaTime;
                if (_jumpRegroundTimer <= 0f)
                {
                    _jumpRegroundTimer = 0.1f;

                    if (Physics.Raycast(
                            new Vector3(bufferedPosition.x, y, bufferedPosition.z) + Vector3.up * 0.5f,
                            Vector3.down, out RaycastHit reground, groundProbeDistance, groundMask,
                            QueryTriggerInteraction.Ignore))
                    {
                        _jumpGroundY = reground.point.y;
                        _jumpGroundKnown = true;
                    }
                }
            }

            // Ground was never found. Rather than let the ballistic
            // freefall run for the full 3s grace period meant for a merely
            // late landing packet - which is what made an entity visibly
            // sink through the floor - give up on the guess much sooner
            // and fall back to whatever the snapshot buffer says. The
            // buffer keeps receiving real server positions the whole time
            // this simulation runs, so it is never stale, only unused.
            float cap = _jumpGroundKnown
                ? MovementConstants.MaxJumpSimulationSeconds
                : maxUngroundedJumpSeconds;

            if (_jumpTime >= cap)
            {
                EndJump();
                return;
            }

            // The server knows about geometry this client didn't probe -
            // roofs, ledges, anything the takeoff raycast missed. The 0.1s
            // guard is because the snapshot describing the pre-jump frame
            // can land just after the jump event does.
           if ((State & MovementState.Airborne) == 0 && _jumpTime > CurrentDelay + 0.1f)
               EndJump();
        }

        private void ApplyRotation()
        {
            float targetYaw = _targetYawDegrees;

            if (DeriveYawFromMotion)
            {
                Vector3 direction = _renderVelocity;
                direction.y = 0f;

                if (direction.sqrMagnitude < 0.0004f)
                    return; // standing still - keep current facing

                targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            }

            // One target quaternion, one slerp. Three independent
            // interpolations on the same transform fight each other,
            // because each writes the whole rotation.
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.Euler(_targetPitchDegrees, targetYaw, _targetRollDegrees),
                Time.deltaTime * yawTurnSpeed);
        }
    }
}