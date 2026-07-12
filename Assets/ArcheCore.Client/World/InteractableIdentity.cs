using UnityEngine;

namespace ArcheCore.Client.World
{
    // Attach to any spawned object that should respond to the interact key -
    // NPCs today, lootables/quest objects later. Kept separate from
    // NpcIdentity so non-NPC interactables don't have to carry NPC-only
    // fields like Level.
    public class InteractableIdentity : MonoBehaviour
    {
        public int NetworkId;
        public float InteractRange = 4f;
        
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, InteractRange);
        }

    }
    
    
}