using System;
using System.Collections.Generic;
using ArcheCore.Client.UI.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.UI
{
    [Serializable]
    public class PanelBinding
    {
        public Key key;
        public UIPanel panel;
    }

    /// <summary>
    /// The one place world-HUD panels open and close. Replaces
    /// HUDInputManager.
    ///
    ///   - Hotkeys: the `bindings` list, Key -> UIPanel. Ignored while the
    ///     player is typing in any input field, so a chat message with an
    ///     "i" in it doesn't toggle the inventory.
    ///   - Escape closes the most recently opened panel that allows it.
    ///     With ConfirmDialog up, Escape cancels it before anything else.
    ///   - Anything can open a panel from code:
    ///         WorldUIManager.Instance.Open(somePanel);   // or somePanel.Open()
    ///     so a loot window can open the inventory, death can CloseAll().
    ///   - IsAnyBlockingInputOpen tells gameplay code a window wants the
    ///     keyboard/mouse.
    ///
    /// Put exactly one in the scene, on an always-active object.
    /// </summary>
    public class WorldUIManager : MonoBehaviour
    {
        public static WorldUIManager Instance { get; private set; }

        [SerializeField] private PanelBinding[] bindings = Array.Empty<PanelBinding>();

        // Most recently opened last. Escape pops from the end.
        private readonly List<IUIPanel> _openStack = new();

        /// <summary>True while any open panel blocks gameplay input.</summary>
        public bool IsAnyBlockingInputOpen
        {
            get
            {
                Prune();
                foreach (var p in _openStack)
                {
                    // A plain IUIPanel (not a UIPanel) is treated as blocking.
                    if (p is not UIPanel up || up.BlocksGameplayInput)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// True while an input field has keyboard focus - chat, a search
        /// box, a rename field. Gameplay hotkeys should check this.
        /// </summary>
        public static bool IsTypingInField
        {
            get
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == null)
                    return false;

                var tmp = selected.GetComponent<TMP_InputField>();
                if (tmp != null && tmp.isFocused)
                    return true;

                var legacy = selected.GetComponent<UnityEngine.UI.InputField>();
                return legacy != null && legacy.isFocused;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[WorldUIManager] More than one in the scene - keeping the first.", this);
                Destroy(this);
                return;
            }

            Instance = this;

            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i] == null || bindings[i].panel == null)
                    Debug.LogError($"[WorldUIManager] Binding #{i} ({bindings[i]?.key}) has no panel assigned.", this);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // Typing owns the keyboard, Escape included - TMP_InputField
            // uses Escape to drop focus, and closing a window on the same
            // press would eat that.
            if (IsTypingInField)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                CloseTopmost();
                return;
            }

            foreach (var b in bindings)
            {
                if (b?.panel == null || b.key == Key.None)
                    continue;

                if (keyboard[b.key].wasPressedThisFrame)
                    Toggle(b.panel);
            }
        }

        // ── API ──────────────────────────────────────────────────────

        public void Open(IUIPanel panel)
        {
            if (panel == null)
                return;

            Prune();

            // Re-opening an open panel brings it to the top of the Escape order.
            _openStack.Remove(panel);

            if (!panel.IsVisible)
                panel.Show();

            if (panel.IsVisible)
                _openStack.Add(panel);
        }

        public void Close(IUIPanel panel)
        {
            if (panel == null)
                return;

            _openStack.Remove(panel);

            if (panel.IsVisible)
                panel.Hide();
        }

        public void Toggle(IUIPanel panel)
        {
            if (panel == null)
                return;

            if (panel.IsVisible) Close(panel);
            else Open(panel);
        }

        /// <summary>Closes everything - death, teleport, logout.</summary>
        public void CloseAll()
        {
            Prune();

            for (int i = _openStack.Count - 1; i >= 0; i--)
                _openStack[i].Hide();

            _openStack.Clear();
        }

        private void CloseTopmost()
        {
            Prune();

            for (int i = _openStack.Count - 1; i >= 0; i--)
            {
                var p = _openStack[i];
                if (p is UIPanel up && !up.CloseOnEscape)
                    continue;

                Close(p);
                return;
            }
        }

        /// <summary>
        /// Drops stack entries for panels something hid behind the
        /// manager's back (a direct Hide() call, a destroyed object), so
        /// Escape never "closes" a window that's already gone.
        /// </summary>
        private void Prune()
        {
            _openStack.RemoveAll(p =>
                p == null ||
                (p is UnityEngine.Object o && o == null) ||
                !p.IsVisible);
        }
    }
}
