using TMPro;
using UnityEngine;
using ArcheCore.Client.UI.Events;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// HUD: shows the local character's gold. Byte-for-byte the same
    /// shape as PlayerSpawnData - one label, subscribe on enable,
    /// unsubscribe on disable, do nothing if no label was wired up in
    /// the Inspector.
    ///
    /// Separate component from PlayerSpawnData rather than one more field
    /// on it, because gold and name/level come from genuinely different
    /// packets on genuinely different cadences - see the comment on
    /// PlayerStatEvents.OnGoldChanged. Two small components that each do
    /// one thing beats one component juggling two unrelated update rates.
    /// </summary>
    public class PlayerGoldDisplay : MonoBehaviour
    {
        [SerializeField] private TMP_Text goldLabel;

        /// <summary>
        /// Optional format string, so a designer can change "120" to
        /// "120g" or "💰 120" from the Inspector without a code change.
        /// {0} is replaced with the gold value.
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