using ArcheCore.Client.Networking;
using ArcheCore.Client.UI.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Server select panel.
    ///
    /// Logging in is the LAUNCHER's job: it starts the client with a fresh
    /// one-shot token (-token). Once that token has been used (any connect
    /// attempt), the only way to play again is to close the game and press
    /// Play in the launcher, which fetches a new one.
    ///
    /// Inspector: ipInput is required. statusText and connectButton are
    /// optional - assign them to show disconnect reasons and to disable
    /// Connect when there is no usable token.
    /// </summary>
    public class ServerSelectUI : MonoBehaviour, IUIPanel
    {
        private const string RelaunchMessage =
            "Your session has ended. Close the game and press Play in the launcher.";

        [SerializeField] private TMP_InputField ipInput;

        [Header("Feedback (optional)")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button   connectButton;

        public bool IsVisible => gameObject.activeSelf;

        public void Show()
        {
            gameObject.SetActive(true);

            bool canConnect = SessionManager.HasUsableToken;
            if (connectButton != null)
                connectButton.interactable = canConnect;

            if (!string.IsNullOrEmpty(ClientNetwork.LastDisconnectMessage))
            {
                SetStatus(canConnect
                    ? ClientNetwork.LastDisconnectMessage
                    : $"{ClientNetwork.LastDisconnectMessage}\n{RelaunchMessage}");
            }
            else if (!canConnect)
            {
                SetStatus("Start the game from the launcher to log in.");
            }
            else
            {
                SetStatus(string.Empty);
            }
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Wired to the Connect button's OnClick.</summary>
        public void Connect()
        {
            if (!SessionManager.HasUsableToken)
            {
                SetStatus(RelaunchMessage);
                return;
            }

            string ip = ipInput == null || string.IsNullOrWhiteSpace(ipInput.text)
                ? "127.0.0.1"
                : ipInput.text.Trim();

            if (connectButton != null)
                connectButton.interactable = false; // the token can only be used once

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
    }
}