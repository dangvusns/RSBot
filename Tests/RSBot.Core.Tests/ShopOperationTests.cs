using RSBot.Core.Network;
using RSBot.Core.Objects.Inventory;
using RSBot.Core.Objects.Shopping;
using Xunit;

namespace RSBot.Core.Tests;

public class ShopOperationTests
{
    private static Packet Reply(byte[] data)
    {
        var packet = new Packet(0xB034, false, false, data);
        packet.Lock();
        return packet;
    }

    [Theory]
    [InlineData(GameClientType.Rigid, false)]
    [InlineData(GameClientType.Chinese, true)]
    public void PurchaseMatchesTabAndSlotForBothQuantityLayouts(GameClientType clientType, bool modern)
    {
        var matcher = new ShopResponseMatcher(InventoryOperation.SP_BUY_ITEM, 3, 20, 2);
        using var packet = Reply(modern
            ? new byte[] { 1, 8, 2, 3, 20, 0, 1, 13 }
            : new byte[] { 1, 8, 2, 3, 1, 13, 20, 0 });
        Assert.Equal(AwaitCallbackResult.Success, matcher.Match(packet, clientType));
    }

    [Theory]
    [InlineData(6, 2, 3)] // pickup
    [InlineData(8, 1, 3)] // wrong tab
    [InlineData(8, 2, 4)] // wrong shop slot
    public void UnrelatedInventorySuccessDoesNotConfirmPurchase(byte operation, byte tab, byte slot)
    {
        var matcher = new ShopResponseMatcher(InventoryOperation.SP_BUY_ITEM, 3, 20, 2);
        using var packet = Reply(new byte[] { 1, operation, tab, slot, 20, 0, 1, 13 });
        Assert.Equal(AwaitCallbackResult.ConditionFailed, matcher.Match(packet, GameClientType.Chinese));
    }

    [Fact]
    public void TransportPurchaseMustMatchActor()
    {
        var matcher = new ShopResponseMatcher(InventoryOperation.SP_BUY_ITEM_COS, 3, 20, 2, actorId: 99);
        using var packet = Reply(new byte[] { 1, 19, 98, 0, 0, 0 });
        Assert.Equal(AwaitCallbackResult.ConditionFailed, matcher.Match(packet, GameClientType.Chinese));
    }

    [Fact]
    public void SaleMatchesSourceQuantityAndNpc()
    {
        var matcher = new ShopResponseMatcher(InventoryOperation.SP_SELL_ITEM, 13, 20, npcId: 99);
        using var wrong = Reply(new byte[] { 1, 9, 13, 19, 0, 99, 0, 0, 0, 1 });
        Assert.Equal(AwaitCallbackResult.ConditionFailed, matcher.Match(wrong, GameClientType.Chinese));
        using var correct = Reply(new byte[] { 1, 9, 13, 20, 0, 99, 0, 0, 0, 1 });
        Assert.Equal(AwaitCallbackResult.Success, matcher.Match(correct, GameClientType.Chinese));
    }

    [Fact]
    public void InventoryErrorRetainsBothBytesWithoutClaimingConfirmation()
    {
        var matcher = new ShopResponseMatcher(InventoryOperation.SP_BUY_ITEM, 3, 20, 2);
        using var packet = Reply(new byte[] { 2, 0x32, 0x1C });
        Assert.Equal(AwaitCallbackResult.Fail, matcher.Match(packet, GameClientType.Chinese));
        Assert.Equal((ushort)0x1C32, matcher.ErrorCode);
        Assert.False(new ShopOperationResult(ShopOperationOutcome.Unconfirmed, matcher.ErrorCode).IsConfirmed);
    }

    [Fact]
    public void EmptyPurchaseCannotConfirm()
    {
        var matcher = new ShopResponseMatcher(InventoryOperation.SP_BUY_ITEM, 3, 20, 2);
        using var packet = Reply(new byte[] { 1, 8, 2, 3, 20, 0, 0 });
        Assert.Equal(AwaitCallbackResult.Fail, matcher.Match(packet, GameClientType.Chinese));
    }
}
