using System.Collections.Generic;
using System.Text;
using ArcheCore.Client.Gameplay.Quests;
using ArcheCore.Client.Movement;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Network.Shared.Packets.W2C;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// The quest log, on the HUD: every quest you're carrying, with its
    /// objectives and how far along each is. Creates itself, and redraws
    /// whenever QuestState changes - so a kill or a picked-up ore ticks the
    /// line up as it happens.
    ///
    ///   right-click a quest   abandon it (asks first)
    ///   L                     hide or show the tracker
    ///
    /// A finished quest turns gold and says where to hand it in.
    /// </summary>
    public class QuestTrackerUI : MonoBehaviour
    {
        // Toggle key: GameHotkeys.ToggleQuestTracker (rebindable, default L).
        [SerializeField] private float width = 260f;
        [SerializeField] private float topOffset = 90f;

        private RectTransform _layer, _column;
        private readonly List<GameObject> _entries = new();
        private bool _hidden;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<QuestTrackerUI>() != null) return;
            var go = new GameObject("QuestTrackerUI");
            DontDestroyOnLoad(go);
            go.AddComponent<QuestTrackerUI>();
        }

        private void OnEnable() => QuestState.Changed += Rebuild;
        private void OnDisable() => QuestState.Changed -= Rebuild;

        private void Update()
        {
            if (_layer == null)
            {
                if (!Build()) return;
                Rebuild();
            }

            if (!GameHotkeys.Pressed(GameHotkeys.ToggleQuestTracker) || WorldUIManager.IsTypingInField)
                return;

            _hidden = !_hidden;
            _column.gameObject.SetActive(!_hidden);
        }

        private bool Build()
        {
            _layer = RuntimeUI.CreateLayer("QuestTracker", 8, clickable: true);
            if (_layer == null) return false;

            _column = RuntimeUI.NewRect("Column", _layer);
            _column.anchorMin = new Vector2(1f, 1f); _column.anchorMax = new Vector2(1f, 1f);
            _column.pivot = new Vector2(1f, 1f);
            _column.anchoredPosition = new Vector2(-14f, -topOffset);
            _column.sizeDelta = new Vector2(width, 0f);

            var layout = _column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f; layout.childAlignment = TextAnchor.UpperRight;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

            var fitter = _column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return true;
        }

        private void Rebuild()
        {
            if (_layer == null)
                return;

            foreach (var entry in _entries) Destroy(entry);
            _entries.Clear();

            foreach (var progress in QuestState.Tracked())
            {
                if (!QuestState.TryGetDefinition(progress.QuestId, out var quest))
                    continue;

                bool complete = (QuestStatus)progress.Status == QuestStatus.Complete;

                var text = RuntimeUI.NewText($"Quest_{progress.QuestId}", _column, 12f,
                                             RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.TopRight);
                text.textWrappingMode = TextWrappingModes.Normal;
                text.raycastTarget = true;   // right-click to abandon

                var body = new StringBuilder();
                body.Append($"<b><color=#{ColorUtility.ToHtmlStringRGB(complete ? RuntimeUI.Gold : RuntimeUI.Text)}>{quest.Name}</color></b>");

                if (complete)
                {
                    body.Append($"\n<color=#{ColorUtility.ToHtmlStringRGB(RuntimeUI.Gold)}>Ready to hand in</color>");
                }
                else if (quest.Objectives != null)
                {
                    var counts = progress.Counts ?? System.Array.Empty<int>();

                    for (int i = 0; i < quest.Objectives.Length; i++)
                    {
                        int have = i < counts.Length ? counts[i] : 0;
                        int need = quest.Objectives[i].RequiredCount;
                        var colour = have >= need ? RuntimeUI.Friendly : RuntimeUI.Muted;
                        body.Append($"\n<color=#{ColorUtility.ToHtmlStringRGB(colour)}>{quest.Objectives[i].Text}: {have}/{need}</color>");
                    }
                }

                text.text = body.ToString();

                var abandon = text.gameObject.AddComponent<QuestTrackerEntry>();
                abandon.QuestId = progress.QuestId;
                abandon.QuestName = quest.Name;

                _entries.Add(text.gameObject);
            }
        }
    }

    /// <summary>Right-click handler for one tracked quest. Added by QuestTrackerUI at runtime.</summary>
    internal sealed class QuestTrackerEntry : MonoBehaviour, IPointerClickHandler
    {
        public int QuestId;
        public string QuestName;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right)
                return;

            // Abandoning loses the progress, so it asks first - the same
            // dialog that guards destroying an item.
            ConfirmDialog.Ask(
                "Abandon quest?",
                $"Abandon \"{QuestName}\"? Any progress is lost.",
                () =>
                {
                    var peer = ClientNetwork.Instance?.ServerPeer;
                    if (peer != null)
                        C2WQuestPacketSender.Abandon(peer, QuestId);
                },
                confirmText: "Abandon");
        }
    }
}
