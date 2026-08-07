using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.UI.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    public class CharacterCreateUI : MonoBehaviour, IUIScreen
    {
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Button         createButton;
        [SerializeField] private TMP_Text       errorText;

        private void Awake()
        {
            createButton.onClick.AddListener(OnCreateClicked);
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

            if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 20)
            {
                ShowError("Name must be 2-20 characters.");
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