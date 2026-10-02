using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects.Cos;
using RSBot.Core.Objects.Inventory;

namespace RSBot.Protection.Components.Pet;

/// <summary>
///     Moves items the pick pet looted into the player inventory while botting: supplies (potions, pills, arrows and
///     bolts) right away, and everything once the pet inventory is full.
/// </summary>
public class PetTransferHandler
{
    /// <summary>
    ///     1 while items are being moved.
    /// </summary>
    private static int _busy;

    /// <summary>
    ///     Initializes this instance.
    /// </summary>
    public static void Initialize()
    {
        SubscribeEvents();
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private static void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnInventoryUpdate", OnInventoryUpdate);
    }

    /// <summary>
    ///     Gets a value indicating whether the pet's items are moved to the player when the pet inventory is full and
    ///     the player still has room for them.
    /// </summary>
    public static bool WillEmptyFullPet()
    {
        return PlayerConfig.Get<bool>("RSBot.Protection.checkPetTransferWhenFull")
            && Game.Player.AbilityPet != null
            && !Game.Player.Inventory.Full;
    }

    /// <summary>
    /// </summary>
    private static void OnInventoryUpdate()
    {
        if (!Kernel.Bot.Running)
            return;

        var pet = Game.Player.AbilityPet;
        if (pet?.Inventory == null)
            return;

        var moveSupplies = PlayerConfig.Get<bool>("RSBot.Protection.checkPetTransferSupplies");
        var moveAll = PlayerConfig.Get<bool>("RSBot.Protection.checkPetTransferWhenFull") && pet.Inventory.Full;
        if (!moveSupplies && !moveAll)
            return;

        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return;

        // Moving waits for the server, which never answers while the packet thread is blocked
        Task.Run(() =>
        {
            try
            {
                Transfer(pet, moveAll);
            }
            catch (Exception e)
            {
                Log.Fatal(e);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private static void Transfer(Ability pet, bool moveAll)
    {
        var items = pet.Inventory.GetItems(i => moveAll || IsSupply(i.Record)).ToList();
        if (items.Count == 0)
            return;

        if (moveAll)
            Log.Notify("The pick pet is full, moving its items to the inventory");

        foreach (var item in items)
        {
            if (!Kernel.Bot.Running || Game.Player.AbilityPet != pet)
                return;

            var freeSlot = Game.Player.Inventory.GetFreeSlot();
            if (freeSlot == 0xFF)
            {
                Log.Warn("The inventory is full, cannot move more items from the pick pet");
                return;
            }

            if (!MoveToPlayer(pet, item.Slot, freeSlot))
                return;
        }
    }

    private static bool IsSupply(RefObjItem record)
    {
        return record != null
            && (
                record.IsHpPotion
                || record.IsMpPotion
                || record.IsAllPotion
                || record.IsUniversalPill
                || record.IsPurificationPill
                || record.IsAmmunition
            );
    }

    /// <summary>
    ///     Moves an item from the pet inventory to the player inventory and waits for the server.
    /// </summary>
    private static bool MoveToPlayer(Ability pet, byte petSlot, byte inventorySlot)
    {
        var packet = new Packet(0x7034);
        packet.WriteByte(InventoryOperation.SP_MOVE_ITEM_PET_PC);
        packet.WriteUInt(pet.UniqueId);
        packet.WriteByte(petSlot);
        packet.WriteByte(inventorySlot);

        var callback = new AwaitCallback(
            response =>
            {
                if (response.ReadByte() != 0x01)
                    return AwaitCallbackResult.Fail;

                return response.ReadByte() == (byte)InventoryOperation.SP_MOVE_ITEM_PET_PC
                    ? AwaitCallbackResult.Success
                    : AwaitCallbackResult.ConditionFailed;
            },
            0xB034
        );

        PacketManager.SendPacket(packet, PacketDestination.Server, callback);
        callback.AwaitResponse();

        return callback.IsCompleted;
    }
}
