using UnityEngine;

namespace ArcheCore.Client.Gameplay
{
    /// <summary>
    /// A spawned harvest node. Added by W2CSpawnHarvestNodeHandler, or put
    /// on the prefab yourself to set up depleted visuals.
    ///
    /// DEPLETED LOOK - two options, pick per prefab:
    ///
    ///   1. Assign `availableVisual` and/or `depletedVisual` (child objects
    ///      on the prefab). Available shows the first, depleted the second -
    ///      a full ore vein vs. a bare rock.
    ///   2. Leave both empty. Depleted then hides every Renderer and
    ///      Collider on the node, so it disappears until it respawns.
    ///
    /// Colliders are left alone in option 1, so a depleted node can still be
    /// hovered and the prompt can say "(depleted)".
    /// </summary>
    public class HarvestNodeIdentity : MonoBehaviour
    {
        [Tooltip("Optional. Shown while the node can be harvested.")]
        [SerializeField] private GameObject availableVisual;

        [Tooltip("Optional. Shown while the node is depleted.")]
        [SerializeField] private GameObject depletedVisual;

        public int    NetworkId;
        public int    TemplateId;
        public string NodeName;

        public bool IsDepleted { get; private set; }

        private bool HasCustomVisuals => availableVisual != null || depletedVisual != null;

        public void SetDepleted(bool depleted)
        {
            IsDepleted = depleted;

            // Keep the interaction side in step: a depleted node drops out of
            // F/G targeting, and its hover tooltip says why.
            if (TryGetComponent(out ArcheCore.Client.World.InteractableIdentity interactable))
            {
                interactable.IsAvailable = !depleted;
                interactable.ExtraLines.Remove("Depleted");
                if (depleted) interactable.ExtraLines.Add("Depleted");
            }

            if (HasCustomVisuals)
            {
                if (availableVisual != null) availableVisual.SetActive(!depleted);
                if (depletedVisual != null) depletedVisual.SetActive(depleted);
                return;
            }

            foreach (var r in GetComponentsInChildren<Renderer>(includeInactive: true))
                r.enabled = !depleted;

            foreach (var c in GetComponentsInChildren<Collider>(includeInactive: true))
                c.enabled = !depleted;
        }
    }
}
