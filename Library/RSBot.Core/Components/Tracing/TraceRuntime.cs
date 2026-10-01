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
        Phase = InitialPhase;
    }

    /// <summary>
    ///     Gets the trace mode.
    /// </summary>
    public TraceMode Mode { get; }

    /// <summary>
    ///     Gets or sets the state.
    /// </summary>
    public TraceState State { get; set; } = TraceState.ResolvingTarget;

    /// <summary>
    ///     Gets or sets the active sub mode.
    /// </summary>
    public TracePhase Phase { get; set; }

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
    ///     Gets or sets the target position of the last move command.
    /// </summary>
    public Vector2 LastMoveTarget { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the target is walking with the keys.
    /// </summary>
    public bool KeyWalking { get; set; }

    /// <summary>
    ///     Gets or sets the tick count the key walking was first seen.
    /// </summary>
    public int KeyWalkSince { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the native trace was sent since the last reset.
    /// </summary>
    public bool GameTraceSent { get; set; }

    /// <summary>
    ///     Gets or sets the tick count of the last native trace request.
    /// </summary>
    public int LastGameTraceTick { get; set; }

    private TracePhase InitialPhase => Mode == TraceMode.Smart ? TracePhase.BotFollow : TracePhase.GameFollow;

    /// <summary>
    ///     Forgets everything about the commands that were issued, for example after the target teleported.
    /// </summary>
    public void ResetMovement()
    {
        HasLastMove = false;
        KeyWalking = false;
        GameTraceSent = false;
        Phase = InitialPhase;
    }
}
