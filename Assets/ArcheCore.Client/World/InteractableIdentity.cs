using UnityEngine;

namespace ArcheCore.Client.World
{
    // Attach to any spawned object that should respond to the interact key -
    // NPCs and harvest nodes today, lootables/quest objects later. Kept
    // separate from NpcIdentity so non-NPC interactables don't have to carry
    // NPC-only fields like Level.
    public class InteractableIdentity : MonoBehaviour
    {
        public int NetworkId;
        public float InteractRange = 4f;

        /// <summary>Shown in the interact prompt, e.g. "Iron Vein".</summary>
        public string DisplayName;

        /// <summary>Shown before the name in the prompt, e.g. "Gather", "Talk to".</summary>
        public string ActionVerb = "Interact with";

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, InteractRange);
        }
    }
}