using System;
using System.Collections.Generic;
using ArcheCore.Client.UI.Interfaces;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.UI
{
    [Serializable]
    public class ToggleBinding
    {
        public Key toggleKey;

        // Unity can't serialize an interface reference directly, so we take
        // the MonoBehaviour and cast to IUIPanel at runtime. Assign any
        // component here that implements IUIPanel (inspector won't stop you
        // assigning a non-IUIPanel component — Awake() validates it).
        [SerializeField] private MonoBehaviour panelBehaviour;

        private IUIPanel _panel;
        public IUIPanel Panel => _panel ??= panelBehaviour as IUIPanel;
        public string PanelName => panelBehaviour != null ? panelBehaviour.name : "(unassigned)";
    }

    public class WorldUIManager : MonoBehaviour
    {
        [SerializeField] private ToggleBinding[] toggles;

        private readonly List<IUIPanel> _openStack = new();

        // Gameplay/camera scripts can check this before consuming
        // movement/look input, instead of each panel needing its own
        // "am I blocking input" flag.
        public bool IsAnyBlockingInputOpen => _openStack.Count > 0;

        private void Awake()
        {
            foreach (var t in toggles)
            {
                if (t.Panel == null)
                    Debug.LogError(
                        $"[WorldUIManager] Toggle bound to {t.toggleKey} is not assigned to " +
                        $"a component implementing IUIPanel ({t.PanelName}).");
            }
        }

        private void Update()
        {
            if (Keyboard.current == null) return;

            foreach (var t in toggles)
            {
                if (t.Panel == null) continue;

                if (Keyboard.current[t.toggleKey].wasPressedThisFrame)
                    Toggle(t.Panel);
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
                CloseTopmost();
        }

        public void Toggle(IUIPanel panel)
        {
            if (panel.IsVisible)
                Close(panel);
            else
                Open(panel);
        }

        public void Open(IUIPanel panel)
        {
            if (panel.IsVisible) return;

            panel.Show();
            _openStack.Add(panel);
        }

        public void Close(IUIPanel panel)
        {
            if (!panel.IsVisible) return;

            panel.Hide();
            _openStack.Remove(panel);
        }

        private void CloseTopmost()
        {
            if (_openStack.Count == 0) return;
            Close(_openStack[^1]);
        }
    }
}