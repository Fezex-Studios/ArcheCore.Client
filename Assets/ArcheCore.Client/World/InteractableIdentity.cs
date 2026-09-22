using System.Collections.Generic;
using ArcheCore.Network.Shared.Packets.W2C;
using UnityEngine;

namespace ArcheCore.Client.World
{
    public enum InteractableKind
    {
        Npc = 1,
        Corpse = 2,
        HarvestNode = 4,
    }

    /// <summary>
    /// Anything the player can interact with: NPCs, harvest nodes, corpses.
    /// Filled in by the spawn handlers from server data - never by hand.
    ///
    ///   Actions      what F / G do (index 0 = F, 1 = G), from the server's
    ///                InteractableActions table
    ///   DisplayName  first line of the hover tooltip
    ///   Title        "Merchant" - shown under the name
    ///   ExtraLines   more tooltip lines ("Owner: Testchar1")
    ///
    /// Every live instance is in All, so proximity targeting can scan the
    /// handful of interactables around the player without a physics query.
    /// </summary>
    public class InteractableIdentity : MonoBehaviour
    {
        private static readonly List<InteractableIdentity> _all = new();
        public static IReadOnlyList<InteractableIdentity> All => _all;

        public int NetworkId;
        public InteractableKind Kind = InteractableKind.Npc;
        public float InteractRange = 4f;

        public string DisplayName;
        public string Title;
        public readonly List<string> ExtraLines = new();

        /// <summary>Kept for old callers; the prompt now uses Actions.</summary>
        public string ActionVerb = "Interact with";

        public InteractionActionData[] Actions = System.Array.Empty<InteractionActionData>();

        /// <summary>
        /// Can the player act on it right now? Depleted nodes set this false,
        /// so proximity skips them without forgetting their actions.
        /// </summary>
        public bool IsAvailable = true;

        public bool HasActions => Actions != null && Actions.Length > 0;

        private void OnEnable() => _all.Add(this);
        private void OnDisable() => _all.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, InteractRange);
        }
    }
}
