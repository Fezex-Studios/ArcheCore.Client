// PlayerInteraction.cs

using System;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ArchCore.Client
{
    public class PlayerInteraction : MonoBehaviour
    {
        [SerializeField] private float interactRange = 4f;
        [SerializeField] private LayerMask interactableLayer;

        private Camera _cam;
        private PlayerController _controller;
        private InteractableIdentity _hovered;
        private InteractableIdentity _pendingTarget; // set when we're auto-walking to interact

        public InteractableIdentity CurrentFocus => _hovered;

        public void Configure(LayerMask layer)
        {
            interactableLayer = layer;
        }

        private void Start()
        {
            _cam = Camera.main;
            _controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            UpdateHover();

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            bool clicked = !overUI
                && Mouse.current != null
                && Mouse.current.leftButton.wasReleasedThisFrame
                && !CameraWasDragging();

            bool fPressed = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;

            if (clicked || fPressed)
                TryInteract();

            CheckArrival();
        }

        private bool CameraWasDragging()
        {
            // MMOCamera locks the cursor while rotating; if it's locked right now,
            // this release is the end of a drag, not a click on the world.
            return Cursor.lockState == CursorLockMode.Locked;
        }

        private void UpdateHover()
        {
            _hovered = null;
            if (_cam == null || Mouse.current == null) return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = _cam.ScreenPointToRay(mousePos);

            if (Physics.Raycast(ray, out var hit, 100f, interactableLayer))
            {
                _hovered = hit.collider.GetComponentInParent<InteractableIdentity>();
                Debug.Log($"[Interact] Hovering '{hit.collider.gameObject.name}', identity found: {_hovered != null}");
            }

            // TODO: drive an outline/nameplate highlight off _hovered here.
        }

        private void TryInteract()
        {
            Debug.Log($"[Interact] Interact triggered. Hovered = {(_hovered != null ? _hovered.NetworkId.ToString() : "null")}");

            if (_hovered == null) return;

            SendInteract(_hovered);
        }

        private void CheckArrival()
        {
            if (_pendingTarget == null) return;

            float distance = Vector3.Distance(transform.position, _pendingTarget.transform.position);

            if (distance <= interactRange)
            {
                var target = _pendingTarget;
                _pendingTarget = null;
                _controller.CancelAutoMove();
                SendInteract(target);
            }
        }

        private void SendInteract(InteractableIdentity target)
        {
            Debug.Log($"[Interact] Sending interact packet for NetworkId {target.NetworkId}");

            if (ClientNetwork.Instance == null || ClientNetwork.Instance.ServerPeer == null)
            {
                Debug.Log("[Interact] ClientNetwork or ServerPeer is null, packet NOT sent");
                return;
            }

            C2WInteractPacketSender.Send(
                ClientNetwork.Instance.ServerPeer,
                target.NetworkId);
        }

       

        private void OnGUI()
        {
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.yellow }
            };

            GUI.Box(new Rect(10, 10, 320, 110), "");

            string hoverText = _hovered != null
                ? $"Hovering: {_hovered.gameObject.name} (id {_hovered.NetworkId})"
                : "Hovering: nothing";

            float dist = _hovered != null
                ? Vector3.Distance(transform.position, _hovered.transform.position)
                : -1f;

            string distText = _hovered != null
                ? $"Distance: {dist:F1} / {_hovered.InteractRange}"
                : "Distance: -";

            string autoText = _pendingTarget != null
                ? $"Auto-walking to: {_pendingTarget.gameObject.name}"
                : "Auto-walking: no";

            GUI.Label(new Rect(20, 15, 300, 25), hoverText, style);
            GUI.Label(new Rect(20, 40, 300, 25), distText, style);
            GUI.Label(new Rect(20, 65, 300, 25), autoText, style);

            if (_hovered != null && dist >= 0f && dist <= interactRange)
            {
                GUIStyle promptStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };

                GUI.Label(new Rect(Screen.width / 2f - 150, Screen.height / 2f + 80, 300, 40),
                    $"Click to interact with {_hovered.gameObject.name}", promptStyle);
            }
        }
    }
}