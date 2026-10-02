using System.Collections.Generic;
using System.Linq;
using RSBot.Core.Network;
using RSBot.Core.Objects.Skill;

namespace RSBot.Core.Objects;

public class Skills
{
    /// <summary>
    ///     Gets or sets the masteries.
    /// </summary>
    /// <value>
    ///     The masteries.
    /// </value>
    public List<MasteryInfo> Masteries { get; set; }

    /// <summary>
    ///     Gets or sets the learned skills.
    /// </summary>
    /// <value>
    ///     The learned skills.
    /// </value>
    public List<SkillInfo> KnownSkills { get; set; }

    /// <summary>
    ///     Gets or sets the pending withdraw skill.
    /// </summary>
    /// <value>
    ///     The pending withdraw skill.
    /// </value>
    internal uint PendingWithdrawSkill { get; set; }

    /// <summary>
    ///     Creates a new Skill object from the given packet
    /// </summary>
    /// <param name="packet">The packet.</param>
    /// <returns></returns>
    internal static Skills FromPacket(Packet packet)
    {
        var result = new Skills { KnownSkills = new List<SkillInfo>(), Masteries = new List<MasteryInfo>() };

        packet.ReadByte(); //unknown

        while (packet.ReadByte() == 0x01)
            result.Masteries.Add(MasteryInfo.FromPacket(packet));

        packet.ReadByte(); //unknown

        while (packet.ReadByte() == 0x01)
            result.KnownSkills.Add(SkillInfo.FromPacket(packet));

        return result;
    }

    /// <summary>
    ///     Gets the name of the skill by.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public SkillInfo GetSkillByName(string name)
    {
        return KnownSkills.Find(s => s.Record?.GetRealName() == name);
    }

    public SkillInfo GetSkillByCodeName(string codeName)
    {
        var skill = KnownSkills.FirstOrDefault(s => s.Record?.Basic_Code == codeName);
        if (skill != null || string.IsNullOrEmpty(codeName))
            return skill;

        // Scripts store the code of one skill level; fall back to the level that is learned now.
        var record = Game.ReferenceManager?.SkillData.Values.FirstOrDefault(r => r.Basic_Code == codeName);

        return record == null ? null : FindLearnedSkill(record.ID);
    }

    /// <summary>
    ///     Finds the learned skill for a saved skill id, even if the skill has been upgraded or withdrawn
    ///     since the id was saved (the id of each skill level differs).
    /// </summary>
    /// <param name="savedId">The saved skill identifier.</param>
    /// <returns>The learned skill, or <c>null</c> if no level of that skill is learned.</returns>
    public SkillInfo FindLearnedSkill(uint savedId)
    {
        var skill = GetSkillInfoById(savedId);
        if (skill != null)
            return skill;

        if (Game.ReferenceManager?.SkillData.TryGetValue(savedId, out var saved) != true || saved == null)
            return null;

        // Every level of a skill shares its group.
        return KnownSkills
            .Where(s =>
                s.Record != null
                && (
                    saved.GroupID != 0
                        ? s.Record.GroupID == saved.GroupID
                        : !string.IsNullOrEmpty(saved.Basic_Group) && s.Record.Basic_Group == saved.Basic_Group
                )
            )
            .OrderByDescending(s => s.Record.Basic_Level)
            .FirstOrDefault();
    }

    /// <summary>
    ///     Gets the name of the skill by.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    public SkillInfo? GetSkillRecordByName(string name)
    {
        if (KnownSkills == null || name == null)
            return null;

        return KnownSkills.Find(s => s.Record.GetRealName() == name);
    }

    /// <summary>
    ///     Gets the skill information by identifier.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <returns></returns>
    public SkillInfo GetSkillInfoById(uint skillId)
    {
        return KnownSkills.Find(s => s.Id == skillId);
    }

    /// <summary>
    ///     Gets the skill information by the group identifier.
    /// </summary>
    /// <param name="skillGroupId">The skill group identifier.</param>
    /// <returns></returns>
    public SkillInfo GetSkillInfoByGroupId(int skillGroupId)
    {
        return KnownSkills.FirstOrDefault(s => s.Record.GroupID == skillGroupId);
    }

    /// <summary>
    ///     Gets the mastery information by identifier.
    /// </summary>
    /// <param name="masteryId">The mastery identifier.</param>
    /// <returns></returns>
    public MasteryInfo GetMasteryInfoById(uint masteryId)
    {
        return Masteries.Find(m => m.Id == masteryId);
    }

    /// <summary>
    ///     Updates the mastery level.
    /// </summary>
    /// <param name="masteryId">The mastery identifier.</param>
    /// <param name="level">The level.</param>
    internal void UpdateMasteryLevel(uint masteryId, byte level)
    {
        var mastery = Masteries.FirstOrDefault(m => m.Id == masteryId);
        if (mastery != null)
            mastery.Level = level;
    }

    /// <summary>
    ///     Determines whether the specified skill exists.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <returns></returns>
    public bool HasSkill(uint skillId)
    {
        return KnownSkills.Any(p => p.Id == skillId);
    }

    /// <summary>
    ///     Removes the skill by identifier.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    public void RemoveSkillById(uint skillId)
    {
        KnownSkills.RemoveAll(p => p.Id == skillId);
    }
}
