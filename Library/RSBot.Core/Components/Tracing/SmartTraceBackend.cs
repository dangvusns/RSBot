using System.Numerics;
using RSBot.Core.Objects;

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
}
