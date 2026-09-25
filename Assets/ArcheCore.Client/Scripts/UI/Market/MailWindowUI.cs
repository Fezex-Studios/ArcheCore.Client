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
    /// Your mailbox. ONE list for everything you're owed: auction proceeds,
    /// things you bought, listings that didn't sell, cash shop purchases,
    /// gifts from the team. Each row says who it's from and what it holds.
    ///
    /// Click a row to take it, or Take All. The server deletes the mail
    /// before anything is handed over, so a double click can't pay twice,
    /// and anything that doesn't fit is posted straight back rather than
    /// lost.
    /// </summary>
    public class MailWindowUI : MonoBehaviour, IUIPanel
    {
        public static MailWindowUI Instance { get; private set; }

        [SerializeField] private Vector2 size = new Vector2(360f, 340f);
        [SerializeField] private float rowHeight = 44f;

        private RectTransform _layer, _window, _rows;
        private TMP_Text _title, _empty;
        private readonly List<GameObject> _rowObjects = new();


        public bool IsVisible => _window != null && _window.gameObject.activeSelf;

        public static void Present(W2CMailListPacket packet)
        {
            if (Instance == null)
            {
                var go = new GameObject("MailWindowUI");
                DontDestroyOnLoad(go);
                go.AddComponent<MailWindowUI>();
            }

            Instance.Fill(packet);
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

        private void Fill(W2CMailListPacket packet)
        {
            if (_layer == null && !Build())
                return;

            foreach (var row in _rowObjects) Destroy(row);
            _rowObjects.Clear();

            var mail = packet.Mail ?? System.Array.Empty<MailEntryData>();
            _title.text = mail.Length > 0 ? $"Mailbox ({mail.Length})" : "Mailbox";
            _empty.gameObject.SetActive(mail.Length == 0);

            foreach (var entry in mail)
                AddRow(entry);

            if (!IsVisible)
            {
                if (WorldUIManager.Instance != null) WorldUIManager.Instance.Open(this);
                else Show();
            }
        }

        private void AddRow(MailEntryData entry)
        {
            var row = RuntimeUI.NewImage($"Mail_{entry.Id}", _rows, RuntimeUI.Inset, raycast: true);
            row.rectTransform.sizeDelta = new Vector2(0f, rowHeight);

            var icon = RuntimeUI.NewImage("Icon", row.rectTransform, Color.white);
            var ir = icon.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = new Vector2(5f, 0f);
            ir.sizeDelta = new Vector2(rowHeight - 10f, rowHeight - 10f);
            var sprite = entry.ItemTemplateId != 0 ? ItemIcons.Get(entry.ItemTemplateId) : ItemIcons.GetByName("Actions/action_trade");
            icon.sprite = sprite; icon.preserveAspect = true; icon.enabled = sprite != null;

            var text = RuntimeUI.NewText("Label", row.rectTransform, 12f, RuntimeUI.Text,
                                         FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(rowHeight, 0f); tr.offsetMax = new Vector2(-8f, 0f);

            var contents = new List<string>();
            if (entry.Gold > 0) contents.Add($"<color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Gold)}>{entry.Gold}g</color>");
            if (entry.ItemTemplateId != 0) contents.Add($"{entry.ItemQuantity}x {entry.ItemName}");

            text.text = $"<b>{entry.Subject}</b>  <size=90%><color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Muted)}>from {entry.Sender}</color></size>\n" +
                        string.Join(", ", contents);

            long id = entry.Id;
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = row;
            button.onClick.AddListener(() => Claim(id));

            _rowObjects.Add(row.gameObject);
        }

        private void Claim(long mailId)
        {
            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer != null)
                C2WMailPacketSender.Claim(peer, mailId);
        }

        /// <summary>Reload the list.</summary>
        public static void RefreshIfOpen()
        {
            if (Instance == null || !Instance.IsVisible)
                return;

            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer != null)
                C2WMailPacketSender.Claim(peer, -1);   // -1 = just show me, take nothing
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
            _layer = RuntimeUI.CreateLayer("MailWindow", 150, clickable: true);
            if (_layer == null) return false;

            var panel = RuntimeUI.NewPanel("Window", _layer, raycast: true);
            _window = panel.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-180f, 0f);
            _window.sizeDelta = size;

            _title = RuntimeUI.NewText("Title", _window, 16f, RuntimeUI.Gold, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var tr = _title.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(14f, -36f); tr.offsetMax = new Vector2(-40f, -8f);
            _title.text = "Mailbox";

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
            _rows.offsetMin = new Vector2(12f, 52f); _rows.offsetMax = new Vector2(-12f, -46f);
            var layout = _rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f; layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true; layout.childControlHeight = false;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

            _empty = RuntimeUI.NewText("Empty", _window, 13f, RuntimeUI.Muted);
            RuntimeUI.Stretch(_empty.rectTransform);
            _empty.text = "Nothing waiting.";

            var takeAll = RuntimeUI.NewPanel("TakeAll", _window, raycast: true);
            var br = takeAll.rectTransform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0f); br.pivot = new Vector2(0.5f, 0f);
            br.anchoredPosition = new Vector2(0f, 12f); br.sizeDelta = new Vector2(120f, 30f);
            takeAll.color = new Color(0.42f, 0.30f, 0.14f, 1f);
            var label = RuntimeUI.NewText("Label", br, 13f, RuntimeUI.Gold, FontStyles.Bold);
            RuntimeUI.Stretch(label.rectTransform); label.text = "Take All";
            var takeButton = takeAll.gameObject.AddComponent<Button>();
            takeButton.targetGraphic = takeAll;
            takeButton.onClick.AddListener(() => Claim(0));

            _window.gameObject.SetActive(false);
            return true;
        }
    }
}
