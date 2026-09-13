using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.UI
{
    public class HUDInputManager : MonoBehaviour
    {
        [Serializable]
        public struct PanelToggle
        {
            public Key toggleKey;
            public GameObject panel;
        }

        [SerializeField] private List<PanelToggle> toggles = new();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            foreach (var toggle in toggles)
            {
                if (toggle.panel == null) continue;

                if (keyboard[toggle.toggleKey].wasPressedThisFrame)
                {
                    toggle.panel.SetActive(!toggle.panel.activeSelf);
                }
            }
        }
    }
}