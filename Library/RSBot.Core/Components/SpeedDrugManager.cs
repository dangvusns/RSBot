using RSBot.Core.Objects;
using RSBot.Core.Objects.Skill;

namespace RSBot.Core.Components;

/// <summary>
///     Uses speed potions without fighting speed skills: a potion is skipped while any speed buff is active or a
///     configured speed skill is ready to be cast.
/// </summary>
public static class SpeedDrugManager
{
    /// <summary>
    ///     The movement speed skill param ('hste'), used by both speed potions and speed skills.
    /// </summary>
    private const int SpeedParam = 1752396901;

    /// <summary>
    ///     A used potion takes a moment to show up as a buff; do not use another one meanwhile.
    /// </summary>
    private const int RetryDelay = 2000;

    private static int _lastUseTick;

    // List.Exists is used on purpose: these lists are changed by the network/UI threads, and unlike a LINQ
    // enumeration it does not throw when that happens mid-check.

    /// <summary>
    ///     Gets a value indicating whether the skill raises the movement speed.
    /// </summary>
    public static bool IsSpeedBuff(SkillInfo skill) => skill?.Record?.Params.Contains(SpeedParam) == true;

    /// <summary>
    ///     Gets a value indicating whether a speed buff from an item (not a learned skill) is active.
    /// </summary>
    public static bool HasItemSpeedBuff =>
        Game.Player.State.ActiveBuffs.Exists(b => IsSpeedBuff(b) && !Game.Player.Skills.HasSkill(b.Id));

    /// <summary>
    ///     Uses a speed potion if no speed buff is active and no configured speed skill can do the job.
    /// </summary>
    public static void TryUse()
    {
        var player = Game.Player;
        if (player == null || Kernel.TickCount - _lastUseTick < RetryDelay)
            return;

        if (player.State.ActiveBuffs.Exists(IsSpeedBuff))
            return;

        // A free speed skill is about to be cast by the buff loop; keep the potion.
        if (SkillManager.Buffs.Exists(b => IsSpeedBuff(b) && player.Skills.HasSkill(b.Id) && b.CanBeCasted))
            return;

        var item = player.Inventory.GetItem(new TypeIdFilter(3, 3, 13, 1), p => p.Record.Desc1.Contains("_SPEED_"));
        if (item == null)
            return;

        _lastUseTick = Kernel.TickCount;
        item.Use();
    }
}
