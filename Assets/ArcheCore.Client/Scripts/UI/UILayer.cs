using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    public enum UILayerKind
    {
        HUD     = 0,    // always-on: health, gold, minimap, chat
        Windows = 100,  // inventory, character, quest log, admin
        Modal   = 200,  // ConfirmDialog - above every window
        Drag    = 300,  // DragGhost - above everything, even modals
    }

    /// <summary>
    /// Puts this object on a fixed draw layer, so what renders on top no
    /// longer depends on where it sits in the Hierarchy.
    ///
    /// Adds a nested Canvas with Override Sorting on, sorted by `layer`.
    /// Also adds a GraphicRaycaster - WITHOUT one, nothing under a nested
    /// Canvas can be clicked, which is the classic nested-canvas bug.
    /// (The Drag layer skips it; the ghost must never catch clicks.)
    ///
    /// Add to the ROOT of a panel (InventoryPanel, ConfirmDialog,
    /// DragGhost...). Use orderOffset to order two panels on the same layer.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class UILayer : MonoBehaviour
    {
        [SerializeField] private UILayerKind layer = UILayerKind.Windows;

        [Tooltip("Added to the layer's base order. Keep it under 100 so layers never overlap.")]
        [SerializeField, Range(0, 99)] private int orderOffset;

        public int SortingOrder => (int)layer + orderOffset;

        private void Awake()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();

            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;

            if (layer != UILayerKind.Drag && GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }
    }
}
