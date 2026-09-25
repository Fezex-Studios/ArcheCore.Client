using System;
using System.Collections.Generic;
using ArcheCore.Client.GameData;
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
    /// The auction house. Opened by an NPC's Auction action. Builds itself -
    /// no scene setup.
    ///
    ///   Search + Enter        filter by item name
    ///   Mine                  only your own listings
    ///   a row                 Buy, or Cancel when it's yours
    ///   right-click in bag    pick something to sell, then set a price and List
    ///
    /// Nothing here is authoritative. Buying sends an id and waits: the
    /// server takes the listing off the board first and only then charges
    /// anyone, so two people clicking the same row at once can't both get
    /// it. After any change the window simply asks for a fresh page rather
    /// than patching the list, which can't drift.
    /// </summary>
    public class AuctionWindowUI : MonoBehaviour, IUIPanel
    {
        public static AuctionWindowUI Instance { get; private set; }

        [SerializeField] private Vector2 size = new Vector2(480f, 380f);
        [SerializeField] private float rowHeight = 38f;

        private RectTransform _layer, _window, _rows;
        private TMP_Text _title, _empty, _sellLabel;
        private TMP_InputField _search, _price;
        private readonly List<GameObject> _rowObjects = new();

        private bool _mineOnly;
        private int _feePercent = 5;

        // What right-clicking in the bag picked, ready to list.
        private int _sellSlot = -1;
        private int _sellItemId;
        private int _sellQuantity;

        private Func<InventorySlotUI, bool> _sellInterceptor;

        public bool IsVisible => _window != null && _window.gameObject.activeSelf;

        public static void Present(W2CAuctionListPacket packet)
        {
            if (Instance == null)
            {
                var go = new GameObject("AuctionWindowUI");
                DontDestroyOnLoad(go);
                go.AddComponent<AuctionWindowUI>();
            }

            Instance.Fill(packet);
        }

        /// <summary>After a buy, cancel or listing: ask the server for the page again.</summary>
        public static void RefreshIfOpen()
        {
            if (Instance != null && Instance.IsVisible)
                Instance.Browse();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _sellInterceptor = PickForSale;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Contents ─────────────────────────────────────────────────

        private void Fill(W2CAuctionListPacket packet)
        {
            if (_layer == null && !Build())
                return;

            _mineOnly = packet.MineOnly;
            _feePercent = packet.FeePercent;

            foreach (var row in _rowObjects) Destroy(row);
            _rowObjects.Clear();

            var listings = packet.Listings ?? Array.Empty<AuctionEntryData>();
            _title.text = _mineOnly ? $"My listings ({listings.Length})" : "Auction House";
            _empty.gameObject.SetActive(listings.Length == 0);

            foreach (var listing in listings)
                AddRow(listing);

            if (!IsVisible)
            {
                if (WorldUIManager.Instance != null) WorldUIManager.Instance.Open(this);
                else Show();
            }
        }

        private void AddRow(AuctionEntryData listing)
        {
            var row = RuntimeUI.NewImage($"Listing_{listing.Id}", _rows, RuntimeUI.Inset, raycast: true);
            row.rectTransform.sizeDelta = new Vector2(0f, rowHeight);

            var icon = RuntimeUI.NewImage("Icon", row.rectTransform, Color.white);
            var ir = icon.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = new Vector2(4f, 0f);
            ir.sizeDelta = new Vector2(rowHeight - 8f, rowHeight - 8f);
            var sprite = ItemIcons.Get(listing.ItemTemplateId);
            icon.sprite = sprite; icon.preserveAspect = true; icon.enabled = sprite != null;

            var text = RuntimeUI.NewText("Label", row.rectTransform, 12f, RuntimeUI.Text,
                                         FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(rowHeight, 0f); tr.offsetMax = new Vector2(-96f, 0f);

            string quantity = listing.Quantity > 1 ? $" x{listing.Quantity}" : "";
            text.text = $"{listing.ItemName}{quantity}   " +
                        $"<color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Gold)}>{listing.Price}g</color>\n" +
                        $"<size=85%><color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Muted)}>" +
                        $"{listing.SellerName} · {listing.MinutesLeft}m left</color></size>";

            var action = RuntimeUI.NewPanel("Action", row.rectTransform, raycast: true);
            var ar = action.rectTransform;
            ar.anchorMin = ar.anchorMax = new Vector2(1f, 0.5f); ar.pivot = new Vector2(1f, 0.5f);
            ar.anchoredPosition = new Vector2(-6f, 0f);
            ar.sizeDelta = new Vector2(80f, rowHeight - 10f);
            action.color = listing.Mine ? new Color(0.36f, 0.18f, 0.14f, 1f) : new Color(0.42f, 0.30f, 0.14f, 1f);

            var label = RuntimeUI.NewText("Label", ar, 12f, RuntimeUI.Gold, FontStyles.Bold);
            RuntimeUI.Stretch(label.rectTransform);
            label.text = listing.Mine ? "Cancel" : "Buy";

            long id = listing.Id;
            bool mine = listing.Mine;
            var button = action.gameObject.AddComponent<Button>();
            button.targetGraphic = action;
            button.onClick.AddListener(() => Act(id, mine, listing));

            _rowObjects.Add(row.gameObject);
        }

        private void Act(long auctionId, bool mine, AuctionEntryData listing)
        {
            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer == null) return;

            if (mine)
            {
                C2WAuctionPacketSender.Cancel(peer, auctionId);
                return;
            }

            // Spending real gold asks first, like destroying an item does.
            ConfirmDialog.Ask(
                "Buy item?",
                $"Buy {listing.Quantity}x {listing.ItemName} for {listing.Price}g?",
                () =>
                {
                    var current = ClientNetwork.Instance?.ServerPeer;
                    if (current != null)
                        C2WAuctionPacketSender.Buy(current, auctionId);
                },
                confirmText: "Buy");
        }

        // ── Selling ──────────────────────────────────────────────────

        protected void OnEnable() { }

        /// <summary>
        /// Right-click in the bag while this is open: pick that stack to
        /// sell. It isn't listed until a price is set and List is pressed -
        /// that's when the server takes it.
        /// </summary>
        private bool PickForSale(InventorySlotUI slot)
        {
            if (!IsVisible)
                return false;   // not our click - the item gets used as normal

            if (slot.IsEmpty)
                return true;

            _sellSlot = slot.Index;
            _sellItemId = slot.ItemTemplateId;
            _sellQuantity = slot.Quantity;

            UpdateSellLabel();
            return true;
        }

        private void UpdateSellLabel()
        {
            if (_sellLabel == null) return;

            if (_sellSlot < 0)
            {
                _sellLabel.text = $"<color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Muted)}>Right-click an item in your bag to sell it</color>";
                return;
            }

            string name = ItemNames.Get(_sellItemId);
            string quantity = _sellQuantity > 1 ? $" x{_sellQuantity}" : "";
            _sellLabel.text = $"Selling: <b>{name}{quantity}</b>  " +
                              $"<size=85%><color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Muted)}>" +
                              $"{_feePercent}% fee on sale</color></size>";
        }

        private void List()
        {
            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer == null) return;

            if (_sellSlot < 0)
            {
                HudMessageDisplay.QueueOrShow("Right-click something in your bag first.");
                return;
            }

            if (!int.TryParse(_price.text, out int price) || price < 1)
            {
                HudMessageDisplay.QueueOrShow("Name a price in gold.");
                return;
            }

            C2WAuctionPacketSender.Create(peer, _sellSlot, _sellQuantity, price);

            _sellSlot = -1;
            _sellItemId = 0;
            _sellQuantity = 0;
            _price.text = "";
            UpdateSellLabel();
        }

        // ── Browsing ─────────────────────────────────────────────────

        private void Browse()
        {
            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer != null)
                C2WAuctionPacketSender.Browse(peer, _search != null ? _search.text : null, _mineOnly);
        }

        private void ToggleMine()
        {
            _mineOnly = !_mineOnly;
            Browse();
        }

        // ── IUIPanel ─────────────────────────────────────────────────

        public void Show()
        {
            if (_window != null) _window.gameObject.SetActive(true);

            // Right-click in the bag now means "pick this to sell".
            InventoryPanelUI.RightClickInterceptor = _sellInterceptor;
        }

        public void Hide()
        {
            if (_window != null) _window.gameObject.SetActive(false);

            if (InventoryPanelUI.RightClickInterceptor == _sellInterceptor)
                InventoryPanelUI.RightClickInterceptor = null;

            _sellSlot = -1;
            UpdateSellLabel();
        }

        private void Close()
        {
            if (WorldUIManager.Instance != null) WorldUIManager.Instance.Close(this);
            else Hide();
        }

        // ── Building ─────────────────────────────────────────────────

        private bool Build()
        {
            _layer = RuntimeUI.CreateLayer("AuctionWindow", 150, clickable: true);
            if (_layer == null) return false;

            var panel = RuntimeUI.NewPanel("Window", _layer, raycast: true);
            _window = panel.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(120f, 0f);
            _window.sizeDelta = size;

            _title = RuntimeUI.NewText("Title", _window, 16f, RuntimeUI.Gold, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var tr = _title.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(14f, -34f); tr.offsetMax = new Vector2(-40f, -8f);
            _title.text = "Auction House";

            var close = RuntimeUI.NewImage("Close", _window, new Color(0, 0, 0, 0), raycast: true);
            var cr = close.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 1f); cr.pivot = new Vector2(1f, 1f);
            cr.anchoredPosition = new Vector2(-6f, -6f); cr.sizeDelta = new Vector2(28f, 28f);
            var x = RuntimeUI.NewText("X", cr, 18f, RuntimeUI.Muted, FontStyles.Bold);
            RuntimeUI.Stretch(x.rectTransform); x.text = "X";
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = close;
            closeButton.onClick.AddListener(Close);

            _search = BuildInput("Search", new Vector2(14f, -64f), new Vector2(240f, 26f), "Search...");
            _search.onSubmit.AddListener(_ => Browse());

            var mine = RuntimeUI.NewPanel("Mine", _window, raycast: true);
            var mr = mine.rectTransform;
            mr.anchorMin = mr.anchorMax = new Vector2(0f, 1f); mr.pivot = new Vector2(0f, 1f);
            mr.anchoredPosition = new Vector2(264f, -64f); mr.sizeDelta = new Vector2(90f, 26f);
            mine.color = new Color(0.42f, 0.30f, 0.14f, 1f);
            var mineLabel = RuntimeUI.NewText("Label", mr, 12f, RuntimeUI.Gold, FontStyles.Bold);
            RuntimeUI.Stretch(mineLabel.rectTransform); mineLabel.text = "Mine";
            var mineButton = mine.gameObject.AddComponent<Button>();
            mineButton.targetGraphic = mine;
            mineButton.onClick.AddListener(ToggleMine);

            _rows = RuntimeUI.NewRect("Rows", _window);
            _rows.anchorMin = Vector2.zero; _rows.anchorMax = Vector2.one;
            _rows.offsetMin = new Vector2(12f, 78f); _rows.offsetMax = new Vector2(-12f, -96f);
            var layout = _rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f; layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true; layout.childControlHeight = false;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

            _empty = RuntimeUI.NewText("Empty", _window, 13f, RuntimeUI.Muted);
            RuntimeUI.Stretch(_empty.rectTransform);
            _empty.text = "Nothing listed.";

            // Sell strip along the bottom.
            _sellLabel = RuntimeUI.NewText("SellLabel", _window, 12f, RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            var sr = _sellLabel.rectTransform;
            sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0f); sr.pivot = new Vector2(0.5f, 0f);
            sr.offsetMin = new Vector2(14f, 44f); sr.offsetMax = new Vector2(-14f, 68f);
            UpdateSellLabel();

            _price = BuildInput("Price", new Vector2(14f, 12f), new Vector2(140f, 28f), "Price in gold", bottom: true);

            var list = RuntimeUI.NewPanel("List", _window, raycast: true);
            var lr = list.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0f, 0f); lr.pivot = new Vector2(0f, 0f);
            lr.anchoredPosition = new Vector2(164f, 12f); lr.sizeDelta = new Vector2(100f, 28f);
            list.color = new Color(0.42f, 0.30f, 0.14f, 1f);
            var listLabel = RuntimeUI.NewText("Label", lr, 13f, RuntimeUI.Gold, FontStyles.Bold);
            RuntimeUI.Stretch(listLabel.rectTransform); listLabel.text = "List";
            var listButton = list.gameObject.AddComponent<Button>();
            listButton.targetGraphic = list;
            listButton.onClick.AddListener(List);

            _window.gameObject.SetActive(false);
            return true;
        }

        /// <summary>A working TMP input field, built the way TMP needs: viewport, text, placeholder.</summary>
        private TMP_InputField BuildInput(string name, Vector2 position, Vector2 fieldSize, string placeholder, bool bottom = false)
        {
            var background = RuntimeUI.NewImage(name, _window, RuntimeUI.Inset, raycast: true);
            var rt = background.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, bottom ? 0f : 1f);
            rt.pivot = new Vector2(0f, bottom ? 0f : 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = fieldSize;

            var viewport = RuntimeUI.NewRect("Viewport", rt);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(8f, 2f); viewport.offsetMax = new Vector2(-8f, -2f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = RuntimeUI.NewText("Text", viewport, 12f, RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            RuntimeUI.Stretch(text.rectTransform);

            var hint = RuntimeUI.NewText("Placeholder", viewport, 12f, RuntimeUI.Muted, FontStyles.Italic, TextAlignmentOptions.MidlineLeft);
            RuntimeUI.Stretch(hint.rectTransform);
            hint.text = placeholder;

            var input = background.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = viewport;
            input.textComponent = (TextMeshProUGUI)text;
            input.placeholder = hint;
            input.targetGraphic = background;
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (bottom) input.contentType = TMP_InputField.ContentType.IntegerNumber;

            return input;
        }
    }
}
