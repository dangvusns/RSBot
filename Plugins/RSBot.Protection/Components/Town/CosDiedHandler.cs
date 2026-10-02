using System;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;

namespace RSBot.Protection.Components.Town;

public class CosDiedHandler : AbstractTownHandler
{
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
        EventManager.SubscribeEvent("OnUpdateEntityLifeState", new Action<uint>(OnUpdateEntityLifeState));
        EventManager.SubscribeEvent("OnUpdateInventoryItem", new Action<byte>(OnUpdateInventoryItem));
    }

    /// <summary>
    ///     A pet or transport died in the world.
    /// </summary>
    /// <param name="uniqueId">The unique identifier.</param>
    private static void OnUpdateEntityLifeState(uint uniqueId)
    {
        if (!Kernel.Bot.Running)
            return;

        if (!SpawnManager.TryGetEntityIncludingMe(uniqueId, out var entity) || entity.State.LifeState != LifeState.Dead)
            return;

        var player = Game.Player;
        if (uniqueId == player.Growth?.UniqueId || uniqueId == player.Fellow?.UniqueId)
            OnPetDied();
        else if (uniqueId == player.Transport?.UniqueId || uniqueId == player.JobTransport?.UniqueId)
            OnTransportDied();
    }

    /// <summary>
    ///     The pet item turns dead when the pet died, even if the pet already left the world.
    /// </summary>
    /// <param name="slot">The slot.</param>
    private static void OnUpdateInventoryItem(byte slot)
    {
        if (!Kernel.Bot.Running)
            return;

        var item = Game.Player.Inventory.GetItemAt(slot);
        if (item == null || item.State != InventoryItemState.Dead)
            return;

        if (item.Record.IsGrowthPet || item.Record.IsFellowPet)
            OnPetDied();
    }

    private static void OnPetDied()
    {
        if (!PlayerConfig.Get<bool>("RSBot.Protection.checkPetDied"))
            return;

        // The pet gets revived on the spot, no reason to go back
        if (
            PlayerConfig.Get<bool>("RSBot.Protection.checkReviveAttackPet")
            && Game.Player.Inventory.GetItem(p => p.Record.IsCosRevivalPotion) != null
        )
            return;

        ReturnToTown("ReturnToTownPetDied");
    }

    private static void OnTransportDied()
    {
        if (!PlayerConfig.Get<bool>("RSBot.Protection.checkTransportDied"))
            return;

        ReturnToTown("ReturnToTownTransportDied");
    }

    private static void ReturnToTown(string langKey)
    {
        if (PlayerInTownScriptRegion())
            return;

        // The pet death is reported twice (world and inventory), the scroll is only used once
        if (Game.Player.State.ScrollState != ScrollState.Cancel)
            return;

        Log.NotifyLang(langKey);
        Game.Player.UseReturnScroll();
    }
}
