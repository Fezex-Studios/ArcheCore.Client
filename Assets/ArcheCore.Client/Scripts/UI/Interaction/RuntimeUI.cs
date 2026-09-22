using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Shared building blocks for UI that creates itself at runtime (prompts,
    /// tooltips, nameplates, the loot window), plus the one dark-fantasy
    /// palette they all use - change a colour here and every one follows.
    /// </summary>
    public static class RuntimeUI
    {
        public static readonly Color Panel   = new Color(0.086f, 0.067f, 0.051f, 0.94f);
        public static readonly Color Inset   = new Color(0.047f, 0.035f, 0.027f, 0.90f);
        public static readonly Color Trim    = new Color(0.722f, 0.537f, 0.227f, 1f);
        public static readonly Color Gold    = new Color(0.910f, 0.753f, 0.416f, 1f);
        public static readonly Color Text    = new Color(0.902f, 0.863f, 0.784f, 1f);
        public static readonly Color Muted   = new Color(0.612f, 0.561f, 0.478f, 1f);
        public static readonly Color Hostile = new Color(0.880f, 0.340f, 0.290f, 1f);
        public static readonly Color Friendly = new Color(0.620f, 0.850f, 0.550f, 1f);
        public static readonly Color Health  = new Color(0.690f, 0.170f, 0.140f, 1f);

        public static Canvas FindRootCanvas()
        {
            Canvas best = null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
                best = c;
                if (c.name == "Canvas") break;   // the main HUD canvas
            }
            return best;
        }

        /// <summary>A full-screen child of the root Canvas on its own sorting layer.</summary>
        public static RectTransform CreateLayer(string name, int sortingOrder, bool clickable)
        {
            var root = FindRootCanvas();
            if (root == null) return null;

            var rt = NewRect(name, root.transform);
            Stretch(rt);

            var canvas = rt.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            if (clickable) rt.gameObject.AddComponent<GraphicRaycaster>();
            else rt.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;

            return rt;
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        public static Image NewImage(string name, Transform parent, Color color, bool raycast = false)
        {
            var img = NewRect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Dark panel with the gold trim.</summary>
        public static Image NewPanel(string name, Transform parent, bool raycast = false)
        {
            var img = NewImage(name, parent, Panel, raycast);
            var trim = img.gameObject.AddComponent<Outline>();
            trim.effectColor = Trim;
            trim.effectDistance = new Vector2(1.5f, -1.5f);
            return img;
        }

        public static TMP_Text NewText(string name, Transform parent, float size, Color color,
                                       FontStyles style = FontStyles.Normal,
                                       TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (t.font == null && TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
            t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        /// <summary>Screen point -> local point in `space` (a full-screen layer).</summary>
        public static bool ScreenToLocal(RectTransform space, Vector2 screen, out Vector2 local)
        {
            var canvas = space.GetComponentInParent<Canvas>().rootCanvas;
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(space, screen, cam, out local);
        }
    }
}
