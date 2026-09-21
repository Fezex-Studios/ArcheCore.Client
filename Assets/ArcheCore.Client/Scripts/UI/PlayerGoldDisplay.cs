using TMPro;
using UnityEngine;
using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.State;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// HUD: shows the local character's gold. One label, subscribe on
    /// enable, unsubscribe on disable, do nothing if no label was wired
    /// up in the Inspector.
    ///
    /// Separate component from AdminPanelData rather than one more field
    /// on it, because this one belongs on the ALWAYS-VISIBLE HUD - the
    /// player's actual gold readout - while the admin panel's copy is a
    /// dev convenience that happens to show the same number.
    ///
    /// Pulls from LocalCharacterState on enable for the same reason
    /// everything else does: the initial balance arrives on W2CEnterWorld
    /// while main_world is still loading, before this object exists.
    /// </summary>
    public class PlayerGoldDisplay : MonoBehaviour
    {
        [SerializeField] private TMP_Text goldLabel;

        /// <summary>
        /// Optional format string, so a designer can change "120" to
        /// "120g" or "Gold: 120" from the Inspector without a code
        /// change. {0} is replaced with the gold value.
        /// </summary>
        [SerializeField] private string format = "{0}";

        private void Awake()
        {
            if (goldLabel == null)
                Debug.LogWarning($"[PlayerGoldDisplay] No label assigned on '{name}' - this component does nothing.", this);
        }

        private void OnEnable()
        {
            PlayerStatEvents.OnGoldChanged += SetGold;

            if (LocalCharacterState.HasEnteredWorld)
                SetGold(LocalCharacterState.Gold);
        }

        private void OnDisable()
        {
            PlayerStatEvents.OnGoldChanged -= SetGold;
        }

        private void SetGold(int gold)
        {
            if (goldLabel != null)
                goldLabel.text = string.Format(format, gold);
        }
    }
}