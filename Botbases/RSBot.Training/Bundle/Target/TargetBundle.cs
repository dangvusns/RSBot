using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private uint _committedAttackerId;
    private readonly Stack<uint> _interruptedTargets = new();
    private int _invoking;
    private int _lifecycleVersion;

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
        ReleaseCommittedAttacker("obstacle");
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
        // Event-driven invocations must not send another selection while a tick awaits confirmation.
        if (Interlocked.CompareExchange(ref _invoking, 1, 0) != 0)
            return;

        try
        {
            InvokeTargetSelection();
        }
        finally
        {
            Volatile.Write(ref _invoking, 0);
        }
    }

    private void InvokeTargetSelection()
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

        if (!Kernel.Bot.Running)
            return;

        if (Game.Player.State.LifeState != LifeState.Alive)
        {
            ReleaseCommittedAttacker("player not alive");
            _interruptedTargets.Clear();
            return;
        }

        // Validate commitment now; lower-type attackers may still interrupt it below.
        KeepCommittedAttacker();

        // Type-based interruption replaces the legacy emergency rule when enabled.
        var weakestEnabled = PlayerConfig.Get("RSBot.Training.checkBoxKillWeakestAttacker", false);
        var attacker = weakestEnabled ? null : GetFromCurrentAttackers();
        if (attacker != null && !Container.Bot.Area.IsInSight(attacker))
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
            && GetTypePriority(attacker.Rarity) >= 0
            && GetTypePriority(attacker.Rarity) < GetTypePriority(selectedMonster.Rarity)
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

        // Several monsters hit the player: kill the one that dies fastest first, so fewer keep hitting
        if (HandleWeakestAttacker())
            return;

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
            || (!IsRecentPlayerAttacker(target) && Bundles.Avoidance.AvoidMonster(target.Rarity))
            || target.Record.IsPandora
            || (target.Record.IsDimensionPillar && ignorePillar)
            || target.Record.IsSummonFlower
        )
            return null;

        var inRange = Container.Bot.Area.IsInSight(target);

        return inRange ? target : null;
    }

    private SpawnedMonster GetFromCurrentAttackers()
    {
        var attackWeakerFirst = PlayerConfig.Get<bool>("RSBot.Training.checkAttackWeakerFirst");
        if (!attackWeakerFirst || !IsEmergencySituation())
            return null;

        if (
            !SpawnManager.TryGetEntities<SpawnedMonster>(
                e => IsRecentPlayerAttacker(e) && IsDefensiveTargetEligible(e),
                out var entities
            )
        )
            return null;

        return entities.Where(e => GetTypePriority(e.Rarity) >= 0)
            .OrderBy(e => GetTypePriority(e.Rarity)).ThenBy(EstimateHealth).ThenBy(e => e.DistanceToPlayer).FirstOrDefault();
    }

    private bool IsEmergencySituation()
    {
        return SpawnManager.Any<SpawnedMonster>(e =>
            IsRecentPlayerAttacker(e) && IsDefensiveTargetEligible(e) && Bundles.Avoidance.AvoidMonster(e.Rarity)
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
                    (IsRecentPlayerAttacker(m) || !Bundles.Avoidance.AvoidMonster(m.Rarity))
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
            .OrderByDescending(IsRecentPlayerAttacker)
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
                m => !IsRecentPlayerAttacker(m) && IsRecent(m.TargetTick) && strangers.Contains(m.TargetId),
                out var monsters
            )
        )
            foreach (var monster in monsters)
                result.Add(monster.UniqueId);

        result.RemoveWhere(id =>
            SpawnManager.TryGetEntity<SpawnedMonster>(id, out var monster) && IsRecentPlayerAttacker(monster)
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
    ///     Handles defensive interruption and resumption. True also means deliberately keeping the current attacker,
    ///     so lower-priority rules cannot immediately undo that decision.
    /// </summary>
    private bool HandleWeakestAttacker()
    {
        if (!PlayerConfig.Get("RSBot.Training.checkBoxKillWeakestAttacker", false))
        {
            _interruptedTargets.Clear();
            return false;
        }

        var current = Game.SelectedEntity as SpawnedMonster;
        if (current != null && !IsDefensiveTargetEligible(current))
            current = null;
        var original = GetInterruptedTarget();
        var comparisonTarget = current ?? original;
        var comparisonPriority = comparisonTarget == null ? int.MaxValue : GetTypePriority(comparisonTarget.Rarity);

        SpawnedMonster attacker = null;
        if (SpawnManager.TryGetEntities<SpawnedMonster>(
                m => IsRecentPlayerAttacker(m) && IsDefensiveTargetEligible(m)
                    && GetTypePriority(m.Rarity) >= 0
                    && GetTypePriority(m.Rarity) < comparisonPriority, out var attackers))
            attacker = attackers.OrderBy(m => GetTypePriority(m.Rarity))
                .ThenBy(EstimateHealth).ThenBy(m => m.DistanceToPlayer).ThenBy(m => m.UniqueId).FirstOrDefault();

        if (attacker != null)
        {
            Log.Debug($"[TargetBundle] Interrupt: from={current?.UniqueId} type={current?.Rarity} "
                + $"to={attacker.UniqueId} type={attacker.Rarity} hp={EstimateHealth(attacker)} "
                + $"distance={attacker.DistanceToPlayer:F1} resume={current?.UniqueId ?? original?.UniqueId}");
            if (!Kernel.Bot.Running)
                return true;
            var lifecycleVersion = Volatile.Read(ref _lifecycleVersion);
            var selected = attacker.TrySelect();
            if (!Kernel.Bot.Running || lifecycleVersion != Volatile.Read(ref _lifecycleVersion))
                return true;

            if (selected && Game.SelectedEntity?.UniqueId == attacker.UniqueId
                && IsDefensiveTargetEligible(attacker))
            {
                // Save each interrupted fight. Strictly decreasing priorities bound nested interruptions.
                if (current != null && !_interruptedTargets.Contains(current.UniqueId))
                    _interruptedTargets.Push(current.UniqueId);
                Bundles.Movement.LastEntityWasBehindObstacle = false;
                CommitAttacker(attacker, "confirmed lower-type attacker selection");
            }
            else
            {
                _blacklist?.TryAdd(attacker.UniqueId, Kernel.TickCount);
                Log.Debug($"[TargetBundle] Defensive selection unconfirmed: {attacker.UniqueId}; retry delayed");
            }
            return true;
        }

        // Equal/higher types cannot pull us away from the attacker we are finishing.
        if (current != null && _committedAttackerId == current.UniqueId)
            return true;

        // After the interruption ends, resume the most recently interrupted valid fight.
        if (original != null)
        {
            Log.Debug($"[TargetBundle] Resume original target: {original.UniqueId} type={original.Rarity}");
            var lifecycleVersion = Volatile.Read(ref _lifecycleVersion);
            var selected = original.TrySelect();
            if (!Kernel.Bot.Running || lifecycleVersion != Volatile.Read(ref _lifecycleVersion))
                return true;
            _interruptedTargets.Pop();
            if (selected && Game.SelectedEntity?.UniqueId == original.UniqueId
                && IsDefensiveTargetEligible(original))
            {
                Bundles.Movement.LastEntityWasBehindObstacle = false;
                CommitAttacker(original, "resumed interrupted fight");
            }
            else
            {
                _blacklist?.TryAdd(original.UniqueId, Kernel.TickCount);
                Log.Debug($"[TargetBundle] Resume failed: {original.UniqueId}; retry delayed");
            }
            return true;
        }
        return false;
    }

    private SpawnedMonster GetInterruptedTarget()
    {
        while (_interruptedTargets.Count > 0)
        {
            var id = _interruptedTargets.Peek();
            if (SpawnManager.TryGetEntity<SpawnedMonster>(id, out var target) && IsDefensiveTargetEligible(target))
                return target;
            _interruptedTargets.Pop();
            Log.Debug($"[TargetBundle] Abandon interrupted target: {id}; dead, missing or invalid");
        }
        return null;
    }

    /// <summary>
    ///     Combat priority is explicit: protocol enum values do not represent relative difficulty.
    ///     Unclassified/event monsters do not participate in type-based interruption.
    /// </summary>
    private static int GetTypePriority(MonsterRarity rarity)
    {
        return rarity switch
        {
            MonsterRarity.General => 0,
            MonsterRarity.Champion => 1,
            MonsterRarity.GeneralParty => 2,
            MonsterRarity.ChampionParty => 3,
            MonsterRarity.Giant => 4,
            MonsterRarity.GiantParty => 5,
            MonsterRarity.Titan => 6,
            MonsterRarity.TitanParty => 7,
            MonsterRarity.Elite => 8,
            MonsterRarity.EliteStrong => 9,
            MonsterRarity.EliteParty => 10,
            MonsterRarity.Unique or MonsterRarity.Unique2 => 11,
            MonsterRarity.UniqueParty or MonsterRarity.Unique2Party => 12,
            _ => -1,
        };
    }

    private static bool IsRecentPlayerAttacker(SpawnedMonster monster)
    {
        return monster.TargetId == Game.Player.UniqueId && IsRecent(monster.TargetTick);
    }

    private bool IsDefensiveTargetEligible(SpawnedMonster monster)
    {
        return monster.State.LifeState == LifeState.Alive
            && monster.Health > 0
            && !monster.IsBehindObstacle
            && (_blacklist == null || !_blacklist.ContainsKey(monster.UniqueId))
            && Container.Bot.Area.IsInSight(monster)
            && !(PlayerConfig.Get("RSBot.Skills.checkWarlockMode", false) && monster.State.HasTwoDots());
    }

    private bool KeepCommittedAttacker()
    {
        if (_committedAttackerId == 0)
            return false;

        if (!PlayerConfig.Get("RSBot.Training.checkBoxKillWeakestAttacker", false))
            ReleaseCommittedAttacker("option disabled");
        else if (!SpawnManager.TryGetEntity<SpawnedMonster>(_committedAttackerId, out var monster))
            ReleaseCommittedAttacker("despawned");
        else if (!IsDefensiveTargetEligible(monster))
            ReleaseCommittedAttacker("dead, obstructed, outside area or excluded");
        else if (Game.SelectedEntity?.UniqueId != _committedAttackerId)
            ReleaseCommittedAttacker("selection changed or cleared");
        else
            return true;

        return false;
    }

    private void CommitAttacker(SpawnedMonster monster, string reason)
    {
        _committedAttackerId = monster.UniqueId;
        Log.Debug($"[TargetBundle] Finish attacker: {monster.UniqueId} reason={reason} "
            + $"hp={EstimateHealth(monster)} source={(HasRecentHealth(monster) ? "server" : "estimated")} distance={monster.DistanceToPlayer:F1}");
    }

    private void ReleaseCommittedAttacker(string reason)
    {
        if (_committedAttackerId == 0)
            return;

        Log.Debug($"[TargetBundle] Release attacker: {_committedAttackerId} reason={reason}");
        _committedAttackerId = 0;
    }

    /// <summary>
    ///     Gets the health a monster has left. Before its first health update the base health of the record is stored,
    ///     which is too low for a champion or giant, so an untouched monster counts as full.
    /// </summary>
    private static int EstimateHealth(SpawnedMonster monster)
    {
        if (monster.Record == null)
            return monster.Health;

        return HasRecentHealth(monster) ? monster.Health : monster.MaxHealth;
    }

    private static bool HasRecentHealth(SpawnedMonster monster)
    {
        return monster.HasObservedHealth && IsRecent(monster.HealthUpdateTick);
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
        Interlocked.Increment(ref _lifecycleVersion);
        ReleaseCommittedAttacker("refresh");
        _interruptedTargets.Clear();
        _blacklist = new Dictionary<uint, int>(8);
    }

    public void Stop()
    {
        Interlocked.Increment(ref _lifecycleVersion);
        ReleaseCommittedAttacker("stop");
        _interruptedTargets.Clear();
        _blacklist = null;
    }

    #endregion Methods
}
