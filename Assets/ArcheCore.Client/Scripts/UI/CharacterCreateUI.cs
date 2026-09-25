using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.UI.Interfaces;
using ArcheCore.Network.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    public class CharacterCreateUI : MonoBehaviour, IUIPanel
    {
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Button         createButton;
        [SerializeField] private TMP_Text       errorText;

        public bool IsVisible => gameObject.activeSelf;

        private void Awake()
        {
            createButton.onClick.AddListener(OnCreateClicked);
            nameInput.characterLimit = CharacterNameRules.MaxLength;
            CharacterFlowEvents.OnCreateCharacterFailed += ShowError;
        }

        private void OnDestroy()
        {
            CharacterFlowEvents.OnCreateCharacterFailed -= ShowError;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            nameInput.text = string.Empty;
            errorText.gameObject.SetActive(false);
            createButton.interactable = true;
            nameInput.Select();
            nameInput.ActivateInputField();
        }

        public void Hide() => gameObject.SetActive(false);

        private void OnCreateClicked()
        {
            string name = nameInput.text.Trim();

            // Same rules the server enforces - this just answers sooner.
            if (!CharacterNameRules.IsValid(name, out var reason))
            {
                ShowError(reason);
                return;
            }

            errorText.gameObject.SetActive(false);
            createButton.interactable = false;

            C2WCreateCharacterPacket.Send(
                ClientNetwork.Instance.ServerPeer,
                name);
        }

        private void ShowError(string message)
        {
            errorText.text = message;
            errorText.gameObject.SetActive(true);
            createButton.interactable = true;
        }
    }
}