using ArcheCore.Client.UI.Interfaces;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Base class for every show/hide-able window in the world HUD.
    ///
    /// Scene setup - ALWAYS the same two objects:
    ///
    ///   MyPanel   (ALWAYS ACTIVE - this component lives here)
    ///   └── Window (the visuals - assign to `window`; hidden on start)
    ///
    /// The split is the whole point. The root stays active for the entire
    /// session, so Awake/OnEnable run at scene load and event
    /// subscriptions stay alive while the panel is closed. Only `window`
    /// is toggled. That's the fix for "a disabled GameObject never runs
    /// Awake, so it missed the packet" - built in once, here, instead of
    /// every panel re-inventing it.
    ///
    /// Subclasses:
    ///   - Subscribe to events in OnEnable / unsubscribe in OnDisable, as
    ///     normal. Those now mean "session start/end", not "opened/closed".
    ///   - Override OnOpened() to pull current state from
    ///     LocalCharacterState. It runs every time the window appears.
    ///   - Override OnClosed() to drop anything that mustn't survive
    ///     closing (a selection, a drag in progress).
    ///   - If you override Awake, call base.Awake() first.
    ///   - A passive overlay (progress bar, tracker) should turn off
    ///     closeOnEscape and blocksGameplayInput - set them in Reset() so
    ///     the Inspector starts with the right values.
    ///
    /// Open with Open()/Close()/Toggle(), not Show()/Hide(). Those go
    /// through WorldUIManager, so Escape and input blocking see the
    /// panel. Show()/Hide() are the IUIPanel hooks the manager calls.
    /// </summary>
    public abstract class UIPanel : MonoBehaviour, IUIPanel
    {
        [Tooltip("The child holding this panel's visuals. Must be a CHILD - never this GameObject.")]
        [SerializeField] protected GameObject window;

        [Tooltip("Open automatically once the scene starts.")]
        [SerializeField] private bool startOpen;

        [Tooltip("Escape closes this panel when it's the topmost one open.")]
        [SerializeField] protected bool closeOnEscape = true;

        [Tooltip("While open, WorldUIManager.IsAnyBlockingInputOpen is true. Turn off for passive overlays.")]
        [SerializeField] protected bool blocksGameplayInput = true;

        public bool IsVisible => window != null && window.activeSelf;
        public bool CloseOnEscape => closeOnEscape;
        public bool BlocksGameplayInput => blocksGameplayInput;

        protected virtual void Awake()
        {
            if (window == null)
            {
                Debug.LogError($"[{GetType().Name}] `window` isn't assigned on '{name}'. The panel can't open.", this);
                return;
            }

            if (window == gameObject)
            {
                // Hiding the root would stop this script running - exactly
                // the bug the split exists to prevent.
                Debug.LogError($"[{GetType().Name}] `window` on '{name}' is the panel itself. It must be a CHILD object.", this);
                window = null;
                return;
            }

            window.SetActive(false);
        }

        protected virtual void Start()
        {
            if (startOpen)
                Open();
        }

        // ── Public API - use these ───────────────────────────────────

        public void Open()
        {
            if (WorldUIManager.Instance != null) WorldUIManager.Instance.Open(this);
            else Show();
        }

        public void Close()
        {
            if (WorldUIManager.Instance != null) WorldUIManager.Instance.Close(this);
            else Hide();
        }

        public void Toggle()
        {
            if (IsVisible) Close();
            else Open();
        }

        // ── IUIPanel - WorldUIManager calls these ────────────────────

        public void Show()
        {
            if (window == null || IsVisible)
                return;

            window.SetActive(true);
            OnOpened();
        }

        public void Hide()
        {
            if (window == null || !IsVisible)
                return;

            window.SetActive(false);
            OnClosed();
        }

        // ── Subclass hooks ───────────────────────────────────────────

        /// <summary>Window just appeared. Pull current state here.</summary>
        protected virtual void OnOpened() { }

        /// <summary>Window just hid. Drop anything that mustn't survive closing.</summary>
        protected virtual void OnClosed() { }
    }
}
