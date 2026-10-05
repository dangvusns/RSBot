using System.Linq;
using RSBot.Core;
using RSBot.Core.Objects.Party;
using RSBot.Core.Objects.Skill;

namespace RSBot.Training.Bundle;

/// <summary>
///     Decides when an instant skill (no duration, e.g. a heal or an MP transfer) is cast: only when someone it
///     restores is not full, or every cooldown if the user turned the check off.
/// </summary>
internal static class InstantSkills
{
    /// <summary>
    ///     The party members' HP and MP come in steps of 10% (high nibble HP, low nibble MP); 10 is full.
    /// </summary>
    private const int FullSteps = 10;

    /// <summary>
    ///     Gets a value indicating whether instant skills are only cast when needed (Party › Buffing setting).
    /// </summary>
    public static bool OnlyWhenNeeded => PlayerConfig.Get("RSBot.Party.Buffing.InstantSkillsWhenNeeded", true);

    /// <summary>
    ///     Gets a value indicating whether the instant skill should be cast on the party member.
    /// </summary>
    /// <param name="skill">The instant skill.</param>
    /// <param name="memberName">The member's name.</param>
    /// <param name="reason">Why, for the log.</param>
    public static bool IsNeededFor(SkillInfo skill, string memberName, out string reason)
    {
        if (!OnlyWhenNeeded)
        {
            reason = "every cooldown";
            return true;
        }

        if (!skill.TryGetRestoredStats(out var health, out var mana))
        {
            reason = "restores nothing known, every cooldown";
            return true;
        }

        var member = Game.Party?.Members?.Find(p => p.Name == memberName);
        if (member == null)
        {
            reason = "no party HP/MP, every cooldown";
            return true;
        }

        reason = Describe(member);

        return IsMissing(member, health, mana);
    }

    /// <summary>
    ///     Gets a value indicating whether an instant skill of the player's own buff list should be cast: the player,
    ///     or for a party skill (e.g. Group Healing) a party member nearby, misses what it restores.
    /// </summary>
    /// <param name="skill">The instant skill.</param>
    /// <param name="reason">Why, for the log.</param>
    public static bool IsNeededBySelfOrParty(SkillInfo skill, out string reason)
    {
        if (!OnlyWhenNeeded)
        {
            reason = "every cooldown";
            return true;
        }

        if (!skill.TryGetRestoredStats(out var health, out var mana))
        {
            reason = "restores nothing known, every cooldown";
            return true;
        }

        var player = Game.Player;
        if ((health && player.Health < player.MaximumHealth) || (mana && player.Mana < player.MaximumMana))
        {
            reason = $"self HP {player.Health}/{player.MaximumHealth} MP {player.Mana}/{player.MaximumMana}";
            return true;
        }

        if (skill.Record?.TargetGroup_Party == true)
        {
            // Only members in sight can be reached by the skill
            var member = Game.Party?.Members?.FirstOrDefault(m =>
                m.Name != player.Name && m.Player != null && IsMissing(m, health, mana)
            );
            if (member != null)
            {
                reason = $"{member.Name} {Describe(member)}";
                return true;
            }
        }

        reason = "everyone full";
        return false;
    }

    private static bool IsMissing(PartyMember member, bool health, bool mana)
    {
        return (health && member.HealthMana >> 4 < FullSteps) || (mana && (member.HealthMana & 0x0F) < FullSteps);
    }

    private static string Describe(PartyMember member)
    {
        return $"HP {member.HealthMana >> 4}/{FullSteps} MP {member.HealthMana & 0x0F}/{FullSteps}";
    }
}
