using ArcheCore.Client.World;
using ArcheCore.Movement;
using UnityEngine;
using SVector3 = System.Numerics.Vector3;

namespace ArcheCore.Client.Movement
{
    /// <summary>
    /// Unity adapter for the local player: gathers input, runs the shared
    /// CharacterMotor on a FIXED timestep, and renders between steps.
    ///
    /// WHY THE FIXED TIMESTEP IS THE POINT OF THIS CLASS
    ///
    /// The motor must see a constant dt or the same inputs produce different
    /// results on two machines, and server reconciliation stops being
    /// possible. So frame time is accumulated and the simulation is stepped
    /// a whole number of times per frame, with the leftover used to
    /// interpolate the RENDERED transform between the last two simulated
    /// states. That is why the visual stays smooth at any framerate while
    /// the simulation runs at a fixed rate - and it is the same reason
    /// remote entities are rendered from a buffer rather than chased.
    ///
    /// This deliberately does not touch CharacterController. The motor does
    /// its own sweeping through ICollisionWorld, and having PhysX's
    /// controller also asserting a position would mean two systems writing
    /// one transform - which is precisely the bug that put remote players
    /// under the terrain.
    ///
    /// WHAT IS NOT HERE YET: prediction history and reconciliation. The
    /// structure is ready for it - inputs carry a Sequence, the motor is
    /// pure, and MoveState is a copyable struct - but until the server runs
    /// the motor too there is nothing to reconcile against. See the notes
    /// in MIGRATION.md.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalCharacterMotor : MonoBehaviour
    {
        [Header("Simulation")]

        /// <summary>
        /// Fixed simulation rate. 60Hz is a reasonable default: high enough
        /// that collision resolution is accurate on fast falls, low enough
        /// that replaying a second of inputs during reconciliation is cheap.
        /// It does NOT have to match the server's tick rate - the server
        /// steps whatever inputs arrive - but keeping them related makes the
        /// arithmetic easier to reason about.
        /// </summary>
        [SerializeField] private int simulationRate = 60;

        /// <summary>
        /// Ceiling on catch-up steps in one frame. Without it, a long editor
        /// hitch produces hundreds of steps in the next frame, which takes
        /// long enough to cause another hitch - the classic spiral of death.
        /// Dropping simulation time is the lesser evil.
        /// </summary>
        [SerializeField] private int maxStepsPerFrame = 5;

        [Header("World")]

        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private LayerMask waterMask = 0;

        [Header("Capsule")]

        [SerializeField] private float height = 2f;
        [SerializeField] private float radius = 0.5f;

        [Header("Ground speed")]

        [SerializeField] private float walkSpeed = 2.5f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float sprintSpeed = 8f;

        /// <summary>
        /// How hard the character chases its target speed, in units/sec^2.
        /// This is the dial that decides whether movement feels crisp or
        /// slippery, and it matters far more to "feel" than top speed does.
        /// At 40 a run reaches 5 u/s in about an eighth of a second. Lower
        /// gives weight and ramp-up; much higher is instant and arcade-like.
        /// </summary>
        [SerializeField] private float groundAcceleration = 40f;

        /// <summary>How hard it stops. Below acceleration and the character
        /// slides on after you release the key.</summary>
        [SerializeField] private float groundFriction = 30f;

        [Header("Air")]

        [SerializeField] private float jumpHeight = 0.8f;

        /// <summary>
        /// The real cure for floatiness. Raising this shortens the whole
        /// arc without lowering the jump - a jump that takes most of a
        /// second is what reads as moon gravity, not one that goes high.
        /// </summary>
        [SerializeField] private float gravity = -30f;

        /// <summary>Fraction of ground acceleration usable mid-air. 0 is
        /// realistic and feels awful; 1 lets players fly.</summary>
        [SerializeField, Range(0f, 1f)] private float airControl = 0.3f;

        [Header("Terrain")]

        [SerializeField] private float slopeLimit = 50f;
        [SerializeField] private float stepHeight = 0.4f;

        /// <summary>How far below the feet to look for ground while already
        /// grounded. Too small and descending slopes turn into a series of
        /// small hops.</summary>
        [SerializeField] private float groundSnapDistance = 0.5f;

        private CharacterMotor _motor;
        private MovementProfile _profile;
        private MoveState _state;
        private MoveState _previousState;

        private float _accumulator;
        private uint _sequence;

        /// <summary>
        /// True while the simulation is held because the terrain under the
        /// character hasn't streamed in yet (see WorldStreamer.IsAreaReady).
        /// The character stays exactly where it was placed - no gravity, no
        /// input - until the ground exists to stand on.
        /// </summary>
        public bool IsWaitingForWorld { get; private set; }

        /// <summary>Latest simulated state. The network layer reads this
        /// rather than transform.position, because the transform is an
        /// interpolated render pose and may sit between two ticks.</summary>
        public MoveState State => _state;
        public MovementProfile Profile => _profile;

        /// <summary>Supplies input each simulation step. Assign from your
        /// input layer; left null, the character stands still.</summary>
        public System.Func<MoveInput> InputSource;

        private float FixedDelta => 1f / Mathf.Max(1, simulationRate);

        /// <summary>
        /// Copies the inspector fields into the live profile, IN PLACE.
        ///
        /// In place, and re-run from OnValidate, because an earlier version
        /// built the profile once in Awake and never looked at these fields
        /// again - so changing a speed during play did nothing at all, and
        /// changing it before play worked. A tuning control that silently
        /// depends on when you touched it is worse than no control, and
        /// these fields exist for no reason other than tuning.
        ///
        /// Mutating the existing object rather than replacing it means
        /// anything holding a reference to the profile keeps seeing current
        /// values.
        /// </summary>
        private void ApplyInspectorValues()
        {
            if (_profile == null) return;

            _profile.Height = height;
            _profile.Radius = radius;

            _profile.WalkSpeed = walkSpeed;
            _profile.RunSpeed = runSpeed;
            _profile.SprintSpeed = sprintSpeed;
            _profile.GroundAcceleration = groundAcceleration;
            _profile.GroundFriction = groundFriction;

            _profile.JumpHeight = jumpHeight;
            _profile.Gravity = gravity;
            _profile.AirControl = airControl;

            _profile.SlopeLimit = slopeLimit;
            _profile.StepHeight = stepHeight;
            _profile.GroundSnapDistance = groundSnapDistance;
        }

        /// <summary>
        /// Unity calls this whenever an inspector field changes, including
        /// during play - which is what makes these values live-tunable.
        /// </summary>
        private void OnValidate()
        {
            // Radius is clamped to half the height, not just to a floor.
            //
            // A radius approaching half the height collapses the capsule
            // into a sphere - CapsuleSegment goes to zero - and a spherical
            // character is broken in ways that look like unrelated bugs:
            // step-up stops working because a ball rolls onto obstacles
            // rather than stepping, the ground probe covers a huge area,
            // wall sliding snags on everything, and the camera pivot ends up
            // inside the body. An earlier version only raised HEIGHT to
            // match radius, which let a dragged inspector field quietly
            // produce exactly that.
            radius = Mathf.Clamp(radius, 0.05f, Mathf.Max(0.1f, height) * 0.5f);
            height = Mathf.Max(radius * 2f + 0.01f, height);

            gravity = Mathf.Min(-0.01f, gravity);
            simulationRate = Mathf.Clamp(simulationRate, 20, 240);

            ApplyInspectorValues();
        }

        /// <summary>
        /// The timestep each simulation step actually sees. Input providers
        /// must use THIS for anything rate-based (turn speed, smoothing),
        /// not Time.deltaTime - Gather() runs inside the fixed loop, which
        /// may execute several times in one frame, and using frame time
        /// there would apply a whole frame's worth of turn on every step.
        /// </summary>
        public float FixedDeltaTime => FixedDelta;

        private void Awake()
        {
            _profile = new MovementProfile();
            ApplyInspectorValues();

            // Passing our own transform is what makes every query ignore
            // colliders parented under this character - weapons, mounts, hit
            // volumes, stray child objects. Without it the ground probe can
            // hit the character's own child collider and report grounded on
            // a surface that moves with it, so the character jumps once and
            // never comes down.
            _motor = new CharacterMotor(
                new UnityCollisionWorld(collisionMask, waterMask, transform));

            // The transform pivot is at the feet by Unity convention; the
            // motor works from the capsule centre. Converting once, here, is
            // what keeps half-height arithmetic from being sprinkled through
            // the rest of the code.
            _state = MoveState.AtFeet(
                UnityCollisionWorld.ToNumerics(transform.position),
                _profile,
                transform.eulerAngles.y * Mathf.Deg2Rad);

            _previousState = _state;
        }

        private void OnEnable()
        {
            WorldOrigin.Shifted += OnWorldShifted;
        }

        private void OnDisable()
        {
            WorldOrigin.Shifted -= OnWorldShifted;
        }

        /// <summary>
        /// The floating origin moved the world. The transform was moved with
        /// it, but the simulation state is a plain struct the shift can't
        /// see - without this the very next Render would put the character
        /// back where it was, which is now the shift distance away.
        /// </summary>
        private void OnWorldShifted(Vector3 localDelta)
        {
            var d = UnityCollisionWorld.ToNumerics(localDelta);
            _state.Position += d;
            _previousState.Position += d;
        }

        private void Update()
        {
            float dt = FixedDelta;

            // THE GROUND GATE. Spawning, respawning or being corrected onto a
            // tile whose scene is still loading would otherwise start gravity
            // with nothing underneath - the character falls through a world
            // that exists a moment later. Hold still until it's there.
            var streamer = WorldStreamer.Instance;
            if (streamer != null)
            {
                var feet = _state.FeetPosition(_profile);
                bool ready = streamer.IsAreaReady(
                    WorldOrigin.ToWorld(new Vector3(feet.X, feet.Y, feet.Z)));

                if (!ready)
                {
                    if (!IsWaitingForWorld)
                        Debug.Log("[LocalCharacterMotor] Waiting for the world to stream in under the player...");

                    IsWaitingForWorld = true;
                    _accumulator = 0f;
                    _previousState = _state;
                    Render(1f);
                    return;
                }

                if (IsWaitingForWorld)
                {
                    // Ground just arrived. Start from rest, and drop the
                    // input backlog so held keys don't fire a burst of
                    // steps all at once.
                    IsWaitingForWorld = false;
                    _state.Velocity = System.Numerics.Vector3.Zero;
                    _previousState = _state;
                }
            }

            _accumulator += Time.deltaTime;

            int steps = 0;
            while (_accumulator >= dt && steps < maxStepsPerFrame)
            {
                _previousState = _state;

                MoveInput input = InputSource != null ? InputSource() : default;
                input.Sequence = _sequence++;

                _state = _motor.Step(_state, input, _profile, dt);

                _accumulator -= dt;
                steps++;
            }

            // Discard the backlog rather than trying to catch up forever.
            if (steps >= maxStepsPerFrame)
                _accumulator = 0f;

            Render(_accumulator / dt);
        }

        /// <summary>
        /// Draw between the last two simulated states. The simulation runs
        /// at a fixed rate; the display should not be locked to it, or a
        /// 144Hz monitor shows 60 distinct positions a second.
        /// </summary>
        private void Render(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);

            SVector3 a = _previousState.FeetPosition(_profile);
            SVector3 b = _state.FeetPosition(_profile);

            transform.position = Vector3.Lerp(
                UnityCollisionWorld.ToUnity(a),
                UnityCollisionWorld.ToUnity(b),
                alpha);

            float yaw = Mathf.LerpAngle(
                _previousState.Yaw * Mathf.Rad2Deg,
                _state.Yaw * Mathf.Rad2Deg,
                alpha);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// Hard placement - spawn, teleport, or a server correction. Clears
        /// velocity and the interpolation history so the render pose doesn't
        /// slide across the map from wherever it used to be.
        /// </summary>
        public void Teleport(Vector3 feetPosition, float yawDegrees)
        {
            _state = MoveState.AtFeet(
                UnityCollisionWorld.ToNumerics(feetPosition),
                _profile,
                yawDegrees * Mathf.Deg2Rad);

            _previousState = _state;
            _accumulator = 0f;

            transform.position = feetPosition;
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 c = transform.position + Vector3.up * (height * 0.5f);
            float seg = Mathf.Max(0f, height * 0.5f - radius);
            Gizmos.DrawWireSphere(c + Vector3.up * seg, radius);
            Gizmos.DrawWireSphere(c - Vector3.up * seg, radius);
        }
    }
}