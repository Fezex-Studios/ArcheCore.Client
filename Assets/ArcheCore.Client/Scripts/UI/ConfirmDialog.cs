using System;
using ArcheCore.Client.UI.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Reusable yes/no modal. Call from anywhere:
    ///
    ///     ConfirmDialog.Ask("Destroy item?", "This can't be undone.", () => DoIt());
    ///
    /// Built for item destruction first, but it's the dialog for every
    /// "are you sure" in the game - selling, trading, deleting a
    /// character, leaving a party. Don't build a second one.
    ///
    /// Scene setup - same split as InventoryPanel:
    ///   ConfirmDialog (ALWAYS ACTIVE, this component)  -> sets Instance in Awake
    ///   └── Window    (starts inactive, the visuals)   -> assign to `window`
    ///
    /// Registered with WorldUIManager when one is assigned, so Escape
    /// cancels it and IsAnyBlockingInputOpen stops the camera while it's up.
    /// Cancel is the default for every way out except the Confirm button:
    /// Escape, Cancel, or a second Ask replacing this one.
    /// </summary>
    public class ConfirmDialog : MonoBehaviour, IUIPanel
    {
        public static ConfirmDialog Instance { get; private set; }

        [SerializeField] private GameObject window;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text messageLabel;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text confirmButtonLabel;
        [SerializeField] private Button cancelButton;

        [Tooltip("Optional. If set, Escape cancels the dialog and it counts as input-blocking.")]
        [SerializeField] private WorldUIManager uiManager;

        private Action _onConfirm;

        public bool IsVisible => window != null && window.activeSelf;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ConfirmDialog] More than one in the scene - keeping the first.", this);
                Destroy(this);
                return;
            }

            Instance = this;

            if (window == null)
            {
                Debug.LogError("[ConfirmDialog] `window` not assigned - dialog can't show.", this);
                return;
            }

            window.SetActive(false);

            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Shows the dialog. onConfirm runs only if the player clicks
        /// Confirm. Returns false if there's no dialog in the scene, in
        /// which case NOTHING happens - callers doing something destructive
        /// must treat false as "cancelled", never as "go ahead".
        /// </summary>
        public static bool Ask(string title, string message, Action onConfirm, string confirmText = "Confirm")
        {
            if (Instance == null || Instance.window == null)
            {
                Debug.LogWarning($"[ConfirmDialog] No dialog in scene - '{title}' treated as cancelled.");
                return false;
            }

            Instance.Present(title, message, onConfirm, confirmText);
            return true;
        }

        private void Present(string title, string message, Action onConfirm, string confirmText)
        {
            // A new question replaces an open one - the old one is cancelled.
            _onConfirm = onConfirm;

            if (titleLabel != null) titleLabel.text = title;
            if (messageLabel != null) messageLabel.text = message;
            if (confirmButtonLabel != null) confirmButtonLabel.text = confirmText;

            if (uiManager != null) uiManager.Open(this);
            else Show();
        }

        // ── IUIPanel ─────────────────────────────────────────────────

        public void Show()
        {
            if (window != null)
                window.SetActive(true);
        }

        /// <summary>Hiding without Confirm is always a cancel.</summary>
        public void Hide()
        {
            _onConfirm = null;

            if (window != null)
                window.SetActive(false);
        }

        // ── Buttons ──────────────────────────────────────────────────

        private void Confirm()
        {
            // Capture first - closing calls Hide(), which clears it.
            var callback = _onConfirm;
            Close();
            callback?.Invoke();
        }

        private void Cancel() => Close();

        private void Close()
        {
            if (uiManager != null) uiManager.Close(this);
            else Hide();
        }
    }
}