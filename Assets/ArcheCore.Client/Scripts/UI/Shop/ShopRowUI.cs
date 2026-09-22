using System;
using ArcheCore.Client.GameData;
using ArcheCore.Network.Shared.Packets.W2C;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArcheCore.Client.UI
{
    /// <summary>
    /// One line in the shop window: icon, name, what it costs, what the
    /// merchant pays for it, and a Buy button. Hovering the row shows the
    /// shared item tooltip with the prices. Pure display - ShopPanelUI
    /// decides what a click does.
    ///
    /// The row's background Image needs Raycast Target ON for the hover to
    /// register (the Buy button still gets its clicks - it's on top).
    /// </summary>
    public class ShopRowUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private TMP_Text buyPriceLabel;
        [SerializeField] private TMP_Text sellPriceLabel;
        [SerializeField] private Button buyButton;

        private ShopItemData _data;
        private Action<ShopItemData> _onBuy;

        public void Bind(ShopItemData data, Action<ShopItemData> onBuy)
        {
            _data = data;
            _onBuy = onBuy;

            bool forSale = data.BuyPrice > 0;

            if (iconImage != null)
            {
                var sprite = ItemIcons.Get(data.ItemTemplateId);
                iconImage.sprite = sprite;
                iconImage.enabled = sprite != null;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
            }

            if (nameLabel != null) nameLabel.text = data.ItemName;
            if (buyPriceLabel != null) buyPriceLabel.text = forSale ? $"{data.BuyPrice}g" : "-";
            if (sellPriceLabel != null) sellPriceLabel.text = data.SellPrice > 0 ? $"{data.SellPrice}g" : "-";

            if (buyButton != null)
            {
                // Runtime listener only - keep the prefab's On Click () list empty.
                buyButton.onClick.RemoveAllListeners();
                buyButton.onClick.AddListener(() => _onBuy?.Invoke(_data));

                // A row the merchant only BUYS (ore traders) can't be bought.
                buyButton.interactable = forSale;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_data == null)
                return;

            string price =
                _data.BuyPrice > 0 && _data.SellPrice > 0 ? $"Price: {_data.BuyPrice}g · Merchant pays {_data.SellPrice}g" :
                _data.BuyPrice > 0                        ? $"Price: {_data.BuyPrice}g" :
                _data.SellPrice > 0                       ? $"Not for sale · Merchant pays {_data.SellPrice}g" :
                                                            null;

            ItemTooltipUI.Show(this, _data.ItemTemplateId, 1, price);
        }

        public void OnPointerExit(PointerEventData eventData) => ItemTooltipUI.Hide(this);

        private void OnDisable() => ItemTooltipUI.Hide(this);
    }
}
