using ArchCore.Client;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.World;
using TMPro;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// "[F] Gather Iron Vein" under the cursor target. Reads whatever the
    /// local player's PlayerInteraction is hovering; owns no input itself.
    ///
    ///   in range          -> "[F] Gather Iron Vein"
    ///   out of range      -> "Iron Vein - too far"
    ///   depleted node     -> "Iron Vein (depleted)"
    ///
    /// Scene setup: put this on an always-active HUD object that is NOT
    /// the label or root itself (hiding those would switch this script off
    /// too, and the prompt would never come back). Assign
    /// `label`, and optionally `root` (the thing to show/hide - e.g. a
    /// background behind the text). If `root` is empty, the label itself
    /// is shown and hidden. Not a UIPanel: it isn't a window, has no
    /// Escape behaviour, and follows hover state every frame.
    /// </summary>
    public class InteractPromptUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private GameObject root;
        [SerializeField] private string interactKeyName = "F";

        private PlayerInteraction _interaction;
        private float _nextSearchTime;

        private GameObject Target => root != null ? root : (label != null ? label.gameObject : null);

        private void Start() => Show(false);

        private void Update()
        {
            // Only the LOCAL player has a PlayerInteraction (PlayerRegistry
            // adds it on spawn), so the first one found is ours. Searched at
            // most once a second until the player exists.
            if (_interaction == null)
            {
                if (Time.unscaledTime < _nextSearchTime)
                    return;

                _nextSearchTime = Time.unscaledTime + 1f;
                _interaction = FindFirstObjectByType<PlayerInteraction>();

                if (_interaction == null)
                {
                    Show(false);
                    return;
                }
            }

            var focus = _interaction.CurrentFocus;
            if (focus == null || label == null)
            {
                Show(false);
                return;
            }

            label.text = BuildText(focus);
            Show(true);
        }

        private string BuildText(InteractableIdentity focus)
        {
            string name = string.IsNullOrEmpty(focus.DisplayName) ? focus.gameObject.name : focus.DisplayName;

            if (HarvestNodeRegistry.TryGet(focus.NetworkId, out var node) && node.IsDepleted)
                return $"{name} (depleted)";

            float distance = Vector3.Distance(_interaction.transform.position, focus.transform.position);
            if (distance > focus.InteractRange)
                return $"{name} - too far";

            return $"[{interactKeyName}] {focus.ActionVerb} {name}";
        }

        private void Show(bool visible)
        {
            var target = Target;
            if (target != null && target.activeSelf != visible)
                target.SetActive(visible);
        }
    }
}
