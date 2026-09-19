using ArcheCore.Client.Gameplay;
using ArcheCore.Movement;
using UnityEngine;
using UnityEngine.InputSystem;
using SVector3 = System.Numerics.Vector3;

namespace ArcheCore.Client.Movement
{
    /// <summary>
    /// Turns keyboard and mouse into a MoveInput, following ArcheAge's
    /// control scheme.
    ///
    /// THE MODEL: MOVEMENT IS CHARACTER-RELATIVE.
    ///
    /// ArcheAge binds Move Left/Right and Turn Left/Right as SEPARATE
    /// actions - Q/E and A/D respectively in its stock layout - and that
    /// separation is the whole design. W runs along the character's own
    /// facing. Q/E strafe relative to that facing. A/D and the arrows rotate
    /// the character. The camera never decides where you
    /// run - it follows you, easing in behind as you move (MMOCamera's
    /// autoAlign), which is the opposite of the character turning to face
    /// the camera.
    ///
    /// The three ways the character can rotate, in precedence order:
    ///   1. Right-drag  - snaps to the camera's heading. Steering.
    ///   2. Arrow keys  - turn at a fixed rate; the camera follows round.
    ///   3. Nothing else. There is no auto-facing, because movement is
    ///      already expressed in the character's own space and has nothing
    ///      to auto-face toward.
    ///
    /// An earlier version made movement camera-relative with the character
    /// auto-facing its direction of travel. That is the Guild Wars 2 model,
    /// not this one, and it had a telling symptom: free look needed a
    /// special case to freeze the movement basis, or orbiting the camera
    /// would steer you. Here free look needs no special case at all - the
    /// camera was never driving movement in the first place. A feature that
    /// stops needing a workaround is usually a sign the model underneath it
    /// got fixed.
    ///
    /// TIMING: Gather() runs inside the motor's FIXED step loop, which may
    /// execute zero, one or several times per frame. So it reads input
    /// STATE, never frame edges, and uses the motor's fixed delta for
    /// anything rate-based. Genuine edges (jump, autorun) are latched in
    /// Update and consumed here.
    /// </summary>
    /// <remarks>
    /// EXECUTION ORDER IS LOAD-BEARING. MMOCamera reads the mouse at -200,
    /// this runs at -100, the motor steps at 0. Out of that order the
    /// character steers by the previous frame's camera angle, which reads as
    /// the camera dragging behind the mouse.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(LocalCharacterMotor))]
    public sealed class LocalPlayerInput : MonoBehaviour
    {
        [Header("Scheme")]

        /// <summary>
        /// What A and D do.
        ///
        /// Turn is ArcheAge's actual default - its stock bindings put Turn
        /// Left/Right on A/D (with the arrows as secondary) and Move
        /// Left/Right on Q/E. Strafe is the very common rebind, and the one
        /// most players who came from newer games reach for first.
        ///
        /// Note this now operates on the Turn Left/Turn Right ACTIONS, not
        /// on the A and D keys specifically - so it keeps working after the
        /// player rebinds them, and whatever they bound to Turn will strafe
        /// instead. That is the right behaviour: the setting is about what
        /// the turn controls DO, not about two particular keys.
        /// </summary>
        [SerializeField] private AdBinding adKeys = AdBinding.Turn;

        /// <summary>Degrees per second for keyboard turning. ~180 is the
        /// classic value: higher is twitchy, lower steers like a barge.</summary>
        [SerializeField] private float keyboardTurnSpeed = 180f;

        [Header("Camera")]

        /// <summary>
        /// Left empty, falls back to MMOCamera.Instance and then
        /// Camera.main - so this works in a test scene with no MMOCamera.
        /// </summary>
        [SerializeField] private MMOCamera mmoCamera;

        private LocalCharacterMotor _motor;
        private bool _jumpLatched;
        private bool _autoRun;
        private float _yawDegrees;

        /// <summary>
        /// Rebindable actions. Owned here rather than read as raw keys,
        /// which is what makes an options screen possible at all - a
        /// Keyboard.current.wKey read can never be rebound by anything.
        /// </summary>
        public PlayerInputActions Actions { get; private set; }

        private void Awake()
        {
            _motor = GetComponent<LocalCharacterMotor>();
            _motor.InputSource = Gather;

            Actions = new PlayerInputActions();

            _yawDegrees = transform.eulerAngles.y;

            if (mmoCamera == null)
                mmoCamera = MMOCamera.Instance;
        }

        private void OnEnable() => Actions?.Enable();
        private void OnDisable() => Actions?.Disable();

        private void OnDestroy()
        {
            Actions?.Dispose();
            Actions = null;
        }

        private void Update()
        {
            if (Actions == null) return;

            // Edges are latched here, in per-frame code, and consumed in
            // Gather. Reading WasPressedThisFrame inside the fixed step
            // drops presses on frames that run no simulation step.
            if (PlayerInputActions.Pressed(Actions.Jump))
                _jumpLatched = true;

            if (PlayerInputActions.Pressed(Actions.AutoRun))
                _autoRun = !_autoRun;

            if (mmoCamera == null)
                mmoCamera = MMOCamera.Instance;

            // Any deliberate backward input cancels it.
            if (PlayerInputActions.Held(Actions.MoveBackward))
                _autoRun = false;
        }

        private MoveInput Gather()
        {
            var input = new MoveInput();
            if (Actions == null) return input;

            bool steering = mmoCamera != null && mmoCamera.IsSteering;

            // Rate-based values use the SIMULATION step, not frame time.
            // Gather() can run several times in one frame, and Time.deltaTime
            // would apply a whole frame's turn on every one of them.
            float dt = _motor.FixedDeltaTime;

            float forwardAxis = 0f;
            float strafeAxis = 0f;
            float turnAxis = 0f;

            forwardAxis = Axis(Actions.MoveForward, Actions.MoveBackward);

            // Both mouse buttons held moves forward WHILE HELD. Momentary,
            // not a toggle - an earlier version latched autorun here, so
            // clicking both buttons once sent the character running until
            // something else stopped it.
            if (mmoCamera != null && mmoCamera.BothButtonsHeld && forwardAxis >= 0f)
                forwardAxis = 1f;

            if (_autoRun && forwardAxis >= 0f) forwardAxis = 1f;

            strafeAxis = Axis(Actions.MoveRight, Actions.MoveLeft);
            float turnKeys = Axis(Actions.TurnRight, Actions.TurnLeft);

            // Holding right-drag converts the turn keys into strafe, because
            // the mouse has taken over steering and leaving them on turn
            // would fight it. Same effect as the AdBinding option, applied
            // only while the button is down.
            if (adKeys == AdBinding.Strafe || steering)
                strafeAxis += turnKeys;
            else
                turnAxis += turnKeys;

            strafeAxis = Mathf.Clamp(strafeAxis, -1f, 1f);
            turnAxis = Mathf.Clamp(turnAxis, -1f, 1f);

            // --- Facing ---

            if (steering && mmoCamera != null)
            {
                // Right-drag steers. SNAP, no smoothing - the camera angle is
                // already exactly what the player asked for, and easing
                // toward it just puts back the lag that moving the camera's
                // mouse read into Update removed.
                _yawDegrees = mmoCamera.Yaw;
            }
            else if (Mathf.Abs(turnAxis) > 0.01f)
            {
                float delta = turnAxis * keyboardTurnSpeed * dt;
                _yawDegrees += delta;

                // Carry the camera round so it stays behind. MMOCamera
                // ignores this during free look, so a deliberate glance over
                // the shoulder survives turning with the keys.
                if (mmoCamera != null)
                    mmoCamera.CarryCharacterTurn(delta);
            }

            input.Yaw = _yawDegrees * Mathf.Deg2Rad;

            // --- Direction of travel ---
            //
            // Always in the CHARACTER's space. This is the heart of the
            // scheme: the camera does not steer you, so free look needs no
            // special handling, and turning is something you do rather than
            // something that happens because you looked somewhere.

            Quaternion facing = Quaternion.Euler(0f, _yawDegrees, 0f);
            Vector3 wish = facing * (Vector3.forward * forwardAxis + Vector3.right * strafeAxis);

            if (wish.sqrMagnitude > 1e-6f)
                wish.Normalize();

            input.WishDirection = new SVector3(wish.x, 0f, wish.z);

            // --- Buttons ---

            MoveButtons buttons = MoveButtons.None;

            if (_jumpLatched)
            {
                buttons |= MoveButtons.Jump;
                _jumpLatched = false;
            }

            if (PlayerInputActions.Held(Actions.Sprint))   buttons |= MoveButtons.Sprint;
            if (PlayerInputActions.Held(Actions.Walk))     buttons |= MoveButtons.Walk;
            if (PlayerInputActions.Held(Actions.SwimUp))   buttons |= MoveButtons.Ascend;
            if (PlayerInputActions.Held(Actions.SwimDown)) buttons |= MoveButtons.Descend;

            input.Buttons = buttons;
            return input;
        }

        private static float Axis(InputAction positive, InputAction negative) =>
            (PlayerInputActions.Held(positive) ? 1f : 0f) -
            (PlayerInputActions.Held(negative) ? 1f : 0f);

        public enum AdBinding
        {
            /// <summary>Move Left / Move Right. The common rebind.</summary>
            Strafe = 0,

            /// <summary>Turn Left / Turn Right. ArcheAge's stock binding.</summary>
            Turn = 1,
        }
    }
}