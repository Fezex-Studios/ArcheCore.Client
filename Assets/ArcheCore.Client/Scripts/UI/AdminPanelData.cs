using TMPro;
using UnityEngine;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2W;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.State;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Dev panel: level/gold readout plus the debug grant buttons.
    ///
    /// Lives on a GameObject that starts INACTIVE in main_world (it's a
    /// WorldUIManager toggle), so it is guaranteed not to be subscribed
    /// when W2CEnterWorld lands. That's why OnEnable pulls from
    /// LocalCharacterState after subscribing - without it the labels stay
    /// blank until something happens to change gold or level, which is
    /// exactly the "gold only shows after I click Add Gold" symptom.
    /// </summary>
    public class AdminPanelData : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private int testItemId = 1;

        [SerializeField] private TMP_Text goldLabel;
        [SerializeField] private int testAddGoldAmount = 100;

        [SerializeField] private int testAddItemQuantity = 1;

        private void OnEnable()
        {
            PlayerStatEvents.OnCharacterDataChanged += OnCharacterDataChanged;
            PlayerStatEvents.OnLevelChanged += SetLevel;
            PlayerStatEvents.OnGoldChanged += SetGold;

            // Catch up on whatever arrived while this panel was closed or
            // didn't exist yet.
            if (LocalCharacterState.HasEnteredWorld)
            {
                SetLevel(LocalCharacterState.Level);
                SetGold(LocalCharacterState.Gold);
            }
            else
            {
                // Pre-EnterWorld only - a level-up round trip is no longer
                // how the initial level arrives.
                RequestLevel();
            }
        }

        private void OnDisable()
        {
            PlayerStatEvents.OnCharacterDataChanged -= OnCharacterDataChanged;
            PlayerStatEvents.OnLevelChanged -= SetLevel;
            PlayerStatEvents.OnGoldChanged -= SetGold;
        }

        public void OnRequestItemButtonClicked()
        {
            if (ClientNetwork.Instance?.ServerPeer == null)
            {
                Debug.LogWarning("[AdminPanel] Not connected to WorldServer.");
                return;
            }

            C2WItemRequestDataPacketSender.Send(ClientNetwork.Instance.ServerPeer, testItemId);
        }

        public void OnLevelUpButtonClicked()
        {
            if (ClientNetwork.Instance?.ServerPeer == null) return;
            C2WLevelUpPacketSender.Send(ClientNetwork.Instance.ServerPeer);
        }

        public void OnAddGoldButtonClicked()
        {
            if (ClientNetwork.Instance?.ServerPeer == null) return;
            C2WDebugAddGoldPacketSender.Send(ClientNetwork.Instance.ServerPeer, testAddGoldAmount);
        }

        // Reuses testItemId - the same field the item-DATA-lookup button
        // already uses - so testing "does this item exist" and "give me
        // this item" point at the same id without two fields to keep in
        // sync by hand.
        public void OnAddItemButtonClicked()
        {
            if (ClientNetwork.Instance?.ServerPeer == null) return;
            C2WDebugAddItemPacketSender.Send(ClientNetwork.Instance.ServerPeer, testItemId, testAddItemQuantity);
        }

        private void RequestLevel()
        {
            // Null-conditional on Instance too: this panel can be enabled
            // from the editor before ClientNetwork's Awake has run.
            if (ClientNetwork.Instance?.ServerPeer == null)
            {
                Debug.LogWarning("[AdminPanel] No worldserver connected.");
                return;
            }

            C2WRequestPlayerLevelPacketSender.Send(ClientNetwork.Instance.ServerPeer);
        }

        private void OnCharacterDataChanged(CharacterData data)
        {
            if (data != null)
                SetLevel(data.Level);
        }

        private void SetLevel(int level)
        {
            if (levelLabel != null)
                levelLabel.text = $"Level {level}";
        }

        private void SetGold(int gold)
        {
            if (goldLabel != null)
                goldLabel.text = $"Gold {gold}";
        }
    }
}