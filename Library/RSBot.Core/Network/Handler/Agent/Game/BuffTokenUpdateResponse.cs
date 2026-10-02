using RSBot.Core.Components;
using RSBot.Core.Event;

namespace RSBot.Core.Network.Handler.Agent;

internal class BuffTokenUpdateResponse : IPacketHandler
{
    /// <summary>
    ///     Gets or sets the opcode.
    /// </summary>
    /// <value>
    ///     The opcode.
    /// </value>
    public ushort Opcode => 0x3077;

    /// <summary>
    ///     Gets or sets the destination.
    /// </summary>
    /// <value>
    ///     The destination.
    /// </value>
    public PacketDestination Destination => PacketDestination.Client;

    /// <summary>
    ///     Handles the packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    public void Invoke(Packet packet)
    {
        Game.Ready = true;
        Game.Player.Teleportation = null;

        Log.Debug("Game loaded!");
        EventManager.FireEvent("OnTeleportComplete");

        var itemCount = packet.ReadByte();
        for (var i = 0; i < itemCount; i++)
        {
            var itemId = packet.ReadUInt();
            var milliseconds = packet.ReadInt();

            // Potion cooldowns still running from before the teleport.
            var record = Game.ReferenceManager.GetRefItem(itemId);
            if (record == null)
                continue;

            Log.Debug($"Item cooldown after teleport: {record.GetRealName()} {milliseconds} ms left");
            Game.Player.SetPotionCooldown(record, milliseconds);
        }

        var skillCount = packet.ReadByte();
        for (var i = 0; i < skillCount; i++)
        {
            var skillId = packet.ReadUInt();
            var milliseconds = packet.ReadInt();

            var skillInfo = Game.Player.Skills.GetSkillInfoById(skillId);
            skillInfo ??= SkillManager.Buffs.Find(p => p.Id == skillId);
            if (skillInfo == null)
                continue;

            // The server sends the time left on the cooldown, not the time since the cast.
            Log.Debug(
                $"Cooldown after teleport: {skillInfo.Record?.GetRealName()} {milliseconds} ms left (reuse {skillInfo.Record?.Action_ReuseDelay} ms)"
            );
            skillInfo.SetRemainingCooldown(milliseconds);
        }
    }
}
