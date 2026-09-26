using System;
using System.Collections.Generic;
using ArcheCore.Client.GameData;
using ArcheCore.Client.Gameplay.Combat;
using ArcheCore.Client.Gameplay.Statuses;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// A row of buff/debuff icons for one entity (roadmap 3.3): its icon (or
    /// its first letter), a green (buff) or red (debuff) border, the stack
    /// count, seconds left, and a darkening sweep as it runs out. Hover for
    /// the name and description.
    ///
    /// Reads StatusState every frame for whichever entity EntityId says, so
    /// it's right whatever order packets arrived in. Used by the player's
    /// own row (PlayerStatusRowUI) and the target frame.
    /// </summary>
    public class StatusRowUI : MonoBehaviour
    {
        public Func<int> EntityId;
        public float IconSize = 30f;
        public float Spacing = 4f;
        public int MaxIcons = 12;
        public bool RightToLeft;

        private static readonly Color BuffBorder = new Color(0.35f, 0.75f, 0.35f, 1f);
        private static readonly Color DebuffBorder = new Color(0.85f, 0.25f, 0.20f, 1f);

        private sealed class Icon
        {
            public GameObject Root;
            public Image Border, Picture, Sweep;
            public TMP_Text Letter, Stacks, Time;
            public StatusView Showing;
        }

        private readonly List<Icon> _icons = new List<Icon>();
        private readonly List<StatusView> _sorted = new List<StatusView>();

        private void LateUpdate()
        {
            int id = EntityId?.Invoke() ?? 0;
            var statuses = id != 0 ? StatusState.Get(id) : null;

            _sorted.Clear();
            if (statuses != null)
            {
                // Buffs first, then debuffs; each in the order they arrived.
                foreach (var s in statuses) if (!s.IsDebuff) _sorted.Add(s);
                foreach (var s in statuses) if (s.IsDebuff) _sorted.Add(s);
            }

            int count = Math.Min(_sorted.Count, MaxIcons);
            while (_icons.Count < count) _icons.Add(NewIcon(_icons.Count));

            for (int i = 0; i < _icons.Count; i++)
            {
                var icon = _icons[i];
                bool on = i < count;
                if (icon.Root.activeSelf != on) icon.Root.SetActive(on);
                if (on) Fill(icon, _sorted[i]);
            }
        }

        private Icon NewIcon(int index)
        {
            var icon = new Icon();

            var border = RuntimeUI.NewImage($"Status_{index}", transform, BuffBorder, raycast: true);
            var rt = border.rectTransform;
            rt.anchorMin = rt.anchorMax = RightToLeft ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            rt.pivot = RightToLeft ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            float x = index * (IconSize + Spacing);
            rt.anchoredPosition = new Vector2(RightToLeft ? -x : x, 0f);
            rt.sizeDelta = new Vector2(IconSize, IconSize);
            icon.Root = border.gameObject;
            icon.Border = border;

            var inset = RuntimeUI.NewImage("Inset", rt, RuntimeUI.Inset);
            var ir = inset.rectTransform;
            ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
            ir.offsetMin = new Vector2(2f, 2f); ir.offsetMax = new Vector2(-2f, -2f);

            icon.Picture = RuntimeUI.NewImage("Icon", ir, Color.white);
            RuntimeUI.Stretch(icon.Picture.rectTransform);
            icon.Picture.preserveAspect = true;

            icon.Letter = RuntimeUI.NewText("Letter", ir, IconSize * 0.5f, RuntimeUI.Text, FontStyles.Bold);
            RuntimeUI.Stretch(icon.Letter.rectTransform);

            // Darkens from the top down as the status runs out.
            icon.Sweep = RuntimeUI.NewImage("Sweep", ir, new Color(0f, 0f, 0f, 0.55f));
            var sr = icon.Sweep.rectTransform;
            sr.anchorMin = new Vector2(0f, 1f); sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.offsetMin = sr.offsetMax = Vector2.zero;

            icon.Stacks = RuntimeUI.NewText("Stacks", ir, 11f, Color.white, FontStyles.Bold, TextAlignmentOptions.BottomRight);
            RuntimeUI.Stretch(icon.Stacks.rectTransform);

            icon.Time = RuntimeUI.NewText("Time", rt, 10f, RuntimeUI.Text, FontStyles.Normal, TextAlignmentOptions.Top);
            var tr = icon.Time.rectTransform;
            tr.anchorMin = new Vector2(0f, 0f); tr.anchorMax = new Vector2(1f, 0f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.anchoredPosition = new Vector2(0f, -1f);
            tr.sizeDelta = new Vector2(0f, 14f);

            var hover = border.gameObject.AddComponent<UIHover>();
            hover.OnEnter = () =>
            {
                var s = icon.Showing;
                if (s == null) return;
                string body = s.Definition?.Description ?? string.Empty;
                if (s.Stacks > 1) body += $"\n{s.Stacks} stacks";
                body += $"\n{Math.Ceiling(s.RemainingSeconds):0}s remaining";
                TextTooltipUI.Show(icon, s.Name, body.Trim());
            };
            hover.OnExit = () => TextTooltipUI.Hide(icon);

            return icon;
        }

        private static void Fill(Icon icon, StatusView status)
        {
            icon.Showing = status;
            icon.Border.color = status.IsDebuff ? DebuffBorder : BuffBorder;

            var sprite = ItemIcons.GetByName(status.Definition?.IconName);
            icon.Picture.sprite = sprite;
            icon.Picture.enabled = sprite != null;
            icon.Letter.text = sprite == null && status.Name.Length > 0 ? status.Name.Substring(0, 1) : string.Empty;

            icon.Stacks.text = status.Stacks > 1 ? status.Stacks.ToString() : string.Empty;

            double left = status.RemainingSeconds;
            icon.Time.text = left >= 60 ? $"{Math.Ceiling(left / 60):0}m" : $"{Math.Ceiling(left):0}s";

            icon.Sweep.rectTransform.sizeDelta = new Vector2(0f, ((RectTransform)icon.Sweep.transform.parent).rect.height * (1f - status.Remaining01));
        }
    }

    /// <summary>Your own buffs and debuffs, top right of the screen. Creates itself.</summary>
    public class PlayerStatusRowUI : MonoBehaviour
    {
        private static PlayerStatusRowUI _instance;

        [SerializeField] private Vector2 offsetFromTopRight = new Vector2(-16f, -16f);

        public static void EnsureInstance()
        {
            if (_instance != null)
                return;

            var layer = RuntimeUI.CreateLayer("PlayerStatuses", 12, clickable: true);
            if (layer == null)
                return;

            _instance = layer.gameObject.AddComponent<PlayerStatusRowUI>();

            var row = RuntimeUI.NewRect("Row", layer);
            row.anchorMin = row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(1f, 1f);
            row.anchoredPosition = _instance.offsetFromTopRight;
            row.sizeDelta = new Vector2(12 * 34f, 46f);

            var statuses = row.gameObject.AddComponent<StatusRowUI>();
            statuses.RightToLeft = true;
            statuses.EntityId = () => CombatClient.LocalPlayerId;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
