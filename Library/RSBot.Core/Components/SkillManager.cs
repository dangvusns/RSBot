using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RSBot.Core.Client.ReferenceObjects;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Skill;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Core.Components;

public static class SkillManager
{
    /// <summary>
    ///     Get the skill using index
    /// </summary>
    private static int _lastIndex;

    /// <summary>
    ///     The last casted skill id
    /// </summary>
    public static uint LastCastedSkillId;

    /// <summary>
    ///     Basic skills
    /// </summary>
    private static IEnumerable<uint> _baseSkills;

    /// <summary>
    ///     The last skill a cast was requested for, to attribute a cooldown refusal from the server.
    /// </summary>
    private static SkillInfo _lastRequestedSkill;

    private static int _lastRequestTick;

    /// <summary>
    ///     Gets or sets the skills organized by their mob priority.
    /// </summary>
    /// <value>
    ///     The skills.
    /// </value>
    public static Dictionary<MonsterRarity, List<SkillInfo>> Skills { get; set; }

    /// <summary>
    ///     Gets or sets the resurrection skill.
    /// </summary>
    /// <value>
    ///     The resurrection skill.
    /// </value>
    public static SkillInfo ResurrectionSkill { get; set; }

    /// <summary>
    ///     Gets or sets the imbue skill.
    /// </summary>
    /// <value>
    ///     The imbue skill.
    /// </value>
    public static SkillInfo ImbueSkill { get; set; }

    /// <summary>
    ///     Gets or sets the buffs.
    /// </summary>
    /// <value>
    ///     The buffs.
    /// </value>
    public static List<SkillInfo> Buffs { get; set; }

    /// <summary>
    ///     Gets or sets the opener skill ids per mob priority. An opener is cast once on each new target before the
    ///     normal rotation, and is never part of the rotation itself.
    /// </summary>
    public static Dictionary<MonsterRarity, HashSet<uint>> OpenerSkills { get; set; }

    /// <summary>
    ///     Gets or sets the ids of the buffs that are only cast while the target is a strong monster.
    /// </summary>
    public static HashSet<uint> StrongTargetBuffs { get; set; }

    private static readonly EncounterOpeners _encounterOpeners = new();

    private static readonly SemaphoreSlim _castOwnership = new(1, 1);
    private static readonly object _castStateLock = new();
    private static int _castGeneration;
    private static Packet _activeCastRequest;
    private static AwaitCallback[] _activeCastCallbacks = Array.Empty<AwaitCallback>();
    private static CancellationTokenSource _castCancellation;
    private static int _lastBuffResult;

    public static bool IsCasting => _castOwnership.CurrentCount == 0;
    public static SkillCastResult LastBuffResult => (SkillCastResult)Volatile.Read(ref _lastBuffResult);

    // ponytail: ownership covers a local API call, not every server skill-chain hit.
    // Retain it through full completion only when captures prove the final transition.
    private static T WithCastOwnership<T>(Func<int, T> cast, T unavailable)
    {
        var generation = Volatile.Read(ref _castGeneration);
        if (!Game.Ready || Game.Player?.State.LifeState != LifeState.Alive || !_castOwnership.Wait(0))
            return unavailable;
        CancellationTokenSource cancellation = null;
        try
        {
            lock (_castStateLock)
            {
                if (generation != _castGeneration)
                    return unavailable;
                _castCancellation = cancellation = new CancellationTokenSource();
            }
            return cast(generation);
        }
        finally
        {
            lock (_castStateLock)
            {
                foreach (var callback in _activeCastCallbacks)
                    callback.Cancel();
                _activeCastCallbacks = Array.Empty<AwaitCallback>();
                _activeCastRequest = null;
                _castCancellation = null;
            }
            cancellation?.Dispose();
            _castOwnership.Release();
        }
    }

    internal static void CancelPendingCasts(bool resetSession = false, bool clearEncounters = true)
    {
        lock (_castStateLock)
        {
            _castGeneration++;
            _castCancellation?.Cancel();
            foreach (var callback in _activeCastCallbacks)
                callback.Cancel();
            _lastRequestedSkill = null;
            if (clearEncounters) _encounterOpeners.Clear();
            if (resetSession)
            {
                Objects.Action.ClearRecentCasts();
                lock (_consecutiveRefusals) _consecutiveRefusals.Clear();
            }
        }
    }

    internal static void ObserveCastRequest(Packet request)
    {
        lock (_castStateLock)
            if (_activeCastRequest != null && !ReferenceEquals(request, _activeCastRequest))
                CancelPendingCasts(clearEncounters: false);
    }

    private static bool SendCastPacket(Packet packet, int generation, params AwaitCallback[] callbacks)
    {
        lock (_castStateLock)
        {
            if (generation != _castGeneration || !Game.Ready || Game.Player?.State.LifeState != LifeState.Alive)
            {
                foreach (var callback in callbacks) callback.Cancel();
                return false;
            }
            _activeCastRequest = packet;
            _activeCastCallbacks = callbacks;
            if (callbacks.Length == 0)
                return PacketManager.TrySendPacket(packet, PacketDestination.Server, false);
            PacketManager.SendPacket(packet, PacketDestination.Server, callbacks);
            return !callbacks.Any(c => c.State is AwaitCallbackState.NotSent or AwaitCallbackState.Cancelled);
        }
    }

    /// <summary>
    ///     Gets or sets the teleport skill.
    /// </summary>
    public static SkillInfo TeleportSkill { get; set; }

    /// <summary>
    ///     Gets the config to always use skills in order.
    /// </summary>
    public static bool UseSkillsInOrder => PlayerConfig.Get("RSBot.Skills.checkUseSkillsInOrder", false);

    /// <summary>
    ///     Is the last action basic skill (Auto attack) <c>true</c>; otherwise <c>false</c>
    /// </summary>
    public static bool IsLastCastedBasic => _baseSkills.Contains(LastCastedSkillId);

    /// <summary>
    ///     Initializes this instance.
    /// </summary>
    internal static void Initialize()
    {
        Skills = Enum.GetValues(typeof(MonsterRarity))
            .Cast<MonsterRarity>()
            .ToDictionary(v => v, v => new List<SkillInfo>());
        Buffs = new List<SkillInfo>();
        OpenerSkills = new Dictionary<MonsterRarity, HashSet<uint>>();
        StrongTargetBuffs = new HashSet<uint>();

        EventManager.SubscribeEvent("OnLoadGameData", OnLoadGamedData);
        EventManager.SubscribeEvent("OnCastSkill", new Action<uint>(OnCastSkill));

        Log.Debug($"Initialized [SkillManager] for [{Skills.Count}] different mob rarities!");
    }

    private static void OnLoadGamedData()
    {
        _baseSkills = Game.ReferenceManager.GetBaseSkills();
    }

    /// <summary>
    ///     Call after casted skill
    /// </summary>
    /// <param name="skillId">The casted skill id</param>
    private static void OnCastSkill(uint skillId)
    {
        LastCastedSkillId = skillId;
    }

    /// <summary>
    ///     Sets the skills.
    /// </summary>
    /// <param name="monsterRarity">The monster rarity.</param>
    /// <param name="skills">The skills.</param>
    public static void SetSkills(MonsterRarity monsterRarity, List<SkillInfo> skills)
    {
        if (Skills == null || !Skills.ContainsKey(monsterRarity))
            return;

        Skills[monsterRarity] = skills;
    }

    private static void RememberCastRequest(SkillInfo skill)
    {
        _lastRequestedSkill = skill;
        _lastRequestTick = Kernel.TickCount;
    }

    /// <summary>
    ///     Called when the server refuses a cast because the skill is still on cooldown.
    ///     The refusal does not name the skill, so it is attributed to the last cast request; our timer
    ///     thought the skill was ready, so hold it back briefly instead of retrying every tick.
    /// </summary>
    internal static void OnCastRefusedByCooldown()
    {
        var skill = _lastRequestedSkill;

        // With our cooldown running, it was a second request sent before the first cast was answered, not a mismatch
        if (skill == null || Kernel.TickCount - _lastRequestTick > 2000 || skill.HasCooldown)
            return;

        // Our timer said ready: the cooldown in the game data is shorter than the server's for this skill
        var sinceLastCast = skill.MillisecondsSinceLastCast;
        Log.Debug(
            $"[Cooldown] Server refused [{skill.Record?.GetRealName()}] ({skill.Record?.Basic_Code}): still on cooldown "
                + $"{(sinceLastCast < 0 ? "with no cast seen yet" : $"{sinceLastCast} ms after its last cast")}, "
                + $"the game data says {skill.Record?.Action_ReuseDelay} ms. Retrying in 3 s."
        );
        skill.SetRemainingCooldown(3000);
    }

    /// <summary>
    ///     The refusals in a row per skill, reset by a successful cast.
    /// </summary>
    private static readonly Dictionary<uint, int> _consecutiveRefusals = new();

    private const int RefusalsBeforePause = 5;

    /// <summary>
    ///     Called when the server refuses a cast for another reason than the cooldown (e.g. not enough MP or an unknown code).
    ///     Like <see cref="OnCastRefusedByCooldown" /> the refusal is attributed to the last cast request; the skill is held back
    ///     longer with every refusal in a row so it is not retried every tick.
    /// </summary>
    /// <param name="errorCode">The error code of the refusal.</param>
    internal static void OnCastRefused(byte errorCode)
    {
        var skill = _lastRequestedSkill;
        if (skill == null || Kernel.TickCount - _lastRequestTick > 2000 || skill.HasCooldown)
        {
            Log.Debug($"Server refused a skill cast (code 0x{errorCode:X2}).");
            return;
        }

        int refusals;
        lock (_consecutiveRefusals)
        {
            _consecutiveRefusals.TryGetValue(skill.Id, out refusals);
            _consecutiveRefusals[skill.Id] = ++refusals;
        }

        var holdMs = refusals switch
        {
            1 => 3_000,
            2 => 10_000,
            < RefusalsBeforePause => 30_000,
            _ => 180_000,
        };

        // Attack skills keep the fight going; a refusal is often only the target (dead, out of reach)
        if (skill.IsAttack)
            holdMs = Math.Min(holdMs, 3_000);

        var name = skill.Record?.GetRealName();
        if (refusals == RefusalsBeforePause && !skill.IsAttack)
            Log.Warn($"[Skills] The server refused [{name}] {refusals} times in a row (code 0x{errorCode:X2}), pausing it for {holdMs / 1000}s.");
        else
            Log.Debug($"Server refused [{name}] (code 0x{errorCode:X2}, {refusals} in a row). Retrying in {holdMs / 1000} s.");

        skill.SetRemainingCooldown(holdMs);
    }

    /// <summary>
    ///     Called when a cast of the player started.
    /// </summary>
    internal static void OnCastSucceeded(SkillInfo skill)
    {
        lock (_consecutiveRefusals)
            _consecutiveRefusals.Remove(skill.Id);
    }

    internal static void ConfirmOpener(uint targetId, uint skillId)
    {
        if (OpenerSkills?.Values.Any(ids => ids.Contains(skillId)) == true)
            _encounterOpeners.Confirm(targetId, skillId);
    }

    internal static void ForgetEncounter(uint targetId) => _encounterOpeners.Forget(targetId);

    /// <summary>
    ///     Gets the monster type whose skill list is used: the own one, otherwise the closest weaker type that has
    ///     skills (a party giant uses the giant skills, a giant the champion skills, ... down to general).
    /// </summary>
    /// <param name="rarity">The monster type.</param>
    private static MonsterRarity GetSkillRarity(MonsterRarity rarity)
    {
        var current = rarity;

        // The chain is short, the limit only protects against a wrong mapping
        for (var i = 0; i < 10; i++)
        {
            if (Skills.TryGetValue(current, out var skills) && skills.Count > 0)
                return current;

            if (current == MonsterRarity.General)
                break;

            current = current switch
            {
                MonsterRarity.GeneralParty => MonsterRarity.General,
                MonsterRarity.ChampionParty => MonsterRarity.Champion,
                MonsterRarity.GiantParty => MonsterRarity.Giant,
                MonsterRarity.TitanParty => MonsterRarity.Titan,
                MonsterRarity.EliteParty => MonsterRarity.Elite,
                MonsterRarity.UniqueParty => MonsterRarity.Unique,
                MonsterRarity.Unique2Party => MonsterRarity.Unique2,
                MonsterRarity.Unique2 => MonsterRarity.Unique,
                MonsterRarity.Unique => MonsterRarity.Titan,
                MonsterRarity.EliteStrong => MonsterRarity.Elite,
                MonsterRarity.Elite => MonsterRarity.Giant,
                MonsterRarity.Titan => MonsterRarity.Giant,
                MonsterRarity.Giant => MonsterRarity.Champion,
                _ => MonsterRarity.General,
            };
        }

        return MonsterRarity.General;
    }

    /// <summary>
    ///     Gets the next skill.
    /// </summary>
    /// <returns></returns>
    public static SkillInfo GetNextSkill()
    {
        var entity = Game.SelectedEntity;
        if (entity == null)
            return null;

        var rarity = entity is SpawnedMonster monster ? GetSkillRarity(monster.Rarity) : MonsterRarity.General;

        var distance = Game.Player.Movement.Source.DistanceTo(entity.Movement.Source);

        var minDifference = int.MaxValue;
        //var weaponRange = 0;
        var closestSkill = default(SkillInfo);

        // Snapshot: ActiveBuffs is filled from the packet thread. Skills already on the target (e.g. DoTs) are skipped.
        var targetBuffIds = entity.State.ActiveBuffs.ToArray().Where(b => b != null).Select(b => b.Id).ToHashSet();

        if (entity.State.HitState != ActionHitStateFlag.KnockDown)
        {
            var opener = GetOpener(entity.UniqueId, rarity);
            if (opener != null)
                return opener;
        }

        if (entity.State.HitState == ActionHitStateFlag.KnockDown)
        {
            // try to get attack skill for only knockdown states
            closestSkill = Skills[rarity].Find(p => p.Record.Params.Contains(25697));
        }
        else if (UseSkillsInOrder || distance < 10)
        {
            var counter = -1;
            var skillCount = Skills[rarity].Count;
            while (skillCount > 0 && counter < skillCount)
            {
                counter++;
                _lastIndex++;

                if (_lastIndex > Skills[rarity].Count - 1)
                    _lastIndex = 0;

                if (!Skills.ContainsKey(rarity) || Skills[rarity].Count < _lastIndex)
                    continue;

                var selectedSkill = Skills[rarity][_lastIndex];
                if (!selectedSkill.CanBeCasted || IsOpener(rarity, selectedSkill) || targetBuffIds.Contains(selectedSkill.Id))
                    continue;

                closestSkill = selectedSkill;
                break;
            }

            //Debug.WriteLine($"while loop: {stopwatch.ElapsedMilliseconds}");
        }
        else
        {
            /*var weapon = Game.Player.Inventory.GetItemAt(6);
            if (weapon != null)
                weaponRange = weapon.Record.Range / 10;
            */

            for (var i = 0; i < Skills[rarity].Count; i++)
            {
                var s = Skills[rarity][i];
                if (!s.CanBeCasted || IsOpener(rarity, s) || targetBuffIds.Contains(s.Id))
                    continue;

                var difference = Math.Abs(
                    s.Record.Action_Range / 10 - distance /* + weaponRange*/
                );
                if (minDifference > difference)
                {
                    minDifference = (short)difference;
                    closestSkill = s;
                }
            }
            //Debug.WriteLine($"for loop: {stopwatch.ElapsedMilliseconds}");
        }

        return closestSkill;
    }

    /// <summary>
    ///     Gets the next opener skill that wasn't used on the target yet.
    /// </summary>
    /// <param name="targetId">The target unique identifier.</param>
    /// <param name="rarity">The mob priority whose skills are used.</param>
    private static SkillInfo GetOpener(uint targetId, MonsterRarity rarity)
    {
        if (OpenerSkills == null || !OpenerSkills.TryGetValue(rarity, out var openers) || openers.Count == 0)
            return null;

        return Skills[rarity].Find(s => openers.Contains(s.Id)
            && !_encounterOpeners.WasAccepted(targetId, s.Id) && s.CanBeCasted);
    }

    private static bool IsOpener(MonsterRarity rarity, SkillInfo skill)
    {
        return OpenerSkills != null && OpenerSkills.TryGetValue(rarity, out var openers) && openers.Contains(skill.Id);
    }

    /// <summary>
    ///     Gets a value indicating whether the buff may be cast now. A buff that is limited to strong monsters is only
    ///     cast while the selected target is a strong (giant, party, titan, elite or unique) monster.
    /// </summary>
    /// <param name="buff">The buff.</param>
    public static bool IsBuffAllowedNow(SkillInfo buff)
    {
        if (StrongTargetBuffs == null || !StrongTargetBuffs.Contains(buff.Id))
            return true;

        return Game.SelectedEntity != null
            && SpawnManager.TryGetEntity<SpawnedMonster>(Game.SelectedEntity?.UniqueId ?? 0, out var monster)
            && monster.State.LifeState == LifeState.Alive
            && monster.Rarity is not (MonsterRarity.General or MonsterRarity.Champion or MonsterRarity.Event);
    }

    /// <summary>
    ///     Check required of the using skill
    /// </summary>
    /// <param name="skill">The using skill</param>
    public static bool CheckSkillRequired(RefSkill skill)
    {
        if (skill == null || Game.Player == null)
            return false;
        if (skill.ReqCommon_Mastery1 == 1)
            return true;

        InventoryItem requiredItem = null;
        TypeIdFilter filter = null;

        var currentWeapon = Game.Player.Inventory.GetItemAt(6);
        if (skill.ReqCast_Weapon1 == WeaponType.Any)
        {
            var list = new List<TypeIdFilter>(8);

            for (var i = 0; i < skill.Params.Count; i++)
            {
                var param = skill.Params[i];
                if (param != 1919250793)
                    continue;

                var paramTypeId3 = (byte)skill.Params[++i];
                var paramTypeId4 = (byte)skill.Params[++i];
                list.Add(new TypeIdFilter(3, 1, paramTypeId3, paramTypeId4));
            }

            if (list.Count == 0)
                return true;

            filter = list.FirstOrDefault(p =>
                p.TypeID3 == currentWeapon?.Record.TypeID3 && p.TypeID4 == currentWeapon?.Record.TypeID4
            );
            if (filter != null)
                return true;

            filter = list.FirstOrDefault();
        }
        else
        {
            filter = new TypeIdFilter(p =>
                p.TypeID2 == 1
                && p.TypeID3 == 6
                && (
                    p.TypeID4 == (byte)skill.ReqCast_Weapon1
                    || ((byte)skill.ReqCast_Weapon2 != 0xFF && p.TypeID4 == (byte)skill.ReqCast_Weapon2)
                )
            );
        }

        requiredItem = Game.Player.Inventory.GetItemBest(filter);
        if (requiredItem == null)
            return false;

        var movingSlot = (byte)(requiredItem.Record.TypeID3 == 6 ? 6 : 7);
        if (requiredItem.Slot == movingSlot)
            return true;

        var result = requiredItem.Equip(movingSlot);

        if (_castCancellation?.IsCancellationRequested == true || !Game.Ready)
            return false;

        if (movingSlot == 6 && requiredItem.Record.TwoHanded == 0)
        {
            // find and equip the shield item automatically
            filter = new TypeIdFilter(3, 1, 4, (byte)(Game.Player.Race == ObjectCountry.Chinese ? 1 : 2));
            var shieldItem = Game.Player.Inventory.GetItemBest(filter);
            if (shieldItem != null && shieldItem.Slot != 7)
                shieldItem.Equip(7);
        }

        return result;
    }

    public static bool CastSkill(SkillInfo skill, uint targetId = 0)
        => WithCastOwnership(generation => CastSkillCore(skill, targetId, generation), false);

    private static bool CastSkillCore(SkillInfo skill, uint targetId, int generation)
    {
        if (skill == null || !Game.Player.Skills.HasSkill(skill.Id))
            return false;

        if (!SpawnManager.TryGetEntity<SpawnedBionic>(targetId, out var entity))
            return false;

        if (entity.State.LifeState == LifeState.Dead)
            return false;

        if (!CheckSkillRequired(skill.Record))
            return false;

        RememberCastRequest(skill);

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Cast); //Use Skill
        packet.WriteUInt(skill.Id);
        packet.WriteByte(ActionTarget.Entity);

        // unknown byte
        if (Game.ClientType < GameClientType.Thailand)
            packet.WriteByte(1);

        packet.WriteUInt(targetId);

        Log.Debug(
            $"Skill Attacking to: {targetId} State: {entity.State.LifeState} Health: {entity.Health} HasHealth: {entity.HasHealth} Dst: {Math.Round(entity.DistanceToPlayer, 1)}"
        );

        return SendCastPacket(packet, generation);
    }

    /// <summary>
    ///     Cast player skill
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <param name="targetId">The target unique identifier.</param>
    /// <returns> <c>true</c> if this successfully used the selected skill; otherwise, <c>false</c>.</returns>
    public static bool CastSkillOld(SkillInfo skill, uint targetId = 0)
        => WithCastOwnership(generation => CastSkillOldCore(skill, targetId, generation), false);

    private static bool CastSkillOldCore(SkillInfo skill, uint targetId, int generation)
    {
        if (skill == null || !Game.Player.Skills.HasSkill(skill.Id))
            return false;

        if (!SpawnManager.TryGetEntity<SpawnedBionic>(targetId, out var entity))
            return false;

        if (entity.State.LifeState == LifeState.Dead)
            return false;

        var weapon = Game.Player.Inventory.GetItemAt(6);

        if (!CheckSkillRequired(skill.Record))
            return false;

        RememberCastRequest(skill);

        var distance = entity.DistanceToPlayer;
        var speed = Game.Player.ActualSpeed;
        var movingSleep = 0d;

        // tel3 warrior sprint teleport
        var tel3Index = skill.Record.Params.FindIndex(p => p == 1952803891);
        if (tel3Index != -1)
        {
            var tel3speed = skill.Record.Params[++tel3Index];
            var tel3meter = skill.Record.Params[++tel3Index] / 10;

            if (distance < tel3meter)
                movingSleep = distance / tel3speed;
            else
                movingSleep = (distance - tel3meter) / speed + tel3meter / tel3speed;
        }
        else
        {
            var range = skill.Record.Action_Range / 10;
            if (distance - 3 > range)
                movingSleep = (distance - range) / speed;
        }

        if (movingSleep < 0)
            movingSleep = 0;
        else
            movingSleep *= 10000.0;

        var duration = (int)movingSleep; /* +
                     skill.Record.Action_CastingTime; +
                     skill.Record.Action_ActionDuration +
                     skill.Record.Action_PreparingTime;*/

        var packet = new Packet(0x7074);
        packet.WriteByte(1); //Execute
        packet.WriteByte(4); //Use Skill
        packet.WriteUInt(skill.Id);
        packet.WriteByte(ActionTarget.Entity);
        packet.WriteUInt(targetId);

        var callback = new AwaitCallback(
            response =>
                response.ReadByte() == 0x02 && response.ReadByte() == 0x00
                    ? AwaitCallbackResult.Success
                    : AwaitCallbackResult.ConditionFailed,
            0xB074
        );

        var altSkill = skill.Record;
        while (altSkill != null)
        {
            duration += altSkill.Action_CastingTime + altSkill.Action_ActionDuration + altSkill.Action_PreparingTime;

            if (altSkill.Basic_ChainCode != 0)
                altSkill = Game.ReferenceManager.GetRefSkill(altSkill.Basic_ChainCode);
            else
                break;
        }

        if (duration < 100)
            duration = 1000;

        Log.Debug(
            $"Skill Attacking to: {targetId} State: {entity.State.LifeState} Health: {entity.Health} HasHealth: {entity.HasHealth} Dst: {Math.Round(entity.DistanceToPlayer, 1)}"
        );

        if (!SendCastPacket(packet, generation, callback))
            return false;
        try
        {
            if (_castCancellation.Token.WaitHandle.WaitOne(duration))
                return false;

            if (skill.Record.Basic_Activity != 1)
            {
                callback.AwaitResponse(duration, _castCancellation.Token);
                return callback.IsCompleted;
            }

            return true;
        }
        finally
        {
            // Basic attacks do not wait for B074; their registration still needs an owner.
            callback.Cancel();
        }
    }

    /// <summary>
    ///     Casts the buff skill.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <returns>Whether the server accepted the cast.</returns>
    public static SkillCastResult CastBuff(SkillInfo skill, uint target = 0, bool awaitBuffResponse = true)
    {
        if (skill == null || !Game.Ready || Game.Player?.State.LifeState != LifeState.Alive)
        {
            Volatile.Write(ref _lastBuffResult, (int)SkillCastResult.NotSent);
            return SkillCastResult.NotSent;
        }
        var result = WithCastOwnership(generation => CastBuffCore(skill, target, awaitBuffResponse, generation), SkillCastResult.Busy);
        Volatile.Write(ref _lastBuffResult, (int)result);
        return result;
    }

    private static SkillCastResult CastBuffCore(SkillInfo skill, uint target, bool awaitBuffResponse, int generation)
    {
        var player = Game.Player;
        if (skill.Id == 0)
            return SkillCastResult.NotSent;

        /*
        if (!Game.Player.Skills.HasSkill(skill.Id))
            return;
        */
        if (!CheckSkillRequired(skill.Record))
            return SkillCastResult.NotSent;

        RememberCastRequest(skill);

        if (target == 0 || target == Game.Player.UniqueId)
            Log.Notify($"Casting skill (self-buff) [{skill.Record.GetRealName()}]");
        else
            Log.Notify($"Casting buff [{skill.Record.GetRealName()}] on {SpawnManager.GetEntity<SpawnedPlayer>(target)?.Name ?? target.ToString()}");

        var packet = new Packet(0x7074);
        packet.WriteByte(1); //Execute
        packet.WriteByte(4); //Use Skill
        packet.WriteUInt(skill.Id);
        uint? expectedTarget = null;

        // An ally buff on another player (e.g. a party member) needs that player as its target. Area buffs
        // without a target group (e.g. marches) are still sent without one.
        if (
            skill.Record.TargetGroup_Self
            || skill.Record.TargetGroup_Party
            || (target != 0 && skill.Record.TargetGroup_Ally)
        )
        {
            expectedTarget = target == 0 ? player.UniqueId : target;
            packet.WriteByte(ActionTarget.Entity);
            packet.WriteUInt(expectedTarget.Value);
        }
        else
        {
            packet.WriteByte(ActionTarget.None);
        }

        if (!awaitBuffResponse)
        {
            return SendCastPacket(packet, generation) ? SkillCastResult.Unconfirmed : SkillCastResult.Cancelled;
        }

        // Match acceptance against the captured caster and recipient. Anonymous errors close
        // the wait without claiming that they identify this request.
        var castCallback = new AwaitCallback(
            response => Objects.Action.MatchCastStart(response, Game.ClientType, skill.Id, player.UniqueId, expectedTarget),
            0xB070
        );

        var actionStateCallback = new AwaitCallback(
            response =>
            {
                if (!castCallback.IsCompleted)
                    return AwaitCallbackResult.ConditionFailed;
                var state = response.ReadByte();
                var recurring = response.ReadByte();

                if (state == 0x02 && recurring == 0x00)
                    return AwaitCallbackResult.Success;

                if (state == 0x03)
                    return AwaitCallbackResult.Fail;

                return AwaitCallbackResult.ConditionFailed;
            },
            0xB074
        );

        var awaitActionState = skill.Record.Basic_Activity != 1;
        SendCastPacket(packet, generation, awaitActionState
            ? new[] { castCallback, actionStateCallback } : new[] { castCallback });

        var timeout =
            skill.Record.Action_CastingTime
            + skill.Record.Action_ActionDuration
            + skill.Record.Action_PreparingTime
            + 1500;

        castCallback.AwaitResponse(timeout, _castCancellation.Token);

        var result = castCallback.State switch
        {
            AwaitCallbackState.Succeeded => SkillCastResult.Accepted,
            AwaitCallbackState.Cancelled => SkillCastResult.Cancelled,
            AwaitCallbackState.NotSent => SkillCastResult.NotSent,
            AwaitCallbackState.TimedOut => SkillCastResult.Timeout,
            _ => SkillCastResult.Unconfirmed, // an anonymous refusal cannot prove which cast failed
        };

        if (!awaitActionState)
            return generation == Volatile.Read(ref _castGeneration) ? result : SkillCastResult.Cancelled;

        // Close the action state callback right away when the cast failed, so it does not linger in the callback list
        if (castCallback.IsCompleted)
            actionStateCallback.AwaitResponse(timeout, _castCancellation.Token);
        else
        {
            actionStateCallback.Cancel();
            return result;
        }

        if (actionStateCallback.State == AwaitCallbackState.Cancelled)
            return SkillCastResult.Cancelled;

        return generation == Volatile.Read(ref _castGeneration) ? result : SkillCastResult.Cancelled;
    }

    /// <summary>
    ///     Casts a skill to the given target position
    /// </summary>
    /// <param name="skill"></param>
    /// <param name="target"></param>
    public static void CastSkillAt(SkillInfo skill, Position target)
        => WithCastOwnership(generation => { CastSkillAtCore(skill, target, generation); return true; }, false);

    private static void CastSkillAtCore(SkillInfo skill, Position target, int generation)
    {
        if (skill == null)
            return;
        if (target.Region == 0 || target.DistanceToPlayer() > 100)
            return;

        if (skill.Id == 0)
            return;

        if (!CheckSkillRequired(skill.Record))
            return;

        RememberCastRequest(skill);

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Cast); //Use Skill
        packet.WriteUInt(skill.Id);
        packet.WriteByte(ActionTarget.Area);
        packet.WriteUShort(target.Region);

        if (target.Region.IsDungeon)
        {
            packet.WriteShort(target.XOffset);
            packet.WriteShort(target.YOffset);
            packet.WriteShort(target.ZOffset);
        }
        else
        {
            packet.WriteInt(target.XOffset);
            packet.WriteInt(target.ZOffset);
            packet.WriteInt(target.YOffset);
        }

        var callback = new AwaitCallback(
            response =>
            {
                return response.ReadByte() == 0x02 && response.ReadByte() == 0x00
                    ? AwaitCallbackResult.Success
                    : AwaitCallbackResult.ConditionFailed;
            },
            0xB074
        );

        SendCastPacket(packet, generation, callback);

        if (skill.Record.Basic_Activity != 1)
            callback.AwaitResponse(1000, _castCancellation.Token);
    }

    /// <summary>
    ///     Casts the skill. Does not check any weapon requirement.
    /// </summary>
    /// <param name="skill"></param>
    /// <param name="targetId"></param>
    /// <returns></returns>
    public static bool CastAutoAttack()
        => WithCastOwnership(CastAutoAttackCore, false);

    private static bool CastAutoAttackCore(int generation)
    {
        var entity = Game.SelectedEntity;
        if (entity == null)
            return false;

        if (entity.State.LifeState == LifeState.Dead)
            return false;

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Attack); //Use Skill
        packet.WriteByte(ActionTarget.Entity);

        // unknown byte
        if (Game.ClientType < GameClientType.Thailand)
            packet.WriteByte(1);

        packet.WriteUInt(entity.UniqueId);

        Log.Debug(
            $"Normal Attacking to: {entity.UniqueId} State: {entity.State.LifeState} Health: {entity.Health} HasHealth: {entity.HasHealth} Dst: {Math.Round(entity.DistanceToPlayer, 1)}"
        );

        return SendCastPacket(packet, generation);
    }

    /// <summary>
    ///     Casts the skill at.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <param name="position">The position.</param>
    public static void CastSkillAt(uint skillId, Position position)
        => WithCastOwnership(generation => { CastSkillAtCore(skillId, position, generation); return true; }, false);

    private static void CastSkillAtCore(uint skillId, Position position, int generation)
    {
        if (!Game.Player.Skills.HasSkill(skillId))
            return;

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Cast); //Use Skill
        packet.WriteUInt(skillId);
        packet.WriteByte(ActionTarget.Area);
        position.Region.Serialize(packet);
        packet.WriteFloat(position.XOffset);
        packet.WriteFloat(position.ZOffset);
        packet.WriteFloat(position.YOffset);

        SendCastPacket(packet, generation);
    }

    /// <summary>
    ///     Cancels the buff.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    public static void CancelBuff(uint skillId)
    {
        if (!Game.Player.Skills.HasSkill(skillId))
            return;

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute); //Execute
        packet.WriteByte(ActionType.Dispel); //Cancel Buff
        packet.WriteUInt(skillId);
        packet.WriteByte(ActionTarget.None);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }

    /// <summary>
    ///     Cancels the action.
    /// </summary>
    /// <param name="timeout">The time to wait for the server to confirm, 0 or less sends the request without waiting.</param>
    /// <returns></returns>
    public static bool CancelAction(int timeout = 5_000)
    {
        if (timeout <= 0)
        {
            PacketManager.SendPacket(CreateCancelActionPacket(), PacketDestination.Server);
            return true;
        }

        var callback = new AwaitCallback(
            response =>
            {
                return response.ReadByte() == 0x02 && response.ReadByte() == 0x00
                    ? AwaitCallbackResult.Success
                    : AwaitCallbackResult.ConditionFailed;
            },
            0xB074
        );

        PacketManager.SendPacket(CreateCancelActionPacket(), PacketDestination.Server, callback);
        callback.AwaitResponse(timeout);

        return callback.IsCompleted;
    }

    private static Packet CreateCancelActionPacket()
    {
        var packet = new Packet(0x7074);
        packet.WriteByte(0x02); //Cancel

        return packet;
    }
}
