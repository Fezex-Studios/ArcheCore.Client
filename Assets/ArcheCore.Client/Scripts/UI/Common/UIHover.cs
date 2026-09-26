using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Pointer enter/exit/click as plain callbacks, for runtime-built UI
    /// (skill bar, buff icons, equipment slots). The object needs a Graphic
    /// with raycastTarget on, under a Canvas with a GraphicRaycaster.
    /// </summary>
    public sealed class UIHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action OnEnter;
        public Action OnExit;
        public Action<PointerEventData.InputButton> OnClick;

        public void OnPointerEnter(PointerEventData eventData) => OnEnter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => OnExit?.Invoke();
        public void OnPointerClick(PointerEventData eventData) => OnClick?.Invoke(eventData.button);

        private void OnDisable() => OnExit?.Invoke();
    }
}
