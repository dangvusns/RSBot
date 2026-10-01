using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Core.Components.Tracing;

/// <summary>
///     One trace of one player. Gathers the game state into a <see cref="TraceInput" />, lets the
///     <see cref="TraceEvaluator" /> decide and carries the decision out.
/// </summary>
/// <remarks>
///     The snapshot of the target and the character is taken inside the <c>OnTick</c> event, which the kernel fires on
///     the same thread right after all entities were updated, so the entity state is never read while it changes.
///     A self driven session evaluates on a worker task; otherwise the owner calls <see cref="Step" /> when it wants
///     the trace to act (for example from a botbase that has its own rules when moving is allowed).
/// </remarks>
public sealed class TraceSession
{
    private readonly TraceTarget _target;
    private readonly TraceRuntime _runtime;
    private readonly object _snapshotLock = new();

    private TraceInput _snapshot;
    private bool _hasSnapshot;
    private volatile bool _stopped;

    private int _busy;
    private int _lastStepTick;
    private int _lastResolveTick;
    private int _jumpPending;
    private volatile bool _historyResetPending;

    private bool _hasPrevious;
    private uint _previousUniqueId;
    private Vector2 _previousPosition;

    private bool _hasMotionHistory;
    private TargetMovementKind _lastKind;
    private Vector2 _lastDestination;
    private float _lastAngle;
    private int _lastMovementChangeTick;
    private bool _movementChanged;

    private string _lastLogged;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TraceSession" /> class.
    /// </summary>
    /// <param name="targetName">The name of the player to trace.</param>
    /// <param name="mode">The trace mode.</param>
    /// <param name="options">The options.</param>
    /// <param name="selfDriven">If <c>true</c> the session evaluates itself; otherwise the owner calls <see cref="Step" />.</param>
    public TraceSession(string targetName, TraceMode mode, TraceOptions options, bool selfDriven)
    {
        Mode = mode;
        Options = options ?? TraceOptions.PartyMaster();
        SelfDriven = selfDriven;

        _target = new TraceTarget(targetName);
        _runtime = new TraceRuntime(mode);
    }

    /// <summary>
    ///     Gets the name of the traced player.
    /// </summary>
    public string TargetName => _target.Name;

    /// <summary>
    ///     Gets the trace mode.
    /// </summary>
    public TraceMode Mode { get; }

    /// <summary>
    ///     Gets the options.
    /// </summary>
    public TraceOptions Options { get; }

    /// <summary>
    ///     Gets a value indicating whether the session evaluates itself.
    /// </summary>
    public bool SelfDriven { get; }

    /// <summary>
    ///     Gets the state of the trace.
    /// </summary>
    public TraceState State => _runtime.State;

    /// <summary>
    ///     Gets the backend that currently follows the target.
    /// </summary>
    public TraceBackend ActiveBackend => _runtime.ActiveBackend;

    /// <summary>
    ///     Gets the trajectory quality of the last evaluation.
    /// </summary>
    public TrajectoryQuality Quality => _runtime.Quality;

    /// <summary>
    ///     Gets a value indicating whether debug messages are written: always in a debug environment, otherwise when
    ///     the option is set.
    /// </summary>
    private bool DebugEnabled => Options.Debug || Kernel.Debug;

    /// <summary>
    ///     Starts the session.
    /// </summary>
    /// <param name="seed">The visible entity of the player if it is already known.</param>
    public void Start(SpawnedPlayer seed = null)
    {
        if (_stopped)
            return;

        if (seed != null)
            _target.OnSpawn(seed);
        else
            _target.Resolve();

        _runtime.State = TraceState.ResolvingTarget;
        _lastResolveTick = Kernel.TickCount;

        TraceManager.Register(this);

        Debug($"[Trace] started for {TargetName}, mode={Mode}");
    }

    /// <summary>
    ///     Stops the session.
    /// </summary>
    /// <param name="cancelAction">If <c>true</c> a running native trace is cancelled.</param>
    public void Stop(bool cancelAction)
    {
        if (_stopped)
            return;

        var nativeActive = _runtime.ActiveBackend == TraceBackend.NativeGameTrace;

        _stopped = true;
        _runtime.State = TraceState.Stopped;

        TraceManager.Unregister(this);

        if (cancelAction && nativeActive)
            Task.Run(() =>
            {
                try
                {
                    SkillManager.CancelAction();
                }
                catch (Exception e)
                {
                    Log.Fatal(e);
                }
            });

        Debug($"[Trace] stopped for {TargetName}");
    }

    /// <summary>
    ///     Evaluates the trace once and carries the decision out. A move blocks until the game answered it, so do not
    ///     call this from the update loop or from a packet handler.
    /// </summary>
    public void Step()
    {
        if (_stopped)
            return;

        TraceInput input;
        lock (_snapshotLock)
        {
            if (!_hasSnapshot)
                return;

            input = _snapshot;
        }

        // A jump is only consumed while the trace is able to react to it
        if (input.Busy == TraceBusy.None && Interlocked.Exchange(ref _jumpPending, 0) == 1)
            input = input.WithJump(true);

        var previousState = _runtime.State;
        var previousBackend = _runtime.ActiveBackend;

        var decision = TraceEvaluator.Evaluate(input, _runtime, Options);

        switch (decision.Action)
        {
            case TraceAction.SendGameTrace:
                GameTraceBackend.Send(input.TargetPlayerId);
                break;

            case TraceAction.Move:
                SmartTraceBackend.MoveTo(decision.Destination);
                break;
        }

        if (DebugEnabled)
            LogDecision(input, decision, previousState, previousBackend);
    }

    /// <summary>
    ///     Called by the manager on every tick of the kernel.
    /// </summary>
    internal void OnTick()
    {
        if (_stopped)
            return;

        var player = Game.Player;
        if (player == null)
            return;

        var now = Kernel.TickCount;

        if (_historyResetPending)
        {
            _historyResetPending = false;
            _hasPrevious = false;
            _hasMotionHistory = false;
        }

        var entity = _target.Entity;
        if (entity == null && TraceTime.Elapsed(now, _lastResolveTick) >= 1000)
        {
            // Safety net in case a spawn event was missed
            _lastResolveTick = now;
            if (_target.Resolve())
                entity = _target.Entity;
        }

        var motion = default(TargetMotionState);
        var resolved = false;

        if (entity != null)
        {
            motion = Capture(entity, now);
            resolved = true;
        }
        else
        {
            _hasPrevious = false;
            _hasMotionHistory = false;
        }

        var fallback = Vector2.Zero;
        var hasFallback = !resolved && TryGetFallback(out fallback);

        var input = new TraceInput(
            now,
            GetBusy(player),
            ToVector(player.Position),
            player.Movement.Moving,
            resolved,
            motion,
            entity != null ? entity.UniqueId : 0u,
            false,
            hasFallback,
            fallback
        );

        lock (_snapshotLock)
        {
            _snapshot = input;
            _hasSnapshot = true;
        }

        if (SelfDriven && ScheduleStep(now, _movementChanged))
            _movementChanged = false;
    }

    /// <summary>
    ///     Called when a player spawned.
    /// </summary>
    internal void OnSpawn(SpawnedPlayer player)
    {
        _target.OnSpawn(player);
    }

    /// <summary>
    ///     Called when an entity despawned.
    /// </summary>
    internal void OnDespawn(SpawnedEntity entity)
    {
        _target.OnDespawn(entity);
    }

    /// <summary>
    ///     Called when all entities were cleared (new map, relog).
    /// </summary>
    internal void OnWorldReset()
    {
        _target.Reset();
        _historyResetPending = true;
    }

    /// <summary>
    ///     Starts an evaluation on a worker task.
    /// </summary>
    /// <param name="now">The current tick count.</param>
    /// <param name="force">If <c>true</c> the update interval is ignored (the target movement just changed).</param>
    /// <returns><c>true</c> if an evaluation was started; otherwise <c>false</c>.</returns>
    private bool ScheduleStep(int now, bool force)
    {
        if (!force && TraceTime.Elapsed(now, _lastStepTick) < Options.TraceUpdateInterval)
            return false;

        // One evaluation at a time: a move blocks until the game answered
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return false;

        _lastStepTick = now;

        Task.Run(() =>
        {
            try
            {
                Step();
            }
            catch (Exception e)
            {
                Log.Fatal(e);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });

        return true;
    }

    /// <summary>
    ///     Captures the motion of the target. A player on a transport is moved by the transport, so its movement is used.
    /// </summary>
    private TargetMotionState Capture(SpawnedPlayer player, int now)
    {
        SpawnedEntity source = player;
        if (player.OnTransport && SpawnManager.TryGetEntity<SpawnedEntity>(player.TransportUniqueId, out var transport))
            source = transport;

        var movement = source.Movement;
        var position = ToVector(movement.Source);
        var destination = movement.HasDestination ? ToVector(movement.Destination) : position;

        var kind =
            !movement.Moving ? TargetMovementKind.Stationary
            : movement.HasDestination ? TargetMovementKind.ClickMove
            : TargetMovementKind.KeyWalk;

        // The entity model moves entities in small steps every tick, a big step can only be a teleport or a correction
        if (
            _hasPrevious
            && _previousUniqueId == source.UniqueId
            && Vector2.Distance(position, _previousPosition) > Options.TeleportDetectionDistance
        )
        {
            Interlocked.Exchange(ref _jumpPending, 1);
            _hasMotionHistory = false;
        }

        _hasPrevious = true;
        _previousUniqueId = source.UniqueId;
        _previousPosition = position;

        // The packet does not carry a timestamp, so remember when the movement last changed
        if (
            !_hasMotionHistory
            || kind != _lastKind
            || (kind == TargetMovementKind.ClickMove && Vector2.Distance(destination, _lastDestination) > 0.1f)
            || (kind == TargetMovementKind.KeyWalk && MathF.Abs(movement.Angle - _lastAngle) > 0.01f)
        )
        {
            _lastMovementChangeTick = now;

            // React to a new movement at once instead of waiting for the next regular evaluation
            if (_hasMotionHistory)
                _movementChanged = true;
        }

        _hasMotionHistory = true;
        _lastKind = kind;
        _lastDestination = destination;
        _lastAngle = movement.Angle;

        return new TargetMotionState(
            source.UniqueId,
            position,
            destination,
            movement.HasDestination,
            movement.Moving,
            movement.Angle,
            source.ActualSpeed * 0.1,
            now,
            _lastMovementChangeTick,
            movement.Spinning
        );
    }

    private bool TryGetFallback(out Vector2 fallback)
    {
        fallback = Vector2.Zero;

        var member = Game.Party?.GetMemberByName(TargetName);
        if (member == null || member.Position.Region.Id == 0)
            return false;

        fallback = ToVector(member.Position);

        return true;
    }

    private static TraceBusy GetBusy(Player player)
    {
        var busy = TraceBusy.None;

        if (!Game.Ready)
            busy |= TraceBusy.NotReady;

        if (player.State.LifeState == LifeState.Dead)
            busy |= TraceBusy.Dead;

        if (player.InAction)
            busy |= TraceBusy.InAction;

        if (player.State.ScrollState == ScrollState.NormalScroll)
            busy |= TraceBusy.ScrollActive;

        if (player.Teleportation != null)
            busy |= TraceBusy.Teleporting;

        if (player.State.MotionState == MotionState.Sitting)
            busy |= TraceBusy.Sitting;

        if (PickupManager.RunningPlayerPickup || ScriptManager.Running || ShoppingManager.Running)
            busy |= TraceBusy.OtherMover;

        return busy;
    }

    private static Vector2 ToVector(Position position)
    {
        return new Vector2(position.X, position.Y);
    }

    private void Debug(string message)
    {
        if (DebugEnabled)
            Log.Debug(message);
    }

    private void LogDecision(
        TraceInput input,
        TraceDecision decision,
        TraceState previousState,
        TraceBackend previousBackend
    )
    {
        var switched = previousBackend != _runtime.ActiveBackend;

        // Do not repeat the same "nothing to do" line
        if (
            decision.Action == TraceAction.None
            && decision.Reason == _lastLogged
            && previousState == _runtime.State
            && !switched
        )
            return;

        _lastLogged = decision.Reason;

        if (switched)
            Log.Debug(
                $"[Trace] {TargetName} backend {previousBackend} -> {_runtime.ActiveBackend}: {_runtime.LastSwitchReason}"
            );

        var target = input.Target;

        Log.Debug(
            $"[Trace] {TargetName} mode={Mode} backend={_runtime.ActiveBackend} quality={_runtime.Quality} "
                + $"state={_runtime.State} busy={input.Busy} "
                + $"uid={input.TargetPlayerId} kind={target.Kind} self=({input.Self.X:0.0},{input.Self.Y:0.0}) "
                + $"target=({target.Position.X:0.0},{target.Position.Y:0.0}) "
                + $"targetDest=({target.Destination.X:0.0},{target.Destination.Y:0.0}) "
                + $"dist={Vector2.Distance(input.Self, target.Position):0.0} action={decision.Action} "
                + $"to=({decision.Destination.X:0.0},{decision.Destination.Y:0.0}) reason={decision.Reason}"
        );
    }
}
