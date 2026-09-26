using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Floating damage numbers above whatever got hit: they rise and fade
    /// over about a second. Your own hits are gold, anyone else's are white.
    ///
    /// Roadmap 3.4: a crit is bigger with a "!", a glancing blow smaller, a
    /// miss says "Miss", a damage-over-time tick is smaller and italic, and
    /// heals float up green with a "+".
    /// Builds itself under the root Canvas on the first hit, and reuses its
    /// labels rather than creating one per hit.
    /// </summary>
    public class DamageNumbersUI : MonoBehaviour
    {
        private static DamageNumbersUI _instance;

        [SerializeField] private float lifetime = 1.0f;
        [SerializeField] private float riseWorldUnits = 1.2f;
        [SerializeField] private float startHeight = 2.2f;   // above the target's feet
        [SerializeField] private float fontSize = 22f;
        [SerializeField] private Color mineColor  = new Color(0.95f, 0.78f, 0.35f, 1f);
        [SerializeField] private Color otherColor = new Color(0.95f, 0.95f, 0.95f, 1f);

        [Tooltip("Damage done to YOU - the one number that must never be missed.")]
        [SerializeField] private Color takenColor = new Color(0.90f, 0.25f, 0.20f, 1f);
        [SerializeField] private float takenScale = 1.3f;
        [SerializeField] private Color healColor = new Color(0.45f, 0.90f, 0.40f, 1f);
        [SerializeField] private Color missColor = new Color(0.70f, 0.70f, 0.70f, 1f);
        [SerializeField] private float critScale = 1.45f;

        private sealed class Number
        {
            public TMP_Text Label;
            public Vector3 World;
            public float Age;
            public Color Color;
            public bool Live;
            public bool Outlined;
        }

        private readonly List<Number> _numbers = new();

        // Floating origin: numbers are anchored to a world point, not a
        // transform, so a shift has to move them explicitly.
        private void OnEnable()  => ArcheCore.Client.World.WorldOrigin.Shifted += OnWorldShifted;
        private void OnDisable() => ArcheCore.Client.World.WorldOrigin.Shifted -= OnWorldShifted;

        private void OnWorldShifted(Vector3 localDelta)
        {
            foreach (var n in _numbers)
                n.World += localDelta;
        }
        private RectTransform _self;
        private Canvas _root;

        public static void Spawn(Vector3 worldPosition, int damage, bool mine, bool takenByMe = false,
                                 ArcheCore.Network.Shared.Combat.HitOutcome outcome = ArcheCore.Network.Shared.Combat.HitOutcome.Hit,
                                 bool periodic = false)
        {
            if (_instance == null && !Create())
                return;

            var o = outcome;
            string text = o == ArcheCore.Network.Shared.Combat.HitOutcome.Miss ? "Miss"
                        : o == ArcheCore.Network.Shared.Combat.HitOutcome.Crit ? damage + "!"
                        : damage.ToString();

            float scale = o == ArcheCore.Network.Shared.Combat.HitOutcome.Crit ? _instance.critScale
                        : o == ArcheCore.Network.Shared.Combat.HitOutcome.Glance || periodic ? 0.8f
                        : 1f;

            Color color = o == ArcheCore.Network.Shared.Combat.HitOutcome.Miss ? _instance.missColor
                        : takenByMe ? _instance.takenColor : mine ? _instance.mineColor : _instance.otherColor;

            _instance.Add(worldPosition, text, color, scale * (takenByMe ? _instance.takenScale : 1f), italic: periodic);
        }

        /// <summary>A heal (a Mend, a heal-over-time tick): green, with a +.</summary>
        public static void SpawnHeal(Vector3 worldPosition, int amount, bool crit)
        {
            if (amount <= 0 || (_instance == null && !Create()))
                return;

            _instance.Add(worldPosition, crit ? $"+{amount}!" : $"+{amount}", _instance.healColor, crit ? _instance.critScale : 1f, italic: false);
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

            var go = new GameObject("DamageNumbers", typeof(RectTransform)) { layer = best.gameObject.layer };
            go.transform.SetParent(best.transform, false);
            _instance = go.AddComponent<DamageNumbersUI>();
            return true;
        }

        private void Awake()
        {
            _self = (RectTransform)transform;
            _self.anchorMin = Vector2.zero; _self.anchorMax = Vector2.one;
            _self.offsetMin = _self.offsetMax = Vector2.zero;
            _self.pivot = new Vector2(0.5f, 0.5f);

            if (!TryGetComponent(out Canvas canvas)) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 5;   // over the world-facing HUD, under windows
            if (!TryGetComponent(out CanvasGroup group)) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            _root = GetComponentInParent<Canvas>().rootCanvas;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Add(Vector3 world, string text, Color color, float scale, bool italic)
        {
            Number n = null;
            foreach (var x in _numbers) if (!x.Live) { n = x; break; }

            if (n == null)
            {
                var go = new GameObject("Number", typeof(RectTransform)) { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                var t = go.AddComponent<TextMeshProUGUI>();
                if (t.font == null && TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
                t.fontSize = fontSize; t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
                ((RectTransform)go.transform).sizeDelta = new Vector2(120, 40);
                n = new Number { Label = t };
                _numbers.Add(n);
            }

            n.World = world + Vector3.up * startHeight + new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));
            n.Age = 0f;
            n.Color = color;
            n.Live = true;
            n.Label.text = text;
            n.Label.fontSize = fontSize * scale;
            n.Label.fontStyle = italic ? FontStyles.Bold | FontStyles.Italic : FontStyles.Bold;
            n.Label.gameObject.SetActive(true);
            Place(n);
        }

        private void LateUpdate()
        {
            foreach (var n in _numbers)
            {
                if (!n.Live) continue;

                n.Age += Time.unscaledDeltaTime;
                if (n.Age >= lifetime)
                {
                    n.Live = false;
                    n.Label.gameObject.SetActive(false);
                    continue;
                }

                // Outline once the label is live and TMP has its material -
                // setting it earlier throws (see InventorySlotUI).
                if (!n.Outlined && n.Label.isActiveAndEnabled && n.Label.fontSharedMaterial != null)
                {
                    n.Label.outlineWidth = 0.2f;
                    n.Label.outlineColor = new Color32(0, 0, 0, 255);
                    n.Outlined = true;
                }

                Place(n);
            }
        }

        private void Place(Number n)
        {
            var cam = Camera.main;
            if (cam == null) return;

            float t = n.Age / lifetime;
            Vector3 world = n.World + Vector3.up * (riseWorldUnits * t);
            Vector3 screen = cam.WorldToScreenPoint(world);

            bool behind = screen.z < 0f;
            n.Label.enabled = !behind;
            if (behind) return;

            Camera uiCam = _root != null && _root.renderMode != RenderMode.ScreenSpaceOverlay ? _root.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_self, screen, uiCam, out var local))
                ((RectTransform)n.Label.transform).anchoredPosition = local;

            var c = n.Color;
            c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;   // hold, then fade out
            n.Label.color = c;
        }
    }
}
