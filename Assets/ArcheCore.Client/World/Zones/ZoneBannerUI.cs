using ArcheCore.Movement.World;
using TMPro;
using UnityEngine;

namespace ArcheCore.Client.World
{
    /// <summary>
    /// The "entering Solzreed Peninsula" banner: zone name large at the top
    /// of the screen, a smaller line under it (subtitle, level range, PvP
    /// state), fading in and out.
    ///
    /// Built at runtime under the root HUD canvas, the same way
    /// DamageNumbersUI is - nothing to wire up in a scene or prefab. Call
    /// Show; ZoneTracker does.
    ///
    /// The name is tinted by PvP mode so the rule is readable at a glance
    /// before anyone reads the subtitle: green is safe, white peaceful,
    /// orange contested, red war.
    /// </summary>
    public sealed class ZoneBannerUI : MonoBehaviour
    {
        private static ZoneBannerUI _instance;

        [SerializeField] private float fadeIn = 0.6f;
        [SerializeField] private float hold = 2.6f;
        [SerializeField] private float fadeOut = 1.2f;
        [SerializeField] private float titleSize = 46f;
        [SerializeField] private float subtitleSize = 20f;
        [SerializeField] private float topOffset = 110f;

        private CanvasGroup _group;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _subtitle;
        private float _age = -1f;

        public static void Show(ZoneDefinition zone)
        {
            if (zone == null) return;
            if (_instance == null && !Create()) return;
            _instance.Display(zone);
        }

        private static bool Create()
        {
            Canvas best = null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
                best = c;
                if (c.name == "Canvas") break;
            }
            if (best == null) return false;

            var go = new GameObject("ZoneBanner", typeof(RectTransform)) { layer = best.gameObject.layer };
            go.transform.SetParent(best.transform, false);
            _instance = go.AddComponent<ZoneBannerUI>();
            return true;
        }

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -topOffset);
            rect.sizeDelta = new Vector2(900f, 110f);

            if (!TryGetComponent(out Canvas canvas)) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 6; // over damage numbers, under windows

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;

            _title = MakeText("Title", titleSize, FontStyles.Bold, new Vector2(0f, 0f));
            _subtitle = MakeText("Subtitle", subtitleSize, FontStyles.Normal, new Vector2(0f, -titleSize - 8f));
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private TextMeshProUGUI MakeText(string name, float size, FontStyles style, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(0f, size + 12f);

            var t = go.AddComponent<TextMeshProUGUI>();
            if (t.font == null && TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;

            // Zone names come from our own data, but never let a stray '<' in
            // a name turn into markup.
            t.richText = false;

            return t;
        }

        private void Display(ZoneDefinition zone)
        {
            _title.text = string.IsNullOrEmpty(zone.DisplayName) ? zone.Key : zone.DisplayName;
            _title.color = ColourFor(zone.PvpMode);
            _subtitle.text = BuildSubtitle(zone);
            _subtitle.color = new Color(0.9f, 0.88f, 0.8f, 1f);

            // Restart from wherever the current fade is, so crossing back and
            // forth over a border doesn't flash.
            float currentAlpha = _group.alpha;
            _age = currentAlpha * fadeIn;
        }

        private static string BuildSubtitle(ZoneDefinition zone)
        {
            var parts = new System.Collections.Generic.List<string>(3);

            if (!string.IsNullOrEmpty(zone.Subtitle))
                parts.Add(zone.Subtitle);

            if (zone.MinLevel > 0 || zone.MaxLevel > 0)
                parts.Add(zone.MaxLevel > zone.MinLevel ? $"Lv {zone.MinLevel}-{zone.MaxLevel}" : $"Lv {zone.MinLevel}");

            parts.Add(PvpLabel(zone.PvpMode));
            return string.Join("  ·  ", parts);
        }

        public static string PvpLabel(ZonePvpMode mode)
        {
            switch (mode)
            {
                case ZonePvpMode.Safe:      return "Sanctuary";
                case ZonePvpMode.Peaceful:  return "Peaceful";
                case ZonePvpMode.Contested: return "Contested - PvP enabled";
                case ZonePvpMode.War:       return "War Zone - PvP enabled";
                default:                    return mode.ToString();
            }
        }

        private static Color ColourFor(ZonePvpMode mode)
        {
            switch (mode)
            {
                case ZonePvpMode.Safe:      return new Color(0.55f, 0.95f, 0.6f, 1f);
                case ZonePvpMode.Contested: return new Color(1f, 0.72f, 0.3f, 1f);
                case ZonePvpMode.War:       return new Color(1f, 0.35f, 0.3f, 1f);
                default:                    return new Color(0.96f, 0.94f, 0.88f, 1f);
            }
        }

        private void Update()
        {
            if (_age < 0f) return;

            _age += Time.unscaledDeltaTime;

            float alpha;
            if (_age < fadeIn) alpha = _age / fadeIn;
            else if (_age < fadeIn + hold) alpha = 1f;
            else if (_age < fadeIn + hold + fadeOut) alpha = 1f - (_age - fadeIn - hold) / fadeOut;
            else { alpha = 0f; _age = -1f; }

            _group.alpha = alpha;
        }
    }
}
