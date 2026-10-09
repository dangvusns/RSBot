using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Core.Components;

public class PickupManager
{
    private static int _playerBusy;
    private static int _petBusy;
    private static int _generation;

    public static bool SeparateRules => PlayerConfig.Get("RSBot.Items.Pickup.SeparateRules", false);
    public static bool PlayerPaused => PlayerConfig.Get("RSBot.Items.Pickup.PausePlayer", false);
    public static bool FallbackWithoutPet => PlayerConfig.Get("RSBot.Items.Pickup.FallbackWithoutPet", true);
    public static int PetRadius => Math.Clamp(PlayerConfig.Get("RSBot.Items.Pickup.PetRadius", 0), 0, 100);

    public static PickupCategories LegacyCategories =>
        (PickupGold ? PickupCategories.Gold : 0)
        | (PickupRareItems ? PickupCategories.Rare : 0)
        | (PickupBlueItems ? PickupCategories.Blue : 0)
        | (PickupAnyEquips ? PickupCategories.Equipment : 0)
        | (PickupQuestItems ? PickupCategories.Quest : 0)
        | (PickupEverything ? PickupCategories.Everything : 0);

    public static PickupCategories Categories(bool pet) => (PickupCategories)PlayerConfig.Get(
        "RSBot.Items.Pickup." + (pet ? "PetCategories" : "PlayerCategories"), (int)LegacyCategories);
    /// <summary>
    ///     Gets or sets a value indicating whether this <see cref="PickupManager" /> is running.
    /// </summary>
    /// <value>
    ///     <c>true</c> if running; otherwise, <c>false</c>.
    /// </value>
    public static bool RunningPlayerPickup => Volatile.Read(ref _playerBusy) != 0;

    /// <summary>
    ///     Gets or sets a value indicating whether this <see cref="PickupManager" /> is running for AbilityPet.
    /// </summary>
    /// <value>
    ///     <c>true</c> if running; otherwise, <c>false</c>.
    /// </value>
    public static bool RunningAbilityPetPickup => Volatile.Read(ref _petBusy) != 0;

    /// <summary>
    ///     Gets or sets the pickup items.
    /// </summary>
    /// <value>
    ///     The pickup items.
    /// </value>
    public static List<(string CodeName, bool PickOnlyChar)> PickupFilter { get; } = new();

    /// <summary>
    ///     Gets or sets a value indicating whether [pickup gold].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [pickup gold]; otherwise, <c>false</c>.
    /// </value>
    public static bool PickupGold => PlayerConfig.Get("RSBot.Items.Pickup.Gold", true);

    /// <summary>
    ///     Gets or sets a value indicating whether [pickup rare items].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [pickup rare items]; otherwise, <c>false</c>.
    /// </value>
    public static bool PickupRareItems => PlayerConfig.Get("RSBot.Items.Pickup.Rare", true);

    /// <summary>
    ///     Gets or sets a value indicating whether [pickup rare items].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [pickup rare items]; otherwise, <c>false</c>.
    /// </value>
    public static bool PickupBlueItems => PlayerConfig.Get("RSBot.Items.Pickup.Blue", true);

    /// <summary>
    ///     Gets or sets a value indicating whether [pickup quest items].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [pickup quest items]; otherwise, <c>false</c>.
    /// </value>
    public static bool PickupQuestItems => PlayerConfig.Get("RSBot.Items.Pickup.Quest", true);

    /// <summary>
    ///     Gets or sets a value indicating whether [pickup clean equips].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [pickup clean equips]; otherwise, <c>false</c>.
    /// </value>
    public static bool PickupAnyEquips => PlayerConfig.Get("RSBot.Items.Pickup.AnyEquips", true);

    /// <summary>
    ///     Gets or sets a value indicating whether [pickup everything].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [pickup everything]; otherwise, <c>false</c>.
    /// </value>
    public static bool PickupEverything => PlayerConfig.Get("RSBot.Items.Pickup.Everything", true);

    /// <summary>
    ///     Gets or sets a value indicating whether [use ability pet].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [use ability pet]; otherwise, <c>false</c>.
    /// </value>
    public static bool UseAbilityPet => PlayerConfig.Get("RSBot.Items.Pickup.EnableAbilityPet", true);

    /// <summary>
    ///     Gets or sets a value indicating whether [just pick my items].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [use ability pet]; otherwise, <c>false</c>.
    /// </value>
    public static bool JustPickMyItems => PlayerConfig.Get("RSBot.Items.Pickup.JustPickMyItems", false);

    /// <summary>
    ///     Runs the specified center position.
    /// </summary>
    /// <param name="playerPosition">The player position.</param>
    /// <param name="centerPosition">The center position.</param>
    /// <param name="radius">The radius.</param>
    public static void RunPlayer(Position playerPosition, Position centerPosition, int radius = 50)
    {
        var player = Game.Player;
        var generation = Volatile.Read(ref _generation);
        if (!Game.Ready || player == null || player.Inventory?.IsSorting == true || PlayerPaused
            || (UseAbilityPet && !player.HasActiveAbilityPet && !FallbackWithoutPet)
            || Interlocked.CompareExchange(ref _playerBusy, 1, 0) != 0)
            return;

        try
        {
            if (Game.Player != player || player.Inventory?.IsSorting == true)
                return;
            var flag = UseAbilityPet && Game.Player.HasActiveAbilityPet;
            if (
                !SpawnManager.TryGetEntities<SpawnedItem>(
                    i => Condition(i, centerPosition, radius, flag, flag, false),
                    out var entities
                )
            )
            {
                return;
            }

            foreach (
                var item in entities.OrderBy(item =>
                    item.Movement.Source.DistanceTo(playerPosition) /*.Take(5)*/
                )
            )
            {
                if (generation != Volatile.Read(ref _generation) || !Game.Ready || Game.Player != player || PlayerPaused)
                    return;

                while (Game.Player.InAction)
                {
                    if (generation != Volatile.Read(ref _generation) || !Game.Ready || Game.Player != player || PlayerPaused)
                        return;
                    Thread.Sleep(50);
                }

                if (!SpawnManager.TryGetEntity<SpawnedItem>(item.UniqueId, out var current) || current != item
                    || !Condition(item, centerPosition, radius, flag, flag, false))
                    continue;

                if (item.Record.IsSpecialtyGoodBox && Game.Player.Job2SpecialtyBag.Full)
                    continue;

                //Make sure the player is at the item's location
                //Game.Player.MoveTo(item.Movement.Source);
                item.Pickup();
            }
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
        finally
        {
            Interlocked.Exchange(ref _playerBusy, 0);
        }
    }

    public static async void RunAbilityPet(Position centerPosition, int radius = 50)
    {
        var player = Game.Player;
        var generation = Volatile.Read(ref _generation);
        if (!Game.Ready || player?.HasActiveAbilityPet != true || player.Inventory?.IsSorting == true || !UseAbilityPet
            || Interlocked.CompareExchange(ref _petBusy, 1, 0) != 0)
            return;

        try
        {
            if (Game.Player != player || player.Inventory?.IsSorting == true)
                return;
            var pet = player.AbilityPet;
            if (pet == null)
                return;
            if (
                !SpawnManager.TryGetEntities<SpawnedItem>(
                    i => Condition(i, centerPosition, radius, true, false, true),
                    out var entities
                )
            )
            {
                return;
            }

            foreach (
                var item in entities.OrderBy(item => item.Movement.Source.DistanceTo(pet.Position))
            )
            {
                if (generation != Volatile.Read(ref _generation) || !Game.Ready || Game.Player != player
                    || !player.HasActiveAbilityPet || player.AbilityPet != pet || !UseAbilityPet)
                    return;

                if (!SpawnManager.TryGetEntity<SpawnedItem>(item.UniqueId, out var current) || current != item
                    || !Condition(item, centerPosition, radius, true, false, true))
                    continue;

                if (item.Record.IsSpecialtyGoodBox && Game.Player.Job2SpecialtyBag.Full)
                    continue;

                await pet.PickupAsync(item.UniqueId);
                await Task.Yield();
            }
        }
        catch (Exception e)
        {
            Log.Fatal(e);
        }
        finally
        {
            Interlocked.Exchange(ref _petBusy, 0);
        }
    }

    private static bool Condition(
        SpawnedItem e,
        Position centerPosition,
        int radius,
        bool applyPickOnlyChar = false,
        bool pickOnlyChar = false,
        bool pet = false
    )
    {
        var player = Game.Player;
        if (!Game.Ready || player == null)
            return false;
        var activePet = player.AbilityPet;
        if (pet && activePet == null)
            return false;
        var playerJid = player.JID;

        if (JustPickMyItems && e.OwnerJID != playerJid)
            return false;

        // Check if Item is within the training area + tolerance
        const int tolerance = 15;
        if (e.Movement.Source.DistanceTo(centerPosition) > radius + tolerance)
            return false;

        if (pet && PetRadius > 0 && e.Movement.Source.DistanceTo(activePet.Position) > PetRadius)
            return false;

        if (applyPickOnlyChar && e.IsBehindObstacle)
            return false;

        bool isItemAutoShareParty = Game.Party.IsInParty &&
                            Game.Party.Settings.GetPartyType() is 2 or 3 or 6 or 7;

        if (!SeparateRules && isItemAutoShareParty && PickupGold && e.Record.IsGold)
        {
            if (!(applyPickOnlyChar && pickOnlyChar))
                return true;
        }

        if (e.HasOwner && e.OwnerJID != playerJid)
        {
            if (!isItemAutoShareParty)
                return false;

            if (e.Record.IsQuest && Game.Party.Members.Any(m => m.MemberId == e.OwnerJID))
                return false;
        }

        if (SeparateRules)
        {
            var filter = PickupFilter.FirstOrDefault(p => p.CodeName == e.Record.CodeName);
            if (pet && filter.PickOnlyChar)
                return false;
            if (!pet && activePet != null && UseAbilityPet && !filter.PickOnlyChar
                && !e.IsBehindObstacle
                && (PetRadius == 0 || e.Movement.Source.DistanceTo(activePet.Position) <= PetRadius)
                && (PickupPolicy.Allows(Categories(true), e.Record.IsGold, e.Record.IsQuest,
                    e.Record.IsEquip, (byte)e.Rarity) || filter.CodeName != null))
                return false;
            return PickupPolicy.Allows(Categories(pet), e.Record.IsGold, e.Record.IsQuest,
                e.Record.IsEquip, (byte)e.Rarity)
                || (filter.CodeName != null && (!pet || !filter.PickOnlyChar));
        }

        if (PickupGold && e.Record.IsGold && !(applyPickOnlyChar && pickOnlyChar))
            return true;

        if (
            (PickupRareItems && (byte)e.Rarity >= 2)
            || (PickupBlueItems && (byte)e.Rarity >= 1)
            || (PickupAnyEquips && e.Record.IsEquip)
            || (PickupQuestItems && e.Record.IsQuest)
            || PickupEverything
        )
            return true;

        return applyPickOnlyChar
            ? PickupFilter.Any(p => p.CodeName == e.Record.CodeName && p.PickOnlyChar == pickOnlyChar)
            : PickupFilter.Any(p => p.CodeName == e.Record.CodeName);
    }

    public static void AddFilter(string codeName, bool pickOnlyChar = false)
    {
        PickupFilter.RemoveAll(p => p.CodeName == codeName);
        PickupFilter.Add((codeName, pickOnlyChar));

        SaveFilter();
    }

    public static void RemoveFilter(string codeName)
    {
        PickupFilter.RemoveAll(p => p.CodeName == codeName);
        SaveFilter();
    }

    /// <summary>
    ///     Adds many items to the filter and saves once.
    /// </summary>
    public static void AddFilters(IEnumerable<string> codeNames, bool pickOnlyChar = false)
    {
        var set = codeNames.ToHashSet();

        PickupFilter.RemoveAll(p => set.Contains(p.CodeName));
        PickupFilter.AddRange(set.Select(codeName => (codeName, pickOnlyChar)));

        SaveFilter();
    }

    /// <summary>
    ///     Removes many items from the filter and saves once.
    /// </summary>
    public static void RemoveFilters(IEnumerable<string> codeNames)
    {
        var set = codeNames.ToHashSet();

        PickupFilter.RemoveAll(p => set.Contains(p.CodeName));
        SaveFilter();
    }

    public static void LoadFilter()
    {
        var config = PlayerConfig.GetArray<string>("RSBot.Shopping.Pickup");

        foreach (var item in config)
        {
            var split = item.Split('|');
            if (split.Length < 2)
                continue;

            PickupFilter.Add((split[0], Convert.ToBoolean(split[1])));
        }
    }

    public static void SaveFilter()
    {
        var array = PickupFilter.Select(p => $"{p.CodeName}|{p.PickOnlyChar}").ToArray();

        PlayerConfig.SetArray("RSBot.Shopping.Pickup", array);
    }

    /// <summary>
    ///     Stops this instance.
    /// </summary>
    public static void Stop()
    {
        Interlocked.Increment(ref _generation);
    }
}
