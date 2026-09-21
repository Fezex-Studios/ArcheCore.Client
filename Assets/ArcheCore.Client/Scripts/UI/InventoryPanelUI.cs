using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.State;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Owns the grid of InventorySlotUI instances and turns slot input into
    /// requests to the server:
    ///
    ///   Left click, left click - move (click the same slot again to deselect)
    ///   Drag onto a slot       - move / swap / merge
    ///   Drag out of dropZone   - destroy, after ConfirmDialog
    ///   Right click            - use
    ///   Shift + right click    - destroy, after ConfirmDialog
    ///
    /// Never changes a slot's contents itself. Every action is a request;
    /// what the grid shows only changes when W2CInventorySlotChanged comes
    /// back. On a bad connection a move visibly waits a beat - that's
    /// correct, not a bug to hide with an optimistic local swap.
    ///
    /// Subscribes to inventory events AND pulls from LocalCharacterState
    /// on enable: events cover changes while alive, the pull covers the
    /// initial inventory, which arrives before this object exists.
    /// </summary>
    public class InventoryPanelUI : MonoBehaviour, IInventorySlotHost
    {
        [SerializeField] private InventorySlotUI slotPrefab;
        [SerializeField] private Transform slotParent;

        [Header("Drag and drop")]
        [Tooltip("Follows the cursor while dragging. Put it under the top-level Canvas, last sibling, so it draws above everything. Starts hidden.")]
        [SerializeField] private RectTransform dragGhost;
        [SerializeField] private TMP_Text dragGhostLabel;

        [Tooltip("Releasing a drag OUTSIDE this rect asks to destroy the item. Assign InventoryWindow. Leave empty to disable drag-to-destroy.")]
        [SerializeField] private RectTransform dropZone;

        private readonly List<InventorySlotUI> _slots = new();
        private int _selectedIndex = -1;

        private InventorySlotUI _dragSource;
        private bool _dropHandled;

        private static ClientNetwork Net => ClientNetwork.Instance;

        private void Awake()
        {
            SetupDragGhost();

            if (slotPrefab == null || slotParent == null)
            {
                Debug.LogWarning($"[InventoryPanelUI] Prefab or parent not assigned on '{name}'.", this);
                return;
            }

            // Matches InventoryConstants.SlotCount on the server. A mismatch
            // only ever shows as an extra/missing slot, never silent data
            // loss, so it isn't worth a shared-assembly dependency.
            const int slotCount = 20;

            for (int i = 0; i < slotCount; i++)
            {
                var slot = Instantiate(slotPrefab, slotParent);
                slot.Bind(i, this);
                _slots.Add(slot);
            }
        }

        private void OnEnable()
        {
            PlayerInventoryEvents.OnInventorySnapshot += HandleSnapshot;
            PlayerInventoryEvents.OnSlotChanged += HandleSlotChanged;

            if (LocalCharacterState.Inventory != null)
                HandleSnapshot(LocalCharacterState.Inventory);
        }

        private void OnDisable()
        {
            PlayerInventoryEvents.OnInventorySnapshot -= HandleSnapshot;
            PlayerInventoryEvents.OnSlotChanged -= HandleSlotChanged;

            // Nothing may survive the panel closing: no armed selection, no
            // drag ghost stuck on screen.
            ClearSelection();
            CancelDrag();
        }

        // ── Server -> grid ───────────────────────────────────────────

        private void HandleSnapshot(InventorySlotData[] slots)
        {
            if (slots == null)
                return;

            foreach (var data in slots)
            {
                if (data == null || data.Index < 0 || data.Index >= _slots.Count)
                    continue;

                _slots[data.Index].Refresh(data.ItemTemplateId, data.Quantity);
            }
        }

        private void HandleSlotChanged(int index, int itemTemplateId, int quantity)
        {
            if (index < 0 || index >= _slots.Count)
                return;

            _slots[index].Refresh(itemTemplateId, quantity);

            // The slot we'd armed to move just changed under us - drop the
            // selection rather than move something the player never saw.
            if (index == _selectedIndex)
                ClearSelection();
        }

        // ── IInventorySlotHost: clicks ───────────────────────────────

        public void OnSlotLeftClick(InventorySlotUI slot)
        {
            if (Net?.ServerPeer == null)
                return;

            int index = slot.Index;

            if (_selectedIndex == -1)
            {
                if (slot.IsEmpty)
                    return; // nothing to move

                _selectedIndex = index;
                slot.SetSelected(true);
                return;
            }

            if (_selectedIndex == index)
            {
                ClearSelection();
                return;
            }

            C2WMoveItemPacketSender.Send(Net.ServerPeer, _selectedIndex, index);
            ClearSelection();
        }

        public void OnSlotUse(InventorySlotUI slot)
        {
            if (Net?.ServerPeer == null || slot.IsEmpty)
                return;

            // Saves a packet the server would reject anyway. The server's
            // check is the real one.
            if (slot.IsOnCooldown)
                return;

            C2WUseItemPacketSender.Send(Net.ServerPeer, slot.Index);
        }

        public void OnSlotDestroyRequested(InventorySlotUI slot) => AskDestroy(slot);

        // ── IInventorySlotHost: drag and drop ────────────────────────

        public void OnSlotBeginDrag(InventorySlotUI slot, PointerEventData eventData)
        {
            ClearSelection();

            _dragSource = slot;
            _dropHandled = false;

            if (dragGhost != null)
            {
                if (dragGhostLabel != null)
                    dragGhostLabel.text = slot.DisplayText;

                dragGhost.gameObject.SetActive(true);
                MoveGhost(eventData);
            }
        }

        public void OnSlotDrag(PointerEventData eventData) => MoveGhost(eventData);

        public void OnSlotDroppedOn(InventorySlotUI target)
        {
            if (_dragSource == null)
                return;

            // Anything landing on a slot is handled - even dropping back on
            // itself - so OnSlotEndDrag never mistakes it for a destroy.
            _dropHandled = true;

            if (target == _dragSource || Net?.ServerPeer == null)
                return;

            C2WMoveItemPacketSender.Send(Net.ServerPeer, _dragSource.Index, target.Index);
        }

        public void OnSlotEndDrag(InventorySlotUI slot, PointerEventData eventData)
        {
            var source = _dragSource;
            bool handled = _dropHandled;
            CancelDrag();

            if (source == null || handled || dropZone == null)
                return;

            // Released inside the window but not on a slot: do nothing.
            // Released outside it: that's the "drag it off to destroy" gesture.
            bool insideWindow = RectTransformUtility.RectangleContainsScreenPoint(
                dropZone, eventData.position, eventData.pressEventCamera);

            if (!insideWindow)
                AskDestroy(source);
        }

        // ── Destroy ──────────────────────────────────────────────────

        private void AskDestroy(InventorySlotUI slot)
        {
            if (Net?.ServerPeer == null || slot.IsEmpty)
                return;

            // Captured NOW. The dialog can sit open while the server moves
            // things around; on confirm we only destroy if the slot still
            // holds what the player was actually asked about.
            int index = slot.Index;
            int itemTemplateId = slot.ItemTemplateId;
            string shown = slot.DisplayText;

            ConfirmDialog.Ask(
                "Destroy item?",
                $"Destroy {shown}? This can't be undone.",
                () =>
                {
                    if (index >= _slots.Count || _slots[index].ItemTemplateId != itemTemplateId)
                    {
                        Debug.Log($"[InventoryPanelUI] Slot {index} changed while confirming - destroy cancelled.");
                        return;
                    }

                    if (Net?.ServerPeer == null)
                        return;

                    C2WDropItemPacketSender.Send(Net.ServerPeer, index);
                },
                confirmText: "Destroy");

            // If Ask returns false there's no dialog in the scene. That is a
            // cancel, never a silent destroy.
        }

        // ── Helpers ──────────────────────────────────────────────────

        private void SetupDragGhost()
        {
            if (dragGhost == null)
                return;

            // The ghost sits under the cursor for the whole drag. If it
            // caught raycasts, the slot underneath would never receive
            // OnDrop and every drag would look like "dropped outside".
            var group = dragGhost.GetComponent<CanvasGroup>();
            if (group == null)
                group = dragGhost.gameObject.AddComponent<CanvasGroup>();

            group.blocksRaycasts = false;
            group.interactable = false;

            dragGhost.gameObject.SetActive(false);
        }

        private void MoveGhost(PointerEventData eventData)
        {
            if (dragGhost == null || !dragGhost.gameObject.activeSelf)
                return;

            var parent = dragGhost.parent as RectTransform;
            if (parent == null)
            {
                dragGhost.position = eventData.position;
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera, out var local))
            {
                dragGhost.localPosition = local;
            }
        }

        private void CancelDrag()
        {
            _dragSource = null;
            _dropHandled = false;

            if (dragGhost != null)
                dragGhost.gameObject.SetActive(false);
        }

        private void ClearSelection()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _slots.Count)
                _slots[_selectedIndex].SetSelected(false);

            _selectedIndex = -1;
        }
    }
}