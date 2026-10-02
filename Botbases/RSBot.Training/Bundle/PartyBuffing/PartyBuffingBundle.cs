using System;
using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Party;
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
    ///     The casts per member and buff that did not show up on the member yet: count, tick of the last cast.
    /// </summary>
    private readonly Dictionary<(string Member, uint Skill), (int Count, int Tick)> _pendingCasts = new();

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

                if (skill == null || skill.HasCooldown)
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

                var isActive = member.State.HasActiveBuff(skill, out var info);
                if (isActive && skill.Isbugged && info.Isbugged)
                {
                    Log.Notify($"The buff on {member.Name} [{skill.Token}-{skill.Record?.GetRealName()}] expired");

                    skill?.Reset();
                    continue;
                }

                var key = (member.Name, skill.Id);
                if (isActive)
                {
                    _pendingCasts.Remove(key);
                    continue;
                }

                if (_pendingCasts.TryGetValue(key, out var pending) && pending.Count >= MAX_FAILED_CASTS)
                {
                    if (Kernel.TickCount - pending.Tick < FAILED_CAST_PAUSE_MS)
                        continue;

                    _pendingCasts.Remove(key);
                    pending = default;
                }

                Log.Status($"Buffing {skill.Record?.GetRealName()} party member {member.Name}");
                skill.Cast(member.UniqueId, true);

                _pendingCasts[key] = (pending.Count + 1, Kernel.TickCount);
                if (pending.Count + 1 == MAX_FAILED_CASTS)
                    Log.Warn(
                        $"[Party buffing] {skill.Record?.GetRealName()} did not show up on {member.Name} after {MAX_FAILED_CASTS} casts, pausing it for {FAILED_CAST_PAUSE_MS / 1000}s"
                    );
            }
        }
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
