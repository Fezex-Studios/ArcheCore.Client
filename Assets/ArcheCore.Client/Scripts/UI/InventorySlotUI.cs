using ArcheCore.Client.UI.State;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// What a slot reports to whatever owns it. An interface rather than a
    /// hard InventoryPanelUI reference, so bank / vendor / equipment grids
    /// can reuse this exact slot prefab later instead of copy-pasting it.
    /// </summary>
    public interface IInventorySlotHost
    {
        void OnSlotLeftClick(InventorySlotUI slot);
        void OnSlotUse(InventorySlotUI slot);
        void OnSlotDestroyRequested(InventorySlotUI slot);

        void OnSlotBeginDrag(InventorySlotUI slot, PointerEventData eventData);
        void OnSlotDrag(PointerEventData eventData);
        void OnSlotEndDrag(InventorySlotUI slot, PointerEventData eventData);

        /// <summary>Called on the TARGET slot. Fires before the source's OnSlotEndDrag.</summary>
        void OnSlotDroppedOn(InventorySlotUI target);
    }

    /// <summary>
    /// One inventory slot.
    ///
    ///   Left click            - select, then left click another to move
    ///   Drag onto a slot      - move / swap / merge
    ///   Drag out of the window- destroy (asks first)
    ///   Right click           - use
    ///   Shift + right click   - destroy (asks first)
    ///
    /// Clicks come through IPointerClickHandler, not Button.onClick, which
    /// only ever reports the left button. The Button stays on the prefab
    /// for hover/pressed visuals but must have NO onClick listener, or
    /// every left click fires twice. Bind clears runtime listeners, but
    /// RemoveAllListeners can't touch ones wired in the Inspector - keep
    /// the prefab's On Click () list empty.
    ///
    /// Owns no inventory logic. It reports input to its host and redraws
    /// when told to; the server is the only thing that changes contents.
    /// </summary>
    public class InventorySlotUI : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        [SerializeField] private TMP_Text itemText;
        [SerializeField] private Button slotButton;
        [SerializeField] private GameObject selectedHighlight;

        [Tooltip("Image with Image Type = Filled, Fill Method = Radial 360. Drawn over the slot while its item is on cooldown.")]
        [SerializeField] private Image cooldownOverlay;

        public int Index { get; private set; }
        public int ItemTemplateId { get; private set; }
        public int Quantity { get; private set; }
        public bool IsEmpty => ItemTemplateId == 0;

        /// <summary>Cosmetic - the server is the real gate.</summary>
        public bool IsOnCooldown => !IsEmpty && LocalCharacterState.TryGetCooldown(ItemTemplateId, out _);

        /// <summary>The text shown in the slot - reused by the drag ghost.</summary>
        public string DisplayText => itemText != null ? itemText.text : string.Empty;

        private IInventorySlotHost _host;

        public void Bind(int index, IInventorySlotHost host)
        {
            Index = index;
            _host = host;

            if (slotButton != null)
                slotButton.onClick.RemoveAllListeners();

            if (cooldownOverlay != null)
            {
                cooldownOverlay.raycastTarget = false; // never block clicks/drops
                cooldownOverlay.enabled = false;
            }

            Refresh(0, 0);
            SetSelected(false);
        }

        private void Update()
        {
            if (cooldownOverlay == null)
                return;

            if (!IsEmpty && LocalCharacterState.TryGetCooldown(ItemTemplateId, out float remaining01))
            {
                cooldownOverlay.enabled = true;
                cooldownOverlay.fillAmount = remaining01;
            }
            else if (cooldownOverlay.enabled)
            {
                cooldownOverlay.enabled = false;
            }
        }

        // ── Clicks ───────────────────────────────────────────────────

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_host == null || (slotButton != null && !slotButton.interactable))
                return;

            // Unity already suppresses the click at the end of a drag.
            switch (eventData.button)
            {
                case PointerEventData.InputButton.Left:
                    _host.OnSlotLeftClick(this);
                    break;

                case PointerEventData.InputButton.Right:
                    if (IsEmpty)
                        return;

                    bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                    if (shift) _host.OnSlotDestroyRequested(this);
                    else       _host.OnSlotUse(this);
                    break;
            }
        }

        // ── Drag and drop ────────────────────────────────────────────

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Left button only, and nothing to drag out of an empty slot.
            // Clearing pointerDrag cancels the drag for Unity entirely, so
            // OnDrag/OnEndDrag won't fire for it.
            if (_host == null || IsEmpty || eventData.button != PointerEventData.InputButton.Left)
            {
                eventData.pointerDrag = null;
                return;
            }

            _host.OnSlotBeginDrag(this, eventData);
        }

        public void OnDrag(PointerEventData eventData) => _host?.OnSlotDrag(eventData);

        public void OnEndDrag(PointerEventData eventData) => _host?.OnSlotEndDrag(this, eventData);

        public void OnDrop(PointerEventData eventData) => _host?.OnSlotDroppedOn(this);

        // ── Display ──────────────────────────────────────────────────

        /// <summary>Updates the label without re-binding.</summary>
        public void Refresh(int itemTemplateId, int quantity)
        {
            ItemTemplateId = itemTemplateId;
            Quantity = quantity;

            if (itemText == null)
                return;

            if (itemTemplateId == 0)
            {
                itemText.text = "";
                return;
            }

            // "#12 x5" - qty omitted for a single item so a non-stackable
            // slot doesn't read "Sword x1" forever. Swap for a name lookup
            // once an item-name cache exists.
            itemText.text = quantity > 1
                ? $"#{itemTemplateId} x{quantity}"
                : $"#{itemTemplateId}";
        }

        public void SetSelected(bool isSelected)
        {
            if (selectedHighlight != null)
                selectedHighlight.SetActive(isSelected);
        }
    }
}