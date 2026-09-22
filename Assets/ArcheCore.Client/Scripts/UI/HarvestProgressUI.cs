using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// "Gathering Iron Vein..." bar. Opened by W2CHarvestStarted, closed by
    /// W2CHarvestCompleted or W2CHarvestCancelled.
    ///
    /// The fill is cosmetic, timed locally from DurationMs. The server
    /// decides when the harvest is really done; if its Completed packet
    /// arrives a beat after the bar is full, the bar just sits full for
    /// that beat. Moving cancels on the server, which sends Cancelled.
    ///
    /// Scene setup - standard UIPanel split:
    ///   HarvestProgress (ALWAYS ACTIVE - this component, plus UILayer = HUD)
    ///   └── Window      (assign to `window`)
    ///       ├── Background (Image)
    ///       ├── Fill       (Image, Type = Filled, Horizontal - assign to `fill`)
    ///       └── Label      (TMP text - assign to `label`)
    ///
    /// A passive overlay: Escape doesn't close it and it doesn't block
    /// gameplay input (both defaulted off in Reset).
    /// </summary>
    public class HarvestProgressUI : UIPanel
    {
        public static HarvestProgressUI Instance { get; private set; }

        [SerializeField] private Image fill;
        [SerializeField] private TMP_Text label;

        private float _startTime;
        private float _duration;

        private void Reset()
        {
            closeOnEscape = false;
            blocksGameplayInput = false;
        }

        protected override void Awake()
        {
            Instance = this;
            base.Awake();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Begin(string nodeName, int durationMs)
        {
            _startTime = Time.unscaledTime;
            _duration = Mathf.Max(0.01f, durationMs / 1000f);

            if (label != null) label.text = $"Gathering {nodeName}...";
            if (fill != null) fill.fillAmount = 0f;

            Open();
        }

        public void End() => Close();

        private void Update()
        {
            if (!IsVisible || fill == null)
                return;

            fill.fillAmount = Mathf.Clamp01((Time.unscaledTime - _startTime) / _duration);
        }
    }
}
