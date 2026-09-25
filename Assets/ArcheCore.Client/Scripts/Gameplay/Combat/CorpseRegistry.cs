using System.Collections.Generic;
using ArcheCore.Client.World;
using ArcheCore.Network.Shared.Packets.W2C;
using UnityEngine;

namespace ArcheCore.Client.Gameplay.Combat
{
    /// <summary>
    /// Spawned corpses by network id. Same shape as HarvestNodeRegistry:
    /// static, no scene object.
    ///
    /// Model: the "Corpse" entry in WorldObjectPrefabRegistry. If you haven't
    /// made one yet, a flat dark box is used instead so looting still works -
    /// put on the same layer as your NPCs, which is the layer the interact
    /// raycast hits.
    /// </summary>
    public static class CorpseRegistry
    {
        private static readonly Dictionary<int, GameObject> Corpses = new();

        public static void Spawn(W2CSpawnCorpsePacket p)
        {
            Despawn(p.NetworkId);

            var pos = WorldOrigin.ToLocal(p.X, p.Y, p.Z);
            GameObject obj = null;

            var prefabs = WorldObjectPrefabRegistry.Instance;
            var prefab = prefabs != null ? prefabs.GetPrefab(p.ModelType) : null;
            if (prefab != null)
            {
                obj = Object.Instantiate(prefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            }
            else
            {
                obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.transform.SetPositionAndRotation(pos + Vector3.up * 0.15f, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                obj.transform.localScale = new Vector3(1.2f, 0.3f, 0.8f);
                if (obj.TryGetComponent(out Renderer r))
                    r.material.color = new Color(0.25f, 0.17f, 0.10f);

                // Same layer as your NPC prefabs, so the interact raycast finds it.
                var anyNpc = Object.FindFirstObjectByType<NpcIdentity>();
                obj.layer = anyNpc != null ? anyNpc.gameObject.layer : 6;
            }

            obj.name = $"Corpse_{p.Name}_{p.NetworkId}";

            if (!obj.TryGetComponent(out InteractableIdentity interactable))
                interactable = obj.AddComponent<InteractableIdentity>();
            interactable.NetworkId = p.NetworkId;
            interactable.InteractRange = p.InteractRange;
            interactable.DisplayName = $"{p.Name}'s remains";
            interactable.ActionVerb = p.OwnerId == CombatClient.LocalPlayerId ? "Loot" : "Search";
            interactable.Kind = InteractableKind.Corpse;
            interactable.Actions = p.Actions ?? System.Array.Empty<InteractionActionData>();
            interactable.ExtraLines.Clear();
            if (!string.IsNullOrEmpty(p.OwnerName))
                interactable.ExtraLines.Add($"Owner: {p.OwnerName}");

            Corpses[p.NetworkId] = obj;
        }

        public static bool TryGet(int networkId, out GameObject corpse) =>
            Corpses.TryGetValue(networkId, out corpse) && corpse != null;

        /// <summary>Returns true if the id was a corpse (so the caller can stop looking).</summary>
        public static bool Despawn(int networkId)
        {
            if (!Corpses.TryGetValue(networkId, out var obj))
                return false;

            Corpses.Remove(networkId);
            if (obj != null)
                Object.Destroy(obj);
            return true;
        }
    }
}
