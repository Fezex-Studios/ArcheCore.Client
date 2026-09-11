using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace TIM
{
#if UNITY_EDITOR
    [Icon("Assets/TIM/Console/Sprites/Console icon mini.png"), HideMonoScript]
#endif
    public class ConsoleButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [BoxGroup, ToggleLeft, PropertyOrder(-1), ShowInInspector] public bool Activity
        {
            get => _activity;
            set
            {
                _activity = value;
                if(DisabledPanel)
                    DisabledPanel.SetActive(!Activity);
                
                SetHighlight(_mouseEntered);
                SetPressed(false);
            }
        }

        [FoldoutGroup("Params"), ToggleLeft] public bool EnableAudio = true;
        [FoldoutGroup("Params"), ToggleLeft] public bool DisableHighlightOnStaticMouse = false;
        
        [FoldoutGroup("Links")] public GameObject HighlightPanel;
        [FoldoutGroup("Links")] public GameObject PressedPanel;
        [FoldoutGroup("Links")] public GameObject DisabledPanel;
        
        [FoldoutGroup("Events")] public UnityEvent ClickEvent = new UnityEvent();

        [SerializeField, HideInInspector] private bool _activity = true;
        
        public bool Highlighted { private set; get; }
        public bool Pressed { private set; get; }

        private bool _mouseEntered;

        #region Overrides

        public void OnPointerEnter(PointerEventData eventData)
        {
            if(DisableHighlightOnStaticMouse && eventData.delta == Vector2.zero)
                return;
            
            _mouseEntered = true;
            SetHighlight(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _mouseEntered = false;
            SetHighlight(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if(eventData.button != PointerEventData.InputButton.Left || !_mouseEntered)
                return;
            
            SetPressed(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if(eventData.button != PointerEventData.InputButton.Left)
                return;

            SetPressed(false);
        }

        private void OnEnable()
        {
            Highlighted = false;
            Pressed = false;
            if(HighlightPanel)
                HighlightPanel.SetActive(false);
            if(PressedPanel)
                PressedPanel.SetActive(false);
            if(DisabledPanel)
                DisabledPanel.SetActive(!Activity);
        }

        #endregion
        
        #region Public Methods

        public void SetHighlight(bool highlighted)
        {
            if(!Activity && highlighted)
                return;
            
            if (Highlighted != highlighted)
            {
                Highlighted = highlighted;
                
                if(EnableAudio && highlighted)
                    ConsoleAudio.PlayHighlightSound();
                
                OnHighlight(highlighted);
            }
            
            if(HighlightPanel)
                HighlightPanel.SetActive(highlighted);
        }

        public void SetPressed(bool pressed)
        {
            if(!Activity && pressed)
                return;
            
            if (Pressed != pressed)
            {
                Pressed = pressed;
                
                if (pressed)
                {
                    if(EnableAudio)
                        ConsoleAudio.PlayClickSound();
                    ClickEvent?.Invoke();
                    OnClick();
                }
            }
           
            if(PressedPanel)
                PressedPanel.SetActive(pressed);
        }

        #endregion

        protected virtual void OnHighlight(bool highlight){}
        protected virtual void OnClick(){}
    }
}
