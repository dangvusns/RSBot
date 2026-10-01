using RSBot.Core.Network;
using RSBot.Core.Objects;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     Sends the native trace action of the game.
/// </summary>
public static class GameTraceBackend
{
    /// <summary>
    ///     Makes the character trace the specified player: 0x7074, execute (1), trace (3), entity (1), unique id.
    /// </summary>
    /// <param name="uniqueId">The unique id of the player to trace.</param>
    public static void Send(uint uniqueId)
    {
        if (uniqueId == 0)
            return;

        var packet = new Packet(0x7074);
        packet.WriteByte(ActionCommandType.Execute);
        packet.WriteByte(ActionType.Trace);
        packet.WriteByte(ActionTarget.Entity);
        packet.WriteUInt(uniqueId);

        PacketManager.SendPacket(packet, PacketDestination.Server);
    }
}
