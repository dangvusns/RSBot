using System.Numerics;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     The state machine of a trace. Pure logic: decides from a <see cref="TraceInput" /> what the trace does next and
///     updates the <see cref="TraceRuntime" />. It does not touch the game state, so it can be tested with synthetic input.
/// </summary>
public static class TraceEvaluator
{
    /// <summary>
    ///     Evaluates the trace.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="runtime">The runtime state of the trace.</param>
    /// <param name="options">The options.</param>
    public static TraceDecision Evaluate(TraceInput input, TraceRuntime runtime, TraceOptions options)
    {
        // The character is busy: pause, but keep the target and the trace
        if (input.Busy != TraceBusy.None)
        {
            runtime.State = TraceState.Suspended;

            return TraceDecision.Idle("suspended");
        }

        // Resume: the world changed meanwhile, forget the old commands
        if (runtime.State == TraceState.Suspended)
        {
            runtime.ResetMovement();
            runtime.State = TraceState.ResolvingTarget;
        }

        if (!input.TargetResolved)
            return EvaluateTargetLost(input, runtime, options);

        var acquired = !runtime.EverResolved || runtime.Lost;
        if (acquired)
        {
            // The target is seen for the first time or came back
            runtime.EverResolved = true;
            runtime.Lost = false;
            runtime.ResetMovement();
        }
        else if (input.TargetJumped)
        {
            // Teleported: nothing that was planned is valid anymore
            runtime.ResetMovement();
        }

        if (options.DestinationFollow)
        {
            runtime.Quality = TrajectoryClassifier.Classify(input.Target);
            if (runtime.Quality == TrajectoryQuality.Spinning)
                return TraceDecision.Idle("waiting for a confirmed position after spinning");
            // Like xBot: start at the player's current position, then copy their clicked destinations.
            // Heading-only movement follows the current position; it never falls back to native trace.
            var aim = !acquired && input.Target.Kind == TargetMovementKind.ClickMove
                && runtime.Quality == TrajectoryQuality.Reliable
                ? input.Target.Destination : input.Target.Position;
            runtime.State = FollowPointSolver.NextRangeState(runtime.State,
                Vector2.Distance(input.Self, aim), options, input.SelfMoving);
            return EvaluateBotFollow(input, runtime, options, aim);
        }

        var distance = Vector2.Distance(input.Self, input.Target.Position);
        runtime.State = FollowPointSolver.NextRangeState(runtime.State, distance, options, input.SelfMoving);

        return runtime.Mode == TraceMode.GameTrace
            ? EvaluateGameFollow(input, runtime, options)
            : EvaluateSmart(input, runtime, options);
    }

    private static TraceDecision EvaluateTargetLost(TraceInput input, TraceRuntime runtime, TraceOptions options)
    {
        if (!runtime.Lost)
        {
            runtime.Lost = true;
            runtime.LostSince = input.Now;
            runtime.ResetMovement();
        }

        var lostFor = TraceTime.Elapsed(input.Now, runtime.LostSince);
        runtime.State =
            runtime.EverResolved && lostFor <= options.TargetLostTimeout
                ? TraceState.TargetLost
                : TraceState.WaitingForTarget;

        if (runtime.Mode != TraceMode.Smart || !input.HasFallback)
            return TraceDecision.Idle("target not visible");

        // The target is out of sight, but the party still knows where it is
        if (Vector2.Distance(input.Self, input.Fallback) > options.MaxMoveDistance)
            return TraceDecision.Idle("target too far away");

        // The native trace needs a visible target, so walk to the party position by ourselves
        runtime.SwitchBackend(TraceBackend.Trajectory, input.Now, "target not visible, walking to the party position");

        return EvaluateBotFollow(input, runtime, options, input.Fallback);
    }

    private static TraceDecision EvaluateSmart(TraceInput input, TraceRuntime runtime, TraceOptions options)
    {
        // The movement flags of the server decide which backend is able to follow: only a trajectory with a known
        // destination (or a confirmed standing position) can be followed by coordinates
        var quality = TrajectoryClassifier.Classify(input.Target);
        runtime.Quality = quality;

        var aim = TrajectoryEstimator.AimPoint(input.Target, input.Now, options);

        if (quality != TrajectoryQuality.Reliable)
            runtime.SwitchBackend(TraceBackend.NativeGameTrace, input.Now, DescribeUnreliable(quality));
        else if (!runtime.Stuck || Vector2.Distance(aim, runtime.StuckAim) > options.DestinationChangeThreshold)
        {
            // Reliable, and not stuck on this aim point (or the target heads somewhere else now)
            runtime.Stuck = false;
            runtime.SwitchBackend(TraceBackend.Trajectory, input.Now, "trajectory reliable again");
        }

        if (runtime.ActiveBackend == TraceBackend.Trajectory && IsStuck(input, runtime, options, aim))
        {
            // Our own click did not get us there: let the game find the way
            runtime.Stuck = true;
            runtime.StuckAim = aim;
            runtime.SwitchBackend(TraceBackend.NativeGameTrace, input.Now, "stuck before the follow point");
        }

        if (runtime.ActiveBackend == TraceBackend.NativeGameTrace)
            return EvaluateGameFollow(input, runtime, options);

        return EvaluateBotFollow(input, runtime, options, aim);
    }

    /// <summary>
    ///     The character stopped well before the destination of its last command although the target still aims at
    ///     the same spot: the server stopped it (blocked terrain, obstacle).
    /// </summary>
    private static bool IsStuck(TraceInput input, TraceRuntime runtime, TraceOptions options, Vector2 aim)
    {
        if (!runtime.HasLastMove || input.SelfMoving || runtime.State == TraceState.InRange)
            return false;

        if (TraceTime.Elapsed(input.Now, runtime.LastMoveTick) < options.IdleRetryInterval)
            return false;

        return Vector2.Distance(input.Self, runtime.LastMoveDestination) > options.StuckDistance
            && Vector2.Distance(aim, runtime.LastMoveTarget) <= options.TargetMovementThreshold;
    }

    private static string DescribeUnreliable(TrajectoryQuality quality)
    {
        return quality switch
        {
            TrajectoryQuality.HeadingOnly => "no destination (keyboard movement or sky click)",
            TrajectoryQuality.Spinning => "target turns on the spot",
            TrajectoryQuality.InvalidDestination => "invalid destination",
            _ => "trajectory not reliable",
        };
    }

    private static TraceDecision EvaluateGameFollow(TraceInput input, TraceRuntime runtime, TraceOptions options)
    {
        if (!runtime.GameTraceSent)
        {
            // Do not flood the server when the backend switches back and forth
            if (
                runtime.HasSentGameTrace
                && TraceTime.Elapsed(input.Now, runtime.LastGameTraceTick) < options.MinGameTraceInterval
            )
                return TraceDecision.Idle("native trace rate limited");

            runtime.GameTraceSent = true;
            runtime.HasSentGameTrace = true;
            runtime.LastGameTraceTick = input.Now;

            return TraceDecision.GameTrace("send native trace");
        }

        if (
            runtime.State == TraceState.Following
            && !input.SelfMoving
            && TraceTime.Elapsed(input.Now, runtime.LastGameTraceTick) >= options.GameTraceResendInterval
        )
        {
            runtime.LastGameTraceTick = input.Now;

            return TraceDecision.GameTrace("resend native trace, idle while out of range");
        }

        return TraceDecision.Idle(runtime.State == TraceState.InRange ? "in range" : "native trace running");
    }

    private static TraceDecision EvaluateBotFollow(
        TraceInput input,
        TraceRuntime runtime,
        TraceOptions options,
        Vector2 target
    )
    {
        if (runtime.State == TraceState.InRange)
            return TraceDecision.Idle("in range");

        if (!FollowPointSolver.TrySolve(input.Self, target, options, out var destination))
            return TraceDecision.Idle("within follow distance");

        var sinceLastMove = TraceTime.Elapsed(input.Now, runtime.LastMoveTick);
        if (runtime.HasLastMove && sinceLastMove < options.MinMoveInterval)
            return TraceDecision.Idle("throttled");

        string reason;
        if (!runtime.HasLastMove)
            reason = "first move";
        else if (options.DestinationFollow && input.SelfMoving
            && (!input.SelfHasDestination
                || Vector2.Distance(input.SelfDestination, runtime.LastMoveDestination) > options.ArrivalTolerance))
            reason = "restore follow after manual movement";
        else if (options.DestinationFollow
            && Vector2.Distance(destination, runtime.LastMoveDestination) > options.DestinationChangeThreshold)
            reason = "follow point changed";
        else if (options.DestinationFollow
            && Vector2.Distance(target, runtime.LastMoveTarget) > options.TargetMovementThreshold)
            reason = "target moved";
        else if (!input.SelfMoving && sinceLastMove >= options.IdleRetryInterval)
            reason = "idle while out of range";
        else if (Vector2.Distance(destination, runtime.LastMoveDestination) > options.DestinationChangeThreshold)
            reason = "follow point changed";
        else if (Vector2.Distance(target, runtime.LastMoveTarget) > options.TargetMovementThreshold)
            reason = "target moved";
        else
            return TraceDecision.Idle("path still valid");

        runtime.HasLastMove = true;
        runtime.LastMoveTick = input.Now;
        runtime.LastMoveDestination = destination;
        runtime.LastMoveTarget = target;

        return TraceDecision.Move(destination, reason);
    }
}
