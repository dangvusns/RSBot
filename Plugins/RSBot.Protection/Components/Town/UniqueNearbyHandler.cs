using System;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Protection.Components.Town;

public class UniqueNearbyHandler : AbstractTownHandler
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
        EventManager.SubscribeEvent("OnSpawnMonster", new Action<SpawnedMonster>(OnSpawnMonster));
    }

    /// <summary>
    /// </summary>
    /// <param name="monster">The spawned monster.</param>
    private static void OnSpawnMonster(SpawnedMonster monster)
    {
        if (!Kernel.Bot.Running)
            return;

        if (!PlayerConfig.Get<bool>("RSBot.Protection.checkUniqueNearby"))
            return;

        if (!IsUnique(monster.Rarity) || monster.State.LifeState != LifeState.Alive)
            return;

        if (PlayerInTownScriptRegion())
            return;

        Log.NotifyLang("ReturnToTownUniqueNearby", monster.Record?.GetRealName());
        Game.Player.UseReturnScroll();
    }

    private static bool IsUnique(MonsterRarity rarity)
    {
        return rarity
            is MonsterRarity.Unique
                or MonsterRarity.Unique2
                or MonsterRarity.UniqueParty
                or MonsterRarity.Unique2Party;
    }
}
