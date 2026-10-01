using System;
using System.Numerics;
using RSBot.Core.Components.Tracing;

namespace RSBot.Core.Tests;

/// <summary>
///     Drives the pure trace evaluator with synthetic input: no game, no kernel, only ticks and positions.
/// </summary>
internal sealed class TraceHarness
{
    public TraceHarness(TraceMode mode, TraceOptions options = null)
    {
        Options = options ?? TraceOptions.PartyMaster();
        Runtime = new TraceRuntime(mode);
    }

    public TraceOptions Options { get; }

    public TraceRuntime Runtime { get; }

    public int Now { get; set; } = 10000;

    public Vector2 Self { get; set; }

    public bool SelfMoving { get; set; }

    public TraceBusy Busy { get; set; }

    /// <summary>
    ///     Evaluates at the current time. A <c>null</c> target means the player is not visible.
    /// </summary>
    public TraceDecision Evaluate(TargetMotionState? target, bool jumped = false, Vector2? fallback = null)
    {
        var resolved = target.HasValue;

        var input = new TraceInput(
            Now,
            Busy,
            Self,
            SelfMoving,
            resolved,
            target ?? default,
            resolved ? 1u : 0u,
            jumped,
            fallback.HasValue,
            fallback ?? default
        );

        return TraceEvaluator.Evaluate(input, Runtime, Options);
    }

    /// <summary>
    ///     Lets time pass and evaluates.
    /// </summary>
    public TraceDecision After(
        int milliseconds,
        TargetMotionState? target,
        bool jumped = false,
        Vector2? fallback = null
    )
    {
        Now += milliseconds;

        return Evaluate(target, jumped, fallback);
    }
}

/// <summary>
///     Builds motion snapshots of a target.
/// </summary>
internal static class Motion
{
    public static TargetMotionState Stationary(float x, float y, int tick = 0)
    {
        var position = new Vector2(x, y);

        return new TargetMotionState(1, position, position, false, false, 0f, 5.0, tick, tick);
    }

    public static TargetMotionState Click(
        float x,
        float y,
        float destinationX,
        float destinationY,
        int tick = 0,
        double speed = 5.0
    )
    {
        var position = new Vector2(x, y);
        var destination = new Vector2(destinationX, destinationY);
        var direction = destination - position;

        return new TargetMotionState(
            1,
            position,
            destination,
            true,
            true,
            MathF.Atan2(direction.Y, direction.X),
            speed,
            tick,
            tick
        );
    }

    public static TargetMotionState Keys(
        float x,
        float y,
        float angle,
        int lastChangeTick,
        int tick,
        double speed = 5.0,
        bool spinning = false
    )
    {
        var position = new Vector2(x, y);

        return new TargetMotionState(
            1,
            position,
            position,
            false,
            true,
            angle,
            speed,
            tick,
            lastChangeTick,
            spinning
        );
    }
}
