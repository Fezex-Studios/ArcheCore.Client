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

        private CharacterController _cc;
        private Vector3 _velocity;
        private Vector3 _targetPosition;
        private MMOCamera _mmoCamera;

        private float _sendTimer;
        private const float SendRate = 0.05f;


        private Vector3? _autoMoveTarget;
        private float _autoMoveStopDistance;

        private void Start()
        {
            _targetPosition = transform.position;
            _cc = GetComponent<CharacterController>();

            if (isLocalPlayer)
            {
                _mmoCamera = Object.FindFirstObjectByType<MMOCamera>();
            }
        }

        private void Update()
        {
            if (isLocalPlayer)
                HandleLocalMovement();
            else
                HandleRemoteMovement();
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

            // Send network updates
            _sendTimer += Time.deltaTime;

            if (_sendTimer >= SendRate &&
                move.sqrMagnitude > 0.001f)
            {
                _sendTimer = 0f;

                C2WPlayerMovePacketSender.Send(
                    ClientNetwork.Instance.ServerPeer,
                    transform.position
                );
            }
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

        private void HandleRemoteMovement()
        {
            transform.position = Vector3.Lerp(
                transform.position,
                _targetPosition,
                Time.deltaTime * 10f
            );
        }

        public void SetTargetPosition(Vector3 position)
        {
            _targetPosition = position;
        }
    }
}