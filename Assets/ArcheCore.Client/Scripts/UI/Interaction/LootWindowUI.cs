using System.Collections.Generic;
using ArchCore.Client;
using ArcheCore.Client.GameData;
using ArcheCore.Client.Gameplay.Combat;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI.Interfaces;
using ArcheCore.Network.Shared.Packets.W2C;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// The Loot window, opened by a corpse's "Obtain loot" action (G, or
    /// right-click). Builds itself the first time it's needed.
    ///
    ///   click a row   take just that (the gold row takes the gold)
    ///   Take All      everything that fits - same as the F action
    ///   X / Escape    close
    ///
    /// Shows only what the server sent: every take is a request, and the
    /// server answers with a fresh W2CLootWindow (or, once empty, despawns the
    /// corpse - which closes this). Also closes itself if you walk away.
    ///
    /// An IUIPanel, so WorldUIManager's Escape and input blocking apply.
    /// </summary>
    public class LootWindowUI : MonoBehaviour, IUIPanel
    {
        public static LootWindowUI Instance { get; private set; }

        [SerializeField] private Vector2 size = new Vector2(280f, 320f);
        [SerializeField] private float rowHeight = 40f;
        [SerializeField] private float walkAwayDistance = 6f;

        private RectTransform _layer, _window, _rows;
        private TMP_Text _title;
        private readonly List<GameObject> _rowObjects = new();
        private int _corpseId;
        private PlayerInteraction _pointer;

        public bool IsVisible => _window != null && _window.gameObject.activeSelf;

        // ── Opening ──────────────────────────────────────────────────

        public static void Present(W2CLootWindowPacket packet)
        {
            if (Instance == null)
            {
                var go = new GameObject("LootWindowUI");
                DontDestroyOnLoad(go);
                go.AddComponent<LootWindowUI>();
            }

            Instance.Fill(packet);
        }

        /// <summary>Close if it's showing this corpse (it despawned).</summary>
        public static void CloseIf(int corpseNetworkId)
        {
            if (Instance != null && Instance._corpseId == corpseNetworkId)
                Instance.Close();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Fill(W2CLootWindowPacket p)
        {
            if (_layer == null && !Build())
                return;

            _corpseId = p.CorpseNetworkId;
            _title.text = string.IsNullOrEmpty(p.CorpseName) ? "Loot" : $"Loot - {p.CorpseName}";

            foreach (var r in _rowObjects) Destroy(r);
            _rowObjects.Clear();

            if (p.Gold > 0)
                AddRow(ItemIcons.GetByName("Actions/action_trade"), $"<color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Gold)}>{p.Gold}g</color>", 0);

            if (p.Items != null)
                foreach (var item in p.Items)
                    AddRow(ItemIcons.Get(item.ItemTemplateId), item.Quantity > 1 ? $"{item.ItemName} x{item.Quantity}" : item.ItemName, item.ItemTemplateId);

            if (!IsVisible)
            {
                if (WorldUIManager.Instance != null) WorldUIManager.Instance.Open(this);
                else Show();
            }
        }

        // ── IUIPanel ─────────────────────────────────────────────────

        public void Show()
        {
            if (_window != null) _window.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_window != null) _window.gameObject.SetActive(false);
            _corpseId = 0;
            ItemTooltipUI.Hide();
        }

        private void Close()
        {
            if (WorldUIManager.Instance != null) WorldUIManager.Instance.Close(this);
            else Hide();
        }

        // ── Walk-away ────────────────────────────────────────────────

        private void Update()
        {
            if (!IsVisible || _corpseId == 0)
                return;

            if (_pointer == null) _pointer = FindFirstObjectByType<PlayerInteraction>();

            if (!CorpseRegistry.TryGet(_corpseId, out var corpse) || corpse == null)
            {
                Close();
                return;
            }

            if (_pointer != null && Vector3.Distance(_pointer.transform.position, corpse.transform.position) > walkAwayDistance)
                Close();
        }

        // ── Building ─────────────────────────────────────────────────

        private bool Build()
        {
            _layer = RuntimeUI.CreateLayer("LootWindow", 150, clickable: true);   // among the windows
            if (_layer == null) return false;

            var panel = RuntimeUI.NewPanel("Window", _layer, raycast: true);
            _window = panel.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-170f, 20f);
            _window.sizeDelta = size;

            _title = RuntimeUI.NewText("Title", _window, 16f, RuntimeUI.Gold, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            Place(_title.rectTransform, top: 8f, height: 26f, left: 14f, right: 40f);

            var close = RuntimeUI.NewImage("Close", _window, new Color(0, 0, 0, 0), raycast: true);
            var cr = close.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 1f); cr.pivot = new Vector2(1f, 1f);
            cr.anchoredPosition = new Vector2(-6f, -6f); cr.sizeDelta = new Vector2(28f, 28f);
            var x = RuntimeUI.NewText("X", cr, 18f, RuntimeUI.Muted, FontStyles.Bold);
            RuntimeUI.Stretch(x.rectTransform); x.text = "X";
            close.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            _rows = RuntimeUI.NewRect("Rows", _window);
            _rows.anchorMin = new Vector2(0f, 0f); _rows.anchorMax = new Vector2(1f, 1f);
            _rows.offsetMin = new Vector2(12f, 52f); _rows.offsetMax = new Vector2(-12f, -42f);
            var v = _rows.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 4f; v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true; v.childControlHeight = false;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var takeAll = RuntimeUI.NewPanel("TakeAll", _window, raycast: true);
            var tr = takeAll.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f); tr.pivot = new Vector2(0.5f, 0f);
            tr.anchoredPosition = new Vector2(0f, 12f); tr.sizeDelta = new Vector2(110f, 30f);
            takeAll.color = new Color(0.42f, 0.30f, 0.14f, 1f);
            var tl = RuntimeUI.NewText("Label", tr, 13f, RuntimeUI.Gold, FontStyles.Bold);
            RuntimeUI.Stretch(tl.rectTransform); tl.text = "Take All";
            var tb = takeAll.gameObject.AddComponent<Button>();
            tb.targetGraphic = takeAll;
            tb.onClick.AddListener(TakeAll);

            _window.gameObject.SetActive(false);
            return true;
        }

        private void AddRow(Sprite icon, string label, int itemTemplateId)
        {
            var row = RuntimeUI.NewImage("Row", _rows, RuntimeUI.Inset, raycast: true);
            var rr = row.rectTransform;
            rr.sizeDelta = new Vector2(0f, rowHeight);

            var ic = RuntimeUI.NewImage("Icon", rr, Color.white);
            var ir = ic.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = new Vector2(4f, 0f); ir.sizeDelta = new Vector2(rowHeight - 8f, rowHeight - 8f);
            ic.sprite = icon; ic.preserveAspect = true; ic.enabled = icon != null;

            var t = RuntimeUI.NewText("Label", rr, 13f, RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            var trr = t.rectTransform;
            trr.anchorMin = new Vector2(0f, 0f); trr.anchorMax = new Vector2(1f, 1f);
            trr.offsetMin = new Vector2(rowHeight + 2f, 0f); trr.offsetMax = new Vector2(-6f, 0f);
            t.text = label;

            int corpse = _corpseId;
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = row;   // not set automatically outside the Editor
            var colors = button.colors;
            colors.highlightedColor = new Color(1.4f, 1.3f, 1.1f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => Take(corpse, itemTemplateId));

            _rowObjects.Add(row.gameObject);
        }

        private static void Place(RectTransform rt, float top, float height, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -top - height); rt.offsetMax = new Vector2(-right, -top);
        }

        // ── Requests ─────────────────────────────────────────────────

        private static void Take(int corpseId, int itemTemplateId)
        {
            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer != null)
                C2WLootTakePacketSender.Send(peer, corpseId, itemTemplateId);
        }

        private void TakeAll()
        {
            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer != null && _corpseId != 0)
                C2WInteractPacketSender.Send(peer, _corpseId, (int)InteractionActionType.LootAll);
        }
    }
}
