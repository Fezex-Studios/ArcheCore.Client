using ArcheCore.Client.UI;
using ArcheCore.Client.UI.Interfaces;
using Shared;
using UnityEngine;

namespace ArcheCore.Client.UI
{
    public class LoginScreenManager : MonoBehaviour
    {
        [SerializeField] private ServerSelectUI    serverSelect;
        [SerializeField] private CharacterCreateUI characterCreate;

        private IUIScreen current;

        private void Awake()
        {
            PlayerUIEvents.OnCharacterNotFound += HandleCharacterNotFound;
            PlayerUIEvents.OnCharacterSpawned  += HandleCharacterSpawned;
        }

        private void OnDestroy()
        {
            PlayerUIEvents.OnCharacterNotFound -= HandleCharacterNotFound;
            PlayerUIEvents.OnCharacterSpawned  -= HandleCharacterSpawned;
        }

        private void Start() => SwitchTo(serverSelect);

        private void HandleCharacterNotFound() => SwitchTo(characterCreate);
        private void HandleCharacterSpawned()  => current?.Hide();

        private void SwitchTo(IUIScreen next)
        {
            current?.Hide();
            current = next;
            current?.Show();
        }
    }
}