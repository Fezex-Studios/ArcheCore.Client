using TMPro;
using UnityEngine;
using ArcheCore.Client.UI.Events;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// HUD: shows the local character's name and level.
    /// Either label may be left empty in the Inspector - it's just skipped.
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