using System;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.UI.Events
{
    /// <summary>
    /// Same shape as PlayerStatEvents, kept as its own class rather than
    /// added to that one - inventory events fire far more often once
    /// looting exists, and a script that only cares about gold shouldn't
    /// recompile its subscriber list every time a slot changes.
    /// </summary>
    public static class PlayerInventoryEvents
    {
        /// <summary>Full inventory, on spawn.</summary>
        public static event Action<InventorySlotData[]> OnInventorySnapshot;
        public static void RaiseInventorySnapshot(InventorySlotData[] slots) =>
            OnInventorySnapshot?.Invoke(slots);

        /// <summary>One slot changed. ItemTemplateId 0 = now empty.</summary>
        public static event Action<int, int, int> OnSlotChanged; // index, itemTemplateId, quantity
        public static void RaiseSlotChanged(int index, int itemTemplateId, int quantity) =>
            OnSlotChanged?.Invoke(index, itemTemplateId, quantity);
    }
}
