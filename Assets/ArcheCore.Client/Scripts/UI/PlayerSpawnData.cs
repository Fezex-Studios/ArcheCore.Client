using TMPro;
using UnityEngine;
using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.State;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// HUD: shows the local character's name and level.
    /// Either label may be left empty in the Inspector - it's just skipped.
    ///
    /// Subscribes to the events AND pulls from LocalCharacterState on
    /// enable, same as every other HUD element. Both are needed: the name
    /// and level arrive in W2CEnterWorld while main_world is still loading,
    /// before this object exists to hear the event, so without the pull
    /// the labels would stay on their placeholder text for the whole
    /// session. The events cover changes after that (level-ups).
    /// </summary>
    public class PlayerSpawnData : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private TMP_Text playerName;

        private void Awake()
        {
            if (levelLabel == null && playerName == null)
                Debug.LogWarning($"[PlayerSpawnData] No labels assigned on '{name}' - this component does nothing.", this);
        }

        private void OnEnable()
        {
            PlayerStatEvents.OnCharacterDataChanged += SetCharacterData;
            PlayerStatEvents.OnLevelChanged += SetLevel;

            // Catch up on what arrived before this object existed.
            if (LocalCharacterState.HasEnteredWorld)
                SetCharacterData(LocalCharacterState.Character);
        }

        private void OnDisable()
        {
            PlayerStatEvents.OnCharacterDataChanged -= SetCharacterData;
            PlayerStatEvents.OnLevelChanged -= SetLevel;
        }

        private void SetCharacterData(CharacterData data)
        {
            if (data == null)
                return;

            SetLevel(data.Level);

            if (playerName != null)
                playerName.text = data.Name;
        }

        private void SetLevel(int level)
        {
            if (levelLabel != null)
                levelLabel.text = level.ToString();
        }
    }
}