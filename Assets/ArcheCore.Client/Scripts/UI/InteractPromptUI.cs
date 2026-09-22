using TMPro;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// RETIRED. The old "[F] Gather Iron Vein" label is replaced by:
    ///   ActionPromptUI  - F / G icons for whatever you're in range of
    ///   HoverTooltipUI  - the name next to the cursor
    /// Both create themselves.
    ///
    /// Kept only so a scene that still has this component doesn't show a
    /// missing-script warning. It hides its label and does nothing else. You
    /// can delete the InteractPrompt and PromptLabel objects from the scene
    /// whenever you like.
    /// </summary>
    public class InteractPromptUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private GameObject root;
        [SerializeField] private string interactKeyName = "F";

        private void Start()
        {
            if (root != null) root.SetActive(false);
            else if (label != null) label.gameObject.SetActive(false);
            enabled = false;
        }
    }
}
