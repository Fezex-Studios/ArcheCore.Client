using System.Text;
using ArcheCore.Client.GameData;
using ArcheCore.Client.UI.State;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// The one item tooltip, shared by inventory slots, shop rows, and later
    /// loot / bank / equipment. ArcheAge-style:
    ///
    ///   [icon]  Iron Ore            ← name, coloured by rarity
    ///           Material · Common   ← category · rarity
    ///   ─────────────────────────
    ///   A lump of raw iron.         ← description
    ///   Required level 10           ← red if you're below it
    ///   Use: Right-click            ← only if the item is usable
    ///   Stack: 66
    ///   Sells for 4g each (264g)    ← whatever the caller adds
    ///
    /// NO SCENE SETUP NEEDED. The first Show() builds the tooltip under your
    /// root Canvas. To restyle it, add this component to any object under
    /// the Canvas and change the fields - that copy is used instead.
    ///
    /// Callers pass themselves as the "owner", so a slot's pointer-exit only
    /// hides the tooltip if that slot is the one showing it. Moving straight
    /// from one slot to the next switches instantly, without the delay.
    ///
    /// Never catches clicks (CanvasGroup.blocksRaycasts = false), and draws
    /// on its own canvas between Modal (200) and Drag (300).
    /// </summary>
    public class ItemTooltipUI : MonoBehaviour
    {
        public static ItemTooltipUI Instance { get; private set; }

        [Header("Behaviour")]
        [SerializeField] private float showDelay = 0.25f;
        [SerializeField] private int sortingOrder = 250;

        [Header("Size and position")]
        [SerializeField] private float width = 260f;
        [SerializeField] private Vector2 cursorOffset = new Vector2(18f, -18f);
        [SerializeField] private float screenMargin = 8f;
        [SerializeField] private float iconSize = 40f;

        [Header("Colours")]
        [SerializeField] private Color panelColor   = new Color(0.086f, 0.067f, 0.051f, 0.96f);
        [SerializeField] private Color trimColor    = new Color(0.722f, 0.537f, 0.227f, 1f);
        [SerializeField] private Color textColor    = new Color(0.902f, 0.863f, 0.784f, 1f);
        [SerializeField] private Color mutedColor   = new Color(0.612f, 0.561f, 0.478f, 1f);
        [SerializeField] private Color useColor     = new Color(0.55f, 0.82f, 0.45f, 1f);
        [SerializeField] private Color warningColor = new Color(0.85f, 0.35f, 0.30f, 1f);
        [SerializeField] private Color goldColor    = new Color(0.910f, 0.753f, 0.416f, 1f);

        [Header("Font sizes")]
        [SerializeField] private float nameSize = 16f;
        [SerializeField] private float subtitleSize = 11f;
        [SerializeField] private float bodySize = 12f;

        // Built parts
        private RectTransform _self;
        private RectTransform _panel;
        private CanvasGroup _group;
        private Canvas _rootCanvas;
        private Image _icon;
        private TMP_Text _name, _subtitle, _body, _details;
        private GameObject _divider;

        // State
        private object _owner;
        private int _itemId, _quantity;
        private string _extra;
        private bool _pending, _visible;
        private float _showAt;

        // ── Public API ───────────────────────────────────────────────

        /// <summary>Show (or update) the tooltip for an item. extraLine is appended at the bottom - prices, etc.</summary>
        public static void Show(object owner, int itemTemplateId, int quantity, string extraLine = null)
        {
            if (itemTemplateId <= 0 || !EnsureInstance())
                return;

            Instance.ShowInternal(owner, itemTemplateId, quantity, extraLine);
        }

        /// <summary>Hide it - but only if `owner` is the one showing it. Pass null to hide regardless.</summary>
        public static void Hide(object owner = null)
        {
            if (Instance != null)
                Instance.HideInternal(owner);
        }

        // ── Setup ────────────────────────────────────────────────────

        private static bool EnsureInstance()
        {
            if (Instance != null)
                return true;

            Canvas target = null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace)
                    continue;

                target = c;
                if (c.name == "Canvas") break;   // prefer the main HUD canvas
            }

            if (target == null)
            {
                Debug.LogWarning("[ItemTooltipUI] No screen-space Canvas in the scene - tooltip can't be shown.");
                return false;
            }

            var go = new GameObject("ItemTooltip", typeof(RectTransform)) { layer = target.gameObject.layer };
            go.transform.SetParent(target.transform, false);
            go.AddComponent<ItemTooltipUI>();
            return Instance != null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ItemTooltipUI] More than one tooltip - keeping the first.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            Build();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Build()
        {
            _self = (RectTransform)transform;
            _self.anchorMin = Vector2.zero;
            _self.anchorMax = Vector2.one;
            _self.pivot = new Vector2(0.5f, 0.5f);
            _self.offsetMin = _self.offsetMax = Vector2.zero;
            _self.SetAsLastSibling();

            // TryGetComponent, not "GetComponent() ?? AddComponent()" - a missing
            // component is a fake-null in the Editor that ?? treats as real.
            if (!TryGetComponent(out Canvas canvas)) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            if (!TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _rootCanvas = GetComponentInParent<Canvas>().rootCanvas;

            // Panel
            _panel = NewRect("Panel", _self);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0f, 1f);
            _panel.sizeDelta = new Vector2(width, 0f);

            var bg = _panel.gameObject.AddComponent<Image>();
            bg.color = panelColor;
            bg.raycastTarget = false;
            var trim = _panel.gameObject.AddComponent<Outline>();
            trim.effectColor = trimColor;
            trim.effectDistance = new Vector2(1.5f, -1.5f);

            var column = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(10, 10, 10, 10);
            column.spacing = 6f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            var fitter = _panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Header: icon + (name / subtitle)
            var header = NewRect("Header", _panel);
            var row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var iconRect = NewRect("Icon", header);
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;
            var iconLayout = iconRect.gameObject.AddComponent<LayoutElement>();
            iconLayout.minWidth = iconLayout.preferredWidth = iconSize;
            iconLayout.minHeight = iconLayout.preferredHeight = iconSize;

            var titles = NewRect("Titles", header);
            var titleColumn = titles.gameObject.AddComponent<VerticalLayoutGroup>();
            titleColumn.spacing = 1f;
            titleColumn.childControlWidth = true;
            titleColumn.childControlHeight = true;
            titleColumn.childForceExpandWidth = true;
            titleColumn.childForceExpandHeight = false;
            titles.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _name = NewText("Name", titles, nameSize, textColor, FontStyles.Bold);
            _subtitle = NewText("Subtitle", titles, subtitleSize, mutedColor, FontStyles.Normal);

            // Divider
            var div = NewRect("Divider", _panel);
            var line = div.gameObject.AddComponent<Image>();
            line.color = new Color(trimColor.r, trimColor.g, trimColor.b, 0.45f);
            line.raycastTarget = false;
            var divLayout = div.gameObject.AddComponent<LayoutElement>();
            divLayout.minHeight = divLayout.preferredHeight = 1f;
            _divider = div.gameObject;

            _body = NewText("Description", _panel, bodySize, textColor, FontStyles.Normal);
            _details = NewText("Details", _panel, bodySize, mutedColor, FontStyles.Normal);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private TMP_Text NewText(string name, Transform parent, float size, Color color, FontStyles style)
        {
            var t = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (t.font == null && TMP_Settings.defaultFontAsset != null)
                t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;
            t.raycastTarget = false;
            t.alignment = TextAlignmentOptions.TopLeft;
            return t;
        }

        // ── Show / hide ──────────────────────────────────────────────

        private void ShowInternal(object owner, int itemId, int quantity, string extra)
        {
            bool alreadyShowing = _visible;

            _owner = owner;
            _itemId = itemId;
            _quantity = quantity;
            _extra = extra;

            if (alreadyShowing)
            {
                // Slot to slot, or an update while hovering: no delay.
                Populate();
                return;
            }

            _pending = true;
            _showAt = Time.unscaledTime + showDelay;
        }

        private void HideInternal(object owner)
        {
            if (owner != null && !ReferenceEquals(owner, _owner))
                return;

            _owner = null;
            _pending = false;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_group != null) _group.alpha = visible ? 1f : 0f;
            if (_panel != null) _panel.gameObject.SetActive(visible);
        }

        private void Update()
        {
            // The owner vanished without a pointer-exit (its window closed).
            if (_owner is Component c && (c == null || !c.gameObject.activeInHierarchy))
            {
                HideInternal(null);
                return;
            }

            if (_pending && Time.unscaledTime >= _showAt)
            {
                _pending = false;
                SetVisible(true);
                Populate();
            }

            if (_visible)
                FollowCursor();
        }

        // ── Content ──────────────────────────────────────────────────

        private void Populate()
        {
            ItemRecord record = null;
            bool known = GameDataDatabase.IsReady && GameDataDatabase.Items != null &&
                         GameDataDatabase.Items.TryGetValue(_itemId, out record);

            // Header
            var sprite = ItemIcons.Get(_itemId);
            _icon.sprite = sprite;
            _icon.gameObject.SetActive(sprite != null);

            _name.text = known && !string.IsNullOrEmpty(record.Name) ? record.Name : $"#{_itemId}";
            _name.color = known && ColorUtility.TryParseHtmlString(record.RarityColor, out var rarity) ? rarity : textColor;

            string subtitle = known
                ? Join(" · ", record.CategoryName, record.RarityName)
                : "Unknown item - this client's gamedata is out of date";
            _subtitle.text = subtitle;
            _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));

            // Description
            string description = known ? record.Description : null;
            _body.text = description ?? string.Empty;
            _body.gameObject.SetActive(!string.IsNullOrEmpty(description));

            // Details
            var sb = new StringBuilder();
            if (known && record.RequiredLevel > 0)
            {
                bool tooLow = LocalCharacterState.HasEnteredWorld && LocalCharacterState.Level < record.RequiredLevel;
                Line(sb, $"Required level {record.RequiredLevel}", tooLow ? warningColor : mutedColor);
            }
            if (known && record.IsUsable)
                Line(sb, record.ConsumeOnUse ? "Use: Right-click (consumed)" : "Use: Right-click", useColor);
            if (_quantity > 1)
                Line(sb, $"Stack: {_quantity}", textColor);
            if (!string.IsNullOrEmpty(_extra))
                Line(sb, _extra, goldColor);

            _details.text = sb.ToString();
            _details.gameObject.SetActive(sb.Length > 0);

            _divider.SetActive(_body.gameObject.activeSelf || _details.gameObject.activeSelf);

            // Size now, so the first frame is positioned with the real height.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
            FollowCursor();
        }

        private static void Line(StringBuilder sb, string text, Color color)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append('>').Append(text).Append("</color>");
        }

        private static string Join(string sep, params string[] parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (sb.Length > 0) sb.Append(sep);
                sb.Append(p);
            }
            return sb.ToString();
        }

        // ── Position ─────────────────────────────────────────────────

        /// <summary>
        /// Down-right of the cursor; flips left or up when it would leave the
        /// screen, then clamps so it's never cut off.
        /// </summary>
        private void FollowCursor()
        {
            if (Mouse.current == null || _rootCanvas == null)
                return;

            Camera cam = _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _rootCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_self, Mouse.current.position.ReadValue(), cam, out var cursor))
                return;

            Vector2 size = _panel.rect.size;
            Rect area = _self.rect;
            Vector2 pos = cursor + cursorOffset;

            if (pos.x + size.x > area.xMax - screenMargin)
                pos.x = cursor.x - cursorOffset.x - size.x;
            if (pos.y - size.y < area.yMin + screenMargin)
                pos.y = cursor.y - cursorOffset.y + size.y;

            pos.x = Mathf.Clamp(pos.x, area.xMin + screenMargin, Mathf.Max(area.xMin + screenMargin, area.xMax - screenMargin - size.x));
            pos.y = Mathf.Clamp(pos.y, Mathf.Min(area.yMax - screenMargin, area.yMin + screenMargin + size.y), area.yMax - screenMargin);

            _panel.anchoredPosition = pos;
        }
    }
}
