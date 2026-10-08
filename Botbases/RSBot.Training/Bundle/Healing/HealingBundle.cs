using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Party;
using RSBot.Core.Objects.Skill;

namespace RSBot.Training.Bundle.Healing;

/// <summary>
///     Casts the heal skills of Party › Healing on the player or party members whose HP dropped below the set percent.
/// </summary>
internal class HealingBundle : IBundle
{
    internal const string MemberEnabledKey = "RSBot.Party.Healing.Member.Enabled";
    internal const string MemberPercentKey = "RSBot.Party.Healing.Member.Percent";
    internal const string GroupEnabledKey = "RSBot.Party.Healing.Group.Enabled";
    internal const string GroupPercentKey = "RSBot.Party.Healing.Group.Percent";
    internal const string SelfEnabledKey = "RSBot.Party.Healing.Self.Enabled";
    internal const string SelfPercentKey = "RSBot.Party.Healing.Self.Percent";
    internal const string SkillsKey = "RSBot.Party.Healing.Skills";

    /// <summary>
    ///     The party members' HP comes in steps of 10%.
    /// </summary>
    private const int PercentPerStep = 10;

    private bool _memberEnabled;
    private int _memberPercent;
    private bool _groupEnabled;
    private int _groupPercent;
    private bool _selfEnabled;
    private int _selfPercent;

    /// <summary>
    ///     The heal skill ids in priority order.
    /// </summary>
    private List<uint> _skills = new();

    public void Invoke()
    {
        if (_skills.Count == 0 || !(_memberEnabled || _groupEnabled || _selfEnabled))
            return;

        var player = Game.Player;
        if (player == null || player.HasActiveVehicle || player.State.LifeState == LifeState.Dead)
            return;

        var skills = GetUsableSkills();
        if (skills.Count == 0)
            return;

        // At most one cast per tick: self first, then the whole party, then a single member
        if (_selfEnabled && GetPlayerPercent() < _selfPercent)
        {
            var skill = skills.FirstOrDefault();
            if (skill != null && Cast(skill, 0, $"self ({GetPlayerPercent()}%)"))
                return;
        }

        var members = GetMembersInSight();

        if (_groupEnabled && Game.Party?.IsInParty == true)
        {
            var percents = members.Select(m => GetMemberPercent(m)).Append(GetPlayerPercent()).ToList();
            var average = (int)percents.Average();

            if (average < _groupPercent)
            {
                var skill = skills.FirstOrDefault(IsGroupHeal);
                if (skill != null && Cast(skill, 0, $"party average {average}%"))
                    return;
            }
        }

        if (_memberEnabled)
        {
            var skill = skills.FirstOrDefault(IsTargetHeal);
            if (skill == null)
                return;

            var range = skill.Record.Action_Range / 10f;
            var member = members
                .Where(m => GetMemberPercent(m) < _memberPercent && m.Player.Position.DistanceToPlayer() <= range)
                .OrderBy(GetMemberPercent)
                .FirstOrDefault();

            if (member != null)
                Cast(skill, member.Player.UniqueId, $"{member.Name} ({GetMemberPercent(member)}%)");
        }
    }

    public void Refresh()
    {
        _memberEnabled = PlayerConfig.Get(MemberEnabledKey, false);
        _memberPercent = PlayerConfig.Get(MemberPercentKey, 70);
        _groupEnabled = PlayerConfig.Get(GroupEnabledKey, false);
        _groupPercent = PlayerConfig.Get(GroupPercentKey, 60);
        _selfEnabled = PlayerConfig.Get(SelfEnabledKey, false);
        _selfPercent = PlayerConfig.Get(SelfPercentKey, 60);
        _skills = PlayerConfig.GetArray<uint>(SkillsKey).ToList();

        Log.Debug(() =>
            $"[Healing] member={_memberEnabled}<{_memberPercent}% group={_groupEnabled}<{_groupPercent}% self={_selfEnabled}<{_selfPercent}% skills=[{string.Join(",", _skills)}]"
        );
    }

    public void Stop()
    {
        //Nothing to do here
    }

    /// <summary>
    ///     Gets the learned heal skills that can be cast now, in the configured order.
    /// </summary>
    private List<SkillInfo> GetUsableSkills()
    {
        var result = new List<SkillInfo>();

        foreach (var id in _skills)
        {
            // The saved id is of the level the skill had when it was added; use the level learned now
            var skill = Game.Player.Skills.FindLearnedSkill(id);
            if (skill?.Record == null || skill.HasCooldown || Game.Player.Mana < skill.Record.Consume_MP)
                continue;

            result.Add(skill);
        }

        return result;
    }

    /// <summary>
    ///     Gets the alive party members (not the player) that are in sight.
    /// </summary>
    private static List<PartyMember> GetMembersInSight()
    {
        var playerName = Game.Player.Name;

        return Game.Party?.Members?
                .Where(m => m.Name != playerName && m.Player != null && m.Player.State.LifeState != LifeState.Dead)
                .ToList()
            ?? new List<PartyMember>();
    }

    /// <summary>
    ///     A skill cast on one chosen player, e.g. Healing.
    /// </summary>
    private static bool IsTargetHeal(SkillInfo skill)
    {
        return skill.Record.Target_Required && (skill.Record.TargetGroup_Ally || skill.Record.TargetGroup_Party);
    }

    /// <summary>
    ///     A skill that heals the party around the caster, e.g. Group Healing.
    /// </summary>
    private static bool IsGroupHeal(SkillInfo skill)
    {
        return !skill.Record.Target_Required && skill.Record.TargetGroup_Party;
    }

    private static int GetPlayerPercent()
    {
        var player = Game.Player;
        return player.MaximumHealth == 0 ? 100 : (int)(player.Health * 100L / player.MaximumHealth);
    }

    private static int GetMemberPercent(PartyMember member)
    {
        return (member.HealthMana >> 4) * PercentPerStep;
    }

    private static bool Cast(SkillInfo skill, uint target, string reason)
    {
        Log.Status($"Healing {reason}");

        var result = skill.CastBuff(target);
        Log.Debug(() => $"[Healing] {skill.Record.GetRealName()} -> {reason}: {result}");

        return result == SkillCastResult.Accepted;
    }
}
