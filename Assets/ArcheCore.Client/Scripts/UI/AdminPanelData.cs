using TMPro;
using UnityEngine;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI.Events;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.UI
{
    public class AdminPanelData : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private int testItemId = 1;

        private void OnEnable()
        {
            PlayerStatEvents.OnCharacterDataChanged += OnCharacterDataChanged;
            PlayerStatEvents.OnLevelChanged += SetLevel;
            RequestLevel();
        }

        private void OnDisable()
        {
            PlayerStatEvents.OnCharacterDataChanged -= OnCharacterDataChanged;
            PlayerStatEvents.OnLevelChanged -= SetLevel;
        }

        public void OnRequestItemButtonClicked()
        {
            if (ClientNetwork.Instance == null || ClientNetwork.Instance.ServerPeer == null)
            {
                Debug.LogWarning("Not Connected to WorldServer");
                return;
            }

            C2WItemRequestDataPacketSender.Send(ClientNetwork.Instance.ServerPeer, testItemId);
        }

        public void OnLevelUpButtonClicked()
        {
            if (ClientNetwork.Instance?.ServerPeer == null) return;
            C2WLevelUpPacketSender.Send(ClientNetwork.Instance.ServerPeer);
        }

        private void RequestLevel()
        {
            if (ClientNetwork.Instance.ServerPeer == null)
            {
                Debug.LogWarning("No worldserver connected.");
                return;
            }
            C2WRequestPlayerLevelPacketSender.Send(ClientNetwork.Instance.ServerPeer);
        }

        private void OnCharacterDataChanged(CharacterData data)
        {
            SetLevel(data.Level);
        }

        private void SetLevel(int level)
        {
            levelLabel.text = $"Level{level}";
        }
    }
}