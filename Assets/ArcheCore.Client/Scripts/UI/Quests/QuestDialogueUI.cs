using System.Collections.Generic;
using ArcheCore.Client.Gameplay.Quests;
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
    /// A quest giver's window: what they can give you, what you can hand in,
    /// and what you're still working on. Opened by their Quests action (F).
    /// Builds itself - no scene setup.
    ///
    /// Pick a quest on the left, read it on the right, and the button at the
    /// bottom does the only thing that makes sense for it: Accept, Complete,
    /// or nothing at all while it's in progress.
    ///
    /// An IUIPanel, so Escape closes it like any other window.
    /// </summary>
    public class QuestDialogueUI : MonoBehaviour, IUIPanel
    {
        public static QuestDialogueUI Instance { get; private set; }

        [SerializeField] private Vector2 size = new Vector2(460f, 300f);

        private RectTransform _layer, _window, _list;
        private TMP_Text _title, _detail;
        private RectTransform _actionButton;
        private TMP_Text _actionLabel;
        private readonly List<GameObject> _rows = new();

        private int _npcNetworkId;
        private int _selected;
        private QuestStatus _selectedStatus;

        public bool IsVisible => _window != null && _window.gameObject.activeSelf;

        public static void Present(W2CQuestOffersPacket packet)
        {
            if (Instance == null)
            {
                var go = new GameObject("QuestDialogueUI");
                DontDestroyOnLoad(go);
                go.AddComponent<QuestDialogueUI>();
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

        private void Fill(W2CQuestOffersPacket packet)
        {
            if (_layer == null && !Build())
                return;

            _npcNetworkId = packet.NpcNetworkId;
            _title.text = string.IsNullOrEmpty(packet.NpcName) ? "Quests" : packet.NpcName;

            foreach (var row in _rows) Destroy(row);
            _rows.Clear();

            // Hand-ins first: finishing something beats starting something.
            AddSection("Ready to hand in", packet.Completable, QuestStatus.Complete);
            AddSection("Available", packet.Available, QuestStatus.None);
            AddSection("In progress", packet.InProgress, QuestStatus.Active);

            Select(0, QuestStatus.None);

            if (!IsVisible)
            {
                if (WorldUIManager.Instance != null) WorldUIManager.Instance.Open(this);
                else Show();
            }
        }

        private void AddSection(string heading, int[] questIds, QuestStatus status)
        {
            if (questIds == null || questIds.Length == 0)
                return;

            var label = RuntimeUI.NewText($"Heading_{heading}", _list, 11f, RuntimeUI.Muted,
                                          FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            label.rectTransform.sizeDelta = new Vector2(0f, 18f);
            label.text = heading.ToUpperInvariant();
            _rows.Add(label.gameObject);

            foreach (int questId in questIds)
            {
                string name = QuestState.TryGetDefinition(questId, out var definition) ? definition.Name : $"Quest {questId}";
                string prefix = status == QuestStatus.Complete ? "? " : status == QuestStatus.None ? "! " : "  ";

                var row = RuntimeUI.NewImage($"Quest_{questId}", _list, RuntimeUI.Inset, raycast: true);
                row.rectTransform.sizeDelta = new Vector2(0f, 26f);

                var text = RuntimeUI.NewText("Label", row.rectTransform, 13f,
                                             status == QuestStatus.Active ? RuntimeUI.Muted : RuntimeUI.Text,
                                             FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                var tr = text.rectTransform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(8f, 0f); tr.offsetMax = new Vector2(-6f, 0f);
                text.text = prefix + name;

                int captured = questId;
                var captureStatus = status;
                var button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = row;
                button.onClick.AddListener(() => Select(captured, captureStatus));

                _rows.Add(row.gameObject);
            }
        }

        private void Select(int questId, QuestStatus status)
        {
            _selected = questId;
            _selectedStatus = status;

            if (questId == 0 || !QuestState.TryGetDefinition(questId, out var quest))
            {
                _detail.text = "Select a quest.";
                _actionButton.gameObject.SetActive(false);
                return;
            }

            var body = new System.Text.StringBuilder();
            body.Append($"<b><color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Gold)}>{quest.Name}</color></b>\n\n");
            body.Append(quest.Description);

            if (quest.Objectives != null && quest.Objectives.Length > 0)
            {
                body.Append("\n\n<b>Objectives</b>");
                var counts = QuestState.CountsOf(questId);

                for (int i = 0; i < quest.Objectives.Length; i++)
                {
                    int have = i < counts.Length ? counts[i] : 0;
                    body.Append($"\n  {quest.Objectives[i].Text}: {have}/{quest.Objectives[i].RequiredCount}");
                }
            }

            if (quest.RewardGold > 0 || quest.RewardItemId != 0)
            {
                body.Append("\n\n<b>Reward</b>\n  ");
                if (quest.RewardGold > 0) body.Append($"{quest.RewardGold}g");
                if (quest.RewardItemId != 0)
                {
                    if (quest.RewardGold > 0) body.Append(", ");
                    body.Append($"{quest.RewardItemQuantity}x {quest.RewardItemName}");
                }
            }

            _detail.text = body.ToString();

            bool canAct = status is QuestStatus.None or QuestStatus.Complete;
            _actionButton.gameObject.SetActive(canAct);
            if (canAct)
                _actionLabel.text = status == QuestStatus.Complete ? "Complete" : "Accept";
        }

        private void Act()
        {
            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer == null || _selected == 0)
                return;

            if (_selectedStatus == QuestStatus.Complete)
                C2WQuestPacketSender.Complete(peer, _npcNetworkId, _selected);
            else
                C2WQuestPacketSender.Accept(peer, _npcNetworkId, _selected);

            // The server answers with a fresh offers packet on hand-in; for an
            // accept, closing is the expected "off you go".
            if (_selectedStatus == QuestStatus.None)
                Close();
        }

        // ── IUIPanel ─────────────────────────────────────────────────

        public void Show() { if (_window != null) _window.gameObject.SetActive(true); }

        public void Hide()
        {
            if (_window != null) _window.gameObject.SetActive(false);
            _npcNetworkId = 0;
            _selected = 0;
        }

        private void Close()
        {
            if (WorldUIManager.Instance != null) WorldUIManager.Instance.Close(this);
            else Hide();
        }

        private bool Build()
        {
            _layer = RuntimeUI.CreateLayer("QuestDialogue", 150, clickable: true);
            if (_layer == null) return false;

            var panel = RuntimeUI.NewPanel("Window", _layer, raycast: true);
            _window = panel.rectTransform;
            _window.anchorMin = _window.anchorMax = new Vector2(0.5f, 0.5f);
            _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = size;

            _title = RuntimeUI.NewText("Title", _window, 16f, RuntimeUI.Gold, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            var tr = _title.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(14f, -36f); tr.offsetMax = new Vector2(-40f, -8f);

            var close = RuntimeUI.NewImage("Close", _window, new Color(0, 0, 0, 0), raycast: true);
            var cr = close.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 1f); cr.pivot = new Vector2(1f, 1f);
            cr.anchoredPosition = new Vector2(-6f, -6f); cr.sizeDelta = new Vector2(28f, 28f);
            var x = RuntimeUI.NewText("X", cr, 18f, RuntimeUI.Muted, FontStyles.Bold);
            RuntimeUI.Stretch(x.rectTransform); x.text = "X";
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = close;
            closeButton.onClick.AddListener(Close);

            // Left: the list of quests.
            _list = RuntimeUI.NewRect("List", _window);
            _list.anchorMin = new Vector2(0f, 0f); _list.anchorMax = new Vector2(0.42f, 1f);
            _list.offsetMin = new Vector2(12f, 48f); _list.offsetMax = new Vector2(-6f, -40f);
            var layout = _list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f; layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true; layout.childControlHeight = false;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

            // Right: the selected quest.
            _detail = RuntimeUI.NewText("Detail", _window, 12f, RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _detail.textWrappingMode = TextWrappingModes.Normal;
            var dr = _detail.rectTransform;
            dr.anchorMin = new Vector2(0.42f, 0f); dr.anchorMax = new Vector2(1f, 1f);
            dr.offsetMin = new Vector2(8f, 48f); dr.offsetMax = new Vector2(-12f, -40f);

            var action = RuntimeUI.NewPanel("Action", _window, raycast: true);
            _actionButton = action.rectTransform;
            _actionButton.anchorMin = _actionButton.anchorMax = new Vector2(1f, 0f);
            _actionButton.pivot = new Vector2(1f, 0f);
            _actionButton.anchoredPosition = new Vector2(-12f, 12f);
            _actionButton.sizeDelta = new Vector2(120f, 30f);
            action.color = new Color(0.42f, 0.30f, 0.14f, 1f);
            _actionLabel = RuntimeUI.NewText("Label", _actionButton, 13f, RuntimeUI.Gold, FontStyles.Bold);
            RuntimeUI.Stretch(_actionLabel.rectTransform);
            var actionButton = action.gameObject.AddComponent<Button>();
            actionButton.targetGraphic = action;
            actionButton.onClick.AddListener(Act);

            _window.gameObject.SetActive(false);
            return true;
        }
    }
}
