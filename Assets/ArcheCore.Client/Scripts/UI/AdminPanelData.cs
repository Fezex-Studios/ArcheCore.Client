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
    /// Standard UIPanel split:
    ///   AdminPanel (ALWAYS ACTIVE - this component)
    ///   └── Window (everything you see - assign to `window`)
    ///
    /// Because the root stays active, the label subscriptions below live
    /// for the whole session, so the readout stays current while the panel
    /// is closed. OnOpened pulls from LocalCharacterState as a backstop.
    /// </summary>
    public class AdminPanelData : UIPanel
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

            PullState();
        }

        private void OnDisable()
        {
            PlayerStatEvents.OnCharacterDataChanged -= OnCharacterDataChanged;
            PlayerStatEvents.OnLevelChanged -= SetLevel;
            PlayerStatEvents.OnGoldChanged -= SetGold;
        }

        protected override void OnOpened()
        {
            PullState();

            // Pre-EnterWorld only - the level normally arrives with it.
            if (!LocalCharacterState.HasEnteredWorld)
                RequestLevel();
        }

        private void PullState()
        {
            if (!LocalCharacterState.HasEnteredWorld)
                return;

            SetLevel(LocalCharacterState.Level);
            SetGold(LocalCharacterState.Gold);
        }

        // ── Buttons (wire these in each Button's On Click) ───────────

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

        // Reuses testItemId, so "does this item exist" and "give me this
        // item" always point at the same id.
        public void OnAddItemButtonClicked()
        {
            if (ClientNetwork.Instance?.ServerPeer == null) return;
            C2WDebugAddItemPacketSender.Send(ClientNetwork.Instance.ServerPeer, testItemId, testAddItemQuantity);
        }

        private void RequestLevel()
        {
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
