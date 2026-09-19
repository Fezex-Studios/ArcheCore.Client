using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.Movement
{
    /// <summary>
    /// Every rebindable character-control action, with ArcheAge's stock
    /// bindings as defaults.
    ///
    /// DEFINED IN CODE, NOT AS AN .inputactions ASSET. Both work with the
    /// rebinding API; code wins here because the bindings are the thing most
    /// likely to be read, diffed and argued about, and in an asset they are
    /// a binary blob you can only inspect through an editor window. It also
    /// means there is no asset to wire into a prefab and nothing to go
    /// missing on someone else's machine.
    ///
    /// TWO BINDING SLOTS PER ACTION, matching ArcheAge's options screen,
    /// which shows a primary and a secondary column. Index 0 is primary,
    /// index 1 is secondary. Actions that ship with only one key still get
    /// an empty second slot so the UI has something to bind into - an
    /// action with no slot cannot be given one at runtime without
    /// rebuilding the map.
    ///
    /// The UI is yours to build; this is the model behind it. StartRebind
    /// listens for the next key and writes it in, SaveOverrides persists,
    /// and ResetToDefaults throws the overrides away.
    /// </summary>
    public sealed class PlayerInputActions : IDisposable
    {
        private const string PrefsKey = "ArcheCore.KeyBindings";

        private readonly InputActionMap _map;

        public InputAction MoveForward  { get; }
        public InputAction MoveBackward { get; }
        public InputAction MoveLeft     { get; }
        public InputAction MoveRight    { get; }
        public InputAction TurnLeft     { get; }
        public InputAction TurnRight    { get; }
        public InputAction Jump         { get; }
        public InputAction Sprint       { get; }
        public InputAction Walk         { get; }
        public InputAction AutoRun      { get; }
        public InputAction SwimUp       { get; }
        public InputAction SwimDown     { get; }

        /// <summary>Everything, in the order an options screen should list
        /// it. Display names come from InputAction.name.</summary>
        public IReadOnlyList<InputAction> All { get; }

        public PlayerInputActions()
        {
            _map = new InputActionMap("Character");

            MoveForward  = Add("Move Forward",  "<Keyboard>/w",         "<Keyboard>/upArrow");
            MoveBackward = Add("Move Backward", "<Keyboard>/s",         "<Keyboard>/downArrow");
            MoveLeft     = Add("Move Left",     "<Keyboard>/q",         null);
            MoveRight    = Add("Move Right",    "<Keyboard>/e",         null);
            TurnLeft     = Add("Turn Left",     "<Keyboard>/a",         "<Keyboard>/leftArrow");
            TurnRight    = Add("Turn Right",    "<Keyboard>/d",         "<Keyboard>/rightArrow");
            Jump         = Add("Jump",          "<Keyboard>/space",     null);
            Sprint       = Add("Sprint",        "<Keyboard>/leftShift", null);
            Walk         = Add("Walk",          "<Keyboard>/leftCtrl",  null);
            AutoRun      = Add("Autorun",       "<Keyboard>/numLock",   "<Mouse>/backButton");
            SwimUp       = Add("Swim Up",       "<Keyboard>/space",     null);
            SwimDown     = Add("Swim Down",     "<Keyboard>/x",         null);

            All = new[]
            {
                MoveForward, MoveBackward, MoveLeft, MoveRight,
                TurnLeft, TurnRight, Jump, Sprint, Walk,
                AutoRun, SwimUp, SwimDown
            };

            LoadOverrides();
        }

        private InputAction Add(string name, string primary, string secondary)
        {
            var action = _map.AddAction(name, InputActionType.Button);

            action.AddBinding(primary);

            // Always add a second slot, even when empty. A binding index
            // that does not exist cannot be rebound at runtime without
            // rebuilding the whole map, so the slot has to be there from the
            // start for the options screen to be able to fill it.
            action.AddBinding(secondary ?? string.Empty);

            return action;
        }

        public void Enable() => _map.Enable();
        public void Disable() => _map.Disable();

        /// <summary>
        /// Held state. Use for anything continuous (movement, turning).
        /// </summary>
        public static bool Held(InputAction action) => action.IsPressed();

        /// <summary>
        /// Pressed THIS FRAME. Only valid in per-frame code - never inside
        /// the motor's fixed step loop, which may run zero or several times
        /// per frame and would drop or double-count the edge.
        /// </summary>
        public static bool Pressed(InputAction action) => action.WasPressedThisFrame();

        // --- Rebinding ---

        /// <summary>
        /// Listen for the next control the player presses and bind it.
        ///
        /// Cancel on Escape, and exclude the mouse POSITION control -
        /// without that exclusion the operation completes instantly, because
        /// a mouse that moves a single pixel counts as actuation and the
        /// player never gets a chance to press anything.
        ///
        /// Dispose the returned operation when it finishes. Callers
        /// generally hold it so the UI can cancel a rebind in progress.
        /// </summary>
        public InputActionRebindingExtensions.RebindingOperation StartRebind(
            InputAction action,
            int bindingIndex,
            Action<InputAction> onComplete = null)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return null;

            // A live action cannot be rebound; re-enabled on completion.
            _map.Disable();

            return action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op =>
                {
                    op.Dispose();
                    _map.Enable();
                    SaveOverrides();
                    onComplete?.Invoke(action);
                })
                .OnCancel(op =>
                {
                    op.Dispose();
                    _map.Enable();
                    onComplete?.Invoke(action);
                })
                .Start();
        }

        /// <summary>
        /// What to print in the options screen for one slot - "W", "Left
        /// Shift", "Mouse 4". Empty for an unbound slot.
        /// </summary>
        public static string DisplayName(InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return string.Empty;

            var binding = action.bindings[bindingIndex];
            if (string.IsNullOrEmpty(binding.effectivePath))
                return string.Empty;

            return InputControlPath.ToHumanReadableString(
                binding.effectivePath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
        }

        /// <summary>Clear one slot without touching the other.</summary>
        public void ClearBinding(InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return;

            action.ApplyBindingOverride(bindingIndex, string.Empty);
            SaveOverrides();
        }

        /// <summary>
        /// Every action already bound to this control, so the UI can warn
        /// about a conflict before committing.
        ///
        /// Deliberately reports rather than prevents. Duplicate bindings are
        /// sometimes exactly what a player wants - the same key for Swim Up
        /// and Jump is the stock layout here - so refusing them outright
        /// would break defaults this class ships with.
        /// </summary>
        public List<InputAction> FindConflicts(string effectivePath, InputAction ignore = null)
        {
            var conflicts = new List<InputAction>();
            if (string.IsNullOrEmpty(effectivePath)) return conflicts;

            foreach (var action in All)
            {
                if (action == ignore) continue;

                for (int i = 0; i < action.bindings.Count; i++)
                {
                    if (action.bindings[i].effectivePath == effectivePath)
                    {
                        conflicts.Add(action);
                        break;
                    }
                }
            }

            return conflicts;
        }

        // --- Persistence ---

        public void SaveOverrides()
        {
            PlayerPrefs.SetString(PrefsKey, _map.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public void LoadOverrides()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
                _map.LoadBindingOverridesFromJson(json);
        }

        public void ResetToDefaults()
        {
            _map.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        public void Dispose()
        {
            _map?.Disable();
            _map?.Dispose();
        }
    }
}