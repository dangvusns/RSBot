using System;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     The state of a trace session.
/// </summary>
public enum TraceState
{
    /// <summary>
    ///     The session was not started.
    /// </summary>
    Disabled,

    /// <summary>
    ///     The target is being looked up.
    /// </summary>
    ResolvingTarget,

    /// <summary>
    ///     The target was never seen yet or is out of reach for a longer time.
    /// </summary>
    WaitingForTarget,

    /// <summary>
    ///     The character is moving towards the target.
    /// </summary>
    Following,

    /// <summary>
    ///     The character is close enough to the target.
    /// </summary>
    InRange,

    /// <summary>
    ///     The trace is paused because the character is busy (dead, casting, teleporting...). The target is kept.
    /// </summary>
    Suspended,

    /// <summary>
    ///     The target left the visual range. The session waits for it to come back.
    /// </summary>
    TargetLost,

    /// <summary>
    ///     The session was stopped.
    /// </summary>
    Stopped,
}

/// <summary>
///     The active sub mode of a trace.
/// </summary>
public enum TracePhase
{
    /// <summary>
    ///     The bot walks to the follow point itself.
    /// </summary>
    BotFollow,

    /// <summary>
    ///     The native trace of the game is used.
    /// </summary>
    GameFollow,
}

/// <summary>
///     Describes how the target is currently moving.
/// </summary>
public enum TargetMovementKind
{
    /// <summary>
    ///     The target stands still.
    /// </summary>
    Stationary,

    /// <summary>
    ///     The target walks to a clicked destination.
    /// </summary>
    ClickMove,

    /// <summary>
    ///     The target walks without a destination (keys, sky click).
    /// </summary>
    KeyWalk,
}

/// <summary>
///     The reasons why a trace is currently paused.
/// </summary>
[Flags]
public enum TraceBusy
{
    None = 0,
    NotReady = 1,
    Dead = 2,
    InAction = 4,
    ScrollActive = 8,
    Teleporting = 16,
    Sitting = 32,
    OtherMover = 64,
}

/// <summary>
///     What the trace wants to do next.
/// </summary>
public enum TraceAction
{
    /// <summary>
    ///     Nothing, the current movement is still fine.
    /// </summary>
    None,

    /// <summary>
    ///     Walk to the destination of the decision.
    /// </summary>
    Move,

    /// <summary>
    ///     Send the native trace request.
    /// </summary>
    SendGameTrace,
}
