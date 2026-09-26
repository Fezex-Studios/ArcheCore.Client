using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// A small text box that follows the cursor - skills, buffs, stats.
    /// Items have their own richer ItemTooltipUI. Builds itself the first
    /// time it's shown; never catches clicks.
    ///
    ///   TextTooltipUI.Show(this, "Rend", "Wound the target...\n6s cooldown");
    ///   TextTooltipUI.Hide(this);
    /// </summary>
    public class TextTooltipUI : MonoBehaviour
    {
        private static TextTooltipUI _instance;

        private RectTransform _layer, _panel;
        private TMP_Text _title, _body;
        private object _owner;

        public static void Show(object owner, string title, string body)
        {
            if (_instance == null && !Create())
                return;

            _instance._owner = owner;
            _instance._title.text = title ?? string.Empty;
            _instance._body.text = body ?? string.Empty;
            _instance._body.gameObject.SetActive(!string.IsNullOrEmpty(body));
            _instance._panel.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_instance._panel);
            _instance.Follow();
        }

        public static void Hide(object owner = null)
        {
            if (_instance == null || (owner != null && !ReferenceEquals(owner, _instance._owner)))
                return;

            _instance._owner = null;
            _instance._panel.gameObject.SetActive(false);
        }

        private static bool Create()
        {
            var layer = RuntimeUI.CreateLayer("TextTooltip", 400, clickable: false);
            if (layer == null)
                return false;

            _instance = layer.gameObject.AddComponent<TextTooltipUI>();
            _instance.Build(layer);
            return true;
        }

        private void Build(RectTransform layer)
        {
            _layer = layer;

            var panel = RuntimeUI.NewPanel("Panel", layer);
            _panel = panel.rectTransform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0f, 1f);
            _panel.sizeDelta = new Vector2(260f, 40f);

            var layout = _panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fit = _panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = RuntimeUI.NewText("Title", _panel, 14f, RuntimeUI.Gold, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            _body = RuntimeUI.NewText("Body", _panel, 12f, RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _body.textWrappingMode = TextWrappingModes.Normal;

            _panel.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_panel == null || !_panel.gameObject.activeSelf)
                return;

            // The owner went away without an exit (its window closed).
            if (_owner is Component c && (c == null || !c.gameObject.activeInHierarchy))
            {
                Hide();
                return;
            }

            Follow();
        }

        private void Follow()
        {
            var mouse = Mouse.current;
            if (mouse == null || _layer == null)
                return;

            if (!RuntimeUI.ScreenToLocal(_layer, mouse.position.ReadValue(), out var local))
                return;

            // Right of and below the cursor; flipped at the screen edges.
            var size = _panel.rect.size;
            var half = _layer.rect.size * 0.5f;
            float x = local.x + 18f;
            float y = local.y - 18f;
            if (x + size.x > half.x) x = local.x - 18f - size.x;
            if (y - size.y < -half.y) y = local.y + 18f + size.y;
            _panel.anchoredPosition = new Vector2(x, y);
        }
    }
}
