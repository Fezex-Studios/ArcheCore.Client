using System.Text;
using ArchCore.Client;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.World;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// The little box next to the cursor when you point at something in the
    /// world - at any distance, ArcheAge-style. Creates itself.
    ///
    ///   Iron Vein                    <- DisplayName (enemies red, friendly green)
    ///   Merchant                     <- Title
    ///   Owner: Testchar1             <- ExtraLines, e.g. a corpse's owner
    ///
    /// Everything shown comes from the object's InteractableIdentity, which
    /// the spawn handlers fill from server data. Never says "too far" - the
    /// F/G prompt is where range matters.
    /// </summary>
    public class HoverTooltipUI : MonoBehaviour
    {
        [SerializeField] private Vector2 cursorOffset = new Vector2(20f, -22f);

        private RectTransform _layer, _box;
        private TMP_Text _text;
        private PlayerInteraction _pointer;
        private float _nextSearch;
        private InteractableIdentity _shown;
        private string _shownText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<HoverTooltipUI>() != null) return;
            var go = new GameObject("HoverTooltipUI");
            DontDestroyOnLoad(go);
            go.AddComponent<HoverTooltipUI>();
        }

        private void Update()
        {
            if (_layer == null && !Build()) return;

            if (_pointer == null && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 1f;
                _pointer = FindFirstObjectByType<PlayerInteraction>();
            }

            var hovered = _pointer != null ? _pointer.CurrentFocus : null;
            if (hovered == null || Mouse.current == null)
            {
                _box.gameObject.SetActive(false);
                _shown = null;
                return;
            }

            string text = Compose(hovered);
            if (hovered != _shown || text != _shownText)
            {
                _shown = hovered;
                _shownText = text;
                _text.text = text;
                _box.sizeDelta = new Vector2(_text.preferredWidth + 20f, _text.preferredHeight + 12f);
            }

            _box.gameObject.SetActive(true);

            if (RuntimeUI.ScreenToLocal(_layer, Mouse.current.position.ReadValue(), out var local))
            {
                Vector2 pos = local + cursorOffset;
                Rect area = _layer.rect;
                if (pos.x + _box.sizeDelta.x > area.xMax - 6f) pos.x = local.x - cursorOffset.x - _box.sizeDelta.x;
                if (pos.y - _box.sizeDelta.y < area.yMin + 6f) pos.y = local.y - cursorOffset.y + _box.sizeDelta.y;
                _box.anchoredPosition = pos;
            }
        }

        private static string Compose(InteractableIdentity i)
        {
            var sb = new StringBuilder();
            string name = string.IsNullOrEmpty(i.DisplayName) ? i.gameObject.name : i.DisplayName;

            Color nameColor = RuntimeUI.Text;
            if (i.Kind == InteractableKind.Npc && NpcRegistry.Instance != null &&
                NpcRegistry.Instance.TryGetNpc(i.NetworkId, out var npc) && npc != null)
                nameColor = npc.MaxHealth > 0 ? RuntimeUI.Hostile : RuntimeUI.Friendly;

            sb.Append("<b><color=#").Append(ColorUtility.ToHtmlStringRGB(nameColor)).Append('>').Append(name).Append("</color></b>");

            if (!string.IsNullOrEmpty(i.Title))
                sb.Append("\n<color=#").Append(ColorUtility.ToHtmlStringRGB(RuntimeUI.Gold)).Append('>').Append(i.Title).Append("</color>");

            foreach (var line in i.ExtraLines)
                sb.Append('\n').Append(line);

            return sb.ToString();
        }

        private bool Build()
        {
            _layer = RuntimeUI.CreateLayer("HoverTooltip", 240, clickable: false);
            if (_layer == null) return false;

            var panel = RuntimeUI.NewPanel("Box", _layer);
            _box = panel.rectTransform;
            _box.anchorMin = _box.anchorMax = new Vector2(0.5f, 0.5f);
            _box.pivot = new Vector2(0f, 1f);

            _text = RuntimeUI.NewText("Text", _box, 13f, RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            var tr = _text.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(10f, 6f); tr.offsetMax = new Vector2(-10f, -6f);

            _box.gameObject.SetActive(false);
            return true;
        }
    }
}
