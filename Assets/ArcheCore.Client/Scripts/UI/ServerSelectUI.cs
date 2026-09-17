using ArcheCore.Client.Networking;
using ArcheCore.Client.UI.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Server select + (optional) login.
    ///
    /// If the client already has an unused token (from the launcher's -token
    /// argument), Connect goes straight to the world server. Otherwise - e.g.
    /// after any disconnect, since tokens are one-shot - it logs in with the
    /// username/password fields first to get a fresh token.
    ///
    /// Inspector: ipInput is required. usernameInput, passwordInput,
    /// statusText and connectButton are optional but needed for in-client
    /// login and for showing disconnect reasons.
    /// </summary>
    public class ServerSelectUI : MonoBehaviour, IUIPanel
    {
        [SerializeField] private TMP_InputField ipInput;

        [Header("Login (needed to reconnect without the launcher)")]
        [SerializeField] private TMP_InputField usernameInput;
        [SerializeField] private TMP_InputField passwordInput;

        [Header("Feedback")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button   connectButton;

        private bool _busy;

        public bool IsVisible => gameObject.activeSelf;

        public void Show()
        {
            gameObject.SetActive(true);
            _busy = false;
            SetInteractable(true);

            if (passwordInput != null)
                passwordInput.text = string.Empty;

            if (!string.IsNullOrEmpty(ClientNetwork.LastDisconnectMessage))
                SetStatus(ClientNetwork.LastDisconnectMessage);
            else if (!SessionManager.HasUsableToken && usernameInput != null)
                SetStatus("Log in to connect.");
            else
                SetStatus(string.Empty);
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Wired to the Connect button's OnClick.</summary>
        public void Connect()
        {
            if (_busy)
                return;

            ConnectAsync();
        }

        private async void ConnectAsync()
        {
            string ip = ipInput == null || string.IsNullOrWhiteSpace(ipInput.text)
                ? "127.0.0.1"
                : ipInput.text.Trim();

            if (!SessionManager.HasUsableToken)
            {
                if (usernameInput == null || passwordInput == null)
                {
                    SetStatus("Session expired. Restart from the launcher to log in again.");
                    return;
                }

                _busy = true;
                SetInteractable(false);
                SetStatus("Logging in...");

                var result = await AuthClient.LoginAsync(usernameInput.text, passwordInput.text);

                // Panel may have been destroyed (scene change) while waiting.
                if (this == null)
                    return;

                _busy = false;
                SetInteractable(true);

                if (!result.Success)
                {
                    SetStatus(result.Error);
                    return;
                }

                SessionManager.Token = result.Token;
                passwordInput.text = string.Empty;
            }

            SetStatus("Connecting...");
            ClientNetwork.Instance.Connect(ip);
        }

        private void SetStatus(string message)
        {
            if (statusText == null)
            {
                if (!string.IsNullOrEmpty(message))
                    Debug.Log($"[ServerSelect] {message}");
                return;
            }

            statusText.text = message ?? string.Empty;
            statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        private void SetInteractable(bool value)
        {
            if (connectButton != null) connectButton.interactable = value;
            if (usernameInput != null) usernameInput.interactable = value;
            if (passwordInput != null) passwordInput.interactable = value;
            if (ipInput != null)       ipInput.interactable       = value;
        }
    }
}
