using TMPro;
using UnityEngine;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.UI.Events;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.UI
{
    public class PlayerSpawnData : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private TMP_Text playerName;

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
            if (ClientNetwork.Instance.ServerPeer == null)
            {
                Debug.LogWarning("No worldserver connected.");
                return;
            }
            levelLabel.text = data.Level.ToString();
            playerName.text = data.Name;
            Debug.Log($"Character Name: {data.Name} | Character Level: {data.Level}");
        }

        private void SetLevel(int level)
        {
            levelLabel.text = level.ToString();
        }
    }
}