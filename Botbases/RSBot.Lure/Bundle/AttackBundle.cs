using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Lure.Components;

namespace RSBot.Lure.Bundle;

internal static class AttackBundle
{
    private static uint _lastTargetId;

    public static void Tick()
    {
        var selected = Game.SelectedEntity;
        if (
            selected == null
            || Game.Player.InAction
            || !Game.Player.CanAttack
            || selected.IsBehindObstacle
        )
            return;

        if (_lastTargetId == selected.UniqueId)
        {
            if (Game.Player.InAction)
                SkillManager.CancelAction();

            return;
        }

        if (LureConfig.UseAttackingSkills)
        {
            var skill = SkillManager.GetNextSkill();

            if (skill == null && !LureConfig.UseNormalAttack)
                return;

            Log.Status("Attacking");
            SkillManager.CancelAction();

            var uniqueId = Game.SelectedEntity?.UniqueId;
            if (uniqueId == null)
                return;

            skill?.Cast(uniqueId.Value);
            _lastTargetId = uniqueId.Value;
        }

        if (LureConfig.UseNormalAttack && !Game.Player.InAction)
            SkillManager.CastAutoAttack();
    }
}
