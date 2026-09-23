using ArcheCore.Client.Gameplay;
using ArcheCore.Client.Gameplay.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Top-centre frame for your current target: name, level, and a health
    /// bar that follows every W2CCombatEvent. Builds itself under the root
    /// Canvas the first time you have a target - no scene setup. To restyle,
    /// add this component to an object under the Canvas and edit the fields.
    ///
    /// The health bar is a plain Image scaled by its anchors, so it needs no
    /// sprite. Never catches clicks.
    /// </summary>
    public class TargetFrameUI : MonoBehaviour
    {
        public static TargetFrameUI Instance { get; private set; }

        [SerializeField] private Vector2 size = new Vector2(240f, 50f);
        [SerializeField] private float topOffset = 14f;
        [SerializeField] private Color panelColor = new Color(0.086f, 0.067f, 0.051f, 0.92f);
        [SerializeField] private Color trimColor  = new Color(0.722f, 0.537f, 0.227f, 1f);
        [SerializeField] private Color textColor  = new Color(0.902f, 0.863f, 0.784f, 1f);
        [SerializeField] private Color levelColor = new Color(0.910f, 0.753f, 0.416f, 1f);
        [SerializeField] private Color barBack    = new Color(0.047f, 0.035f, 0.027f, 1f);
        [SerializeField] private Color barFill    = new Color(0.690f, 0.170f, 0.140f, 1f);

        private RectTransform _panel;
        private RectTransform _fill;
        private TMP_Text _name, _hp, _distance;
        private RectTransform _barBack;
        private Transform _player;

        public static void EnsureInstance()
        {
            if (Instance != null)
                return;

            var canvas = FindRootCanvas();
            if (canvas == null)
                return;

            var go = new GameObject("TargetFrame", typeof(RectTransform)) { layer = canvas.gameObject.layer };
            go.transform.SetParent(canvas.transform, false);
            go.AddComponent<TargetFrameUI>();
        }

        private static Canvas FindRootCanvas()
        {
            Canvas best = null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
                best = c;
                if (c.name == "Canvas") break;
            }
            return best;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Build();
            Refresh();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            var self = (RectTransform)transform;
            self.anchorMin = self.anchorMax = new Vector2(0.5f, 1f);
            self.pivot = new Vector2(0.5f, 1f);
            self.anchoredPosition = new Vector2(0f, -topOffset);
            self.sizeDelta = size;

            if (!TryGetComponent(out Canvas canvas)) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 10;   // above the HUD, below every window
            if (!TryGetComponent(out CanvasGroup group)) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            _panel = Rect("Panel", self, stretch: true);
            var bg = _panel.gameObject.AddComponent<Image>();
            bg.color = panelColor; bg.raycastTarget = false;
            var trim = _panel.gameObject.AddComponent<Outline>();
            trim.effectColor = trimColor; trim.effectDistance = new Vector2(1.5f, -1.5f);

            _name = Text("Name", _panel, 14f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Place(_name.rectTransform, new Vector2(10, -6), new Vector2(-10, -24));

            _distance = Text("Distance", _panel, 11f, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            Place(_distance.rectTransform, new Vector2(10, -6), new Vector2(-10, -24));

            var back = Rect("HealthBack", _panel, stretch: false);
            _barBack = back;
            back.anchorMin = new Vector2(0, 0); back.anchorMax = new Vector2(1, 0);
            back.pivot = new Vector2(0.5f, 0);
            back.offsetMin = new Vector2(10, 7); back.offsetMax = new Vector2(-10, 21);
            var backImg = back.gameObject.AddComponent<Image>(); backImg.color = barBack; backImg.raycastTarget = false;

            _fill = Rect("HealthFill", back, stretch: true);
            var fillImg = _fill.gameObject.AddComponent<Image>(); fillImg.color = barFill; fillImg.raycastTarget = false;

            _hp = Text("HealthText", back, 10f, FontStyles.Bold, TextAlignmentOptions.Center);
            var hr = _hp.rectTransform; hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one; hr.offsetMin = hr.offsetMax = Vector2.zero;
        }

        private static RectTransform Rect(string name, Transform parent, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            if (stretch) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
            return rt;
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 bottomRightFromTopRight)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(topLeft.x, bottomRightFromTopRight.y);
            rt.offsetMax = new Vector2(bottomRightFromTopRight.x, topLeft.y);
        }

        private TMP_Text Text(string name, Transform parent, float size, FontStyles style, TextAlignmentOptions align)
        {
            var t = Rect(name, parent, stretch: false).gameObject.AddComponent<TextMeshProUGUI>();
            if (t.font == null && TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size; t.fontStyle = style; t.alignment = align; t.color = textColor;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false; t.richText = true;
            return t;
        }

        // Cheap enough to do every frame, and it means the bar is right even
        // if an NPC spawns already damaged (someone else was hitting it).
        private void Update() => Refresh();

        private void Refresh()
        {
            int id = CombatClient.TargetId;

            // A target is an NPC or another player; both get the same frame.
            string label = null;
            int health = 0, maxHealth = 0;
            Transform where = null;

            if (id != 0 && NpcRegistry.Instance != null &&
                NpcRegistry.Instance.TryGetNpc(id, out var npcTarget) && npcTarget != null)
            {
                label = $"<color=#{ColorUtility.ToHtmlStringRGB(levelColor)}>{npcTarget.Level}</color>  {npcTarget.NpcName}";
                health = npcTarget.Health;
                maxHealth = npcTarget.MaxHealth;
                where = npcTarget.transform;
            }
            else if (id != 0 && PlayerRegistry.Instance != null &&
                     PlayerRegistry.Instance.TryGetPlayer(id, out var playerTarget) && playerTarget != null)
            {
                label = string.IsNullOrEmpty(playerTarget.playerName) ? "Player" : playerTarget.playerName;
                health = playerTarget.health;
                maxHealth = playerTarget.maxHealth;
                where = playerTarget.transform;
            }

            bool has = label != null;
            if (_panel.gameObject.activeSelf != has)
                _panel.gameObject.SetActive(has);
            if (!has)
                return;

            _name.text = label;

            // No bar for something with no health to show (a friendly NPC, or
            // a player the server hasn't told us about yet).
            _barBack.gameObject.SetActive(maxHealth > 0);

            if (_player == null)
            {
                var pi = FindFirstObjectByType<ArchCore.Client.PlayerInteraction>();
                if (pi != null) _player = pi.transform;
            }
            _distance.text = _player != null && where != null
                ? $"{Vector3.Distance(_player.position, where.position):F1} m"
                : "";

            float pct = maxHealth > 0 ? Mathf.Clamp01((float)health / maxHealth) : 0f;
            _fill.anchorMax = new Vector2(pct, 1f);
            _hp.text = maxHealth > 0 ? $"{health} / {maxHealth}" : "";
        }
    }
}
