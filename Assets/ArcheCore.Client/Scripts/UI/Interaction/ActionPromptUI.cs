using System.Collections.Generic;
using ArcheCore.Client.GameData;
using ArcheCore.Client.Gameplay.Interaction;
using ArcheCore.Client.World;
using ArcheCore.Network.Shared.Packets.W2C;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// The F / G icons at the bottom of the screen for whatever you're in
    /// range of (InteractionController.CurrentTarget). Creates itself.
    ///
    /// Icons come from Resources/Icons/Actions/&lt;IconName&gt;.png (server data);
    /// a missing icon shows the action's first letter instead. Hover an icon
    /// for its label, click it to do it. Disabled actions are dimmed.
    /// </summary>
    public class ActionPromptUI : MonoBehaviour
    {
        private static readonly string[] KeyNames = { "F", "G" };

        [SerializeField] private float iconSize = 44f;
        [SerializeField] private float spacing = 14f;
        [SerializeField] private float bottomOffset = 70f;

        private RectTransform _layer;
        private RectTransform _row;
        private TMP_Text _hoverLabel;
        private RectTransform _hoverLabelBox;
        private readonly List<GameObject> _icons = new();

        private InteractableIdentity _shownTarget;
        private InteractionActionData[] _shownActions;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<ActionPromptUI>() != null) return;
            var go = new GameObject("ActionPromptUI");
            DontDestroyOnLoad(go);
            go.AddComponent<ActionPromptUI>();
        }

        private void Update()
        {
            if (_layer == null && !Build())
                return;

            var target = InteractionController.CurrentTarget;
            var actions = target != null ? target.Actions : null;

            if (target != _shownTarget || actions != _shownActions)
                Rebuild(target, actions);
        }

        private bool Build()
        {
            _layer = RuntimeUI.CreateLayer("ActionPrompt", 20, clickable: true);
            if (_layer == null) return false;

            _row = RuntimeUI.NewRect("Row", _layer);
            _row.anchorMin = _row.anchorMax = new Vector2(0.5f, 0f);
            _row.pivot = new Vector2(0.5f, 0f);
            _row.anchoredPosition = new Vector2(0f, bottomOffset);
            var h = _row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childAlignment = TextAnchor.LowerCenter;
            h.childControlWidth = h.childControlHeight = false;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var fit = _row.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var box = RuntimeUI.NewPanel("HoverLabel", _layer);
            _hoverLabelBox = box.rectTransform;
            _hoverLabelBox.anchorMin = _hoverLabelBox.anchorMax = new Vector2(0.5f, 0f);
            _hoverLabelBox.pivot = new Vector2(0.5f, 0f);
            _hoverLabelBox.anchoredPosition = new Vector2(0f, bottomOffset + iconSize + 26f);
            _hoverLabel = RuntimeUI.NewText("Text", _hoverLabelBox, 13f, RuntimeUI.Text);
            RuntimeUI.Stretch(_hoverLabel.rectTransform);
            box.gameObject.SetActive(false);
            return true;
        }

        private void Rebuild(InteractableIdentity target, InteractionActionData[] actions)
        {
            _shownTarget = target;
            _shownActions = actions;

            foreach (var icon in _icons) Destroy(icon);
            _icons.Clear();
            ShowLabel(null);

            if (actions == null) return;

            for (int i = 0; i < actions.Length && i < KeyNames.Length; i++)
                _icons.Add(MakeIcon(target, actions[i], KeyNames[i]));
        }

        private GameObject MakeIcon(InteractableIdentity target, InteractionActionData action, string key)
        {
            var cell = RuntimeUI.NewRect($"Action_{key}", _row);
            cell.sizeDelta = new Vector2(iconSize, iconSize + 18f);

            var img = RuntimeUI.NewImage("Icon", cell, Color.white, raycast: true);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(iconSize, iconSize);

            var sprite = ItemIcons.GetByName("Actions/" + action.IconName);
            if (sprite != null)
            {
                img.sprite = sprite;
            }
            else
            {
                img.color = RuntimeUI.Panel;
                var letter = RuntimeUI.NewText("Letter", rt, 20f, RuntimeUI.Gold, FontStyles.Bold);
                RuntimeUI.Stretch(letter.rectTransform);
                letter.text = string.IsNullOrEmpty(action.Label) ? "?" : action.Label.Substring(0, 1);
            }

            var keyText = RuntimeUI.NewText("Key", cell, 14f, action.IsEnabled ? RuntimeUI.Gold : RuntimeUI.Muted, FontStyles.Bold);
            var kr = keyText.rectTransform;
            kr.anchorMin = kr.anchorMax = new Vector2(0.5f, 0f); kr.pivot = new Vector2(0.5f, 0f);
            kr.sizeDelta = new Vector2(iconSize, 18f);
            keyText.text = key;

            var group = cell.gameObject.AddComponent<CanvasGroup>();
            group.alpha = action.IsEnabled ? 1f : 0.4f;

            var button = img.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => InteractionController.Perform(target, action));

            var hover = img.gameObject.AddComponent<ActionPromptHover>();
            hover.Owner = this;
            hover.Label = action.IsEnabled ? action.Label : $"{action.Label} (not available yet)";

            return cell.gameObject;
        }

        internal void ShowLabel(string text)
        {
            if (_hoverLabelBox == null) return;
            bool show = !string.IsNullOrEmpty(text);
            _hoverLabelBox.gameObject.SetActive(show);
            if (!show) return;

            _hoverLabel.text = text;
            _hoverLabelBox.sizeDelta = new Vector2(_hoverLabel.preferredWidth + 20f, 26f);
        }
    }

    /// <summary>Shows an action icon's label while the mouse is over it. Added by ActionPromptUI at runtime.</summary>
    internal sealed class ActionPromptHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public ActionPromptUI Owner;
        public string Label;
        public void OnPointerEnter(PointerEventData e) => Owner.ShowLabel(Label);
        public void OnPointerExit(PointerEventData e) => Owner.ShowLabel(null);
    }
}
