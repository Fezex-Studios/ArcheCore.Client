using System.Collections.Generic;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.W2C;
using ArcheCore.Network.Shared;
using ArcheCore.Network.Shared.Packets.W2C;
using Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    public class ChatUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField messageInput;
        [SerializeField] private TMP_Text       chatLog;
        [SerializeField] private Button         localTabButton;
        [SerializeField] private Button         shoutTabButton;
        [SerializeField] private Button         whisperTabButton;
        [SerializeField] private ScrollRect scrollRect;

        private ChatChannel _activeChannel = ChatChannel.Local;

        private readonly Dictionary<ChatChannel, string> _channelColors = new()
        {
            { ChatChannel.Local,   "#FFFFFF" },
            { ChatChannel.Shout,   "#FF6B00" },
            { ChatChannel.Whisper, "#FF66CC" }
        };

        private void Awake()
        {
            localTabButton.onClick.AddListener(() => SetChannel(ChatChannel.Local));
            shoutTabButton.onClick.AddListener(() => SetChannel(ChatChannel.Shout));
            whisperTabButton.onClick.AddListener(() => SetChannel(ChatChannel.Whisper));

            messageInput.onSubmit.AddListener(_ => OnSendClicked());
        }

        private void OnEnable()
        {
            W2CChatMessageHandler.OnChatMessageReceived += HandleIncoming;
        }

        private void OnDisable()
        {
            W2CChatMessageHandler.OnChatMessageReceived -= HandleIncoming;
        }

        private void SetChannel(ChatChannel channel)
        {
            _activeChannel = channel;
        }

        private void OnSendClicked()
        {
            string rawText = messageInput.text.Trim();
            messageInput.text = string.Empty;
            messageInput.ActivateInputField();

            if (string.IsNullOrEmpty(rawText))
                return;

            ChatChannel channel;
            string      targetName = null;
            string      message;

            if (rawText.StartsWith("/"))
            {
                if (!TryParseCommand(rawText, out channel, out targetName, out message))
                    return; // unrecognized command, drop it silently
            }
            else
            {
                channel = _activeChannel;
                message = rawText;
            }

            if (string.IsNullOrEmpty(message))
                return;

            if (channel == ChatChannel.Whisper && string.IsNullOrEmpty(targetName))
                return; // "/whisper" with no name and no message — nothing to send

            C2WChatMessagePacketSender.Send(
                ClientNetwork.Instance.ServerPeer,
                message,
                channel,
                targetName);
        }

        private static bool TryParseCommand(
            string rawText,
            out ChatChannel channel,
            out string targetName,
            out string message)
        {
            // rawText starts with "/", split off the command word.
            int spaceIndex = rawText.IndexOf(' ');
            string command = spaceIndex < 0 ? rawText : rawText.Substring(0, spaceIndex);
            string rest    = spaceIndex < 0 ? string.Empty : rawText.Substring(spaceIndex + 1).Trim();

            targetName = null;

            switch (command.ToLowerInvariant())
            {
                case "/local":
                    channel = ChatChannel.Local;
                    message = rest;
                    return true;

                case "/shout":
                    channel = ChatChannel.Shout;
                    message = rest;
                    return true;

                case "/whisper":
                case "/w":
                    channel = ChatChannel.Whisper;
                    // rest looks like "PlayerName the actual message"
                    int nameEnd = rest.IndexOf(' ');
                    if (nameEnd < 0)
                    {
                        // just "/whisper PlayerName" with nothing else typed yet
                        targetName = rest;
                        message    = string.Empty;
                    }
                    else
                    {
                        targetName = rest.Substring(0, nameEnd);
                        message    = rest.Substring(nameEnd + 1).Trim();
                    }
                    return true;

                default:
                    channel = ChatChannel.Local;
                    message = null;
                    return false;
            }
        }

        private void HandleIncoming(W2CChatMessagePacket packet)
        {
            string color = _channelColors.TryGetValue(packet.Channel, out var c) ? c : "#FFFFFF";
            chatLog.text += $"\n<color={color}>[{RichText.Safe(packet.SenderName)}]: {RichText.Safe(packet.Message)}</color>";

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f; // 0 = scrolled to bottom
        }
    }
}