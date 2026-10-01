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

        if (!runtime.EverResolved || runtime.Lost)
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

        return EvaluateBotFollow(input, runtime, options, input.Fallback);
    }

    private static TraceDecision EvaluateGameFollow(TraceInput input, TraceRuntime runtime, TraceOptions options)
    {
        if (!runtime.GameTraceSent)
        {
            runtime.GameTraceSent = true;
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

    private static TraceDecision EvaluateSmart(TraceInput input, TraceRuntime runtime, TraceOptions options)
    {
        var kind = input.Target.Kind;

        if (kind == TargetMovementKind.KeyWalk)
        {
            // Walking with the keys or clicking into the sky has no destination to mirror, the game trace follows it
            if (!runtime.KeyWalking)
            {
                runtime.KeyWalking = true;
                runtime.KeyWalkSince = input.Now;
            }

            if (
                runtime.Phase != TracePhase.GameFollow
                && TraceTime.Elapsed(input.Now, runtime.KeyWalkSince) >= options.KeyWalkSwitchDelay
            )
            {
                runtime.Phase = TracePhase.GameFollow;
                runtime.GameTraceSent = false;
                runtime.HasLastMove = false;
            }
        }
        else
        {
            runtime.KeyWalking = false;

            // The target clicked the ground again, walk by ourselves
            if (kind == TargetMovementKind.ClickMove && runtime.Phase == TracePhase.GameFollow)
            {
                runtime.Phase = TracePhase.BotFollow;
                runtime.HasLastMove = false;
            }
        }

        if (runtime.Phase == TracePhase.GameFollow)
            return EvaluateGameFollow(input, runtime, options);

        var target = TrajectoryEstimator.Predict(input.Target, input.Now, options);

        return EvaluateBotFollow(input, runtime, options, target);
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
