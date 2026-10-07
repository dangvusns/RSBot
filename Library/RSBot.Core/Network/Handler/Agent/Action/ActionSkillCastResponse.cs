using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Core.Network.Handler.Agent.Action;

internal class ActionSkillCastResponse : IPacketHandler
{
    /// <summary>
    ///     Gets or sets the destination.
    /// </summary>
    /// <value>
    ///     The destination.
    /// </value>
    public PacketDestination Destination => PacketDestination.Client;

    /// <summary>
    ///     Gets or sets the opcode.
    /// </summary>
    /// <value>
    ///     The opcode.
    /// </value>
    public ushort Opcode => 0xB070;

    /// <summary>
    ///     Invokes the specified packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    public void Invoke(Packet packet)
    {
        var result = packet.ReadByte();
        if (result != 0x01)
        {
            var errorCode = packet.ReadByte();

            switch (errorCode)
            {
                case 0x0C:
                    // Same other skills are already running
                    break;

                case 0x0E:
                    Game.Player.EquipAmmunition();
                    break;

                case 0x05:
                    // Still on cooldown: correct our timer so the bot stops retrying every tick.
                    SkillManager.OnCastRefusedByCooldown();
                    break;

                case 0x06: // invalid target
                    break;

                case 0x10: // obstacle
                    EventManager.FireEvent("OnTargetBehindObstacle");
                    break;

                // 0x04: not enough MP (seen right before MP potions); others are not known yet
                default:
                    SkillManager.OnCastRefused(errorCode);
                    break;
            }

            return;
        }

        var action = Objects.Action.ReadCastStart(packet, Game.ClientType);
        Objects.Action.RememberCast(action);
        if (action.PlayerIsExecutor)
            SkillManager.ConfirmOpener(action.TargetId, action.SkillId);
        action.ReadPacket(packet);

        if (action.PlayerIsExecutor)
        {
            //Game.Player.StopMoving();

            var skillInfo = Game.Player.Skills.GetSkillInfoById(action.SkillId);
            if (skillInfo == null)
                skillInfo = SkillManager.Buffs.Find(p => p.Id == action.SkillId);

            skillInfo?.Update();
            if (skillInfo != null)
                SkillManager.OnCastSucceeded(skillInfo);

            EventManager.FireEvent("OnCastSkill", action.SkillId);

            return;
        }

        if (!action.TryGetExecutor<SpawnedBionic>(out var executor))
            return;

        executor.TargetId = action.TargetId;
        executor.TargetTick = Kernel.TickCount;
        //executor.StopMoving();

        if (!action.PlayerIsTarget)
            return;

        EventManager.FireEvent("OnEnemySkillOnPlayer");

        executor.StartAttackingTimer();
    }
}
