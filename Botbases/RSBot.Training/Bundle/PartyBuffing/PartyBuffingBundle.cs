using System;
using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Party;
using RSBot.Core.Objects.Skill;
using RSBot.Core.Components;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Training.Bundle.PartyBuffing;

internal class PartyBuffingBundle : IBundle
{
    /// <summary>
    ///     <c>true</c> if refreshing this bundle; otherwise <c>false</c>
    /// </summary>
    private bool _refreshing;

    /// <summary>
    ///     The buffing party members
    /// </summary>
    private List<BuffingPartyMember> BuffingPartyMembers;

    /// <summary>
    ///     A buff that is cast this often on a member without showing up there is paused for a while.
    /// </summary>
    private const int MAX_FAILED_CASTS = 3;

    private const int FAILED_CAST_PAUSE_MS = 60_000;

    /// <summary>
    ///     The time a buff gets to show up on the member after the server accepted the cast.
    /// </summary>
    private const int SHOW_UP_GRACE_MS = 2_000;

    /// <summary>
    ///     The accepted casts per member and buff that did not show up on the member yet: count, tick of the last cast.
    /// </summary>
    private readonly Dictionary<(string Member, uint Skill), (int Count, int Tick)> _pendingCasts = new();

    /// <summary>
    ///     The instant skills whose params were already logged.
    /// </summary>
    private readonly HashSet<uint> _loggedInstantSkills = new();

    /// <summary>
    ///     Initialize the instance of <seealso cref="PartyBuffingBundle" />
    /// </summary>
    public PartyBuffingBundle()
    {
        EventManager.SubscribeEvent("OnPartyBuffSettingsChanged", OnPartyBuffSettingsChanged);
    }

    /// <summary>
    ///     Invoke the bundle
    /// </summary>
    public void Invoke()
    {
        if (_refreshing)
            return;

        if (Game.Player.HasActiveVehicle)
            return;

        var selectedGroup = PlayerConfig.Get("RSBot.Party.Buffing.SelectedGroup", "Default");

        SpawnManager.TryGetEntities<SpawnedPlayer>(p =>
            BuffingPartyMembers.Any(s => s.Group == selectedGroup && s.Name == p.Name),
            out var members
        );

        foreach (var member in members)
        {
            // A character can be in several groups, only the selected group's buffs are cast
            var buffingMember = BuffingPartyMembers.Find(p => p.Name == member.Name && p.Group == selectedGroup);
            if (buffingMember == null)
                continue;

            if (buffingMember.Buffs.Count == 0)
                continue;

            //if party member is dead, dont try to buff
            if (member.State.LifeState == LifeState.Dead)
                continue;

            Log.Status($"Buffing party");

            var activeBuffs = member.State.ActiveBuffs;

            foreach (var buff in buffingMember.Buffs)
            {
                // The saved id is of the level the buff had when it was added; use the level learned now
                var skill = Game.Player.Skills.FindLearnedSkill(buff);
                if (skill == null)
                    continue;

                // Check if the skill can target this player:
                // - If skill targets allies (TargetGroup_Ally), we can buff any nearby player
                // - If skill targets party only (TargetGroup_Party), player must be in our party
                if (skill.Record.TargetGroup_Party && !skill.Record.TargetGroup_Ally)
                {
                    if (!(Game.Party?.Members?.Any(p => p.Name == member.Name) ?? false))
                    {
                        continue;
                    }
                }

                // Out of range the caster walks to the member; skip members it could only reach by leaving the training area
                var area = Container.Bot.Area;
                var range = skill.Record.Action_Range / 10f;
                if (
                    member.Position.DistanceToPlayer() > range
                    && member.Position.DistanceTo(area.Position) > area.Radius + range
                )
                    continue;

                var key = (member.Name, skill.Id);

                // Checked before the cooldown: a buff whose cooldown outlasts it is only seen while on cooldown.
                // Also for skills without a duration param, some (e.g. bard buffs) still leave a buff.
                var isActive = member.State.HasActiveBuff(skill, out var info);
                if (isActive && skill.Isbugged && info.Isbugged)
                {
                    Log.Notify($"The buff on {member.Name} [{info.Token}-{skill.Record?.GetRealName()}] expired");

                    // Drop the stale entry, otherwise it is found again on every tick and the buff is never recast
                    member.State.TryRemoveActiveBuff(info.Token, out _);
                    skill?.Reset();
                    continue;
                }

                if (isActive)
                {
                    _pendingCasts.Remove(key);
                    continue;
                }

                string instantReason = null;
                if (!skill.HasDuration && !NeedsInstantSkill(skill, member.Name, out instantReason))
                    continue;

                if (skill.HasCooldown || Game.Player.Mana < skill.Record.Consume_MP)
                    continue;

                if (_pendingCasts.TryGetValue(key, out var pending) && pending.Count >= MAX_FAILED_CASTS)
                {
                    var elapsed = Kernel.TickCount - pending.Tick;

                    // The last accepted cast may still show up
                    if (elapsed < SHOW_UP_GRACE_MS)
                        continue;

                    if (pending.Count == MAX_FAILED_CASTS)
                    {
                        Log.Warn(
                            $"[Party buffing] {skill.Record?.GetRealName()} did not show up on {member.Name} after {MAX_FAILED_CASTS} casts, pausing it for {FAILED_CAST_PAUSE_MS / 1000}s"
                        );
                        _pendingCasts[key] = (pending.Count + 1, pending.Tick);
                    }

                    if (elapsed < FAILED_CAST_PAUSE_MS)
                        continue;

                    _pendingCasts.Remove(key);
                    pending = default;
                }

                Log.Status($"Buffing {skill.Record?.GetRealName()} party member {member.Name}");

                // A refused cast (e.g. not enough MP) did not reach the member, so it is not counted
                var result = skill.CastBuff(member.UniqueId);

                // Shows whether the "only when needed" setting picked the right moments
                if (instantReason != null)
                    Log.Debug($"[Party buffing] {skill.Record?.GetRealName()} -> {member.Name} ({instantReason}): {result}");
                if (result != SkillCastResult.Accepted || !skill.HasDuration)
                    continue;

                _pendingCasts[key] = (pending.Count + 1, Kernel.TickCount);
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether an instant skill (no duration, e.g. a heal or MP transfer) should be cast on the member.
    /// </summary>
    /// <param name="skill">The instant skill.</param>
    /// <param name="memberName">The member's name.</param>
    private bool NeedsInstantSkill(SkillInfo skill, string memberName, out string reason)
    {
        if (_loggedInstantSkills.Add(skill.Id))
        {
            skill.TryGetRestoredStats(out var health, out var mana);
            Log.Debug(
                $"[Party buffing] {skill.Record?.GetRealName()} has no duration; restores HP={health} MP={mana} (params: {string.Join(",", skill.Record?.Params ?? new List<int>())})"
            );
        }

        return InstantSkills.IsNeededFor(skill, memberName, out reason);
    }

    /// <summary>
    ///     Refresh the bundle
    /// </summary>
    public void Refresh()
    {
        _refreshing = true;

        // Don't need to use clear, because gc will handle the unnecessary objects
        BuffingPartyMembers = new List<BuffingPartyMember>();

        var settings = PlayerConfig.Get("RSBot.Party.Buffing", string.Empty);
        var collection = settings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var item in collection)
        {
            try
            {
                BuffingPartyMembers.Add(new BuffingPartyMember(item));
            }
            catch (InvalidOperationException)
            {
                // A broken entry must not stop the other members from being buffed
            }
        }

        _pendingCasts.Clear();
        _loggedInstantSkills.Clear();
        _refreshing = false;
    }

    public void Stop()
    {
        //Nothing to do here
    }

    /// <summary>
    ///     Update the <see cref="BuffingPartyMembers" /> list when settings changed from another plugins
    /// </summary>
    private void OnPartyBuffSettingsChanged()
    {
        Refresh();
    }
}
