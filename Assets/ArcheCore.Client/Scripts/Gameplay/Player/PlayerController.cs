using ArcheCore.Client.Gameplay;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.C2WSenders;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArchCore.Client
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public bool isLocalPlayer;
        public int networkId;

        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float jumpHeight = 1.5f;
        private const float CellSize = 50f;
        private const int RadiusCells = 1;


        private CharacterController _cc;
        private Vector3 _velocity;
        private MMOCamera _mmoCamera;

        /// <summary>
        /// Remote players only. Owns position and facing for anyone who
        /// isn't us; see RemoteEntityInterpolator for why the old
        /// lerp-toward-target in this class was replaced.
        /// </summary>
        private RemoteEntityInterpolator _interpolator;

        private float _sendTimer;
        private const float SendRate = 0.05f;

        /// <summary>
        /// Below this horizontal speed we consider the character stopped.
        /// Deliberately not zero - CharacterController.velocity carries
        /// tiny residuals from ground friction and slope resolution, and
        /// treating those as movement means never sending the stop packet.
        /// </summary>
        private const float StoppedSpeedThreshold = 0.05f;

        /// <summary>
        /// Send an update for rotation alone past this many degrees of
        /// change. Players turn on the spot constantly - to face a target,
        /// to look at something - and with a movement-gated send that
        /// rotation never reaches anyone. This is rate-limited by SendRate
        /// like everything else.
        /// </summary>
        private const float YawSendThresholdDegrees = 4f;

        private bool  _wasMoving;
        private float _lastSentYaw;

        private Vector3? _autoMoveTarget;
        private float _autoMoveStopDistance;

        private void Start()
        {
            _cc = GetComponent<CharacterController>();

            if (isLocalPlayer)
            {
                _mmoCamera = Object.FindFirstObjectByType<MMOCamera>();
                _lastSentYaw = transform.eulerAngles.y;

                // A local player is driven by input, never by the server.
                // If an interpolator ended up on the prefab, make sure it
                // isn't also writing transform.position - two things
                // fighting over the transform produces a jitter that looks
                // exactly like a network problem and isn't one.
                var stray = GetComponent<RemoteEntityInterpolator>();
                if (stray != null)
                    stray.enabled = false;
            }
            else
            {
                // Added at runtime rather than required on the prefab, so
                // this drops in without touching any prefab assets.
                _interpolator = GetComponent<RemoteEntityInterpolator>();
                if (_interpolator == null)
                    _interpolator = gameObject.AddComponent<RemoteEntityInterpolator>();

                // Players send real yaw - they can face the camera while
                // strafing, or turn while standing still, and deriving
                // facing from movement would get both wrong.
                _interpolator.DeriveYawFromMotion = false;
                _interpolator.Initialize(transform.position, transform.eulerAngles.y);
            }
        }

        private void Update()
        {
            // Remote players are entirely the interpolator's business now.
            // There is no HandleRemoteMovement anymore.
            if (isLocalPlayer)
                HandleLocalMovement();
        }

        private void HandleLocalMovement()
        {
            bool grounded = _cc.isGrounded;

            if (grounded && _velocity.y < 0f)
                _velocity.y = -2f;

            // WASD input
            Vector2 input = Vector2.zero;

            bool manualInputThisFrame =
                Keyboard.current != null &&
                (
                    Keyboard.current.wKey.isPressed ||
                    Keyboard.current.aKey.isPressed ||
                    Keyboard.current.sKey.isPressed ||
                    Keyboard.current.dKey.isPressed
                );

            // Manual WASD always wins - cancel any pending click-to-interact walk.
            if (manualInputThisFrame)
                _autoMoveTarget = null;

            if (Keyboard.current != null)
            {
                input = new Vector2(
                    (Keyboard.current.dKey.isPressed ? 1f : 0f) -
                    (Keyboard.current.aKey.isPressed ? 1f : 0f),

                    (Keyboard.current.wKey.isPressed ? 1f : 0f) -
                    (Keyboard.current.sKey.isPressed ? 1f : 0f)
                ).normalized;
            }

            Vector3 move = Vector3.zero;

            if (_autoMoveTarget.HasValue)
            {
                Vector3 toTarget = _autoMoveTarget.Value - transform.position;
                toTarget.y = 0f;

                if (toTarget.magnitude <= _autoMoveStopDistance)
                {
                    // Arrived - PlayerInteraction's CheckArrival will pick this up
                    // next frame and fire the actual interact packet.
                    _autoMoveTarget = null;
                }
                else
                {
                    move = toTarget.normalized * moveSpeed;

                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(toTarget.normalized),
                        Time.deltaTime * 10f
                    );
                }
            }
            else if (input != Vector2.zero && _mmoCamera != null)
            {
                Vector3 forward = _mmoCamera.GetCameraForward();
                Vector3 right = _mmoCamera.GetCameraRight();

                move =
                    (forward * input.y +
                     right * input.x).normalized *
                    moveSpeed;

                bool mouseHeld =
                    Mouse.current != null &&
                    Mouse.current.rightButton.isPressed;

                if (mouseHeld)
                {
                    // Rotate toward camera direction
                    if (forward.sqrMagnitude > 0.001f)
                    {
                        transform.rotation = Quaternion.Slerp(
                            transform.rotation,
                            Quaternion.LookRotation(forward),
                            Time.deltaTime * 15f
                        );
                    }
                }
                else if (move.sqrMagnitude > 0.001f)
                {
                    // Rotate toward movement direction
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(move),
                        Time.deltaTime * 10f
                    );
                }
            }

            // Jump
            if (
                grounded &&
                Keyboard.current != null &&
                Keyboard.current.spaceKey.wasPressedThisFrame
            )
            {
                _velocity.y =
                    Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            // Gravity
            _velocity.y += gravity * Time.deltaTime;
            move.y = _velocity.y;

            // Move character
            _cc.Move(move * Time.deltaTime);

            SendMovementIfNeeded(grounded);
        }

        /// <summary>
        /// Decides whether this frame's state needs to go to the server.
        ///
        /// The old rule was "every SendRate seconds, if we're moving." That
        /// misses two things the observers need:
        ///
        ///   - THE STOP. If the last thing you send is a moving update, the
        ///     server's snapshot dispatcher goes quiet (a stationary entity
        ///     has nothing to report), and every observer is left holding a
        ///     position with a non-zero velocity attached to it. They
        ///     extrapolate you forward past where you actually stopped and
        ///     get no correction, because you're not sending anymore. One
        ///     explicit zero-velocity packet on the moving->stopped edge
        ///     fixes it; without it, extrapolation makes things worse than
        ///     no extrapolation.
        ///   - ROTATION WITHOUT MOVEMENT. Turning on the spot changes
        ///     nothing about position, so a movement-gated send never
        ///     reports it, and other players see you frozen mid-turn.
        /// </summary>
        private void SendMovementIfNeeded(bool grounded)
        {
            // CharacterController.velocity is the velocity that was
            // actually APPLIED after collision resolution, which is what
            // observers should extrapolate along - not the input vector we
            // asked for. They differ whenever you're sliding along a wall,
            // and the applied one is the truthful answer.
            Vector3 netVelocity = _cc.velocity;

            // Zero the grounded residual. CharacterController reports a
            // constant small downward velocity while grounded (the -2f we
            // feed it to keep it pinned to the floor). Shipping that means
            // every observer extrapolates standing players slowly into the
            // terrain during any gap between updates.
            if (grounded)
                netVelocity.y = 0f;

            Vector3 horizontal = new Vector3(netVelocity.x, 0f, netVelocity.z);
            bool isMoving = horizontal.sqrMagnitude > StoppedSpeedThreshold * StoppedSpeedThreshold;

            float yaw = transform.eulerAngles.y;
            bool yawChanged = Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentYaw)) >= YawSendThresholdDegrees;

            _sendTimer += Time.deltaTime;

            // The stop edge. Sent immediately rather than waiting out the
            // rate limit - up to 50ms of observers extrapolating you past
            // your own stopping point is exactly the overshoot this exists
            // to prevent.
            if (_wasMoving && !isMoving)
            {
                _wasMoving = false;
                Send(Vector3.zero, yaw);
                return;
            }

            if (_sendTimer < SendRate)
                return;

            if (!isMoving && !yawChanged)
                return;

            _wasMoving = isMoving;
            Send(isMoving ? netVelocity : Vector3.zero, yaw);
        }

        private void Send(Vector3 velocity, float yawDegrees)
        {
            _sendTimer = 0f;
            _lastSentYaw = yawDegrees;

            C2WPlayerMovePacketSender.Send(
                ClientNetwork.Instance.ServerPeer,
                transform.position,
                velocity,
                yawDegrees * Mathf.Deg2Rad);
        }

        public void SetAutoMoveTarget(Vector3 target, float stopDistance)
        {
            _autoMoveTarget = target;
            _autoMoveStopDistance = stopDistance;
        }

        public void CancelAutoMove()
        {
            _autoMoveTarget = null;
        }

        /// <summary>
        /// Kept for the legacy W2CPlayerPosition path, which carries no
        /// velocity or facing. Prefer ApplyNetworkState.
        /// </summary>
        public void SetTargetPosition(Vector3 position)
        {
            ApplyNetworkState(position, Vector3.zero, transform.eulerAngles.y);
        }

        /// <param name="yawDegrees">Facing in degrees - callers convert from the wire's radians.</param>
        public void ApplyNetworkState(Vector3 position, Vector3 velocity, float yawDegrees)
        {
            if (isLocalPlayer || _interpolator == null)
                return;

            _interpolator.ApplyUpdate(position, velocity, yawDegrees);
        }

        private void OnDrawGizmos()
        {
            Vector3 pos = transform.position;

            // highlight the 3x3 neighborhood this player is "aware" of
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.15f);
            Vector3 center = new Vector3(
                Mathf.Floor(pos.x / CellSize) * CellSize + CellSize / 2f,
                pos.y,
                Mathf.Floor(pos.z / CellSize) * CellSize + CellSize / 2f);
            float size = CellSize * (RadiusCells * 2 + 1);
            Gizmos.DrawCube(new Vector3(center.x, pos.y, center.z), new Vector3(size, 0.1f, size));

            // draw grid lines across the world so you can see cell boundaries
            Gizmos.color = Color.gray;
            for (float x = -500; x <= 500; x += CellSize)
                Gizmos.DrawLine(new Vector3(x, pos.y, -500), new Vector3(x, pos.y, 500));
            for (float z = -500; z <= 500; z += CellSize)
                Gizmos.DrawLine(new Vector3(-500, pos.y, z), new Vector3(500, pos.y, z));
        }
    }
}