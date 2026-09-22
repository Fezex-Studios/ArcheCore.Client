using System.Collections.Generic;
using UnityEngine;

namespace ArcheCore.Client.Gameplay
{
    /// <summary>
    /// Spawned harvest nodes by network id. Same job as NpcRegistry, but
    /// static - it needs no scene object, so there's nothing to forget to
    /// place. Entries whose GameObject was destroyed by a scene change are
    /// treated as absent.
    /// </summary>
    public static class HarvestNodeRegistry
    {
        private static readonly Dictionary<int, HarvestNodeIdentity> Nodes = new();

        public static void Register(HarvestNodeIdentity node)
        {
            // A duplicate spawn (re-entering view) replaces the old object
            // rather than stacking a second copy on top of it.
            if (Nodes.TryGetValue(node.NetworkId, out var existing) && existing != null && existing != node)
                Object.Destroy(existing.gameObject);

            Nodes[node.NetworkId] = node;
        }

        public static bool TryGet(int networkId, out HarvestNodeIdentity node)
        {
            if (Nodes.TryGetValue(networkId, out node) && node != null)
                return true;

            Nodes.Remove(networkId);
            node = null;
            return false;
        }

        public static void SetDepleted(int networkId, bool depleted)
        {
            if (TryGet(networkId, out var node))
                node.SetDepleted(depleted);
        }

        /// <summary>Returns true if the id was a node (so the caller can stop looking).</summary>
        public static bool Despawn(int networkId)
        {
            if (!Nodes.TryGetValue(networkId, out var node))
                return false;

            Nodes.Remove(networkId);

            if (node != null)
                Object.Destroy(node.gameObject);

            return true;
        }
    }
}
