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

        public void UpdatePosition(int networkId, Vector3 position)
        {
            if (players.TryGetValue(networkId, out PlayerController pc))
                pc.SetTargetPosition(position);
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