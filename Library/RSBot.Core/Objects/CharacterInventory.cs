using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Components;
using RSBot.Core.Components.Tracing;
using RSBot.Core.Network;

namespace RSBot.Core.Objects;

/// <summary>
///     The Character's Invetory with EquippedPart and NormalPart.
/// </summary>
public class CharacterInventory : InventoryItemCollection
{
    /// <summary>
    ///     Minimum slot of NormalPart.
    /// </summary>
    public static byte NORMAL_PART_MIN_SLOT
    {
        get
        {
            return (
                Game.ClientType == GameClientType.Global
                || Game.ClientType == GameClientType.Korean
                || Game.ClientType == GameClientType.VTC_Game
                || Game.ClientType == GameClientType.RuSro
                || Game.ClientType == GameClientType.Turkey
                || Game.ClientType == GameClientType.Taiwan
                || Game.ClientType == GameClientType.Japanese
            )
                ? (byte)17 //4 slots for relics
                : (byte)13;
        }
    }

    /// <summary>
    ///     The constructor.
    /// </summary>
    /// <param name="size">The size.</param>
    public CharacterInventory(Packet packet)
        : base(packet) { }

    /// <summary>
    ///     Gets the size of NormalPart.
    /// </summary>
    public byte NormalPartSize => (byte)(Capacity - NORMAL_PART_MIN_SLOT);

    /// <summary>
    ///     Gets a value indicating whether the NormalPart is full.
    /// </summary>
    /// <value>
    ///     <c>true</c> if the NormalPart is full; otherwise, <c>false</c>.
    /// </value>
    public override bool Full => GetNormalPartItems().Count >= NormalPartSize;

    /// <summary>
    ///     Gets a value indicating whether this instance is sorting.
    /// </summary>
    /// <value>
    ///     <c>true</c> if this instance is sorting; otherwise, <c>false</c>.
    /// </value>
    private int _sorting;
    public bool IsSorting => Volatile.Read(ref _sorting) != 0;

    /// <summary>
    ///     Gets the number of free slots in NormalPart inventory.
    /// </summary>
    public new byte FreeSlots => (byte)(NormalPartSize - GetNormalPartItems().Count);

    /// <summary>
    ///     Gets the first free slot number inside NormalPart.
    /// </summary>
    /// <returns>if found: the first free slot number; otherwise: 0</returns>
    public override byte GetFreeSlot()
    {
        for (var slot = NORMAL_PART_MIN_SLOT; slot < Capacity; slot++)
            if (GetItemAt(slot) == null)
                return slot;

        return 0;
    }

    /// <summary>
    ///     Gets items of EquippedPart, ordered by slot.
    /// </summary>
    /// <returns>If found: list of item(s), ordered by slot; otherwise empty list</returns>
    public ICollection<InventoryItem> GetEquippedPartItems()
    {
        return GetItems(item => item.Slot < NORMAL_PART_MIN_SLOT);
    }

    /// <summary>
    ///     Gets items of NormalPart, ordered by slot.
    /// </summary>
    /// <returns>If found: list of item(s), ordered by slot; otherwise empty list</returns>
    public ICollection<InventoryItem> GetNormalPartItems()
    {
        return GetItems(item => item.Slot >= NORMAL_PART_MIN_SLOT);
    }

    /// <summary>
    ///     Gets items of NormalPart, ordered by slot.
    /// </summary>
    /// <returns>If found: list of item(s), ordered by slot; otherwise empty list</returns>
    public ICollection<InventoryItem> GetNormalPartItems(Predicate<InventoryItem> predicate)
    {
        return GetItems(item => item.Slot >= NORMAL_PART_MIN_SLOT && predicate(item));
    }

    /// <summary>
    ///     Gets items of NormalPart by ItemId, ordered by slot.
    /// </summary>
    /// <param name="itemId">The identifier of item.</param>
    /// <returns>If found: list of item(s), ordered by slot; otherwise empty list</returns>
    public ICollection<InventoryItem> GetNormalPartItems(uint itemId)
    {
        return GetItems(item => item.Slot >= NORMAL_PART_MIN_SLOT && item.ItemId == itemId);
    }

    /// <summary>
    ///     Moves the item inside Character's Inventory.
    /// </summary>
    /// <param name="sourceSlot">The source slot.</param>
    /// <param name="destinationSlot">The destination slot.</param>
    /// <param name="amount">The amount.</param>
    /// <returns><c>true</c> if successfully moved; otherwise, <c>false</c>.</returns>
    public bool MoveItem(byte sourceSlot, byte destinationSlot, ushort amount = 0)
        => MoveItem(sourceSlot, destinationSlot, amount, 500);

    private bool MoveItem(byte sourceSlot, byte destinationSlot, ushort amount, int timeout)
    {
        var itemAtSource = GetItemAt(sourceSlot);
        if (itemAtSource == null)
            return false;

        if (amount == 0)
            amount = itemAtSource.Amount;

        var packet = new Packet(0x7034);
        packet.WriteByte(0); //kinda flag
        packet.WriteByte(sourceSlot);
        packet.WriteByte(destinationSlot);
        packet.WriteUShort(amount);

        var asyncResult = new AwaitCallback(
            response =>
            {
                var result = response.ReadByte();
                if (result == 0x01)
                {
                    var operation = response.ReadByte();
                    if (operation != 0)
                        return AwaitCallbackResult.ConditionFailed;

                    var source = response.ReadByte();
                    var destination = response.ReadByte();
                    if (source == sourceSlot && destination == destinationSlot)
                        return AwaitCallbackResult.Success;
                    return AwaitCallbackResult.ConditionFailed;
                }

                return AwaitCallbackResult.Fail;
            },
            0xB034
        );

        PacketManager.SendPacket(packet, PacketDestination.Server, asyncResult);
        asyncResult.AwaitResponse(timeout);

        return asyncResult.IsCompleted;
    }

    public void Sort()
    {
        if (Game.Player?.Inventory != this || Game.Player.InAction || Game.Player.Exchanging
            || PickupManager.RunningPlayerPickup || PickupManager.RunningAbilityPetPickup
            || Interlocked.CompareExchange(ref _sorting, 1, 0) != 0)
            return;

        try
        {
            if (PickupManager.RunningPlayerPickup || PickupManager.RunningAbilityPetPickup)
                return;
            MergeStacks(false);
        }
        finally
        {
            Interlocked.Exchange(ref _sorting, 0);
        }
    }

    public void Organize()
    {
        if (!CanOrganize() || Interlocked.CompareExchange(ref _sorting, 1, 0) != 0)
        {
            Log.Notify("Inventory organization requires an idle character and a stopped bot.");
            return;
        }

        try
        {
            if (!CanOrganize())
                return;
            Log.Notify("Organizing inventory...");
            if (!MergeStacks(true))
                return;

            var expected = Snapshot();
            var ordered = GetNormalPartItems().OrderBy(i => GetOrganizationCategory(i.Record))
                .ThenBy(i => i.Record.CodeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.ItemId).ThenByDescending(i => i.Amount).ThenBy(i => i.Slot).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                if (!CanOrganize() || !Matches(expected))
                {
                    Log.Notify("Inventory organization stopped: character or inventory changed.");
                    return;
                }
                var item = ordered[index];
                var slot = (byte)(NORMAL_PART_MIN_SLOT + index);
                if (item.Slot != slot && !MoveForOrganization(item.Slot, slot, item.Amount, expected))
                    return;
            }
            Log.Notify("Inventory organization finished.");
        }
        finally
        {
            Interlocked.Exchange(ref _sorting, 0);
        }
    }

    private bool CanOrganize()
    {
        var player = Game.Player;
        var trace = TraceManager.Current;
        return Game.Ready && player?.Inventory == this
            && Kernel.Bot?.Running != true && !player.InAction && !player.Exchanging
            && !SkillManager.IsCasting && !PickupManager.RunningPlayerPickup && !PickupManager.RunningAbilityPetPickup
            && (trace == null || trace.State == TraceState.Stopped)
            && !ScriptManager.Running && !ShoppingManager.Running
            && player.State.LifeState != LifeState.Dead && !player.Movement.Moving
            && player.State.ScrollState != ScrollState.NormalScroll
            && player.State.ScrollState != ScrollState.ThiefScroll
            && player.Teleportation?.IsTeleporting != true;
    }

    private static int GetOrganizationCategory(RefObjItem record)
    {
        if (record.IsPotion || record.IsPurificationPill || record.IsUniversalPill || record.IsAbnormalPotion) return 0;
        if (record.IsAmmunition) return 1;
        if (record.IsEquip || record.IsJobEquip || record.IsFellowEquip) return 2;
        if (record.IsQuest) return 3;
        if (record.IsPet) return 4;
        return 5;
    }

    private Dictionary<byte, (InventoryItem Item, uint ItemId, ushort Amount)> Snapshot() =>
        this.ToDictionary(i => i.Slot, i => (i, i.ItemId, i.Amount));

    private bool Matches(Dictionary<byte, (InventoryItem Item, uint ItemId, ushort Amount)> expected) =>
        Count == expected.Count && this.All(i => expected.TryGetValue(i.Slot, out var entry)
            && ReferenceEquals(i, entry.Item) && i.ItemId == entry.ItemId && i.Amount == entry.Amount);

    private bool MoveForOrganization(byte source, byte destination, ushort amount,
        Dictionary<byte, (InventoryItem Item, uint ItemId, ushort Amount)> expected)
    {
        if (!CanOrganize() || !Matches(expected))
        {
            Log.Notify("Inventory organization stopped: character or inventory changed.");
            return false;
        }

        var from = expected[source];
        expected.TryGetValue(destination, out var to);
        if (to.Item != null && to.Item.ItemId == from.Item.ItemId
            && to.Amount + amount <= to.Item.Record.MaxStack)
        {
            expected[destination] = (to.Item, to.ItemId, (ushort)(to.Amount + amount));
            if (amount == from.Amount) expected.Remove(source);
            else expected[source] = (from.Item, from.ItemId, (ushort)(from.Amount - amount));
        }
        else
        {
            expected[destination] = from;
            if (to.Item == null) expected.Remove(source);
            else expected[source] = to;
        }

        // The server handler may finish updating local slots after the callback wakes this worker.
        if (!MoveItem(source, destination, amount, 5_000)
            || !SpinWait.SpinUntil(() => Matches(expected), 500) || !CanOrganize())
        {
            Log.Notify("Inventory organization stopped: move was not confirmed or inventory changed.");
            return false;
        }
        return true;
    }

    private bool MergeStacks(bool organize)
    {
        Log.Debug("Sorting the character inventory...");

        //Use iterations to avoid deadlocks!
        var maxIterations = organize ? NormalPartSize * 2 : 10;
        var iterations = 0;

        //Ignore items which move operations failed in the next iteration
        var blacklistedItems = new List<uint>(4);

        var firstSlot = NORMAL_PART_MIN_SLOT;
        var expected = organize ? Snapshot() : null;

        for (var iIteration = 0; iIteration < maxIterations; iIteration++)
        {
            if (Game.Player?.Inventory != this || Game.Player.InAction || Game.Player.Exchanging)
                return false;
            iterations++;

            var itemsToStackGroups = this.Where(i =>
                    i.Slot >= firstSlot
                    && i.Record.IsStackable
                    && i.Record.MaxStack > i.Amount
                    && !blacklistedItems.Contains(i.ItemId)
                )
                .GroupBy(i => i.ItemId);

            if (!itemsToStackGroups.Any())
                break;

            var itemsToStack = itemsToStackGroups.FirstOrDefault(g => g.Count() >= 2)?.OrderBy(i => i.Slot).ToList();

            if (itemsToStack == null)
                break;

            var source = itemsToStack.FirstOrDefault();
            if (source == null)
                continue;

            var destination = itemsToStack.FirstOrDefault(i => i.Record.ID == source.ItemId && i.Slot != source.Slot);
            if (destination == null)
                continue;

            var amount = destination.Record.MaxStack - destination.Amount;
            var actualAmount = source.Amount > amount ? amount : source.Amount;

            if (organize)
            {
                if (!MoveForOrganization(source.Slot, destination.Slot, (ushort)actualAmount, expected))
                    return false;
            }
            else if (!MoveItem(source.Slot, destination.Slot, (ushort)actualAmount))
                blacklistedItems.Add(source.ItemId);
        }

        Log.Debug($"Sorting finished after {iterations}/{maxIterations}");
        return true;
    }
}
