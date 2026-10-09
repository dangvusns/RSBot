using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Party;
using RSBot.Core.Objects.Skill;

namespace RSBot.Training.Bundle.Healing;

/// <summary>
///     Casts the heal skills of Party › Healing on the player or party members whose HP dropped below the set percent,
///     and the cure skills on the player or party members that have a bad status.
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
    internal const string CureMemberEnabledKey = "RSBot.Party.Healing.Cure.Member.Enabled";
    internal const string CureSelfEnabledKey = "RSBot.Party.Healing.Cure.Self.Enabled";
    internal const string CureSkillsKey = "RSBot.Party.Healing.Cure.Skills";

    /// <summary>
    ///     The statuses a cure is cast for: Frozen … Combustion (Hidden and the unknown high bits are left out).
    ///     shortcut: any of them triggers any chosen cure skill; map statuses to skills if one skill can't cure all.
    /// </summary>
    private const BadEffect CurableEffects = (BadEffect)((1u << 23) - 1);

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
    private bool _cureMemberEnabled;
    private bool _cureSelfEnabled;

    /// <summary>
    ///     The heal skill ids in priority order.
    /// </summary>
    private List<uint> _skills = new();

    /// <summary>
    ///     The cure skill ids in priority order.
    /// </summary>
    private List<uint> _cureSkills = new();

    public void Invoke()
    {
        var player = Game.Player;
        if (player == null || player.HasActiveVehicle || player.State.LifeState == LifeState.Dead)
            return;

        // At most one cast per tick: heals first, then cures
        if (Heal())
            return;

        Cure();
    }

    /// <summary>
    ///     Heals the player, the whole party or a single member; returns true if a heal was cast.
    /// </summary>
    private bool Heal()
    {
        if (_skills.Count == 0 || !(_memberEnabled || _groupEnabled || _selfEnabled))
            return false;

        var skills = GetUsableSkills(_skills);
        if (skills.Count == 0)
            return false;

        // Self first, then the whole party, then a single member
        if (_selfEnabled && GetPlayerPercent() < _selfPercent)
        {
            var skill = skills.FirstOrDefault();
            if (skill != null && Cast(skill, 0, $"self ({GetPlayerPercent()}%)"))
                return true;
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
                    return true;
            }
        }

        if (_memberEnabled)
        {
            var skill = skills.FirstOrDefault(IsTargetHeal);
            if (skill == null)
                return false;

            var range = skill.Record.Action_Range / 10f;
            var member = members
                .Where(m => GetMemberPercent(m) < _memberPercent && m.Player.Position.DistanceToPlayer() <= range)
                .OrderBy(GetMemberPercent)
                .FirstOrDefault();

            if (member != null && Cast(skill, member.Player.UniqueId, $"{member.Name} ({GetMemberPercent(member)}%)"))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Casts a cure skill on the player or a party member that has a bad status.
    /// </summary>
    private void Cure()
    {
        if (_cureSkills.Count == 0 || !(_cureMemberEnabled || _cureSelfEnabled))
            return;

        var skills = GetUsableSkills(_cureSkills);
        if (skills.Count == 0)
            return;

        var playerEffect = Game.Player.BadEffect & CurableEffects;
        if (_cureSelfEnabled && playerEffect != BadEffect.None)
        {
            var skill = skills.FirstOrDefault();
            if (skill != null && Cast(skill, 0, $"cure self ({playerEffect})"))
                return;
        }

        if (!_cureMemberEnabled)
            return;

        var members = GetMembersInSight().Where(m => (m.Player.BadEffect & CurableEffects) != BadEffect.None).ToList();
        if (members.Count == 0)
            return;

        var targetSkill = skills.FirstOrDefault(IsTargetHeal);
        if (targetSkill != null)
        {
            var range = targetSkill.Record.Action_Range / 10f;
            var member = members
                .Where(m => m.Player.Position.DistanceToPlayer() <= range)
                .OrderBy(m => m.Player.Position.DistanceToPlayer())
                .FirstOrDefault();

            if (member != null
                && Cast(targetSkill, member.Player.UniqueId, $"cure {member.Name} ({member.Player.BadEffect & CurableEffects})"))
                return;
        }

        var groupSkill = skills.FirstOrDefault(IsGroupHeal);
        if (groupSkill != null)
            Cast(groupSkill, 0, $"cure party ({string.Join(", ", members.Select(m => m.Name))})");
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
        _cureMemberEnabled = PlayerConfig.Get(CureMemberEnabledKey, false);
        _cureSelfEnabled = PlayerConfig.Get(CureSelfEnabledKey, false);
        _cureSkills = PlayerConfig.GetArray<uint>(CureSkillsKey).ToList();

        Log.Debug(() =>
            $"[Healing] member={_memberEnabled}<{_memberPercent}% group={_groupEnabled}<{_groupPercent}% self={_selfEnabled}<{_selfPercent}% skills=[{string.Join(",", _skills)}] cure member={_cureMemberEnabled} self={_cureSelfEnabled} skills=[{string.Join(",", _cureSkills)}]"
        );
    }

    public void Stop()
    {
        //Nothing to do here
    }

    /// <summary>
    ///     Gets the learned skills of the list that can be cast now, in the configured order.
    /// </summary>
    private static List<SkillInfo> GetUsableSkills(List<uint> ids)
    {
        var result = new List<SkillInfo>();

        foreach (var id in ids)
        {
            // The saved id is of the level the skill had when it was added; use the level learned now
            var skill = Game.Player.Skills.FindLearnedSkill(id);
            if (skill?.Record == null || skill.HasCooldown || !skill.HasEnoughResources)
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
        return member.HealthSteps * PercentPerStep;
    }

    private static bool Cast(SkillInfo skill, uint target, string reason)
    {
        Log.Status($"Healing {reason}");

        var result = skill.CastBuff(target);
        Log.Debug(() => $"[Healing] {skill.Record.GetRealName()} -> {reason}: {result}");

        return result == SkillCastResult.Accepted;
    }
}
