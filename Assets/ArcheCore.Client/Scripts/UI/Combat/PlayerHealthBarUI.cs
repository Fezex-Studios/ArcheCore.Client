using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Drives your HUD health Slider from LocalCharacterState. Put it on the
    /// Slider itself (HUD/Character/Health) - it finds the Slider on the same
    /// object. Optional label shows "85 / 100".
    ///
    /// Pulls on enable and follows PlayerStatEvents.OnHealthChanged, same
    /// pattern as every other HUD element (the value arrives in W2CEnterWorld
    /// before the HUD exists).
    /// </summary>
    [RequireComponent(typeof(Slider))]
    public class PlayerHealthBarUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;

        private Slider _slider;

        private void Awake()
        {
            _slider = GetComponent<Slider>();
            _slider.interactable = false;       // a display, not a control
            _slider.minValue = 0f;
            _slider.wholeNumbers = true;
        }

        private void OnEnable()
        {
            PlayerStatEvents.OnHealthChanged += SetHealth;

            if (LocalCharacterState.HasEnteredWorld)
                SetHealth(LocalCharacterState.Health, LocalCharacterState.MaxHealth);
        }

        private void OnDisable()
        {
            PlayerStatEvents.OnHealthChanged -= SetHealth;
        }

        private void SetHealth(int health, int maxHealth)
        {
            _slider.maxValue = Mathf.Max(1, maxHealth);
            _slider.value = Mathf.Clamp(health, 0, Mathf.Max(1, maxHealth));

            if (label != null)
                label.text = maxHealth > 0 ? $"{health} / {maxHealth}" : string.Empty;
        }
    }
}
