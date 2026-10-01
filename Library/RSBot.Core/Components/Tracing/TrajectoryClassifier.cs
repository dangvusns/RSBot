namespace RSBot.Core.Components.Tracing;

/// <summary>
///     How far the movement data of a target can be trusted to calculate a follow point.
/// </summary>
public enum TrajectoryQuality
{
    /// <summary>
    ///     The target walks to a clicked destination, or stands on a position the server confirmed.
    /// </summary>
    Reliable,

    /// <summary>
    ///     The target walks along a heading without destination (keyboard movement or a click into the sky). The
    ///     position is only dead reckoned and the end of the movement is unknown.
    /// </summary>
    HeadingOnly,

    /// <summary>
    ///     The target turns on the spot. The entity model still advances the position along the heading, so the
    ///     position is wrong until the server sends the next position.
    /// </summary>
    Spinning,

    /// <summary>
    ///     The destination is not a usable position.
    /// </summary>
    InvalidDestination,
}

/// <summary>
///     Classifies the trajectory quality from the movement flags the server sent (0xB021). Pure logic, no timers.
/// </summary>
/// <remarks>
///     0xB021 either carries a destination (ground click) or a heading with a key movement flag (0 = spinning,
///     1 = walking). Keyboard movement and sky clicks both arrive as heading only movement. A stop (0xB023) sends the
///     real position and ends the movement, so a standing target is always reliable.
/// </remarks>
public static class TrajectoryClassifier
{
    /// <summary>
    ///     Classifies the specified motion snapshot.
    /// </summary>
    /// <param name="state">The motion snapshot.</param>
    public static TrajectoryQuality Classify(TargetMotionState state)
    {
        if (!state.Moving)
            return TrajectoryQuality.Reliable;

        if (!state.HasDestination)
            return state.Spinning ? TrajectoryQuality.Spinning : TrajectoryQuality.HeadingOnly;

        if (!float.IsFinite(state.Destination.X) || !float.IsFinite(state.Destination.Y))
            return TrajectoryQuality.InvalidDestination;

        return TrajectoryQuality.Reliable;
    }
}
