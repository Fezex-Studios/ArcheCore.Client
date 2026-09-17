using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.Interfaces;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    public class LoginScreenManager : MonoBehaviour
    {
        [SerializeField] private ServerSelectUI     serverSelect;
        [SerializeField] private CharacterCreateUI  characterCreate;
        [SerializeField] private CharacterSelectUI  characterSelect;

        private IUIPanel current;

        private void Awake()
        {
            CharacterFlowEvents.OnCharacterListReceived += HandleCharacterListReceived;
            CharacterFlowEvents.OnCharacterSpawned      += HandleCharacterSpawned;
            ConnectionEvents.OnDisconnected             += HandleDisconnected;
            characterSelect.OnCreateNewRequested        += HandleCreateNewRequested;
        }

        private void OnDestroy()
        {
            CharacterFlowEvents.OnCharacterListReceived -= HandleCharacterListReceived;
            CharacterFlowEvents.OnCharacterSpawned      -= HandleCharacterSpawned;
            ConnectionEvents.OnDisconnected             -= HandleDisconnected;
            characterSelect.OnCreateNewRequested        -= HandleCreateNewRequested;
        }

        private void Start() => SwitchTo(serverSelect);

        private void HandleCharacterListReceived(CharacterSummary[] characters)
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

        // Disconnected while still on this screen (bad token, server down,
        // kicked during character select) - back to server select, which
        // shows the reason.
        private void HandleDisconnected(string message) => SwitchTo(serverSelect);

        private void SwitchTo(IUIPanel next)
        {
            current?.Hide();
            current = next;
            current?.Show();
        }
    }
}
