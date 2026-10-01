namespace RSBot.Core.Components.Tracing;

/// <summary>
///     Defines how a player is traced.
/// </summary>
public enum TraceMode
{
    /// <summary>
    ///     Uses the native trace action of the game (0x7074).
    /// </summary>
    GameTrace,

    /// <summary>
    ///     Controlled by the bot: follows the trajectory of the target and falls back to the native trace while the
    ///     target walks with the keys.
    /// </summary>
    Smart,
}
