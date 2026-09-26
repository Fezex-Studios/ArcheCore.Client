using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.Movement
{
    /// <summary>
    /// Rebindable hotkeys that aren't character movement: attack, the two
    /// interaction slots, the quest tracker toggle (audit, client section).
    /// These were read straight from Keyboard.current with a hardcoded Key,
    /// so no options screen could ever rebind them.
    ///
    /// Same model as PlayerInputActions (primary + secondary slot per action,
    /// overrides in PlayerPrefs), but static and always enabled: the scripts
    /// that read these (CombatController, InteractionController,
    /// QuestTrackerUI) live on HUD objects that don't know about the local
    /// player's input component. A keybind options screen lists
    /// PlayerInputActions.All and GameHotkeys.All together.
    ///
    /// Skill keys 1-5 (Phase 3's skill bar) and the character window (C)
    /// live here too; the Phase 4 hotbar extends the skill slots.
    /// </summary>
    public static class GameHotkeys
    {
        private const string PrefsKey = "ArcheCore.HotkeyBindings";

        private static InputActionMap _map;
        private static InputAction _attack, _interact1, _interact2, _questTracker, _characterWindow;
        private static InputAction[] _skills;
        private static InputAction[] _all;

        /// <summary>Skill bar key 1 (the first skill, Strike). Default 1.</summary>
        public static InputAction Attack             { get { Ensure(); return _attack; } }

        /// <summary>Skill bar keys 1..SkillSlotCount (roadmap 3.x). Slot 1 is Attack.</summary>
        public static InputAction SkillSlot(int slot) { Ensure(); return slot >= 1 && slot <= _skills.Length ? _skills[slot - 1] : null; }

        public const int SkillSlotCount = 5;

        /// <summary>Open/close the character window (stats and equipment). Default C.</summary>
        public static InputAction ToggleCharacterWindow { get { Ensure(); return _characterWindow; } }

        /// <summary>First action on the focused interactable (Gather, Talk...). Default F.</summary>
        public static InputAction InteractPrimary    { get { Ensure(); return _interact1; } }

        /// <summary>Second action on the focused interactable. Default G.</summary>
        public static InputAction InteractSecondary  { get { Ensure(); return _interact2; } }

        /// <summary>Show/hide the quest tracker. Default L.</summary>
        public static InputAction ToggleQuestTracker { get { Ensure(); return _questTracker; } }

        /// <summary>For an options screen, in display order.</summary>
        public static IReadOnlyList<InputAction> All { get { Ensure(); return _all; } }

        /// <summary>Interaction slot by index: 0 = primary, 1 = secondary, else null.</summary>
        public static InputAction InteractSlot(int slot) =>
            slot == 0 ? InteractPrimary : slot == 1 ? InteractSecondary : null;

        /// <summary>Number of interaction slots with a hotkey.</summary>
        public const int InteractSlotCount = 2;

        /// <summary>Pressed this frame (per-frame code only).</summary>
        public static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();

        private static void Ensure()
        {
            if (_map != null)
                return;

            _map = new InputActionMap("Hotkeys");

            _attack       = Add("Skill 1",            "<Keyboard>/1", null);
            var skill2    = Add("Skill 2",            "<Keyboard>/2", null);
            var skill3    = Add("Skill 3",            "<Keyboard>/3", null);
            var skill4    = Add("Skill 4",            "<Keyboard>/4", null);
            var skill5    = Add("Skill 5",            "<Keyboard>/5", null);
            _interact1    = Add("Interact",           "<Keyboard>/f", null);
            _interact2    = Add("Interact (second)",  "<Keyboard>/g", null);
            _questTracker = Add("Toggle Quest Tracker","<Keyboard>/l", null);
            _characterWindow = Add("Character",       "<Keyboard>/c", null);

            _skills = new[] { _attack, skill2, skill3, skill4, skill5 };
            _all = new[] { _attack, skill2, skill3, skill4, skill5, _interact1, _interact2, _questTracker, _characterWindow };

            LoadOverrides();
            _map.Enable();
        }

        private static InputAction Add(string name, string primary, string secondary)
        {
            var action = _map.AddAction(name, InputActionType.Button);
            action.AddBinding(primary);
            action.AddBinding(secondary ?? string.Empty);   // empty second slot, see PlayerInputActions.Add
            return action;
        }

        // --- Rebinding / persistence (same shape as PlayerInputActions) ---

        public static InputActionRebindingExtensions.RebindingOperation StartRebind(
            InputAction action, int bindingIndex, Action<InputAction> onComplete = null)
        {
            Ensure();
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return null;

            _map.Disable();

            return action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op => { op.Dispose(); _map.Enable(); SaveOverrides(); onComplete?.Invoke(action); })
                .OnCancel(op => { op.Dispose(); _map.Enable(); onComplete?.Invoke(action); })
                .Start();
        }

        public static void SaveOverrides()
        {
            Ensure();
            PlayerPrefs.SetString(PrefsKey, _map.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        private static void LoadOverrides()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
                _map.LoadBindingOverridesFromJson(json);
        }

        public static void ResetToDefaults()
        {
            Ensure();
            _map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        /// <summary>Fresh map on every play session, even with domain reload off.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _map?.Disable();
            _map?.Dispose();
            _map = null;
        }
    }
}
