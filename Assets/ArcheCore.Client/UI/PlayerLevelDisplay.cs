using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;

namespace ArcheCore.Client.UI
{
    public class PlayerLevelDisplay : MonoBehaviour
    {
        public static PlayerLevelDisplay Instance { get; private set; }

        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private Button getLevelButton;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            getLevelButton.onClick.AddListener(OnGetLevelClicked);
        }

        private void OnGetLevelClicked()
        {
            if (ClientNetwork.Instance.ServerPeer == null)
            {
                Debug.LogWarning("[PlayerLevelDisplay] Not connected to world server.");
                return;
            }

            levelLabel.text = "Level: ...";
            C2WRequestPlayerLevelPacketSender.Send(ClientNetwork.Instance.ServerPeer);
        }

        public void SetLevel(int level)
        {
            levelLabel.text = $"Level: {level}";
        }
    }
}