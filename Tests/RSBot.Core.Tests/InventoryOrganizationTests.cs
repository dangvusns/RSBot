using System.Reflection;
using RSBot.Core.Client;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using Xunit;

namespace RSBot.Core.Tests;

[Collection("Packet callbacks")]
public class InventoryOrganizationTests
{
    [Theory]
    [InlineData(GameClientType.Vietnam, 13)]
    [InlineData(GameClientType.Chinese, 13)]
    [InlineData(GameClientType.Global, 17)]
    [InlineData(GameClientType.Korean, 17)]
    [InlineData(GameClientType.VTC_Game, 17)]
    [InlineData(GameClientType.RuSro, 17)]
    [InlineData(GameClientType.Turkey, 17)]
    [InlineData(GameClientType.Taiwan, 17)]
    [InlineData(GameClientType.Japanese, 17)]
    public void NormalPartStartsAfterClientEquipmentSlots(GameClientType client, byte firstSlot)
    {
        var previous = Game.ClientType;
        try
        {
            Game.ClientType = client;
            Assert.Equal(firstSlot, CharacterInventory.NORMAL_PART_MIN_SLOT);
        }
        finally
        {
            Game.ClientType = previous;
        }
    }

    [Theory]
    [InlineData(3, 1, 1, 0)] // potion
    [InlineData(3, 4, 0, 1)] // ammunition
    [InlineData(1, 6, 0, 2)] // weapon
    [InlineData(3, 9, 0, 3)] // quest item
    [InlineData(2, 1, 2, 4)] // grab pet
    [InlineData(3, 8, 0, 5)] // trade goods
    public void CategoryOrderSeparatesSuppliesEquipmentAndQuestItems(byte type2, byte type3, byte type4, int expected)
    {
        var record = new RefObjItem { TypeID2 = type2, TypeID3 = type3, TypeID4 = type4 };
        var category = typeof(CharacterInventory).GetMethod("GetOrganizationCategory", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.Equal(expected, (int)category.Invoke(null, new object[] { record }));
    }

    [Fact]
    public void SnapshotRejectsChangedAmountsSlotsAndReplacementItems()
    {
        using var packet = new Packet(0, false, false, new byte[] { 32, 0 });
        packet.Lock();
        var inventory = new CharacterInventory(packet);
        var item = new InventoryItem { Slot = 17, ItemId = 42, Amount = 5 };
        inventory.Add(item);
        var snapshot = typeof(CharacterInventory).GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(inventory, null);
        var matches = typeof(CharacterInventory).GetMethod("Matches", BindingFlags.Instance | BindingFlags.NonPublic);
        bool Matches() => (bool)matches.Invoke(inventory, new[] { snapshot });
        Assert.True(Matches());
        item.Amount = 4;
        Assert.False(Matches());
        item.Amount = 5;
        item.ItemId = 43;
        Assert.False(Matches());
        item.ItemId = 42;
        item.Slot = 18;
        Assert.False(Matches());
        item.Slot = 17;
        inventory.Remove(item);
        inventory.Add(new InventoryItem { Slot = 17, ItemId = 42, Amount = 5 });
        Assert.False(Matches());
    }

    [Fact]
    public void OrganizationRequiresSameSessionStoppedBotAndIdleLivingCharacter()
    {
        var previousPlayer = Game.Player;
        var previousReady = Game.Ready;
        var previousReferences = Game.ReferenceManager;
        var previousBot = Kernel.Bot;
        var playerProperty = typeof(Game).GetProperty(nameof(Game.Player));
        var readyProperty = typeof(Game).GetProperty(nameof(Game.Ready));
        using var packet = new Packet(0, false, false, new byte[] { 32, 0 });
        packet.Lock();
        var inventory = new CharacterInventory(packet);
        var guard = typeof(CharacterInventory).GetMethod("CanOrganize", BindingFlags.Instance | BindingFlags.NonPublic);
        bool CanOrganize() => (bool)guard.Invoke(inventory, null);
        try
        {
            Game.ReferenceManager = new ReferenceManager();
            var player = new Player(0) { Inventory = inventory };
            player.State.LifeState = LifeState.Alive;
            playerProperty.SetValue(null, player);
            readyProperty.SetValue(null, true);
            Kernel.Bot = new Bot();
            Assert.True(CanOrganize());
            Kernel.Bot.Running = true;
            Assert.False(CanOrganize());
            Kernel.Bot.Running = false;
            player.InAction = true;
            Assert.False(CanOrganize());
            player.InAction = false;
            player.Movement.Moving = true;
            Assert.False(CanOrganize());
            player.Movement.Moving = false;
            player.Teleportation = new Teleportation { IsTeleporting = true };
            Assert.False(CanOrganize());
            player.Teleportation.IsTeleporting = false;
            player.State.LifeState = LifeState.Dead;
            Assert.False(CanOrganize());
            player.State.LifeState = LifeState.Alive;
            readyProperty.SetValue(null, false);
            Assert.False(CanOrganize());
            readyProperty.SetValue(null, true);
            playerProperty.SetValue(null, null);
            Assert.False(CanOrganize());
        }
        finally
        {
            playerProperty.SetValue(null, previousPlayer);
            readyProperty.SetValue(null, previousReady);
            Game.ReferenceManager = previousReferences;
            Kernel.Bot = previousBot;
        }
    }
}
