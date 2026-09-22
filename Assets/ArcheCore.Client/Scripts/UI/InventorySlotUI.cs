using ArcheCore.Client.GameData;
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
    /// One inventory slot, ArcheAge-style: the item's icon fills the slot and
    /// the stack size sits in the bottom-right corner (hidden at 1). Hovering
    /// shows the shared ItemTooltipUI.
    ///
    ///   Left click             - select, then left click another to move
    ///   Drag onto a slot       - move / swap / merge
    ///   Drag out of the window - destroy (asks first)
    ///   Right click            - use (or sell, while a shop is open)
    ///   Shift + right click    - destroy (asks first)
    ///
    /// ICONS: come from ItemIcons by the item's icon_name. An item with no
    /// icon image shows its name in the slot instead (itemText), so a
    /// missing PNG is obvious but never breaks anything.
    ///
    /// Clicks come through IPointerClickHandler, not Button.onClick (which
    /// only reports the left button). Keep the prefab's On Click () list
    /// empty, or every left click fires twice.
    ///
    /// Owns no inventory logic - it reports input and redraws when told.
    /// </summary>
    public class InventorySlotUI : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("Fallback text: the item's name, shown only when it has no icon.")]
        [SerializeField] private TMP_Text itemText;
        [SerializeField] private Button slotButton;
        [SerializeField] private GameObject selectedHighlight;

        [Tooltip("Image with Image Type = Filled, Fill Method = Radial 360. Drawn over the slot while its item is on cooldown.")]
        [SerializeField] private Image cooldownOverlay;

        [Tooltip("Fills the slot with the item's icon. Hidden when the item has no icon.")]
        [SerializeField] private Image iconImage;

        [Tooltip("Stack size in the bottom-right corner. Hidden when the quantity is 1.")]
        [SerializeField] private TMP_Text countLabel;

        public int Index { get; private set; }
        public int ItemTemplateId { get; private set; }
        public int Quantity { get; private set; }
        public bool IsEmpty => ItemTemplateId == 0;

        /// <summary>The icon currently shown, or null. The drag ghost uses it.</summary>
        public Sprite IconSprite { get; private set; }

        /// <summary>Cosmetic - the server is the real gate.</summary>
        public bool IsOnCooldown => !IsEmpty && LocalCharacterState.TryGetCooldown(ItemTemplateId, out _);

        /// <summary>Item name (with quantity) - used by the drag ghost and the destroy dialog.</summary>
        public string DisplayText => IsEmpty ? string.Empty
            : Quantity > 1 ? $"{ItemNames.Get(ItemTemplateId)} x{Quantity}" : ItemNames.Get(ItemTemplateId);

        private IInventorySlotHost _host;
        private bool _hovered;
        private bool _dragging;
        private bool _countOutlineApplied;

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

            if (iconImage != null)
            {
                iconImage.raycastTarget = false;
                iconImage.preserveAspect = true;
            }

            if (countLabel != null)
                countLabel.raycastTarget = false;

            // The count's outline is NOT set here: Bind runs while the slot is
            // inside the hidden inventory window, before TextMeshPro has set up
            // its material, and the outline setter throws a NullReference on
            // an uninitialised label. See ApplyCountOutlineOnce.

            Refresh(0, 0);
            SetSelected(false);
        }

        private void Update()
        {
            ApplyCountOutlineOnce();

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

        /// <summary>
        /// A dark outline keeps the stack number readable on any icon. Applied
        /// the first frame the label is live (active, enabled, material ready),
        /// then never again - setting it creates this label's own material
        /// instance, which only needs to happen once.
        /// </summary>
        private void ApplyCountOutlineOnce()
        {
            if (_countOutlineApplied || countLabel == null)
                return;

            if (!countLabel.isActiveAndEnabled || countLabel.fontSharedMaterial == null)
                return; // not ready yet - try again next frame

            countLabel.outlineWidth = 0.25f;
            countLabel.outlineColor = new Color32(0, 0, 0, 255);
            _countOutlineApplied = true;
        }

        private void OnDisable()
        {
            _hovered = false;
            ItemTooltipUI.Hide(this);
        }

        // ── Hover ────────────────────────────────────────────────────

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            ShowTooltip();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            ItemTooltipUI.Hide(this);
        }

        private void ShowTooltip()
        {
            if (IsEmpty || _dragging)
            {
                ItemTooltipUI.Hide(this);
                return;
            }

            // While a merchant is open, the tooltip also says what it sells for.
            ItemTooltipUI.Show(this, ItemTemplateId, Quantity, ShopPanelUI.DescribeSale(ItemTemplateId, Quantity));
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
            // Clearing pointerDrag cancels the drag for Unity entirely.
            if (_host == null || IsEmpty || eventData.button != PointerEventData.InputButton.Left)
            {
                eventData.pointerDrag = null;
                return;
            }

            _dragging = true;
            ItemTooltipUI.Hide();
            _host.OnSlotBeginDrag(this, eventData);
        }

        public void OnDrag(PointerEventData eventData) => _host?.OnSlotDrag(eventData);

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
            _host?.OnSlotEndDrag(this, eventData);
        }

        public void OnDrop(PointerEventData eventData) => _host?.OnSlotDroppedOn(this);

        // ── Display ──────────────────────────────────────────────────

        /// <summary>Updates the slot without re-binding.</summary>
        public void Refresh(int itemTemplateId, int quantity)
        {
            ItemTemplateId = itemTemplateId;
            Quantity = quantity;

            IconSprite = itemTemplateId == 0 ? null : ItemIcons.Get(itemTemplateId);
            bool hasIcon = IconSprite != null;

            if (iconImage != null)
            {
                iconImage.sprite = IconSprite;
                iconImage.enabled = hasIcon;
            }

            // Name only when there's no icon to show. With no iconImage
            // wired at all, this behaves exactly like the old text slot.
            if (itemText != null)
            {
                bool showName = itemTemplateId != 0 && (!hasIcon || iconImage == null);
                itemText.text = showName ? ItemNames.Get(itemTemplateId) : string.Empty;
            }

            if (countLabel != null)
            {
                bool showCount = itemTemplateId != 0 && quantity > 1;
                countLabel.text = showCount ? quantity.ToString() : string.Empty;
                countLabel.enabled = showCount;
            }
            else if (itemText != null && itemTemplateId != 0 && quantity > 1)
            {
                // No count label on this prefab yet - keep the quantity visible.
                itemText.text = $"{itemText.text} x{quantity}".Trim();
            }

            // Hovered while the contents changed (a stack grew, an item
            // was sold): keep the tooltip honest.
            if (_hovered)
                ShowTooltip();
        }

        public void SetSelected(bool isSelected)
        {
            if (selectedHighlight != null)
                selectedHighlight.SetActive(isSelected);
        }
    }
}