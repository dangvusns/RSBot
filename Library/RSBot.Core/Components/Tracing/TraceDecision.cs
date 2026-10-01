using System.Numerics;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     The result of one evaluation of a trace: what to do and why.
/// </summary>
public readonly struct TraceDecision
{
    private TraceDecision(TraceAction action, Vector2 destination, string reason)
    {
        Action = action;
        Destination = destination;
        Reason = reason;
    }

    /// <summary>
    ///     Gets what the trace wants to do.
    /// </summary>
    public TraceAction Action { get; }

    /// <summary>
    ///     Gets the destination, only valid for <see cref="TraceAction.Move" />.
    /// </summary>
    public Vector2 Destination { get; }

    /// <summary>
    ///     Gets the reason of the decision, used for debugging.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    ///     Does nothing.
    /// </summary>
    /// <param name="reason">The reason.</param>
    public static TraceDecision Idle(string reason)
    {
        return new TraceDecision(TraceAction.None, default, reason);
    }

    /// <summary>
    ///     Walks to the destination.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="reason">The reason.</param>
    public static TraceDecision Move(Vector2 destination, string reason)
    {
        return new TraceDecision(TraceAction.Move, destination, reason);
    }

    /// <summary>
    ///     Sends the native trace request.
    /// </summary>
    /// <param name="reason">The reason.</param>
    public static TraceDecision GameTrace(string reason)
    {
        return new TraceDecision(TraceAction.SendGameTrace, default, reason);
    }
}
