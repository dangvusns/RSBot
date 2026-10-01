using System.Numerics;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     An immutable snapshot of how the traced player moves. Positions are world coordinates.
/// </summary>
public readonly struct TargetMotionState
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TargetMotionState" /> struct.
    /// </summary>
    /// <param name="uniqueId">The unique id of the entity that moves (the player or its transport).</param>
    /// <param name="position">The current position, already interpolated by the entity model.</param>
    /// <param name="destination">The clicked destination, equals the position without one.</param>
    /// <param name="hasDestination">A value indicating whether the target walks to a clicked destination.</param>
    /// <param name="moving">A value indicating whether the target is moving.</param>
    /// <param name="angle">The walking direction in radians.</param>
    /// <param name="speed">The speed in world units per second.</param>
    /// <param name="snapshotTick">The tick count of the snapshot.</param>
    /// <param name="lastMovementTick">The tick count of the last observed change of the movement.</param>
    /// <param name="spinning">A value indicating whether the target turns on the spot.</param>
    public TargetMotionState(
        uint uniqueId,
        Vector2 position,
        Vector2 destination,
        bool hasDestination,
        bool moving,
        float angle,
        double speed,
        int snapshotTick,
        int lastMovementTick,
        bool spinning = false
    )
    {
        Spinning = spinning;
        UniqueId = uniqueId;
        Position = position;
        Destination = destination;
        HasDestination = hasDestination;
        Moving = moving;
        Angle = angle;
        Speed = speed;
        SnapshotTick = snapshotTick;
        LastMovementTick = lastMovementTick;
    }

    /// <summary>
    ///     Gets the unique id of the entity that moves.
    /// </summary>
    public uint UniqueId { get; }

    /// <summary>
    ///     Gets the current position.
    /// </summary>
    public Vector2 Position { get; }

    /// <summary>
    ///     Gets the clicked destination.
    /// </summary>
    public Vector2 Destination { get; }

    /// <summary>
    ///     Gets a value indicating whether the target walks to a clicked destination.
    /// </summary>
    public bool HasDestination { get; }

    /// <summary>
    ///     Gets a value indicating whether the target is moving.
    /// </summary>
    public bool Moving { get; }

    /// <summary>
    ///     Gets the walking direction in radians.
    /// </summary>
    public float Angle { get; }

    /// <summary>
    ///     Gets the speed in world units per second.
    /// </summary>
    public double Speed { get; }

    /// <summary>
    ///     Gets the tick count of the snapshot.
    /// </summary>
    public int SnapshotTick { get; }

    /// <summary>
    ///     Gets the tick count of the last observed change of the movement.
    /// </summary>
    public int LastMovementTick { get; }

    /// <summary>
    ///     Gets a value indicating whether the target turns on the spot (only for a movement without destination).
    /// </summary>
    public bool Spinning { get; }

    /// <summary>
    ///     Gets how the target is moving.
    /// </summary>
    public TargetMovementKind Kind =>
        !Moving ? TargetMovementKind.Stationary
        : HasDestination ? TargetMovementKind.ClickMove
        : TargetMovementKind.KeyWalk;
}
