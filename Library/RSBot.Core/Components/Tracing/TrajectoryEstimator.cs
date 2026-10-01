using System;
using System.Numerics;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     Estimates the position of a traced player. Pure logic, does not touch the game state.
/// </summary>
/// <remarks>
///     The entity model of the bot already advances the position of every entity each tick towards its destination
///     (or along its heading while walking with the keys), so the position of a snapshot is the current estimate.
///     This class only adds a short, conservative look-ahead on top of it.
/// </remarks>
public static class TrajectoryEstimator
{
    /// <summary>
    ///     Gets the estimated current position of the target.
    /// </summary>
    /// <param name="state">The motion snapshot.</param>
    public static Vector2 EstimateCurrent(TargetMotionState state)
    {
        return state.Position;
    }

    /// <summary>
    ///     Gets the point the character should aim at. While the target walks to a clicked destination the end of its
    ///     trajectory is known exactly, so the character heads for the same spot instead of chasing the moving target.
    ///     Otherwise the (optionally predicted) current position is used.
    /// </summary>
    /// <param name="state">The motion snapshot.</param>
    /// <param name="now">The current tick count.</param>
    /// <param name="options">The options.</param>
    public static Vector2 AimPoint(TargetMotionState state, int now, TraceOptions options)
    {
        if (options.AimAtDestination && state.Kind == TargetMovementKind.ClickMove)
            return state.Destination;

        return Predict(state, now, options);
    }

    /// <summary>
    ///     Gets a value indicating whether the movement of the target is not trusted anymore. Only a target walking
    ///     with the keys can become stale, because the entity model walks that heading until the server stops it.
    /// </summary>
    /// <param name="state">The motion snapshot.</param>
    /// <param name="now">The current tick count.</param>
    /// <param name="options">The options.</param>
    public static bool IsStale(TargetMotionState state, int now, TraceOptions options)
    {
        if (state.Kind != TargetMovementKind.KeyWalk)
            return false;

        return TraceTime.Elapsed(now, state.LastMovementTick) > options.MovementStaleTimeout;
    }

    /// <summary>
    ///     Gets the position of the target a short time ahead. The result never passes the clicked destination and
    ///     equals the current position if the prediction is disabled, the target stands or its data is stale.
    /// </summary>
    /// <param name="state">The motion snapshot.</param>
    /// <param name="now">The current tick count.</param>
    /// <param name="options">The options.</param>
    public static Vector2 Predict(TargetMotionState state, int now, TraceOptions options)
    {
        if (!options.PredictionEnabled || !state.Moving || state.Speed <= 0)
            return state.Position;

        if (IsStale(state, now, options))
            return state.Position;

        var time = Math.Min(options.PredictionTime, options.MaxPredictionTime);
        if (time <= 0)
            return state.Position;

        var distance = (float)(state.Speed * time / 1000.0);

        if (state.HasDestination)
        {
            var offset = state.Destination - state.Position;
            var remaining = offset.Length();
            if (remaining < 0.001f)
                return state.Position;

            return state.Position + offset / remaining * Math.Min(distance, remaining);
        }

        var direction = new Vector2(MathF.Cos(state.Angle), MathF.Sin(state.Angle));

        return state.Position + direction * distance;
    }
}
