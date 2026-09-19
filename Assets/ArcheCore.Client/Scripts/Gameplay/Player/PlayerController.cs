using ArcheCore.Client.Gameplay;
using ArcheCore.Client.Movement;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Movement;
using ArcheCore.Network.Shared;
using UnityEngine;
using SVector3 = System.Numerics.Vector3;

namespace ArchCore.Client
{
    /// <summary>
    /// Owns the local/remote split and the network side of one character.
    /// It no longer simulates movement: LocalCharacterMotor does that for
    /// the local player, RemoteEntityInterpolator plays back received state
    /// for everyone else.
    ///
    /// EXACTLY ONE THING MAY WRITE transform.position. For the local player
    /// that is the motor; for a remote it is the interpolator. Both live on
    /// the same prefab, so this class is responsible for making sure only
    /// the right one is enabled - two systems writing one transform is what
    /// put remote players under the terrain, and the CharacterController
    /// this replaced was a third.
    ///
    /// PIVOT: the transform is at the character's FEET. The old
    /// CharacterController had center (0,0,0) with height 2, which put it at
    /// the waist. Everything that carried a Y - spawn points, NPC spawner
    /// rows, saved character positions, the camera's pivotHeight - is
    /// therefore a metre off until the data is migrated. See MIGRATION notes.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        public bool isLocalPlayer;
        public int networkId;

        private const float SendRate = 0.05f;
        private const float YawSendThresholdDegrees = 4f;
        private const float StoppedSpeedThreshold = 0.05f;

        private LocalCharacterMotor _motor;
        private LocalPlayerInput _input;
        private RemoteEntityInterpolator _interpolator;

        private float _sendTimer;
        private bool _wasMoving;
        private bool _wasGrounded = true;
        private float _lastSentYaw;
        private MovementState _lastSentState = MovementState.None;

        private void Start()
        {
            _motor = GetComponent<LocalCharacterMotor>();
            _input = GetComponent<LocalPlayerInput>();

            if (isLocalPlayer)
                SetUpLocal();
            else
                SetUpRemote();
        }

        private void SetUpLocal()
        {
            // Both are disabled on the prefab so a remote player never runs
            // a simulation step or reads the keyboard. Enabling here is what
            // makes one instance the player and the rest puppets.
            if (_motor != null) _motor.enabled = true;
            if (_input != null) _input.enabled = true;

            var camera = MMOCamera.Instance;
            if (camera != null)
                camera.SetTarget(transform);

            var interaction = GetComponent<PlayerInteraction>();
            if (interaction != null)
                interaction.enabled = true;

            _lastSentYaw = transform.eulerAngles.y;
        }

        private void SetUpRemote()
        {
            if (_motor != null) _motor.enabled = false;
            if (_input != null) _input.enabled = false;

            var interaction = GetComponent<PlayerInteraction>();
            if (interaction != null)
                interaction.enabled = false;

            _interpolator = GetComponent<RemoteEntityInterpolator>();
            if (_interpolator == null)
                _interpolator = gameObject.AddComponent<RemoteEntityInterpolator>();

            // Players send real yaw, and can face somewhere other than where
            // they are going, so facing is taken off the wire rather than
            // derived from the motion vector.
            _interpolator.DeriveYawFromMotion = false;
            _interpolator.Initialize(transform.position, transform.eulerAngles.y);
        }

        private void Update()
        {
            if (!isLocalPlayer || _motor == null) return;
            SendMovementIfNeeded();
        }

        /// <summary>
        /// Reports state to the server, from the SIMULATION rather than from
        /// the transform. The transform holds an interpolated render pose
        /// that can sit between two simulation ticks, so reading it would
        /// send positions the simulation never actually occupied - and those
        /// are what the server validates against.
        ///
        /// Three edges have to be reported or observers are left holding a
        /// stale position indefinitely, because the server deliberately goes
        /// quiet about an entity whose transform has not changed:
        ///
        ///   THE STOP - the last packet must carry zero velocity, or every
        ///   observer extrapolates the character past where it stopped and
        ///   never gets a correction.
        ///
        ///   GROUND TRANSITIONS - takeoff and landing, sent immediately.
        ///   Airborne counts as moving even with no horizontal speed;
        ///   without that, releasing the keys mid-jump reads as "stopped"
        ///   and the character hangs in the air on every other screen.
        ///
        ///   STATE AND FACING - a character can change what it is doing, or
        ///   which way it looks, without moving at all.
        /// </summary>
        private void SendMovementIfNeeded()
        {
            MoveState state = _motor.State;
            MovementProfile profile = _motor.Profile;

            SVector3 feet = state.FeetPosition(profile);
            Vector3 position = new Vector3(feet.X, feet.Y, feet.Z);
            Vector3 velocity = new Vector3(state.Velocity.X, state.Velocity.Y, state.Velocity.Z);

            bool grounded = state.IsGrounded;

            // A grounded character carries a small downward bias to stay in
            // contact with the floor. Shipping it makes observers
            // extrapolate standing players into the terrain.
            if (grounded) velocity.y = 0f;

            float horizontalSpeedSq = velocity.x * velocity.x + velocity.z * velocity.z;
            bool horizontalMoving = horizontalSpeedSq > StoppedSpeedThreshold * StoppedSpeedThreshold;
            bool isMoving = horizontalMoving || !grounded;

            float yaw = state.Yaw * Mathf.Rad2Deg;
            bool yawChanged = Mathf.Abs(Mathf.DeltaAngle(yaw, _lastSentYaw)) >= YawSendThresholdDegrees;

            MovementState moveState = BuildState(state, horizontalMoving);
            bool stateChanged = moveState != _lastSentState;

            _sendTimer += Time.deltaTime;

            bool groundedChanged = grounded != _wasGrounded;
            _wasGrounded = grounded;

            if (groundedChanged)
            {
                _wasMoving = isMoving;
                Send(position, isMoving ? velocity : Vector3.zero, state, moveState);
                return;
            }

            if (_wasMoving && !isMoving)
            {
                _wasMoving = false;
                Send(position, Vector3.zero, state, moveState);
                return;
            }

            if (_sendTimer < SendRate) return;
            if (!isMoving && !yawChanged && !stateChanged) return;

            _wasMoving = isMoving;
            Send(position, isMoving ? velocity : Vector3.zero, state, moveState);
        }

        private static MovementState BuildState(in MoveState state, bool horizontalMoving)
        {
            MovementState result = MovementState.None;

            if (horizontalMoving)
                result |= MovementState.Moving;

            if (!state.IsGrounded)
            {
                result |= MovementState.Airborne;

                // The motor tracks whether the airtime began with a
                // deliberate jump, which an observer cannot infer - walking
                // off a ledge and jumping look identical after a few frames.
                if (state.Mode == LocomotionMode.Airborne && state.JumpedThisStep)
                    result |= MovementState.Jumping;
            }

            if (state.Mode == LocomotionMode.Swimming) result |= MovementState.Swimming;
            if (state.Mode == LocomotionMode.Gliding)  result |= MovementState.Gliding;

            return result;
        }

        private void Send(Vector3 position, Vector3 velocity, in MoveState state, MovementState moveState)
        {
            // Bookkeeping happens whether or not a packet goes out, so that
            // an offline scene behaves exactly like an online one minus the
            // packet. Skipping it would leave _lastSentState stale, every
            // frame would look like a state change, and the send gate would
            // fire continuously - a different bug wearing the same costume.
            _sendTimer = 0f;
            _lastSentYaw = state.Yaw * Mathf.Rad2Deg;
            _lastSentState = moveState;

            // No network in an isolated test scene - no NetworkManager, so
            // ClientNetwork.Instance is null. Movement has to work there
            // regardless: proving the motor against stairs and slopes with
            // no server involved is the whole point of testing it offline,
            // and a component that throws without a connection makes that
            // impossible.
            var network = ClientNetwork.Instance;
            if (network == null || network.ServerPeer == null)
                return;

            C2WPlayerMovePacketSender.Send(
                network.ServerPeer,
                position,
                velocity,
                new Vector3(
                    state.Pitch * Mathf.Rad2Deg,
                    state.Yaw * Mathf.Rad2Deg,
                    state.Roll * Mathf.Rad2Deg),
                moveState);
        }

        // --- Remote application ---

        /// <summary>Legacy W2CPlayerPosition path - position only.</summary>
        public void SetTargetPosition(Vector3 position)
        {
            var current = _interpolator != null ? _interpolator.State : MovementState.None;
            ApplyNetworkState(position, Vector3.zero, transform.eulerAngles.y, 0f, 0f, current);
        }

        public void ApplyNetworkState(
            Vector3 position, Vector3 velocity,
            float yawDegrees, float pitchDegrees, float rollDegrees,
            MovementState state)
        {
            if (isLocalPlayer || _interpolator == null)
                return;

            _interpolator.ApplyUpdate(position, velocity, yawDegrees, pitchDegrees, rollDegrees, state);
        }

        /// <summary>
        /// Server correction (W2CPositionCorrection). Only ever applies to
        /// the local player - a remote's position is already whatever the
        /// server said.
        /// </summary>
        public void ForcePosition(Vector3 feetPosition)
        {
            if (!isLocalPlayer || _motor == null) return;

            _motor.Teleport(feetPosition, _motor.State.Yaw * Mathf.Rad2Deg);
            _sendTimer = SendRate; // report the corrected position at once
        }

        public void SetAutoMoveTarget(Vector3 target, float stopDistance)
        {
            // Click-to-move needs rebuilding on top of MoveInput: it is a
            // source of INTENT, so it belongs in LocalPlayerInput producing
            // a WishDirection toward the target, not here nudging a
            // transform behind the motor's back.
        }

        public void CancelAutoMove() { }
    }
}