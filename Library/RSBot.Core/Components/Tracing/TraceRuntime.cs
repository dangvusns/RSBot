using System.Numerics;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     The mutable state the evaluator keeps between two evaluations of one trace.
/// </summary>
public sealed class TraceRuntime
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TraceRuntime" /> class.
    /// </summary>
    /// <param name="mode">The trace mode.</param>
    public TraceRuntime(TraceMode mode)
    {
        Mode = mode;
        ActiveBackend = mode == TraceMode.GameTrace ? TraceBackend.NativeGameTrace : TraceBackend.Trajectory;
    }

    /// <summary>
    ///     Gets the trace mode the user asked for.
    /// </summary>
    public TraceMode Mode { get; }

    /// <summary>
    ///     Gets or sets the state.
    /// </summary>
    public TraceState State { get; set; } = TraceState.ResolvingTarget;

    /// <summary>
    ///     Gets the backend that currently follows the target. Only changed by <see cref="SwitchBackend" />.
    /// </summary>
    public TraceBackend ActiveBackend { get; private set; }

    /// <summary>
    ///     Gets the tick count of the last backend switch.
    /// </summary>
    public int BackendSince { get; private set; }

    /// <summary>
    ///     Gets the reason of the last backend switch.
    /// </summary>
    public string LastSwitchReason { get; private set; }

    /// <summary>
    ///     Gets or sets the trajectory quality of the last evaluation.
    /// </summary>
    public TrajectoryQuality Quality { get; set; } = TrajectoryQuality.Reliable;

    /// <summary>
    ///     Gets or sets a value indicating whether the target was seen at least once.
    /// </summary>
    public bool EverResolved { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the target is currently not visible.
    /// </summary>
    public bool Lost { get; set; }

    /// <summary>
    ///     Gets or sets the tick count the target was lost.
    /// </summary>
    public int LostSince { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether a move command was issued since the last reset.
    /// </summary>
    public bool HasLastMove { get; set; }

    /// <summary>
    ///     Gets or sets the tick count of the last move command.
    /// </summary>
    public int LastMoveTick { get; set; }

    /// <summary>
    ///     Gets or sets the destination of the last move command.
    /// </summary>
    public Vector2 LastMoveDestination { get; set; }

    /// <summary>
    ///     Gets or sets the aim point of the last move command.
    /// </summary>
    public Vector2 LastMoveTarget { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the native trace was sent for the active backend.
    /// </summary>
    public bool GameTraceSent { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether a native trace was ever sent in this trace.
    /// </summary>
    public bool HasSentGameTrace { get; set; }

    /// <summary>
    ///     Gets or sets the tick count of the last native trace request.
    /// </summary>
    public int LastGameTraceTick { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the character got stuck before reaching the follow point, so the
    ///     native trace finds the way until the target heads somewhere else.
    /// </summary>
    public bool Stuck { get; set; }

    /// <summary>
    ///     Gets or sets the aim point the character got stuck on.
    /// </summary>
    public Vector2 StuckAim { get; set; }

    /// <summary>
    ///     Switches the backend that follows the target. The commands of the previous backend do not apply to the new
    ///     one, so they are forgotten. A native game trace never switches.
    /// </summary>
    /// <param name="backend">The new backend.</param>
    /// <param name="now">The current tick count.</param>
    /// <param name="reason">The reason, used for debugging.</param>
    /// <returns><c>true</c> if the backend changed; otherwise <c>false</c>.</returns>
    public bool SwitchBackend(TraceBackend backend, int now, string reason)
    {
        if (Mode == TraceMode.GameTrace || ActiveBackend == backend)
            return false;

        ActiveBackend = backend;
        BackendSince = now;
        LastSwitchReason = reason;

        HasLastMove = false;
        GameTraceSent = false;

        return true;
    }

    /// <summary>
    ///     Forgets everything about the commands that were issued, for example after the target teleported. The
    ///     backend stays, the next evaluation decides it again.
    /// </summary>
    public void ResetMovement()
    {
        HasLastMove = false;
        GameTraceSent = false;
        Stuck = false;
    }
}
