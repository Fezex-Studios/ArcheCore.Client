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

        // ── ADDED ────────────────────────────────────────────────────
        // Optional label for a live gold readout on the debug panel, and
        // the amount OnAddGoldButtonClicked sends each press. Negative
        // testAddGoldAmount is a legitimate way to test the
        // refusal-to-go-negative path server-side without editing code -
        // just set it negative in the Inspector and press the button.
        [SerializeField] private TMP_Text goldLabel;
        [SerializeField] private int testAddGoldAmount = 100;

        private void OnEnable()
        {
            PlayerStatEvents.OnCharacterDataChanged += OnCharacterDataChanged;
            PlayerStatEvents.OnLevelChanged += SetLevel;
            PlayerStatEvents.OnGoldChanged += SetGold; // ADDED
            RequestLevel();
        }

        private void OnDisable()
        {
            PlayerStatEvents.OnCharacterDataChanged -= OnCharacterDataChanged;
            PlayerStatEvents.OnLevelChanged -= SetLevel;
            PlayerStatEvents.OnGoldChanged -= SetGold; // ADDED
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

        // ── ADDED ────────────────────────────────────────────────────
        // Wire this to a button the same way OnLevelUpButtonClicked
        // already is. Server rejects it unless AllowDebugCommands is
        // true - see C2WDebugAddGoldHandler - so this button doing
        // nothing on a non-dev server is expected, not broken.
        public void OnAddGoldButtonClicked()
        {
            if (ClientNetwork.Instance?.ServerPeer == null) return;
            C2WDebugAddGoldPacketSender.Send(ClientNetwork.Instance.ServerPeer, testAddGoldAmount);
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

        // ── ADDED ────────────────────────────────────────────────────
        private void SetGold(int gold)
        {
            if (goldLabel != null)
                goldLabel.text = $"Gold {gold}";
        }
    }
}