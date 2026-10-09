using System;
using System.Threading;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Components.Tracing;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;
using RSBot.NavMeshApi;

namespace RSBot.Training.Bundle.Movement;

internal class MovementBundle : IBundle
{
    /// <summary>
    ///     Gets or sets the configuration.
    /// </summary>
    /// <value>
    ///     The configuration.
    /// </value>
    public MovementConfig Config { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether [last entity was behind obstacle].
    ///     Used to move around even though the player is being attacked.
    /// </summary>
    /// <value>
    ///     <c>true</c> if [last entity was behind obstacle]; otherwise, <c>false</c>.
    /// </value>
    public bool LastEntityWasBehindObstacle { get; set; }

    /// <summary>
    ///     The trace of the party master while the player follows it.
    /// </summary>
    private TraceSession _masterTrace;
    private bool _returningToArea;
    private int _lastAreaReturnTick;
    private ObstacleRecovery _recovery = new();
    private int _recoveryRequested;

    public bool Recovering => _recovery.Active || Volatile.Read(ref _recoveryRequested) != 0;

    public void RequestObstacleRecovery()
    {
        if (Kernel.Bot.Running)
            Interlocked.Exchange(ref _recoveryRequested, 1);
    }

    public bool RecoverFromObstacle()
    {
        var player = Game.Player;
        if (!Kernel.Bot.Running || player.State.LifeState != LifeState.Alive)
        {
            Interlocked.Exchange(ref _recoveryRequested, 0);
            _recovery.Finish(Kernel.TickCount);
            return false;
        }

        var position = player.Position;
        if (Interlocked.Exchange(ref _recoveryRequested, 0) != 0
            && _recovery.Start(Kernel.TickCount, position.X, position.Y))
        {
            StopMasterTrace();
            Log.Status("Recovering from obstacle");
        }

        var step = _recovery.Update(Kernel.TickCount, position.X, position.Y);
        if (step == ObstacleRecovery.Step.Recovered || step == ObstacleRecovery.Step.Exhausted)
        {
            LastEntityWasBehindObstacle = false;
            Log.Status("Resuming training");
            Log.Debug(step == ObstacleRecovery.Step.Recovered
                ? "[Recovery] Position changed; resuming training"
                : "[Recovery] Escape attempts exhausted; resuming target selection");
            return false;
        }
        if (step != ObstacleRecovery.Step.Attempt)
            return _recovery.Active;

        // Resurrects, heals and buffs keep their cast ownership; recovery still expires while waiting.
        if (player.InAction || SkillManager.IsCasting || Bundles.Resurrect.HasPendingResurrect()
            || PickupManager.RunningPlayerPickup)
            return true;

        var area = Container.Bot.Area;
        var angle = Math.Atan2(area.Position.Y - position.Y, area.Position.X - position.X);
        if (!position.TryGetNavMeshTransform(out var source))
            return true;
        for (var direction = 0; direction < 8; direction++)
        {
            var offset = angle + (direction + _recovery.Attempts - 1) * Math.PI / 4;
            var destination = new Position(position.X + (float)Math.Cos(offset) * 4,
                position.Y + (float)Math.Sin(offset) * 4, position.Region)
            {
                WorldId = position.WorldId,
                LayerId = position.LayerId,
                ZOffset = position.ZOffset,
            };
            if (area.Position.DistanceTo(destination) > area.Radius
                || !destination.TryGetNavMeshTransform(out var target)
                || !NavMeshManager.Raycast(new NavMeshTransform(source), new NavMeshTransform(target), NavMeshRaycastType.Move))
                continue;
            destination.ZOffset = target.Offset.Y;
            if (Kernel.Bot.Running && !player.InAction && !SkillManager.IsCasting)
                player.MoveTo(destination, false);
            break;
        }
        return true;
    }

    /// <summary>
    ///     How far the player may step over the radius before being brought back, so a step just outside does not trigger it.
    /// </summary>
    private const float BoundaryTolerance = 2f;

    /// <summary>Training boundaries take precedence over attacks, pickup and party following.</summary>
    public bool EnsureInsideTrainingArea()
    {
        // A dead member outside the area may be resurrected first; the check brings the player back afterwards
        if (Bundles.Resurrect.HasPendingResurrect())
            return false;

        var area = Container.Bot.Area;
        var distance = Game.Player.Position.DistanceTo(area.Position);
        var movement = Game.Player.Movement;
        var leavingArea = movement.Moving && movement.HasDestination
            && area.Position.DistanceTo(movement.Destination) > area.Radius;
        if (distance <= area.Radius + BoundaryTolerance && !leavingArea && !_returningToArea)
            return false;

        if (_returningToArea && distance <= System.Math.Max(1, area.Radius - 2) && !leavingArea)
        {
            _returningToArea = false;
            Log.Debug("[Training boundary] Back inside training area");
            return false;
        }

        if (!_returningToArea)
        {
            Interlocked.Exchange(ref _recoveryRequested, 0);
            if (_recovery.Active)
                _recovery.Finish(Kernel.TickCount);
            _returningToArea = true;
            _lastAreaReturnTick = Kernel.TickCount - 2000;
            Log.Warn($"[Training boundary] Returning to center: distance={distance:F1} radius={area.Radius} "
                + $"destinationOutside={leavingArea} selected={Game.SelectedEntity?.UniqueId}");
            StopMasterTrace();
            PickupManager.Stop();
            if (Game.Player.InAction || Game.Player.Movement.Moving)
                SkillManager.CancelAction();
            Game.SelectedEntity = null;
        }

        // Retry bounded movement periodically; a selected target or stale threat flag cannot block recovery.
        if (Kernel.Bot.Running && ((Kernel.TickCount - _lastAreaReturnTick) & int.MaxValue) >= 2000)
        {
            _lastAreaReturnTick = Kernel.TickCount;
            Game.Player.MoveTo(area.Position, false);
        }
        return true;
    }

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        if (EnsureInsideTrainingArea())
            return;
        if (RecoverFromObstacle())
            return;
        if (Game.SelectedEntity != null && !LastEntityWasBehindObstacle)
            return;

        var playerUnderAttack = SpawnManager.Any<SpawnedMonster>(m =>
            m.TargetId == Game.Player.UniqueId && Kernel.TickCount - m.TargetTick < 10_000
                && m.State.LifeState == LifeState.Alive && Container.Bot.Area.IsInSight(m)
        );
        if (playerUnderAttack && !LastEntityWasBehindObstacle)
            return;

        // Handled before the moving check: a trace renews its destination while the character is walking
        if (FollowPartyMaster())
            return;

        if (Game.Player.Movement.Moving)
            return;

        var distance = Game.Player.Position.DistanceTo(Container.Bot.Area.Position);
        var hasCollision = Game.Player.Position.HasCollisionBetween(Container.Bot.Area.Position);

        //Go back if the player is out of the radius
        if ((distance > Container.Bot.Area.Radius || (Config.WalkToCenter && distance > 3)) && !hasCollision)
        {
            Log.Status("Walking to center");
            Game.Player.MoveTo(Container.Bot.Area.Position);

            return;
        }

        if (Config.WalkToCenter)
            return;

        Log.Status("Walking around");

        //Find a not colliding position. Do it in a while loop to prevent the bot from processing it in the next cycle (tick).
        //This is how we can find our next position very fast instead of waiting for the next circle to come.
        var destination = Container.Bot.Area.GetRandomPosition();

        var attempt = 0;
        while (Game.Player.Position.HasCollisionBetween(destination) && distance < Container.Bot.Area.Radius)
        {
            destination = Container.Bot.Area.GetRandomPosition();
            if (attempt++ > 3)
                break;

            Thread.Sleep(100);
        }

        Game.Player.MoveTo(destination, false);
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        Config = new MovementConfig
        {
            WalkAround = PlayerConfig.Get("RSBot.Training.radioWalkAround", true),
            WalkToCenter = PlayerConfig.Get<bool>("RSBot.Training.radioCenter"),
        };
    }

    public void Stop()
    {
        LastEntityWasBehindObstacle = false;
        _returningToArea = false;
        Interlocked.Exchange(ref _recoveryRequested, 0);
        _recovery = new ObstacleRecovery();

        StopMasterTrace();
    }

    /// <summary>
    ///     Follows the party master when the player set it up to. The follow rules (distance, hysteresis, trajectory,
    ///     target lost) are owned by the trace engine; this bundle only decides when moving is allowed.
    /// </summary>
    /// <returns><c>true</c> if the player follows the party master, which suppresses walking around the training area.</returns>
    private bool FollowPartyMaster()
    {
        if (
            !PlayerConfig.Get("RSBot.Party.AlwaysFollowPartyMaster", false)
            || !Game.Party.IsInParty
            || Game.Party.IsLeader
        )
        {
            StopMasterTrace();

            return false;
        }

        if (Game.Player.InAction)
            return true;

        var leader = Game.Party.Leader;
        if (leader == null)
            return true;

        if (leader.Player == null || !Container.Bot.Area.IsInSight(leader.Player))
        {
            StopMasterTrace();
            return false;
        }

        if (_masterTrace == null || _masterTrace.TargetName != leader.Name)
        {
            StopMasterTrace();

            _masterTrace = new TraceSession(
                leader.Name,
                TraceMode.Smart,
                TraceOptions.PartyMaster().ApplyConfig("RSBot.Party.Trace."),
                false
            );
            _masterTrace.MovementAllowed = destination =>
                Container.Bot.Area.Position.DistanceTo(new Position(destination.X, destination.Y, Game.Player.Position.Region))
                    <= Container.Bot.Area.Radius;
            _masterTrace.Start();
        }

        _masterTrace.Step();

        return true;
    }

    /// <summary>
    ///     Stops following the party master.
    /// </summary>
    private void StopMasterTrace()
    {
        _masterTrace?.Stop(false);
        _masterTrace = null;
    }
}
