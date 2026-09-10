using System;
using ArcheCore.Network.Shared.Packets.PersistenceServer.P2W;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    public class CharacterSlotUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private Button   selectButton;
        [SerializeField] private GameObject selectedHighlight;

        private CharacterSummary _data;
        private Action<CharacterSummary, CharacterSlotUI> _onSelected;

        public void Bind(CharacterSummary data, Action<CharacterSummary, CharacterSlotUI> onSelected)
        {
            _data = data;
            _onSelected = onSelected;

            nameText.text  = data.Name;
            levelText.text = $"Lv. {data.Level}";

            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(() => _onSelected?.Invoke(_data, this));

            SetSelected(false);
        }

        public void SetSelected(bool isSelected)
        {
            selectedHighlight.SetActive(isSelected);
        }
    }
}