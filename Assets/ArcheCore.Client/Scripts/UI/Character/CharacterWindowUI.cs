using System.Text;
using ArcheCore.Client.GameData;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.Interfaces;
using ArcheCore.Client.UI.State;
using ArcheCore.Network.Shared.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// The character window (C): what you're wearing and every stat
    /// (roadmaps 3.1 and 3.2).
    ///
    ///   Left   ten equipment slots. Hover for the item; click to take it
    ///          off (it goes to your first free bag slot). Gear goes ON with
    ///          a right-click in the bag.
    ///   Right  primary stats, then what they turn into, then resistances
    ///          and multipliers that aren't zero. A bonus from gear or a buff
    ///          shows as "base (+bonus)".
    ///
    /// Everything comes from LocalCharacterState (W2CStats, W2CEquipment),
    /// so it's current whenever it opens. Builds itself; goes through
    /// WorldUIManager, so Escape closes it.
    /// </summary>
    public class CharacterWindowUI : MonoBehaviour, IUIPanel
    {
        private static CharacterWindowUI _instance;

        [SerializeField] private Vector2 size = new Vector2(520f, 430f);
        [SerializeField] private float slotSize = 44f;

        private static readonly EquipSlot[] LeftColumn  = { EquipSlot.Head, EquipSlot.Neck, EquipSlot.Chest, EquipSlot.Hands, EquipSlot.Legs };
        private static readonly EquipSlot[] RightColumn = { EquipSlot.Feet, EquipSlot.MainHand, EquipSlot.OffHand, EquipSlot.Ring, EquipSlot.Trinket };

        private sealed class SlotView
        {
            public EquipSlot Slot;
            public Image Picture;
            public TMP_Text Label;
            public int ItemId;
        }

        private RectTransform _layer, _window;
        private TMP_Text _title, _stats;
        private readonly SlotView[] _slots = new SlotView[EquipSlots.Count];

        public bool IsVisible => _window != null && _window.gameObject.activeSelf;

        public static void Toggle()
        {
            if (_instance == null && !Create())
                return;

            if (WorldUIManager.Instance != null) WorldUIManager.Instance.Toggle(_instance);
            else if (_instance.IsVisible) _instance.Hide();
            else _instance.Show();
        }

        private static bool Create()
        {
            var layer = RuntimeUI.CreateLayer("CharacterWindow", 60, clickable: true);
            if (layer == null)
                return false;

            _instance = layer.gameObject.AddComponent<CharacterWindowUI>();
            _instance.Build(layer);
            return true;
        }

        private void OnEnable()
        {
            PlayerStatEvents.OnStatsChanged += Refresh;
            PlayerStatEvents.OnEquipmentChanged += Refresh;
            PlayerStatEvents.OnLevelChanged += OnLevel;
        }

        private void OnDisable()
        {
            PlayerStatEvents.OnStatsChanged -= Refresh;
            PlayerStatEvents.OnEquipmentChanged -= Refresh;
            PlayerStatEvents.OnLevelChanged -= OnLevel;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void OnLevel(int _) => Refresh();

        // ── IUIPanel ─────────────────────────────────────────────────

        public void Show()
        {
            if (_window == null) return;
            _window.gameObject.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            if (_window == null) return;
            _window.gameObject.SetActive(false);
            ItemTooltipUI.Hide(this);
        }

        // ── Building ─────────────────────────────────────────────────

        private void Build(RectTransform layer)
        {
            _layer = layer;

            var window = RuntimeUI.NewPanel("Window", layer, raycast: true);
            _window = window.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-220f, 20f);
            _window.sizeDelta = size;

            _title = RuntimeUI.NewText("Title", _window, 16f, RuntimeUI.Gold, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var tr = _title.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(14f, -34f); tr.offsetMax = new Vector2(-40f, -8f);

            var close = RuntimeUI.NewText("Close", _window, 16f, RuntimeUI.Muted, FontStyles.Bold);
            close.raycastTarget = true;
            var cr = close.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(1f, 1f);
            cr.anchoredPosition = new Vector2(-8f, -6f);
            cr.sizeDelta = new Vector2(26f, 26f);
            close.text = "X";
            close.gameObject.AddComponent<UIHover>().OnClick = _ => Close();

            // Equipment: two columns of slots down the left.
            var gear = RuntimeUI.NewImage("Equipment", _window, RuntimeUI.Inset);
            var gr = gear.rectTransform;
            gr.anchorMin = new Vector2(0f, 0f); gr.anchorMax = new Vector2(0f, 1f);
            gr.pivot = new Vector2(0f, 0.5f);
            gr.offsetMin = new Vector2(12f, 12f); gr.offsetMax = new Vector2(12f + 230f, -44f);

            for (int i = 0; i < LeftColumn.Length; i++)
                AddSlot(gr, LeftColumn[i], new Vector2(8f, -8f - i * (slotSize + 22f)), leftAligned: true);
            for (int i = 0; i < RightColumn.Length; i++)
                AddSlot(gr, RightColumn[i], new Vector2(-8f, -8f - i * (slotSize + 22f)), leftAligned: false);

            // Stats down the right.
            var statsBack = RuntimeUI.NewImage("Stats", _window, RuntimeUI.Inset);
            var sr = statsBack.rectTransform;
            sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 1f);
            sr.offsetMin = new Vector2(254f, 12f); sr.offsetMax = new Vector2(-12f, -44f);

            _stats = RuntimeUI.NewText("StatsText", sr, 12f, RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            var st = _stats.rectTransform;
            st.anchorMin = Vector2.zero; st.anchorMax = Vector2.one;
            st.offsetMin = new Vector2(10f, 8f); st.offsetMax = new Vector2(-10f, -8f);
            _stats.textWrappingMode = TextWrappingModes.Normal;

            _window.gameObject.SetActive(false);
        }

        private void AddSlot(RectTransform parent, EquipSlot slot, Vector2 offset, bool leftAligned)
        {
            var view = new SlotView { Slot = slot };

            var frame = RuntimeUI.NewPanel($"Slot_{slot}", parent, raycast: true);
            var fr = frame.rectTransform;
            fr.anchorMin = fr.anchorMax = leftAligned ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            fr.pivot = leftAligned ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            fr.anchoredPosition = offset;
            fr.sizeDelta = new Vector2(slotSize, slotSize);

            view.Picture = RuntimeUI.NewImage("Icon", fr, Color.white);
            var pr = view.Picture.rectTransform;
            pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
            pr.offsetMin = new Vector2(3f, 3f); pr.offsetMax = new Vector2(-3f, -3f);
            view.Picture.preserveAspect = true;

            view.Label = RuntimeUI.NewText("Label", parent, 10f, RuntimeUI.Muted, FontStyles.Normal,
                                           leftAligned ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight);
            var lr = view.Label.rectTransform;
            lr.anchorMin = lr.anchorMax = fr.anchorMin;
            lr.pivot = fr.pivot;
            lr.anchoredPosition = offset + new Vector2(0f, -slotSize - 2f);
            lr.sizeDelta = new Vector2(100f, 16f);

            var hover = frame.gameObject.AddComponent<UIHover>();
            hover.OnEnter = () =>
            {
                if (view.ItemId > 0)
                {
                    var worn = LocalCharacterState.Worn((int)slot);
                    ItemTooltipUI.Show(this, view.ItemId, 1, "Click to take it off", bound: worn != null && worn.Bound);
                }
                else
                {
                    TextTooltipUI.Show(view, GearCatalog.SlotName((int)slot), "Empty. Right-click gear in your bag to wear it.");
                }
            };
            hover.OnExit = () =>
            {
                ItemTooltipUI.Hide(this);
                TextTooltipUI.Hide(view);
            };
            hover.OnClick = _ =>
            {
                if (view.ItemId <= 0) return;
                var peer = ClientNetwork.Instance?.ServerPeer;
                if (peer != null) C2WUnequipPacketSender.Send(peer, (int)slot);
                ItemTooltipUI.Hide(this);
            };

            _slots[(int)slot] = view;
        }

        private void Close()
        {
            if (WorldUIManager.Instance != null) WorldUIManager.Instance.Close(this);
            else Hide();
        }

        // ── Contents ─────────────────────────────────────────────────

        private void Refresh()
        {
            if (!IsVisible)
                return;

            _title.text = $"{RichText.Safe(LocalCharacterState.Name)}  <size=75%><color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Muted)}>Level {LocalCharacterState.Level}</color></size>";

            foreach (var view in _slots)
            {
                if (view == null) continue;

                var worn = LocalCharacterState.Worn((int)view.Slot);
                view.ItemId = worn?.ItemTemplateId ?? 0;

                var sprite = view.ItemId > 0 ? ItemIcons.Get(view.ItemId) : null;
                view.Picture.sprite = sprite;
                view.Picture.enabled = sprite != null;

                string name = view.ItemId > 0 ? ItemNames.Get(view.ItemId) : null;
                view.Label.text = name ?? GearCatalog.SlotName((int)view.Slot);
                view.Label.color = name != null ? RuntimeUI.Text : RuntimeUI.Muted;
            }

            _stats.text = BuildStats();
        }

        private static string BuildStats()
        {
            if (!LocalCharacterState.HasStats)
                return "Waiting for the server...";

            var sb = new StringBuilder();
            Section(sb, "Attributes");
            for (var s = StatId.Strength; s <= StatId.Spirit; s++) Row(sb, s);

            Section(sb, "Combat");
            Row(sb, StatId.MaxHealth);
            Row(sb, StatId.MaxMana);
            Row(sb, StatId.AttackPower);
            Row(sb, StatId.SpellPower);
            Row(sb, StatId.Armor);
            Row(sb, StatId.CritChance);
            Row(sb, StatId.Haste);
            Row(sb, StatId.HealthRegen);
            Row(sb, StatId.ManaRegen);

            bool header = false;
            for (var s = StatId.ResistFire; s < StatId.Count; s++)
            {
                if (Mathf.Approximately(LocalCharacterState.Stat(s), 0f)) continue;
                if (!header) { Section(sb, "Other"); header = true; }
                Row(sb, s);
            }

            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append($"<b><color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Gold)}>{title}</color></b>\n");
        }

        private static void Row(StringBuilder sb, StatId stat)
        {
            float value = LocalCharacterState.Stat(stat);
            float bonus = value - LocalCharacterState.BaseStat(stat);

            sb.Append(StatNames.Name(stat)).Append("<pos=62%>").Append(StatNames.Value(stat, value));

            if (Mathf.Abs(bonus) >= 0.05f)
            {
                var color = bonus > 0 ? new Color(0.40f, 0.85f, 0.45f) : new Color(0.85f, 0.35f, 0.30f);
                string text = StatNames.IsPercent(stat) ? $"{bonus:+0.#;-0.#}%" : $"{bonus:+0;-0}";
                sb.Append($"  <color=#{ColorUtility.ToHtmlStringRGB(color)}>({text})</color>");
            }

            sb.Append('\n');
        }
    }
}
