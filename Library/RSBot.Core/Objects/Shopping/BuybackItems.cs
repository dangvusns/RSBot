using System.Collections.Generic;

namespace RSBot.Core.Objects.Shopping;

/// <summary>Owns separate snapshots of sold items and preserves any partially restored quantity.</summary>
public static class BuybackItems
{
    public static bool RecordSale(Dictionary<byte, InventoryItem> items, byte slot, InventoryItem source, ushort quantity)
    {
        if (source == null || quantity == 0 || quantity > source.Amount)
            return false;
        var sold = source.CloneDetached();
        sold.Amount = quantity;
        items[slot] = sold;
        return true;
    }

    public static bool TryRestore(Dictionary<byte, InventoryItem> items, byte sourceSlot, byte destinationSlot,
        ushort quantity, out InventoryItem restored)
    {
        restored = null;
        if (!items.TryGetValue(sourceSlot, out var source) || quantity == 0 || quantity > source.Amount)
            return false;

        restored = source.CloneDetached();
        restored.Slot = destinationSlot;
        restored.Amount = quantity;
        if (quantity < source.Amount)
        {
            source.Amount -= quantity;
            return true;
        }

        // Build shifts before mutating the dictionary so no entry is lost while another is moved.
        var shifted = new Dictionary<byte, InventoryItem>();
        foreach (var entry in items)
            if (entry.Key != sourceSlot)
                shifted.Add(entry.Key > sourceSlot ? (byte)(entry.Key - 1) : entry.Key, entry.Value);
        items.Clear();
        foreach (var entry in shifted)
            items.Add(entry.Key, entry.Value);
        return true;
    }
}
