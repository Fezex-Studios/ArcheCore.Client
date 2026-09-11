using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TIM
{
#if UNITY_EDITOR
    [Icon("Assets/TIM/Console/Sprites/Console icon mini.png"), HideMonoScript]
#endif
    public class ConsoleToggle : ConsoleButton
    {
        [BoxGroup, ShowInInspector, ToggleLeft, PropertyOrder(-2)]
        public bool Selected
        {
            get => _selected;
            set
            {
                bool changed = _selected != value;
                _selected = value;
                if (_iconImage)
                    _iconImage.color = _selected ? _iconColor : Color.black;
                if(_selectedPanel)
                    _selectedPanel.SetActive(_selected);
                
                if (changed)
                {
                    OnToggleEvent?.Invoke(_selected);
                }
            }
        }

        [SerializeField, HideInInspector] private bool _selected = true;

        [FoldoutGroup("Links"), SerializeField, OnValueChanged(nameof(GetImageColor))] private Image _iconImage;
        [FoldoutGroup("Links"), SerializeField, InlineButton(nameof(GetImageColor), Label = "", Icon = SdfIconType.ArrowClockwise)] private Color _iconColor;
        [FoldoutGroup("Links"), SerializeField] private GameObject _selectedPanel;

        [FoldoutGroup("Events")] public UnityEvent<bool> OnToggleEvent = new UnityEvent<bool>(); 

        protected override void OnClick()
        {
            Selected = !Selected;
        }
        
        private void GetImageColor()
        {
            if (_iconImage)
                _iconColor = _iconImage.color;
        }
    }
}
