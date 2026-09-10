using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI.Interfaces;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    public class CharacterSelectUI : MonoBehaviour, IUIScreen
    {
        [SerializeField] private Transform        characterListContainer;
        [SerializeField] private CharacterSlotUI  slotPrefab;
        [SerializeField] private Button           createNewButton;
        [SerializeField] private Button           enterWorldButton;
        [SerializeField] private TMP_Text         errorText;

        [SerializeField] private int maxSlots = 6; // enforced server-side too; this is just UI gating

        private CharacterSummary   _selected;
        private CharacterSlotUI    _selectedSlot;

        public System.Action OnCreateNewRequested;

        private void Awake()
        {
            createNewButton.onClick.AddListener(() => OnCreateNewRequested?.Invoke());
            enterWorldButton.onClick.AddListener(OnEnterWorldClicked);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            enterWorldButton.interactable = false;
            errorText.gameObject.SetActive(false);
        }

        public void Hide() => gameObject.SetActive(false);

        public void Populate(CharacterSummary[] characters)
        {
            foreach (Transform child in characterListContainer)
                Destroy(child.gameObject);

            _selected = null;
            _selectedSlot = null;
            enterWorldButton.interactable = false;

            foreach (var c in characters)
            {
                var slot = Instantiate(slotPrefab, characterListContainer);
                slot.Bind(c, OnCharacterSelected);
            }

            createNewButton.interactable = characters.Length < maxSlots;
        }

        private void OnCharacterSelected(CharacterSummary c, CharacterSlotUI slot)
        {
            _selected = c;

            _selectedSlot?.SetSelected(false);
            _selectedSlot = slot;
            _selectedSlot.SetSelected(true);

            enterWorldButton.interactable = true;
        }

        private void OnEnterWorldClicked()
        {
            if (_selected == null)
                return;

            enterWorldButton.interactable = false;

            C2WSelectCharacterPacket.Send(
                ClientNetwork.Instance.ServerPeer,
                _selected.CharacterId);
        }

        public void ShowError(string message)
        {
            errorText.text = message;
            errorText.gameObject.SetActive(true);
            enterWorldButton.interactable = _selected != null;
        }
    }
}