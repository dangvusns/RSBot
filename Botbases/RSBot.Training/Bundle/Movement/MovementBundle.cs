using System.Threading;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Components.Tracing;
using RSBot.Core.Objects.Spawn;

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

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        if (Game.SelectedEntity != null && !LastEntityWasBehindObstacle)
            return;

        var playerUnderAttack = SpawnManager.Any<SpawnedMonster>(m =>
            m.AttackingPlayer && Container.Bot.Area.IsInSight(m)
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

        if (_masterTrace == null || _masterTrace.TargetName != leader.Name)
        {
            StopMasterTrace();

            _masterTrace = new TraceSession(
                leader.Name,
                TraceMode.Smart,
                TraceOptions.PartyMaster().ApplyConfig("RSBot.Party.Trace."),
                false
            );
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
