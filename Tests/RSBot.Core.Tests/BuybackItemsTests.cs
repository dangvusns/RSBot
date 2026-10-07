using System.Collections.Generic;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Item;
using RSBot.Core.Objects.Shopping;
using Xunit;

namespace RSBot.Core.Tests;

public class BuybackItemsTests
{
    [Fact]
    public void PartialSaleAndRestoreKeepIndependentQuantitiesAndMetadata()
    {
        var original = new InventoryItem
        {
            Slot = 13, ItemId = 42, Amount = 100,
            MagicOptions = new List<MagicOptionInfo> { new() { Id = 1, Value = 2 } },
            BindingOptions = new List<BindingOption> { new() { Id = 3, Value = 4 } },
            Rental = new RentInfo { Type = 1, CanDelete = 1 },
        };
        var items = new Dictionary<byte, InventoryItem>();
        Assert.True(BuybackItems.RecordSale(items, 0, original, 30));
        original.Amount = 70;
        original.MagicOptions[0].Value = 9;
        original.BindingOptions[0].Value = 9;
        original.Rental.CanDelete = 0;
        Assert.Equal((ushort)30, items[0].Amount);
        Assert.Equal((uint)2, items[0].MagicOptions[0].Value);
        Assert.Equal((uint)4, items[0].BindingOptions[0].Value);
        Assert.Equal((ushort)1, items[0].Rental.CanDelete);

        Assert.True(BuybackItems.TryRestore(items, 0, 14, 10, out var restored));
        Assert.Equal((ushort)20, items[0].Amount);
        Assert.Equal((ushort)10, restored.Amount);
        Assert.Equal((byte)14, restored.Slot);
        restored.MagicOptions[0].Value = 100;
        Assert.Equal((uint)2, items[0].MagicOptions[0].Value);
        Assert.Equal((ushort)70, original.Amount);
    }

    [Fact]
    public void FullRestoreShiftsLaterSlotsOnly()
    {
        var first = new InventoryItem { Amount = 5 };
        var later = new InventoryItem { Amount = 7 };
        var items = new Dictionary<byte, InventoryItem>
        {
            [0] = first, [1] = new() { Amount = 3 }, [2] = later,
        };
        Assert.True(BuybackItems.TryRestore(items, 1, 14, 3, out var restored));
        Assert.Equal(2, items.Count);
        Assert.Same(first, items[0]);
        Assert.Same(later, items[1]);
        Assert.Equal((ushort)3, restored.Amount);
    }

    [Theory]
    [InlineData(1, 1)] // unknown slot
    [InlineData(0, 0)] // zero quantity
    [InlineData(0, 6)] // more than sold
    public void InvalidRestoreDoesNotMutateKnownInventory(byte slot, ushort amount)
    {
        var source = new InventoryItem { Amount = 5 };
        var items = new Dictionary<byte, InventoryItem> { [0] = source };
        Assert.False(BuybackItems.TryRestore(items, slot, 14, amount, out var restored));
        Assert.Null(restored);
        Assert.Equal((ushort)5, source.Amount);
        Assert.Single(items);
    }
}
