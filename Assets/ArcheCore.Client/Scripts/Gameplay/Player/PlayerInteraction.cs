using ArcheCore.Client.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ArchCore.Client
{
    /// <summary>
    /// What's under the mouse. That's all this does now: the raycast that
    /// sets CurrentFocus, plus the F3 debug overlay.
    ///
    /// Acting on things moved to InteractionController (F / G / right-click,
    /// proximity targeting) and CombatController (left-click to target,
    /// attack key). CurrentFocus is what they - and the cursor, the hover
    /// tooltip and the nameplates - all read.
    ///
    /// Only the LOCAL player has one; PlayerRegistry adds it on spawn.
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        [SerializeField] private LayerMask interactableLayer;
        [SerializeField] private Key debugOverlayToggleKey = Key.F3;
        [SerializeField] private float hoverDistance = 100f;

        private bool _showDebugOverlay;
        private Camera _cam;
        private InteractableIdentity _hovered;

        /// <summary>The interactable under the mouse, at ANY distance. Null over UI.</summary>
        public InteractableIdentity CurrentFocus => _hovered;

        public LayerMask InteractableLayer => interactableLayer;

        public void Configure(LayerMask layer)
        {
            interactableLayer = layer;
        }

        private void Start()
        {
            _cam = Camera.main;
        }

        private void Update()
        {
            UpdateHover();

            if (Keyboard.current != null && Keyboard.current[debugOverlayToggleKey].wasPressedThisFrame)
                _showDebugOverlay = !_showDebugOverlay;
        }

        private void UpdateHover()
        {
            _hovered = null;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null || Mouse.current == null) return;

            // The mouse over a window isn't pointing at the world.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, hoverDistance, interactableLayer))
                _hovered = hit.collider.GetComponentInParent<InteractableIdentity>();
        }

        private void OnGUI()
        {
            if (!_showDebugOverlay)
                return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.yellow } };
            GUI.Box(new Rect(10, 10, 360, 86), "");

            string hover = _hovered != null ? $"Hover: {_hovered.DisplayName} (id {_hovered.NetworkId}, {_hovered.Kind})" : "Hover: nothing";
            float dist = _hovered != null ? Vector3.Distance(transform.position, _hovered.transform.position) : -1f;
            var target = ArcheCore.Client.Gameplay.Interaction.InteractionController.CurrentTarget;

            GUI.Label(new Rect(20, 14, 340, 24), hover, style);
            GUI.Label(new Rect(20, 38, 340, 24), _hovered != null ? $"Distance: {dist:F1} / {_hovered.InteractRange}" : "Distance: -", style);
            GUI.Label(new Rect(20, 62, 340, 24), target != null ? $"F/G target: {target.DisplayName}" : "F/G target: none", style);
        }
    }
}
