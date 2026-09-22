using System;
using System.Collections.Generic;
using ArchCore.Client;
using ArcheCore.Client.Gameplay;
using ArcheCore.Client.Networking;
using ArcheCore.Client.Networking.C2WSenders;
using ArcheCore.Client.UI.Events;
using ArcheCore.Client.UI.State;
using ArcheCore.Client.World;
using ArcheCore.Network.Shared.Packets.W2C;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// Merchant window. Opened by W2CShopOpen when you interact with a
    /// merchant NPC.
    ///
    ///   Buy  - click a row's Buy button. Shift+click buys `shiftBuyQuantity`.
    ///   Sell - right-click an item in your inventory while this is open.
    ///          Sales worth `confirmSellAtOrAbove` gold or more ask first.
    ///
    /// Nothing here decides a price or changes gold/items. Every buy and sell
    /// is a request; the server re-prices it from its own table, and the
    /// result arrives as W2CGoldUpdate + W2CInventorySlotChanged + a
    /// W2CShopResult message.
    ///
    /// Closes itself if you walk away from the merchant or they despawn -
    /// the server would refuse the trades anyway, this just stops the
    /// window lying about it.
    ///
    /// Scene setup - standard UIPanel split:
    ///   ShopPanel (ALWAYS ACTIVE - this component, plus UILayer = Windows)
    ///   └── Window (assign to `window`)
    ///       ├── Title (TMP)             -> titleLabel
    ///       ├── Gold  (TMP)             -> goldLabel
    ///       ├── Hint  (TMP, optional)   -> hintLabel
    ///       └── Rows  (VerticalLayoutGroup) -> rowParent
    /// </summary>
    public class ShopPanelUI : UIPanel
    {
        public static ShopPanelUI Instance { get; private set; }

        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text goldLabel;
        [SerializeField] private TMP_Text hintLabel;

        [SerializeField] private ShopRowUI rowPrefab;
        [SerializeField] private Transform rowParent;

        [Tooltip("Opened alongside the shop so you can sell. Assign InventoryPanel. Optional.")]
        [SerializeField] private UIPanel inventoryPanel;

        [Tooltip("Shift+click Buy buys this many.")]
        [SerializeField, Min(1)] private int shiftBuyQuantity = 10;

        [Tooltip("Sales worth at least this much gold ask for confirmation. 0 = always ask.")]
        [SerializeField, Min(0)] private int confirmSellAtOrAbove = 50;

        [Tooltip("Extra distance past the merchant's interact range before the window closes itself.")]
        [SerializeField] private float walkAwaySlack = 1.5f;

        private int _npcNetworkId;
        private readonly Dictionary<int, ShopItemData> _byItem = new();
        private readonly List<ShopRowUI> _rows = new();

        private PlayerInteraction _localPlayer;

        // One cached delegate, so "is the interceptor still ours?" is a
        // plain reference comparison.
        private Func<InventorySlotUI, bool> _sellInterceptor;

        public bool IsTrading => IsVisible && _npcNetworkId != 0;

        protected override void Awake()
        {
            Instance = this;
            _sellInterceptor = TrySellFromSlot;
            base.Awake();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void OnEnable()
        {
            PlayerStatEvents.OnGoldChanged += SetGold;
        }

        private void OnDisable()
        {
            PlayerStatEvents.OnGoldChanged -= SetGold;
            ReleaseRightClick();
        }

        // ── Opening ──────────────────────────────────────────────────

        public void Present(W2CShopOpenPacket packet)
        {
            _npcNetworkId = packet.NpcNetworkId;

            if (titleLabel != null)
                titleLabel.text = packet.ShopName;

            if (hintLabel != null)
                hintLabel.text = $"Right-click items in your bag to sell. Shift+click Buy for {shiftBuyQuantity}.";

            RebuildRows(packet.Items ?? Array.Empty<ShopItemData>());

            Open();

            if (inventoryPanel != null)
                inventoryPanel.Open();
        }

        protected override void OnOpened()
        {
            SetGold(LocalCharacterState.Gold);

            // Right-click in the bag now means "sell here".
            InventoryPanelUI.RightClickInterceptor = _sellInterceptor;
        }

        protected override void OnClosed()
        {
            ItemTooltipUI.Hide();
            ReleaseRightClick();
            _npcNetworkId = 0;
        }

        private void ReleaseRightClick()
        {
            // Only clear it if it's still ours - another window may have
            // taken it since.
            if (_sellInterceptor != null && InventoryPanelUI.RightClickInterceptor == _sellInterceptor)
                InventoryPanelUI.RightClickInterceptor = null;
        }

        private void RebuildRows(ShopItemData[] items)
        {
            foreach (var row in _rows)
                if (row != null) Destroy(row.gameObject);

            _rows.Clear();
            _byItem.Clear();

            foreach (var item in items)
            {
                _byItem[item.ItemTemplateId] = item;

                if (rowPrefab == null || rowParent == null)
                    continue;

                var row = Instantiate(rowPrefab, rowParent);
                row.Bind(item, OnBuyClicked);
                _rows.Add(row);
            }

            if (rowPrefab == null || rowParent == null)
                Debug.LogWarning("[ShopPanelUI] Row Prefab or Row Parent not assigned - rows can't be shown.", this);
        }

        // ── Walk-away ────────────────────────────────────────────────

        private void Update()
        {
            if (!IsTrading)
                return;

            if (_localPlayer == null)
                _localPlayer = FindFirstObjectByType<PlayerInteraction>();

            if (NpcRegistry.Instance == null ||
                !NpcRegistry.Instance.TryGetNpc(_npcNetworkId, out var npc) || npc == null)
            {
                Close();
                return;
            }

            if (_localPlayer == null)
                return;

            float range = npc.TryGetComponent(out InteractableIdentity id) ? id.InteractRange : 4f;
            float distance = Vector3.Distance(_localPlayer.transform.position, npc.transform.position);

            if (distance > range + walkAwaySlack)
                Close();
        }

        // ── Buy ──────────────────────────────────────────────────────

        private void OnBuyClicked(ShopItemData item)
        {
            if (!IsTrading || item.BuyPrice <= 0)
                return;

            var peer = ClientNetwork.Instance?.ServerPeer;
            if (peer == null)
                return;

            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            int quantity = shift ? shiftBuyQuantity : 1;

            C2WShopBuyPacketSender.Send(peer, _npcNetworkId, item.ItemTemplateId, quantity);
        }

        // ── Sell ─────────────────────────────────────────────────────

        /// <summary>InventoryPanelUI's right-click, while trading. Always "handled".</summary>
        private bool TrySellFromSlot(InventorySlotUI slot)
        {
            if (!IsTrading)
                return false; // not trading - let the item be used normally

            if (slot.IsEmpty)
                return true;

            if (!_byItem.TryGetValue(slot.ItemTemplateId, out var row) || row.SellPrice <= 0)
            {
                HudMessageDisplay.QueueOrShow("The merchant won't buy that.");
                return true;
            }

            // Captured now: the dialog can sit open while the server moves
            // things, so only sell if the slot still holds what was asked about.
            int index = slot.Index;
            int itemId = slot.ItemTemplateId;
            int quantity = slot.Quantity;
            int npcId = _npcNetworkId;
            long total = (long)row.SellPrice * quantity;

            void Sell()
            {
                if (!IsTrading || _npcNetworkId != npcId)
                    return;

                var peer = ClientNetwork.Instance?.ServerPeer;
                if (peer == null)
                    return;

                // quantity 0 = whole stack, which is what the player saw.
                C2WShopSellPacketSender.Send(peer, npcId, index, 0);
            }

            if (total < confirmSellAtOrAbove && confirmSellAtOrAbove > 0)
            {
                Sell();
                return true;
            }

            bool shown = ConfirmDialog.Ask(
                "Sell item?",
                $"Sell {quantity}x {row.ItemName} for {total}g?",
                () =>
                {
                    if (slot == null || slot.Index != index || slot.ItemTemplateId != itemId)
                    {
                        HudMessageDisplay.QueueOrShow("That slot changed - nothing was sold.");
                        return;
                    }

                    Sell();
                },
                confirmText: "Sell");

            if (!shown)
                Debug.LogWarning("[ShopPanelUI] ConfirmDialog missing - sale cancelled.", this);

            return true;
        }

        /// <summary>
        /// Tooltip line for an item in the player's bag while this merchant is
        /// open - "Sells for 4g each (264g)", or "won't buy this". Null when no
        /// shop is open, so the tooltip simply leaves the line out.
        /// </summary>
        public static string DescribeSale(int itemTemplateId, int quantity)
        {
            var shop = Instance;
            if (shop == null || !shop.IsTrading || itemTemplateId <= 0)
                return null;

            if (!shop._byItem.TryGetValue(itemTemplateId, out var row) || row.SellPrice <= 0)
                return "The merchant won't buy this.";

            if (quantity <= 1)
                return $"Sells for {row.SellPrice}g";

            long total = (long)row.SellPrice * quantity;
            return $"Sells for {row.SellPrice}g each ({total}g)";
        }

        private void SetGold(int gold)
        {
            if (goldLabel != null)
                goldLabel.text = $"Your gold: {gold}g";
        }
    }
}
