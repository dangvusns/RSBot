using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Skill;

namespace RSBot.Training.Bundle.Buff;

internal class BuffBundle : IBundle
{
    private bool _invoked;

    /// <summary>
    ///     The last logged reason a buff was skipped, by skill id; only changes are logged.
    /// </summary>
    private readonly Dictionary<uint, string> _skipReasons = new();
    private bool _buffBetweenAttacks { get; set; }

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        if (_invoked)
            return;

        if ((Game.Player.Untouchable || Game.Player.InAction) && !_buffBetweenAttacks)
            return;
        if ((Game.Player.Untouchable || Game.Player.Berzerking) && _buffBetweenAttacks)
            return;

        // A dead member that can be resurrected now goes first
        if (Bundles.Resurrect.HasPendingResurrect())
            return;

        try
        {
            _invoked = true;

            /*
             * #377
             * I think the bug now fixed but As a precaution, I find it appropriate to keep this solution here.
             * If the last fix is working, I will remove this code block from here in v2.4
             * Temporary fixer:
             * Issue: Sometimes the buffs dont removing with token from the active buffs list
             *      Problems:
             *          ActionBuffRemoveResponse:0xb072 does not calling
             *          There have another opcode for remove buff with token
             */
            foreach (
                var buff in SkillManager.Buffs.Union(new[] { SkillManager.ImbueSkill, SkillManager.ResurrectionSkill })
            )
            {
                if (buff == null)
                    continue;

                var isActive = Game.Player.State.HasActiveBuff(buff, out var info);
                if (isActive && buff.Isbugged && info.Isbugged)
                {
                    //#377 bug detected!
                    Log.Notify($"[#377] The buff [{info.Token}-{buff.Record?.GetRealName()}] expired");

                    EventManager.FireEvent("OnRemoveBuff", info);

                    var playerSkill = Game.Player.Skills.GetSkillInfoById(info.Id);
                    playerSkill?.Reset();
                    Game.Player.State.TryRemoveActiveBuff(info.Token, out _);
                }
            }

            // A speed skill can not be cast over a speed potion buff; wait until the potion runs out.
            var itemSpeedBuff = SpeedDrugManager.HasItemSpeedBuff;
            var buffs = SkillManager.Buffs.FindAll(p => IsCastableNow(p, itemSpeedBuff));
            if (buffs == null || buffs.Count == 0)
                return;

            Log.Status("Buffing");

            foreach (var buff in buffs)
            {
                if (Game.Player.State.LifeState != LifeState.Alive || Game.Player.HasActiveVehicle)
                    break;

                if (Bundles.Resurrect.HasPendingResurrect())
                    break;

                if (Game.Player.State.HasActiveBuff(buff, out _) && !buff.HasCooldown)
                    break;

                Log.Debug($"Trying to cast buff: {buff} {buff.Record.Basic_Code}");

                if (buff.HasDuration)
                {
                    buff.Cast(buff: true);
                    continue;
                }

                var result = buff.CastBuff();
                Log.Debug($"[Buff] {buff.Record?.GetRealName()}: {result}");
            }
        }
        finally
        {
            _invoked = false;
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the buff should be cast now; logs why not when the reason changes.
    /// </summary>
    private bool IsCastableNow(SkillInfo buff, bool itemSpeedBuff)
    {
        var reason = GetSkipReason(buff, itemSpeedBuff);

        if (!_skipReasons.TryGetValue(buff.Id, out var last) || last != reason)
        {
            _skipReasons[buff.Id] = reason;
            Log.Debug(() => $"[Buff] {buff.Record?.GetRealName()} ({buff.Id}) {reason ?? "castable"}");
        }

        return reason == null;
    }

    /// <summary>
    ///     Gets why the buff can not be cast now, or <c>null</c> if it can.
    /// </summary>
    private static string GetSkipReason(SkillInfo buff, bool itemSpeedBuff)
    {
        if (Game.Player.State.HasActiveBuff(buff, out var active))
            return $"skipped: active ({active.Record?.GetRealName()} {active.Id})";

        if (buff.HasCooldown)
            return "skipped: cooldown";

        if (!buff.HasEnoughResources)
            return "skipped: not enough HP/MP";

        if (buff.CanNotBeCasted)
            return "skipped: duration lock";

        if (itemSpeedBuff && SpeedDrugManager.IsSpeedBuff(buff))
            return "skipped: speed potion";

        if (!SkillManager.IsBuffAllowedNow(buff))
            return "skipped: strong-target only";

        return null;
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        _buffBetweenAttacks = PlayerConfig.Get<bool>("RSBot.Skills.checkCastBuffsBetweenAttacks", false);
        _invoked = false;
    }

    public void Stop()
    {
        _invoked = false;
        _skipReasons.Clear();
    }
}
