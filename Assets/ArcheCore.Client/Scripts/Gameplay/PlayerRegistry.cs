using System.Collections.Generic;
using ArchCore.Client;
using Unity.Cinemachine;
using UnityEngine;

namespace ArcheCore.Client.Gameplay
{
    public class PlayerRegistry : MonoBehaviour
    {
        public static PlayerRegistry Instance;

        [SerializeField] private GameObject        playerPrefab;
        [SerializeField] private CinemachineCamera virtualCamera;
        [SerializeField] private LayerMask         interactableLayer;

        private Dictionary<int, PlayerController> players = new();

        private void Awake()
        {
            Instance = this;
        }

        public PlayerController Spawn(
            int networkId,
            Vector3 position,
            bool isLocal)
        {
            if (players.ContainsKey(networkId))
                Despawn(networkId);

            GameObject go = Instantiate(
                playerPrefab,
                position,
                Quaternion.identity);

            PlayerController pc = go.GetComponent<PlayerController>();
            pc.networkId = networkId;
            pc.isLocalPlayer = isLocal;

            players[networkId] = pc;

            // Assign Cinemachine target when local player spawns
            if (isLocal)
            {
                var mmoCamera = FindFirstObjectByType<MMOCamera>();
                if (mmoCamera != null)
                    mmoCamera.SetTarget(go.transform);

                // Interaction is a local-only concern (raycast + input),
                // same reason MMOCamera only gets wired up for isLocal.
                var interaction = go.GetComponent<PlayerInteraction>();
                if (interaction == null)
                    interaction = go.AddComponent<PlayerInteraction>();

                interaction.Configure(interactableLayer);
            }

            return pc;
        }

        public bool TryGetPlayer(int networkId, out PlayerController pc)
        {
            return players.TryGetValue(networkId, out pc);
        }

        /// <summary>
        /// Legacy entry point for W2CPlayerPosition, which predates the
        /// snapshot packet and carries position only. Kept so that path
        /// still compiles and works; anything coming from a world snapshot
        /// should use ApplyNetworkState instead, because position without
        /// velocity gives the interpolator nothing to extrapolate with when
        /// an update is dropped.
        /// </summary>
        public void UpdatePosition(int networkId, Vector3 position)
        {
            if (players.TryGetValue(networkId, out PlayerController pc))
                pc.SetTargetPosition(position);
        }

        /// <param name="yawDegrees">Facing in degrees (the wire carries radians).</param>
        public void ApplyNetworkState(
            int networkId, Vector3 position, Vector3 velocity,
            float yawDegrees, float pitchDegrees, float rollDegrees,
            ArcheCore.Network.Shared.MovementState state, uint serverTick)
        {
            if (players.TryGetValue(networkId, out PlayerController pc))
                pc.ApplyNetworkState(position, velocity, yawDegrees, pitchDegrees, rollDegrees, state, serverTick);
        }

        /// <summary>
        /// Start a locally simulated jump arc for a remote player.
        ///
        /// Returns false when this network id isn't a player we know
        /// about, so the caller can try the NPC registry instead - the
        /// jump packet carries no flag saying which kind of entity it
        /// refers to.
        /// </summary>
        public bool BeginJump(
            int networkId, Vector3 origin, Vector3 horizontalVelocity, float verticalVelocity)
        {
            if (!players.TryGetValue(networkId, out PlayerController pc))
                return false;

            pc.BeginJump(origin, horizontalVelocity, verticalVelocity);
            return true;
        }

        public void Despawn(int networkId)
        {
            if (!players.TryGetValue(networkId, out PlayerController pc))
                return;

            players.Remove(networkId);
            Destroy(pc.gameObject);
        }
    }
}