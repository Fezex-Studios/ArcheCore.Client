using System;
using ArcheCore.Client.GameData;
using ArcheCore.Client.Gameplay.Combat;
using ArcheCore.Client.Movement;
using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.State;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Bottom-centre skill bar: your five skills on keys 1-5 (from
    /// W2CSkillCatalog), each with its key, a cooldown sweep and seconds
    /// left, greyed out when you can't afford it - and your mana bar above
    /// it. Click a skill or press its key. Hover for what it does.
    ///
    /// Creates itself once you're in the world; the Phase 4 hotbar replaces
    /// the fixed five slots with a configurable bar.
    /// </summary>
    public class SkillBarUI : MonoBehaviour
    {
        private static SkillBarUI _instance;

        [SerializeField] private float slotSize = 46f;
        [SerializeField] private float spacing = 6f;
        [SerializeField] private float bottomOffset = 16f;

        private static readonly Color ManaColor = new Color(0.20f, 0.42f, 0.85f, 1f);

        private sealed class Slot
        {
            public int Number;
            public Image Frame, Picture, Sweep;
            public TMP_Text Key, Letter, Seconds;
        }

        private readonly Slot[] _slots = new Slot[SkillBook.SlotCount];
        private RectTransform _manaFill;
        private TMP_Text _manaText;
        private int _shownMana = -1, _shownMaxMana = -1;

        public static void EnsureInstance()
        {
            if (_instance != null)
                return;

            var layer = RuntimeUI.CreateLayer("SkillBar", 15, clickable: true);
            if (layer == null)
                return;

            _instance = layer.gameObject.AddComponent<SkillBarUI>();
            _instance.Build(layer);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Build(RectTransform layer)
        {
            float width = SkillBook.SlotCount * slotSize + (SkillBook.SlotCount - 1) * spacing;

            var bar = RuntimeUI.NewRect("Bar", layer);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = new Vector2(0f, bottomOffset);
            bar.sizeDelta = new Vector2(width, slotSize + 20f);

            // Mana
            var manaBack = RuntimeUI.NewImage("ManaBack", bar, RuntimeUI.Inset);
            var mb = manaBack.rectTransform;
            mb.anchorMin = new Vector2(0f, 1f); mb.anchorMax = new Vector2(1f, 1f);
            mb.pivot = new Vector2(0.5f, 1f);
            mb.anchoredPosition = Vector2.zero;
            mb.sizeDelta = new Vector2(0f, 12f);

            var fill = RuntimeUI.NewImage("ManaFill", mb, ManaColor);
            _manaFill = fill.rectTransform;
            _manaFill.anchorMin = Vector2.zero; _manaFill.anchorMax = Vector2.one;
            _manaFill.offsetMin = _manaFill.offsetMax = Vector2.zero;

            _manaText = RuntimeUI.NewText("ManaText", mb, 10f, Color.white, FontStyles.Bold);
            RuntimeUI.Stretch(_manaText.rectTransform);

            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = NewSlot(bar, i + 1, i * (slotSize + spacing));
        }

        private Slot NewSlot(RectTransform bar, int number, float x)
        {
            var slot = new Slot { Number = number };

            slot.Frame = RuntimeUI.NewPanel($"Skill_{number}", bar, raycast: true);
            var rt = slot.Frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(slotSize, slotSize);

            slot.Picture = RuntimeUI.NewImage("Icon", rt, Color.white);
            var pr = slot.Picture.rectTransform;
            pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
            pr.offsetMin = new Vector2(3f, 3f); pr.offsetMax = new Vector2(-3f, -3f);
            slot.Picture.preserveAspect = true;

            slot.Letter = RuntimeUI.NewText("Letter", rt, 20f, RuntimeUI.Gold, FontStyles.Bold);
            RuntimeUI.Stretch(slot.Letter.rectTransform);

            slot.Sweep = RuntimeUI.NewImage("Cooldown", rt, new Color(0f, 0f, 0f, 0.65f));
            var sr = slot.Sweep.rectTransform;
            sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0f);
            sr.pivot = new Vector2(0.5f, 0f);
            sr.offsetMin = sr.offsetMax = Vector2.zero;

            slot.Seconds = RuntimeUI.NewText("Seconds", rt, 16f, Color.white, FontStyles.Bold);
            RuntimeUI.Stretch(slot.Seconds.rectTransform);

            slot.Key = RuntimeUI.NewText("Key", rt, 11f, RuntimeUI.Text, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            var kr = slot.Key.rectTransform;
            kr.anchorMin = Vector2.zero; kr.anchorMax = Vector2.one;
            kr.offsetMin = new Vector2(4f, 2f); kr.offsetMax = new Vector2(-2f, -2f);

            var hover = slot.Frame.gameObject.AddComponent<UIHover>();
            hover.OnClick = button =>
            {
                if (button == PointerEventData.InputButton.Left)
                    CombatController.Instance?.UseSkillSlot(slot.Number);
            };
            hover.OnEnter = () =>
            {
                var skill = SkillBook.BySlot(slot.Number);
                if (skill == null) return;
                var lines = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(skill.Description)) lines.Append(skill.Description).Append('\n');
                if (skill.ManaCost > 0) lines.Append($"{skill.ManaCost} mana   ");
                if (skill.TargetRule == 0) lines.Append($"{skill.Range:0.#}m range   ");
                if (skill.CooldownMs > 0) lines.Append($"{skill.CooldownMs / 1000f:0.#}s cooldown");
                TextTooltipUI.Show(slot, skill.Name, lines.ToString().Trim());
            };
            hover.OnExit = () => TextTooltipUI.Hide(slot);

            return slot;
        }

        private void Update()
        {
            foreach (var slot in _slots)
                Refresh(slot);

            int mana = LocalCharacterState.Mana, max = LocalCharacterState.MaxMana;
            if (mana != _shownMana || max != _shownMaxMana)
            {
                _shownMana = mana;
                _shownMaxMana = max;
                _manaFill.anchorMax = new Vector2(max > 0 ? Mathf.Clamp01((float)mana / max) : 0f, 1f);
                _manaText.text = max > 0 ? $"{mana} / {max}" : string.Empty;
            }
        }

        private void Refresh(Slot slot)
        {
            var skill = SkillBook.BySlot(slot.Number);
            var action = GameHotkeys.SkillSlot(slot.Number);
            slot.Key.text = action != null && action.bindings.Count > 0
                ? UnityEngine.InputSystem.InputControlPath.ToHumanReadableString(action.bindings[0].effectivePath,
                    UnityEngine.InputSystem.InputControlPath.HumanReadableStringOptions.OmitDevice)
                : slot.Number.ToString();

            if (skill == null)
            {
                slot.Picture.enabled = false;
                slot.Letter.text = string.Empty;
                slot.Seconds.text = string.Empty;
                slot.Sweep.rectTransform.sizeDelta = Vector2.zero;
                return;
            }

            var sprite = ItemIcons.GetByName(skill.IconName);
            slot.Picture.sprite = sprite;
            slot.Picture.enabled = sprite != null;
            slot.Letter.text = sprite == null && skill.Name.Length > 0 ? skill.Name.Substring(0, 1) : string.Empty;

            float cd = SkillBook.Remaining01(skill.Id);
            slot.Sweep.rectTransform.sizeDelta = new Vector2(0f, slotSize * cd);
            double secs = SkillBook.RemainingSeconds(skill.Id);
            slot.Seconds.text = cd > 0f && secs >= 1 ? $"{Math.Ceiling(secs):0}" : string.Empty;

            bool affordable = skill.ManaCost <= 0 || LocalCharacterState.MaxMana <= 0 || skill.ManaCost <= LocalCharacterState.Mana;
            var tint = affordable ? Color.white : new Color(0.45f, 0.45f, 0.75f, 1f);
            slot.Picture.color = tint;
            slot.Letter.color = affordable ? RuntimeUI.Gold : new Color(0.45f, 0.45f, 0.75f, 1f);
        }
    }
}
