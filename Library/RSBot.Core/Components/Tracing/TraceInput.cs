using System.Numerics;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     Everything the evaluator needs to decide what a trace does next. Positions are world coordinates.
/// </summary>
public readonly struct TraceInput
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TraceInput" /> struct.
    /// </summary>
    /// <param name="now">The current tick count.</param>
    /// <param name="busy">The reasons the character cannot follow right now.</param>
    /// <param name="self">The position of the character.</param>
    /// <param name="selfMoving">A value indicating whether the character is walking.</param>
    /// <param name="targetResolved">A value indicating whether the target is visible.</param>
    /// <param name="target">The motion of the target, only valid if it is visible.</param>
    /// <param name="targetPlayerId">The unique id of the target player, used for the native trace.</param>
    /// <param name="targetJumped">A value indicating whether the target made an impossible jump (teleport).</param>
    /// <param name="hasFallback">A value indicating whether <paramref name="fallback" /> is valid.</param>
    /// <param name="fallback">The last known position from the party, used while the target is not visible.</param>
    public TraceInput(
        int now,
        TraceBusy busy,
        Vector2 self,
        bool selfMoving,
        bool targetResolved,
        TargetMotionState target,
        uint targetPlayerId,
        bool targetJumped,
        bool hasFallback,
        Vector2 fallback
    )
    {
        Now = now;
        Busy = busy;
        Self = self;
        SelfMoving = selfMoving;
        TargetResolved = targetResolved;
        Target = target;
        TargetPlayerId = targetPlayerId;
        TargetJumped = targetJumped;
        HasFallback = hasFallback;
        Fallback = fallback;
    }

    /// <summary>
    ///     Gets the current tick count.
    /// </summary>
    public int Now { get; }

    /// <summary>
    ///     Gets the reasons the character cannot follow right now.
    /// </summary>
    public TraceBusy Busy { get; }

    /// <summary>
    ///     Gets the position of the character.
    /// </summary>
    public Vector2 Self { get; }

    /// <summary>
    ///     Gets a value indicating whether the character is walking.
    /// </summary>
    public bool SelfMoving { get; }

    /// <summary>
    ///     Gets a value indicating whether the target is visible.
    /// </summary>
    public bool TargetResolved { get; }

    /// <summary>
    ///     Gets the motion of the target.
    /// </summary>
    public TargetMotionState Target { get; }

    /// <summary>
    ///     Gets the unique id of the target player.
    /// </summary>
    public uint TargetPlayerId { get; }

    /// <summary>
    ///     Gets a value indicating whether the target made an impossible jump.
    /// </summary>
    public bool TargetJumped { get; }

    /// <summary>
    ///     Gets a value indicating whether <see cref="Fallback" /> is valid.
    /// </summary>
    public bool HasFallback { get; }

    /// <summary>
    ///     Gets the last known position from the party.
    /// </summary>
    public Vector2 Fallback { get; }

    /// <summary>
    ///     Returns a copy with the specified jump flag.
    /// </summary>
    /// <param name="jumped">A value indicating whether the target made an impossible jump.</param>
    public TraceInput WithJump(bool jumped)
    {
        return new TraceInput(
            Now,
            Busy,
            Self,
            SelfMoving,
            TargetResolved,
            Target,
            TargetPlayerId,
            jumped,
            HasFallback,
            Fallback
        );
    }
}
