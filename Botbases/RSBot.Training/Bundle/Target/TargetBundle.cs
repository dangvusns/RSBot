using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Extensions;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Training.Bundle.Target;

internal class TargetBundle : IBundle
{
    private const int BLACKLIST_TIMEOUT = 5_000;

    /// <summary>
    ///     How long an entity counts as fighting its target after its last skill cast on it.
    /// </summary>
    private const int FIGHT_TIMEOUT = 10_000;

    #region Fields

    private Dictionary<uint, int> _blacklist;

    #endregion Fields

    #region Constructor

    public TargetBundle()
    {
        SubscribeEvents();
    }

    #endregion Constructor

    #region Events

    private void OnTargetBehindObstacle()
    {
        if (Game.SelectedEntity == null)
            return;

        var selectedEntityUniqueId = Game.SelectedEntity.UniqueId;
        Game.SelectedEntity?.TryDeselect();
        Game.SelectedEntity = null;

        Bundles.Movement.LastEntityWasBehindObstacle = true;

        if (_blacklist?.TryAdd(selectedEntityUniqueId, Kernel.TickCount) == true)
            Log.Debug($"Add mob [{selectedEntityUniqueId} to blacklist for {BLACKLIST_TIMEOUT}ms");
    }

    #endregion Events

    #region Methods

    private void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnTargetBehindObstacle", OnTargetBehindObstacle);
    }

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        _blacklist?.RemoveAll(
            (uniqueId, tick) =>
            {
                var flag = Kernel.TickCount - tick > BLACKLIST_TIMEOUT;
                if (flag)
                    Log.Debug($"Removed mob [{uniqueId} from blacklist!");

                return flag;
            }
        );

        // Counterattacks stay inside the training area, unless the player follows the party master away from it
        var attacker = GetFromCurrentAttackers();
        if (attacker != null && !IsFollowingPartyMaster() && !Container.Bot.Area.IsInSight(attacker))
            attacker = null;

        if (attacker != null && Game.SelectedEntity == null)
        {
            Log.Debug("[TargetBundle] Emergency situation: Attacking the weaker mob first!");

            if (attacker.TrySelect())
                Bundles.Movement.LastEntityWasBehindObstacle = false;

            return;
        }

        if (
            attacker != null
            && SpawnManager.TryGetEntity<SpawnedMonster>(Game.SelectedEntity.UniqueId, out var selectedMonster)
            && (byte)attacker.Rarity < (byte)selectedMonster.Rarity
        )
        {
            Log.Debug("[TargetBundle] Emergency situation: Found a weaker mob to attack first, switching target!");

            if (attacker.TrySelect())
                Bundles.Movement.LastEntityWasBehindObstacle = false;

            return;
        }

        var warlockModeEnabled = PlayerConfig.Get("RSBot.Skills.checkWarlockMode", false);
        if (warlockModeEnabled && Game.SelectedEntity?.State.HasTwoDots() == true)
            return;

        if (Game.SelectedEntity != null && Game.SelectedEntity is not SpawnedMonster)
            Game.SelectedEntity = null;

        // Defend the pet: attack the monster that is hitting it, unless the selected monster is already hitting
        // the player or the pet
        var petAttacker = GetPetAttacker();
        if (
            petAttacker != null
            && !IsSelectedHitting(
                Game.Player.UniqueId,
                Game.Player.Growth?.UniqueId ?? 0,
                Game.Player.Fellow?.UniqueId ?? 0
            )
        )
        {
            Log.Debug($"[TargetBundle] Defending the pet against: {petAttacker.Record?.GetRealName()}");

            if (petAttacker.TrySelect())
                Bundles.Movement.LastEntityWasBehindObstacle = false;

            return;
        }

        // Leave the target to the other player who is fighting it
        if (
            PlayerConfig.Get("RSBot.Training.checkBoxSwitchTargetIfStolen", false)
            && Game.SelectedEntity != null
            && SpawnManager.TryGetEntity<SpawnedMonster>(Game.SelectedEntity.UniqueId, out var currentMonster)
            && currentMonster.State.LifeState == LifeState.Alive
            && GetMonstersFoughtByOthers().Contains(currentMonster.UniqueId)
        )
        {
            Log.Debug($"[TargetBundle] Another player attacks the target, switching: {currentMonster.Record?.GetRealName()}");

            Game.SelectedEntity?.TryDeselect();
            Game.SelectedEntity = null;

            _blacklist?.TryAdd(currentMonster.UniqueId, Kernel.TickCount);
        }

        // Assist: attack what the party leader attacks. Checked after the counterattack, so a member under attack
        // still defends itself first.
        var leaderTarget = GetPartyLeaderTarget();
        if (leaderTarget != null)
        {
            if (Game.SelectedEntity?.UniqueId != leaderTarget.UniqueId)
            {
                Log.Debug($"[TargetBundle] Attacking the party leader's target: {leaderTarget.Record?.GetRealName()}");

                if (leaderTarget.TrySelect())
                    Bundles.Movement.LastEntityWasBehindObstacle = false;
            }

            return;
        }

        if (Game.SelectedEntity?.State.LifeState == LifeState.Alive)
            return;

        var monster = GetNearestEnemy();
        if (monster == null)
            return;

        if (!Container.Bot.Area.IsInSight(monster))
            return;

        if (monster.TrySelect())
            Bundles.Movement.LastEntityWasBehindObstacle = false;
    }

    /// <summary>
    ///     Gets the monster the party leader is attacking, if the player assists the leader and the monster passes
    ///     the same checks as a normal target.
    /// </summary>
    private SpawnedMonster GetPartyLeaderTarget()
    {
        // The leader's target is only known from its skill casts; after this long without one it is out of date.
        const int staleTargetMs = 10_000;

        // While following the leader away from the training area, only assist close to the player,
        // otherwise the bot would run off and trigger the walk back to the training area.
        const float assistRange = 40f;

        if (!PlayerConfig.Get("RSBot.Party.AttackLeaderTarget", false) || !Game.Party.IsInParty || Game.Party.IsLeader)
            return null;

        var leader = Game.Party.Leader?.Player;
        if (leader == null || leader.TargetId == 0 || Kernel.TickCount - leader.TargetTick > staleTargetMs)
            return null;

        if (!SpawnManager.TryGetEntity<SpawnedMonster>(leader.TargetId, out var target))
            return null;

        var warlockModeEnabled = PlayerConfig.Get<bool>("RSBot.Skills.checkWarlockMode");
        var ignorePillar = PlayerConfig.Get<bool>("RSBot.Training.checkBoxDimensionPillar");

        if (
            target.State.LifeState != LifeState.Alive
            || (warlockModeEnabled && target.State.HasTwoDots())
            || target.IsBehindObstacle
            || (_blacklist != null && _blacklist.ContainsKey(target.UniqueId))
            || (!target.AttackingPlayer && Bundles.Avoidance.AvoidMonster(target.Rarity))
            || target.Record.IsPandora
            || (target.Record.IsDimensionPillar && ignorePillar)
            || target.Record.IsSummonFlower
        )
            return null;

        var inRange = Container.Bot.Area.IsInSight(target)
            || (IsFollowingPartyMaster() && target.DistanceToPlayer <= assistRange);

        return inRange ? target : null;
    }

    private static bool IsFollowingPartyMaster()
    {
        return PlayerConfig.Get("RSBot.Party.AlwaysFollowPartyMaster", false)
            && Game.Party.IsInParty
            && !Game.Party.IsLeader;
    }

    private SpawnedMonster GetFromCurrentAttackers()
    {
        var attackWeakerFirst = PlayerConfig.Get<bool>("RSBot.Training.checkAttackWeakerFirst");
        if (!attackWeakerFirst || !IsEmergencySituation())
            return null;

        if (
            !SpawnManager.TryGetEntities<SpawnedMonster>(
                e => e.AttackingPlayer && e.State.LifeState == LifeState.Alive,
                out var entities
            )
        )
            return null;

        return entities
            .OrderBy(e => (byte)e.Rarity)
            .OrderBy(e => e.Record.Level)
            .OrderByDescending(e => e.Position.DistanceToPlayer())
            .FirstOrDefault();
    }

    private bool IsEmergencySituation()
    {
        return SpawnManager.Any<SpawnedMonster>(e =>
            e.AttackingPlayer && e.State.LifeState == LifeState.Alive && Bundles.Avoidance.AvoidMonster(e.Rarity)
        );
    }

    /// <summary>
    ///     Gets the nearest enemy.
    /// </summary>
    /// <returns></returns>
    private SpawnedMonster GetNearestEnemy()
    {
        var warlockModeEnabled = PlayerConfig.Get<bool>("RSBot.Skills.checkWarlockMode");
        var ignorePillar = PlayerConfig.Get<bool>("RSBot.Training.checkBoxDimensionPillar");
        var foughtByOthers = PlayerConfig.Get("RSBot.Training.checkBoxAvoidKillSteal", false)
            ? GetMonstersFoughtByOthers()
            : null;

        if (
            !SpawnManager.TryGetEntities<SpawnedMonster>(
                m =>
                    m.State.LifeState == LifeState.Alive
                    && //Only alive
                    (foughtByOthers == null || !foughtByOthers.Contains(m.UniqueId))
                    && //Isn't fought by another player
                    !(warlockModeEnabled && m.State.HasTwoDots())
                    && //Has two Dots?
                    m.IsBehindObstacle == false
                    && //Is not behind obstacle
                    (_blacklist == null || !_blacklist.ContainsKey(m.UniqueId))
                    && //Is not blacklisted
                    (m.AttackingPlayer || !Bundles.Avoidance.AvoidMonster(m.Rarity))
                    && //Is attacking player or shouldn't be avoided
                    Container.Bot.Area.IsInSight(m)
                    && //Is in training area
                    !m.Record.IsPandora
                    && //Isn't pandora box
                    !(m.Record.IsDimensionPillar && ignorePillar)
                    && //Isn't dimension pillar
                    !m.Record.IsSummonFlower,
                out var entities
            )
        )
            return default;

        return entities
            .OrderBy(m => m.Movement.Source.DistanceTo(Container.Bot.Area.Position))
            .OrderBy(m => Bundles.Avoidance.PreferMonster(m.Rarity))
            .OrderByDescending(m => m.AttackingPlayer)
            .FirstOrDefault();
    }

    /// <summary>
    ///     Gets the unique ids of the monsters another player (or another player's pet) is fighting. A monster that
    ///     attacked the player is never in the list, the player defends itself.
    /// </summary>
    private static HashSet<uint> GetMonstersFoughtByOthers()
    {
        var result = new HashSet<uint>();

        if (!SpawnManager.TryGetEntities<SpawnedBionic>(e => e is SpawnedPlayer || e is SpawnedCos, out var bionics))
            return result;

        var strangers = new HashSet<uint>();
        foreach (var bionic in bionics.Where(IsStranger))
        {
            strangers.Add(bionic.UniqueId);

            // The stranger hit the monster
            if (bionic.TargetId != 0 && IsRecent(bionic.TargetTick))
                result.Add(bionic.TargetId);
        }

        if (strangers.Count == 0)
            return result;

        // The monster hits the stranger
        if (
            SpawnManager.TryGetEntities<SpawnedMonster>(
                m => !m.AttackingPlayer && IsRecent(m.TargetTick) && strangers.Contains(m.TargetId),
                out var monsters
            )
        )
            foreach (var monster in monsters)
                result.Add(monster.UniqueId);

        result.RemoveWhere(id =>
            SpawnManager.TryGetEntity<SpawnedMonster>(id, out var monster) && monster.AttackingPlayer
        );

        return result;
    }

    /// <summary>
    ///     Gets a value indicating whether the entity is neither the player, a party member nor a pet of them.
    /// </summary>
    private static bool IsStranger(SpawnedBionic entity)
    {
        return entity switch
        {
            SpawnedPlayer player => Game.Party.GetMemberByName(player.Name) == null,
            SpawnedCos cos => cos.OwnerUniqueId != Game.Player.UniqueId
                && (string.IsNullOrEmpty(cos.OwnerName) || Game.Party.GetMemberByName(cos.OwnerName) == null),
            _ => false,
        };
    }

    private static bool IsRecent(int tick)
    {
        return Kernel.TickCount - tick < FIGHT_TIMEOUT;
    }

    /// <summary>
    ///     Gets the nearest monster that is hitting the player's attack or fellow pet, if the player defends its pet.
    /// </summary>
    private SpawnedMonster GetPetAttacker()
    {
        if (!PlayerConfig.Get("RSBot.Training.checkBoxDefendPet", false))
            return null;

        var growthId = Game.Player.Growth?.UniqueId ?? 0;
        var fellowId = Game.Player.Fellow?.UniqueId ?? 0;
        if (growthId == 0 && fellowId == 0)
            return null;

        if (
            !SpawnManager.TryGetEntities<SpawnedMonster>(
                m =>
                    m.State.LifeState == LifeState.Alive
                    && m.TargetId != 0
                    && (m.TargetId == growthId || m.TargetId == fellowId)
                    && IsRecent(m.TargetTick)
                    && !m.IsBehindObstacle
                    && (_blacklist == null || !_blacklist.ContainsKey(m.UniqueId))
                    && Container.Bot.Area.IsInSight(m),
                out var attackers
            )
        )
            return null;

        return attackers.OrderBy(m => m.DistanceToPlayer).FirstOrDefault();
    }

    /// <summary>
    ///     Gets a value indicating whether the selected monster is currently hitting one of the specified entities.
    /// </summary>
    /// <param name="uniqueIds">The unique ids of the entities.</param>
    private static bool IsSelectedHitting(params uint[] uniqueIds)
    {
        return Game.SelectedEntity != null
            && SpawnManager.TryGetEntity<SpawnedMonster>(Game.SelectedEntity.UniqueId, out var monster)
            && monster.State.LifeState == LifeState.Alive
            && monster.TargetId != 0
            && uniqueIds.Contains(monster.TargetId)
            && IsRecent(monster.TargetTick);
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        _blacklist = new Dictionary<uint, int>(8);
    }

    public void Stop()
    {
        _blacklist = null;
    }

    #endregion Methods
}
