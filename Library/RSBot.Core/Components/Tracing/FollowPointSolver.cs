using System;
using System.Numerics;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     Calculates where the character has to walk to and when it follows. Pure logic, does not touch the game state.
/// </summary>
public static class FollowPointSolver
{
    /// <summary>
    ///     Gets the range state for the distance to the target, with hysteresis: the character starts to follow above
    ///     <see cref="TraceOptions.StartMoveDistance" /> and is in range below <see cref="TraceOptions.StopMoveDistance" />.
    ///     In between the previous state is kept, so the character does not alternate around a single threshold.
    /// </summary>
    /// <param name="current">The current state.</param>
    /// <param name="distance">The distance to the target.</param>
    /// <param name="options">The options.</param>
    /// <param name="selfMoving">A value indicating whether the character is walking.</param>
    public static TraceState NextRangeState(
        TraceState current,
        double distance,
        TraceOptions options,
        bool selfMoving
    )
    {
        if (distance > options.StartMoveDistance)
            return TraceState.Following;

        if (distance <= options.StopMoveDistance)
            return TraceState.InRange;

        var arrived = distance <= options.FollowDistance + options.ArrivalTolerance;

        if (current == TraceState.Following)
            return !selfMoving && arrived ? TraceState.InRange : TraceState.Following;

        if (current == TraceState.InRange)
            return TraceState.InRange;

        return arrived ? TraceState.InRange : TraceState.Following;
    }

    /// <summary>
    ///     Gets the point the character has to walk to: on the line to the target, at the follow distance.
    /// </summary>
    /// <param name="self">The position of the character.</param>
    /// <param name="target">The position of the target.</param>
    /// <param name="options">The options.</param>
    /// <param name="destination">The destination.</param>
    /// <returns><c>false</c> if the character is already within the follow distance (plus the arrival tolerance).</returns>
    public static bool TrySolve(Vector2 self, Vector2 target, TraceOptions options, out Vector2 destination)
    {
        var offset = target - self;
        var distance = offset.Length();

        // Moves shorter than the tolerance only make the character click its own position
        if (distance <= options.FollowDistance + options.ArrivalTolerance || distance < 0.001f)
        {
            destination = self;
            return false;
        }

        var step = Math.Min(distance - options.FollowDistance, options.MaxMoveDistance);
        destination = self + offset / distance * (float)step;

        return true;
    }
}
