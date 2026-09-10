using ArcheCore.Client.UI;
using ArcheCore.Client.UI.Interfaces;
using Shared;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    public class LoginScreenManager : MonoBehaviour
    {
        [SerializeField] private ServerSelectUI     serverSelect;
        [SerializeField] private CharacterCreateUI  characterCreate;
        [SerializeField] private CharacterSelectUI  characterSelect;

        private IUIScreen current;

        private void Awake()
        {
            PlayerUIEvents.OnCharacterListReceived += HandleCharacterListReceived;
            PlayerUIEvents.OnCharacterSpawned      += HandleCharacterSpawned;

            characterSelect.OnCreateNewRequested += HandleCreateNewRequested;
        }

        private void OnDestroy()
        {
            PlayerUIEvents.OnCharacterListReceived -= HandleCharacterListReceived;
            PlayerUIEvents.OnCharacterSpawned      -= HandleCharacterSpawned;

            characterSelect.OnCreateNewRequested -= HandleCreateNewRequested;
        }

        private void Start() => SwitchTo(serverSelect);

        private void HandleCharacterListReceived(
            ArcheCore.Network.Shared.Packets.PersistenceServer.P2W.CharacterSummary[] characters)
        {
            if (characters.Length == 0)
            {
                SwitchTo(characterCreate);
            }
            else
            {
                SwitchTo(characterSelect);
                characterSelect.Populate(characters);
            }
        }

        private void HandleCreateNewRequested() => SwitchTo(characterCreate);

        private void HandleCharacterSpawned() => current?.Hide();

        private void SwitchTo(IUIScreen next)
        {
            current?.Hide();
            current = next;
            current?.Show();
        }
    }
}