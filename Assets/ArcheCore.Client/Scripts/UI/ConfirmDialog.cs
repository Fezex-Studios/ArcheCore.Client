using System;
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
    /// The dialog for every "are you sure" in the game - destroying,
    /// selling, trading, deleting a character, leaving a party. Don't build
    /// a second one.
    ///
    /// Scene setup is the standard UIPanel split:
    ///   ConfirmDialog (ALWAYS ACTIVE - this component, plus UILayer = Modal)
    ///   └── Window    (the visuals - assign to `window`)
    ///
    /// Every way out except the Confirm button is a cancel: the Cancel
    /// button, Escape (via WorldUIManager), or a second Ask replacing an
    /// open one.
    /// </summary>
    public class ConfirmDialog : UIPanel
    {
        public static ConfirmDialog Instance { get; private set; }

        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text messageLabel;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text confirmButtonLabel;
        [SerializeField] private Button cancelButton;

        private Action _onConfirm;

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ConfirmDialog] More than one in the scene - keeping the first.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            base.Awake();

            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirmClicked);
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Shows the dialog. onConfirm runs only if the player clicks
        /// Confirm. Returns false if there's no usable dialog in the scene -
        /// callers doing something destructive must treat false as
        /// "cancelled", never as "go ahead".
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
            // A new question replaces an open one; the old one is cancelled.
            _onConfirm = onConfirm;

            if (titleLabel != null) titleLabel.text = title;
            if (messageLabel != null) messageLabel.text = message;
            if (confirmButtonLabel != null) confirmButtonLabel.text = confirmText;

            Open();
        }

        private void OnConfirmClicked()
        {
            // Capture first - Close() runs OnClosed, which clears it.
            var callback = _onConfirm;
            Close();
            callback?.Invoke();
        }

        /// <summary>Closing without Confirm is always a cancel.</summary>
        protected override void OnClosed()
        {
            _onConfirm = null;
        }
    }
}
