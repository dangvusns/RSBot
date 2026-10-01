using System.Numerics;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Cos;
using RSBot.Core.Network;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     Walks the character to a point with the regular movement of the bot. It does not plan a path: the click is sent
///     to the game, which routes the character itself.
/// </summary>
public static class SmartTraceBackend
{
    /// <summary>
    ///     Walks to the specified world coordinates. Blocks until the game answered the click.
    /// </summary>
    /// <param name="destination">The destination in world coordinates.</param>
    /// <returns><c>true</c> if the game accepted the click; otherwise <c>false</c>.</returns>
    public static bool MoveTo(Vector2 destination)
    {
        var player = Game.Player;
        if (player == null)
            return false;

        var position = new Position(destination.X, destination.Y, player.Position.Region);

        return player.MoveTo(position, false);
    }

    /// <summary>
    ///     Sends a destination without waiting on the packet thread. Progress is checked by the next snapshot.
    ///     Packet layouts match Player.MoveTo and Cos.MoveTo, including mounted and dungeon movement.
    /// </summary>
    public static bool SendMove(Vector2 destination)
    {
        var player = Game.Player;
        if (player == null || !Game.Ready)
            return false;

        var position = new Position(destination.X, destination.Y, player.Position.Region);
        if (player.Movement.Source.DistanceTo(position) > 150)
            return false;

        var vehicle = player.Vehicle;
        var packet = new Packet(vehicle != null ? (ushort)0x70C5 : (ushort)0x7021);
        if (vehicle != null)
        {
            packet.WriteUInt(vehicle.UniqueId);
            packet.WriteByte(CosCommand.Move);
        }
        packet.WriteByte(1);
        position.Region.Serialize(packet);
        if (!position.Region.IsDungeon)
        {
            packet.WriteShort(position.XOffset);
            packet.WriteShort(position.ZOffset);
            packet.WriteShort(position.YOffset);
        }
        else
        {
            packet.WriteInt(position.XOffset);
            packet.WriteInt(position.ZOffset);
            packet.WriteInt(position.YOffset);
        }
        PacketManager.SendPacket(packet, PacketDestination.Server);
        return true;
    }

}
