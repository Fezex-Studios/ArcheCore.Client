using TMPro;
using UnityEngine;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;

namespace ArcheCore.Client.UI
{
    public class PlayerLevelDisplay : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelLabel;

        private void OnEnable()
        {
            PlayerUIEvents.OnLevelChanged += SetLevel;
            RequestLevel();
        }

        private void OnDisable()
        {
            PlayerUIEvents.OnLevelChanged -= SetLevel;
        }

        private void RequestLevel()
        {
            if (ClientNetwork.Instance.ServerPeer == null)
            {
                Debug.LogWarning("[PlayerLevelDisplay] Not connected to world server.");
                return;
            }

            levelLabel.text = "Level: ...";
            C2WRequestPlayerLevelPacketSender.Send(ClientNetwork.Instance.ServerPeer);
        }

        private void SetLevel(int level)
        {
            levelLabel.text = $"Level: {level}";
        }
    }
}