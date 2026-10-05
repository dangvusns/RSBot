using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Party;
using RSBot.Core.Objects.Skill;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Training.Bundle.Resurrect;

/// <summary>
///     Resurrects dead party members. A castable resurrect goes before buffing and attacking (see
///     <see cref="HasPendingResurrect" />).
/// </summary>
internal class ResurrectBundle : IBundle
{
    /// <summary>
    ///     After a refused or unanswered cast the member is tried again this soon.
    /// </summary>
    private const int RetryLockoutMs = 3_000;

    /// <summary>
    ///     How long the pending check is reused; it walks the party with a collision test per member.
    /// </summary>
    private const int PendingCacheMs = 250;

    /// <summary>
    ///     The tick until which a member is not resurrected again, by name.
    /// </summary>
    private readonly Dictionary<string, int> _lockedUntil = new();

    private bool _pendingCache;
    private int _pendingCacheTick;
    private bool _hasPendingCache;

    public void Invoke()
    {
        if (!TryFindDeadMember(out var member, out var player, out var skill))
        {
            SetPendingCache(false);
            return;
        }

        SetPendingCache(true);

        var range = skill.Record.Action_Range / 10f;
        if (player.Movement.Source.DistanceTo(Game.Player.Movement.Source) > range)
        {
            // Walk into range first; nothing is locked, the next tick checks again
            Log.Status($"Moving to resurrect {member.Name}");
            Game.Player.MoveTo(player.Movement.Source, false);
            return;
        }

        Log.Status($"Resurrecting player {member.Name}");
        var result = skill.CastBuff(player.UniqueId);

        if (result == SkillCastResult.Accepted)
        {
            Log.Notify($"Resurrecting {member.Name}");
            Lock(member.Name, PlayerConfig.Get<ushort>("RSBot.Skills.numResDelay", 120) * 1000);
        }
        else
        {
            Log.Debug($"[Resurrect] Casting on {member.Name} did not start ({result}), retrying in {RetryLockoutMs / 1000} s");
            Lock(member.Name, RetryLockoutMs);
        }

        // The locked member no longer counts; let the next check decide
        _hasPendingCache = false;
    }

    /// <summary>
    ///     Gets a value indicating whether a dead party member can be resurrected right now: the skill is ready and
    ///     a dead member is near. Buffing and attacking wait while it is <c>true</c>.
    /// </summary>
    public bool HasPendingResurrect()
    {
        if (_hasPendingCache && Kernel.TickCount - _pendingCacheTick < PendingCacheMs)
            return _pendingCache;

        var pending = TryFindDeadMember(out _, out _, out _);
        SetPendingCache(pending);

        return pending;
    }

    /// <summary>
    ///     Finds a dead party member that can be resurrected now.
    /// </summary>
    private bool TryFindDeadMember(out PartyMember deadMember, out SpawnedPlayer deadPlayer, out SkillInfo skill)
    {
        deadMember = null;
        deadPlayer = null;
        skill = SkillManager.ResurrectionSkill;

        if (Game.Party?.Members == null || Game.Player == null || Game.Player.HasActiveVehicle)
            return false;

        if (!PlayerConfig.Get<bool>("RSBot.Skills.checkResurrectParty"))
            return false;

        // Not CanBeCasted: it also blocks a skill for its own duration after a cast
        if (skill?.Record == null || skill.HasCooldown || Game.Player.Mana < skill.Record.Consume_MP)
            return false;

        var resRadius = PlayerConfig.Get<ushort>("RSBot.Skills.numResRadius", 100);
        var now = Kernel.TickCount;
        var source = Game.Player.Movement.Source;

        foreach (var member in Game.Party.Members.ToArray())
        {
            // Only a member in sight can be targeted; its own state says whether it is dead
            var player = member.Player;
            if (player == null)
                continue;

            if (player.State.LifeState != LifeState.Dead)
            {
                // Alive again: a next death is handled at once
                _lockedUntil.Remove(member.Name);
                continue;
            }

            if (_lockedUntil.TryGetValue(member.Name, out var until) && now - until < 0)
                continue;

            var position = player.Movement.Source;
            if (position.DistanceTo(source) > resRadius || position.HasCollisionBetween(source))
                continue;

            deadMember = member;
            deadPlayer = player;

            return true;
        }

        return false;
    }

    private void Lock(string name, int milliseconds)
    {
        _lockedUntil[name] = Kernel.TickCount + milliseconds;
    }

    private void SetPendingCache(bool pending)
    {
        _pendingCache = pending;
        _pendingCacheTick = Kernel.TickCount;
        _hasPendingCache = true;
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        _hasPendingCache = false;
    }

    public void Stop()
    {
        _lockedUntil.Clear();
        _hasPendingCache = false;
    }
}
