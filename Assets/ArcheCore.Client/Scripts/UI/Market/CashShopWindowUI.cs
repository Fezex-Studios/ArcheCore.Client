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
    /// The cash shop. Opened by an NPC's Cash Shop action, or by calling
    /// Open() from your HUD's Cash Shop button. Builds itself.
    ///
    /// Everything here is priced in CREDITS, which are bought outside the game
    /// and are not gold. Purchases are charged and delivered by the
    /// persistence server in one transaction, and the goods arrive in the
    /// MAILBOX - which is also why buying with a full bag is never
    /// a problem.
    ///
    /// GIFTING: items the server has flagged giftable get a Gift button next
    /// to Buy. Type a character's name in the "Gift to" box, press Gift, and
    /// it's charged to you and lands in THEIR mailbox from your name. The
    /// button is a courtesy - the server refuses non-giftable items anyway.
    ///
    /// Nothing in this window decides anything: it shows what the server
    /// sent and asks. The balance shown is the server's number.
    /// </summary>
    public class CashShopWindowUI : MonoBehaviour, IUIPanel
    {
        public static CashShopWindowUI Instance { get; private set; }

        [SerializeField] private Vector2 size = new Vector2(440f, 380f);
        [SerializeField] private float rowHeight = 44f;

        private RectTransform _layer, _window, _rows;
        private TMP_Text _title, _balance, _empty;
        private TMP_InputField _giftTo;
        private readonly List<GameObject> _rowObjects = new();

        public bool IsVisible => _window != null && _window.gameObject.activeSelf;

        /// <summary>Wire your HUD's Cash Shop button to this.</summary>
        public static void Open()
        {
            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer != null)
                C2WCashShopPacketSender.Browse(peer);
        }

        public static void Present(W2CCashShopListPacket packet)
        {
            if (Instance == null)
            {
                var go = new GameObject("CashShopWindowUI");
                DontDestroyOnLoad(go);
                go.AddComponent<CashShopWindowUI>();
            }

            Instance.Fill(packet);
        }

        public static void RefreshIfOpen()
        {
            if (Instance != null && Instance.IsVisible)
                Open();
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

        private void Fill(W2CCashShopListPacket packet)
        {
            if (_layer == null && !Build())
                return;

            foreach (var row in _rowObjects) Destroy(row);
            _rowObjects.Clear();

            var items = packet.Items ?? Array.Empty<CashShopEntryData>();
            _balance.text = $"Balance: <color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Gold)}>{packet.Balance} credits</color>";
            _empty.gameObject.SetActive(items.Length == 0);

            string category = null;
            foreach (var item in items)
            {
                // A heading each time the category changes - the server sends
                // them in sort order, so this needs no grouping logic.
                if (!string.IsNullOrEmpty(item.Category) && item.Category != category)
                {
                    category = item.Category;
                    var heading = RuntimeUI.NewText($"Category_{category}", _rows, 11f, RuntimeUI.Muted,
                                                    FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                    heading.rectTransform.sizeDelta = new Vector2(0f, 18f);
                    heading.text = category.ToUpperInvariant();
                    _rowObjects.Add(heading.gameObject);
                }

                AddRow(item, packet.Balance);
            }

            if (!IsVisible)
            {
                if (WorldUIManager.Instance != null) WorldUIManager.Instance.Open(this);
                else Show();
            }
        }

        private void AddRow(CashShopEntryData item, int balance)
        {
            var row = RuntimeUI.NewImage($"Item_{item.Id}", _rows, RuntimeUI.Inset, raycast: true);
            row.rectTransform.sizeDelta = new Vector2(0f, rowHeight);

            var icon = RuntimeUI.NewImage("Icon", row.rectTransform, Color.white);
            var ir = icon.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = new Vector2(5f, 0f);
            ir.sizeDelta = new Vector2(rowHeight - 10f, rowHeight - 10f);
            var sprite = ItemIcons.Get(item.ItemTemplateId);
            icon.sprite = sprite; icon.preserveAspect = true; icon.enabled = sprite != null;

            var text = RuntimeUI.NewText("Label", row.rectTransform, 12f, RuntimeUI.Text,
                                         FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(rowHeight, 0f); tr.offsetMax = new Vector2(item.IsGiftable ? -166f : -96f, 0f);

            string quantity = item.Quantity > 1 ? $" x{item.Quantity}" : "";
            text.text = $"{item.DisplayName}{quantity}\n" +
                        $"<size=85%><color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Gold)}>{item.PriceCredits} credits</color></size>";

            var buy = RuntimeUI.NewPanel("Buy", row.rectTransform, raycast: true);
            var br = buy.rectTransform;
            br.anchorMin = br.anchorMax = new Vector2(1f, 0.5f); br.pivot = new Vector2(1f, 0.5f);
            br.anchoredPosition = new Vector2(-6f, 0f);
            br.sizeDelta = new Vector2(80f, rowHeight - 12f);

            bool affordable = balance >= item.PriceCredits;
            buy.color = affordable ? new Color(0.42f, 0.30f, 0.14f, 1f) : new Color(0.22f, 0.18f, 0.13f, 1f);

            var label = RuntimeUI.NewText("Label", br, 12f, affordable ? RuntimeUI.Gold : RuntimeUI.Muted, FontStyles.Bold);
            RuntimeUI.Stretch(label.rectTransform);
            label.text = "Buy";

            var button = buy.gameObject.AddComponent<Button>();
            button.targetGraphic = buy;
            button.interactable = affordable;
            button.onClick.AddListener(() => Confirm(item));

            // Only items flagged giftable get a Gift button.
            if (item.IsGiftable)
            {
                var gift = RuntimeUI.NewPanel("Gift", row.rectTransform, raycast: true);
                var gr = gift.rectTransform;
                gr.anchorMin = gr.anchorMax = new Vector2(1f, 0.5f); gr.pivot = new Vector2(1f, 0.5f);
                gr.anchoredPosition = new Vector2(-92f, 0f);
                gr.sizeDelta = new Vector2(62f, rowHeight - 12f);
                gift.color = affordable ? new Color(0.24f, 0.32f, 0.20f, 1f) : new Color(0.18f, 0.20f, 0.16f, 1f);

                var giftLabel = RuntimeUI.NewText("Label", gr, 12f, affordable ? RuntimeUI.Gold : RuntimeUI.Muted, FontStyles.Bold);
                RuntimeUI.Stretch(giftLabel.rectTransform);
                giftLabel.text = "Gift";

                var giftButton = gift.gameObject.AddComponent<Button>();
                giftButton.targetGraphic = gift;
                giftButton.interactable = affordable;
                giftButton.onClick.AddListener(() => ConfirmGift(item));
            }

            _rowObjects.Add(row.gameObject);
        }

        private void ConfirmGift(CashShopEntryData item)
        {
            string recipient = _giftTo != null ? _giftTo.text.Trim() : string.Empty;

            if (recipient.Length == 0)
            {
                HudMessageDisplay.QueueOrShow("Type a character's name in the \"Gift to\" box first.");
                return;
            }

            ConfirmDialog.Ask(
                "Send a gift?",
                $"Send {item.DisplayName} to {recipient} for {item.PriceCredits} credits? " +
                "It will be charged to you and arrive in their mailbox.",
                () =>
                {
                    var peer = ClientNetwork.Instance?.ServerPeer;
                    if (peer != null)
                        C2WCashShopPacketSender.Gift(peer, item.Id, recipient);
                },
                confirmText: "Send");
        }

        private static void Confirm(CashShopEntryData item)
        {
            // Real money asks first.
            ConfirmDialog.Ask(
                "Buy from the cash shop?",
                $"Buy {item.DisplayName} for {item.PriceCredits} credits? It will arrive in your mailbox.",
                () =>
                {
                    var peer = ClientNetwork.Instance?.ServerPeer;
                    if (peer != null)
                        C2WCashShopPacketSender.Buy(peer, item.Id);
                },
                confirmText: "Buy");
        }

        public void Show() { if (_window != null) _window.gameObject.SetActive(true); }
        public void Hide() { if (_window != null) _window.gameObject.SetActive(false); }

        private void Close()
        {
            if (WorldUIManager.Instance != null) WorldUIManager.Instance.Close(this);
            else Hide();
        }

        private bool Build()
        {
            _layer = RuntimeUI.CreateLayer("CashShopWindow", 150, clickable: true);
            if (_layer == null) return false;

            var panel = RuntimeUI.NewPanel("Window", _layer, raycast: true);
            _window = panel.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = size;

            _title = RuntimeUI.NewText("Title", _window, 16f, RuntimeUI.Gold, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var tr = _title.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(14f, -34f); tr.offsetMax = new Vector2(-40f, -8f);
            _title.text = "Cash Shop";

            _balance = RuntimeUI.NewText("Balance", _window, 12f, RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            var bl = _balance.rectTransform;
            bl.anchorMin = new Vector2(0f, 1f); bl.anchorMax = new Vector2(1f, 1f); bl.pivot = new Vector2(0.5f, 1f);
            bl.offsetMin = new Vector2(14f, -58f); bl.offsetMax = new Vector2(-14f, -36f);

            var close = RuntimeUI.NewImage("Close", _window, new Color(0, 0, 0, 0), raycast: true);
            var cr = close.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 1f); cr.pivot = new Vector2(1f, 1f);
            cr.anchoredPosition = new Vector2(-6f, -6f); cr.sizeDelta = new Vector2(28f, 28f);
            var x = RuntimeUI.NewText("X", cr, 18f, RuntimeUI.Muted, FontStyles.Bold);
            RuntimeUI.Stretch(x.rectTransform); x.text = "X";
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = close;
            closeButton.onClick.AddListener(Close);

            _rows = RuntimeUI.NewRect("Rows", _window);
            _rows.anchorMin = Vector2.zero; _rows.anchorMax = Vector2.one;
            _rows.offsetMin = new Vector2(12f, 52f); _rows.offsetMax = new Vector2(-12f, -62f);
            var layout = _rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f; layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true; layout.childControlHeight = false;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

            _empty = RuntimeUI.NewText("Empty", _window, 13f, RuntimeUI.Muted);
            RuntimeUI.Stretch(_empty.rectTransform);
            _empty.text = "Nothing for sale.";

            // "Gift to: [ name ]" - used by the Gift button on giftable items.
            var giftLabel = RuntimeUI.NewText("GiftToLabel", _window, 12f, RuntimeUI.Muted, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var gl = giftLabel.rectTransform;
            gl.anchorMin = gl.anchorMax = new Vector2(0f, 0f); gl.pivot = new Vector2(0f, 0f);
            gl.anchoredPosition = new Vector2(14f, 12f); gl.sizeDelta = new Vector2(60f, 28f);
            giftLabel.text = "Gift to:";

            _giftTo = BuildTextInput("GiftTo", new Vector2(76f, 12f), new Vector2(220f, 28f), "Character name");
            _giftTo.characterLimit = 64;

            _window.gameObject.SetActive(false);
            return true;
        }

        /// <summary>A working TMP text field, built the way TMP needs: viewport, text, placeholder. Anchored bottom-left.</summary>
        private TMP_InputField BuildTextInput(string name, Vector2 position, Vector2 fieldSize, string placeholder)
        {
            var background = RuntimeUI.NewImage(name, _window, RuntimeUI.Inset, raycast: true);
            var rt = background.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
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

            return input;
        }
    }
}
