using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.Gameplay
{
    public class MMOCamera : MonoBehaviour
    {
        [SerializeField] private float distance         = 6f;
        [SerializeField] private float minDistance      = 2f;
        [SerializeField] private float maxDistance      = 15f;
        [SerializeField] private float zoomSpeed        = 2f;
        [SerializeField] private float mouseSensitivity = 3f;
        [SerializeField] private float minPitch         = -20f;
        [SerializeField] private float maxPitch         = 60f;

        public static MMOCamera Instance { get; private set; }

        private Transform _target;
        private float     _yaw;
        private float     _pitch = 15f;

        private void Awake()
        {
            Instance = this;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            HandleZoom();
            HandleRotation();
            PositionCamera();
        }

        private void HandleZoom()
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            distance    -= scroll * zoomSpeed * 0.01f;
            distance     = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        private void HandleRotation()
        {
            bool rightClick = Mouse.current.rightButton.isPressed;
            bool leftClick  = Mouse.current.leftButton.isPressed;

            if (!rightClick && !leftClick) return;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            Vector2 mouseDelta = Mouse.current.delta.ReadValue();

            _yaw   += mouseDelta.x * mouseSensitivity * Time.deltaTime * 10f;
            _pitch -= mouseDelta.y * mouseSensitivity * Time.deltaTime * 10f;
            _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private void PositionCamera()
        {
            bool rightClick = Mouse.current.rightButton.isPressed;
            bool leftClick  = Mouse.current.leftButton.isPressed;

            if (!rightClick && !leftClick)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
            }

            Quaternion rotation   = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3    desiredPos = _target.position
                - rotation * Vector3.forward * distance
                + Vector3.up * 1.5f;

            transform.position = desiredPos;
            transform.LookAt(_target.position + Vector3.up * 1.5f);
        }

        public void SetTarget(Transform t)
        {
            _target = t;
            _yaw    = t.eulerAngles.y;
        }

        public Vector3 GetCameraForward()
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.normalized;
        }

        public Vector3 GetCameraRight()
        {
            Vector3 r = transform.right;
            r.y = 0f;
            return r.normalized;
        }
    }
}