using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.UI
{
    [Serializable]
    public class ToggleBinding
    {
        public Key        toggleKey;
        public GameObject panel;
    }

    public class HUDInputManager : MonoBehaviour
    {
        [SerializeField] private ToggleBinding[] toggles;

        private void Update()
        {
            if (Keyboard.current == null) return;

            foreach (var t in toggles)
            {
                if (Keyboard.current[t.toggleKey].wasPressedThisFrame)
                    t.panel.SetActive(!t.panel.activeSelf);
            }
        }
    }
}