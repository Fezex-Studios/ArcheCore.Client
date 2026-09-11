using System.Collections.Generic;
using UnityEngine;

namespace ArcheCore.Client.Gameplay
{
    // Mirrors PlayerRegistry's shape - tracks spawned NPCs by NetworkId so
    // W2CNpcPositionHandler/W2CNpcDespawnHandler have something to look up.
    // Without this, W2CSpawnNpcHandler instantiates a GameObject that
    // nothing can ever find again.
    public class NpcRegistry : MonoBehaviour
    {
        public static NpcRegistry Instance;

        private readonly Dictionary<int, NpcIdentity> _npcs = new();

        private void Awake()
        {
            Instance = this;
        }

        public void Register(NpcIdentity npc)
        {
            if (_npcs.TryGetValue(npc.NetworkId, out var existing) && existing != null)
                Object.Destroy(existing.gameObject);

            _npcs[npc.NetworkId] = npc;
        }

        public bool TryGetNpc(int networkId, out NpcIdentity npc) =>
            _npcs.TryGetValue(networkId, out npc);

        public void UpdatePosition(int networkId, Vector3 position)
        {
            if (_npcs.TryGetValue(networkId, out var npc))
                npc.SetTargetPosition(position);
        }

        public void Despawn(int networkId)
        {
            if (!_npcs.TryGetValue(networkId, out var npc))
                return;

            _npcs.Remove(networkId);

            if (npc != null)
                Object.Destroy(npc.gameObject);
        }
    }
}