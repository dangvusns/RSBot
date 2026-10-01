using System.Numerics;
using RSBot.Core.Components.Tracing;
using Xunit;

namespace RSBot.Core.Tests;

public class TraceEvaluatorTests
{
    private static void AssertNear(float expected, float actual)
    {
        Assert.InRange(actual, expected - 0.01f, expected + 0.01f);
    }

    [Fact]
    public void StationaryTarget_FarAway_MovesToTheFollowPoint()
    {
        var h = new TraceHarness(TraceMode.Smart);

        var decision = h.Evaluate(Motion.Stationary(50, 0));

        Assert.Equal(TraceAction.Move, decision.Action);
        AssertNear(40, decision.Destination.X);
        AssertNear(0, decision.Destination.Y);
        Assert.Equal(TraceState.Following, h.Runtime.State);
    }

    [Fact]
    public void Follower_EntersTheFollowRange_AndStops()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Stationary(50, 0));

        h.Self = new Vector2(40, 0);
        h.SelfMoving = false;
        var decision = h.After(500, Motion.Stationary(50, 0));

        Assert.Equal(TraceAction.None, decision.Action);
        Assert.Equal(TraceState.InRange, h.Runtime.State);
    }

    [Fact]
    public void Hysteresis_DoesNotAlternateAroundTheThreshold_AndFollowsWhenTheTargetMovesOutAgain()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Stationary(50, 0));
        h.Self = new Vector2(40, 0);
        h.After(500, Motion.Stationary(50, 0));
        Assert.Equal(TraceState.InRange, h.Runtime.State);

        // 11.5 is between stop (8) and start (12): still in range
        var inBand = h.After(500, Motion.Stationary(51.5f, 0));
        Assert.Equal(TraceAction.None, inBand.Action);
        Assert.Equal(TraceState.InRange, h.Runtime.State);

        // 13 is above start: follow again
        var outOfRange = h.After(500, Motion.Stationary(53, 0));
        Assert.Equal(TraceAction.Move, outOfRange.Action);
        AssertNear(43, outOfRange.Destination.X);
        Assert.Equal(TraceState.Following, h.Runtime.State);
    }

    [Fact]
    public void SlowMovingTarget_DoesNotCauseMoveCommandSpam()
    {
        var h = new TraceHarness(TraceMode.Smart) { SelfMoving = true };
        var moves = 0;

        for (var i = 0; i < 20; i++)
        {
            var decision = h.After(100, Motion.Click(20 + i * 0.5f, 0, 100, 0));
            if (decision.Action == TraceAction.Move)
                moves++;
        }

        Assert.InRange(moves, 1, 9);
    }

    [Fact]
    public void FastMovingTarget_RenewsTheDestinationRegularly_ButNotFasterThanTheThrottle()
    {
        var h = new TraceHarness(TraceMode.Smart) { SelfMoving = true };
        var moves = 0;

        for (var i = 0; i < 20; i++)
        {
            // The target keeps clicking ahead of itself
            var decision = h.After(100, Motion.Click(30 + i * 10, 0, 60 + i * 10, 0));
            if (decision.Action == TraceAction.Move)
                moves++;
        }

        Assert.InRange(moves, 4, 9);
    }

    [Fact]
    public void TargetChangesDirection_IssuesANewDestination()
    {
        var h = new TraceHarness(TraceMode.Smart) { SelfMoving = true };
        h.Evaluate(Motion.Click(30, 0, 60, 0));

        var decision = h.After(300, Motion.Click(30, 20, 30, 60));

        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.True(decision.Destination.Y > 5);
    }

    [Fact]
    public void TargetStopsSuddenly_FollowerStopsAtTheFollowDistanceAndDoesNotOvershoot()
    {
        var h = new TraceHarness(TraceMode.Smart) { SelfMoving = true };
        h.Evaluate(Motion.Click(40, 0, 80, 0));

        var decision = h.After(300, Motion.Stationary(60, 0));

        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.InRange(Vector2.Distance(decision.Destination, new Vector2(60, 0)), 9.99f, 10.01f);
    }

    [Fact]
    public void NoMovementUpdate_KeepsTheCurrentPath()
    {
        var h = new TraceHarness(TraceMode.Smart) { SelfMoving = true };
        h.Evaluate(Motion.Click(40, 0, 80, 0));

        var decision = h.After(300, Motion.Click(40, 0, 80, 0));

        Assert.Equal(TraceAction.None, decision.Action);
        Assert.Equal("path still valid", decision.Reason);
    }

    [Fact]
    public void RapidMovementUpdates_AreThrottled()
    {
        var h = new TraceHarness(TraceMode.Smart) { SelfMoving = true };
        var moves = 0;

        for (var i = 0; i < 20; i++)
        {
            // A new destination every 50 ms (repeated ground clicks)
            var decision = h.After(50, Motion.Click(30, 0, 60 + i * 3, 0));
            if (decision.Action == TraceAction.Move)
                moves++;
        }

        // 1 second with a minimum of 250 ms between two commands
        Assert.InRange(moves, 2, 5);
    }

    [Fact]
    public void Despawn_WithoutPartyPosition_IsReportedAsLostAndThenWaits()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Stationary(50, 0));

        var lost = h.After(100, null);
        Assert.Equal(TraceAction.None, lost.Action);
        Assert.Equal(TraceState.TargetLost, h.Runtime.State);

        h.After(6000, null);
        Assert.Equal(TraceState.WaitingForTarget, h.Runtime.State);
    }

    [Fact]
    public void Despawn_WithPartyPosition_ApproachesIt()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Stationary(50, 0));

        var decision = h.After(100, null, fallback: new Vector2(40, 0));

        Assert.Equal(TraceAction.Move, decision.Action);
        AssertNear(30, decision.Destination.X);
        Assert.Equal(TraceState.TargetLost, h.Runtime.State);
    }

    [Fact]
    public void Respawn_ResumesFollowing()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Stationary(50, 0));
        h.After(100, null);

        var decision = h.After(100, Motion.Stationary(60, 0));

        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.Equal(TraceState.Following, h.Runtime.State);
    }

    [Fact]
    public void Teleport_ResetsThePlannedMovement()
    {
        var h = new TraceHarness(TraceMode.Smart) { SelfMoving = true };
        h.Evaluate(Motion.Stationary(50, 0));

        // Inside the throttle time nothing happens...
        var throttled = h.After(10, Motion.Stationary(50, 0));
        Assert.Equal(TraceAction.None, throttled.Action);

        // ...but a teleport makes the old command invalid at once
        var decision = h.After(10, Motion.Stationary(300, 300), jumped: true);
        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.Equal("first move", decision.Reason);
    }

    [Fact]
    public void LeaderReturnsToTown_AndComesBack()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Stationary(50, 0));

        // The leader is gone (town)...
        h.After(100, null);
        Assert.Equal(TraceState.TargetLost, h.Runtime.State);

        // ...we use a return scroll too
        h.Busy = TraceBusy.ScrollActive;
        var suspended = h.After(100, null);
        Assert.Equal(TraceAction.None, suspended.Action);
        Assert.Equal(TraceState.Suspended, h.Runtime.State);

        // ...and arrive in town, the leader spawns somewhere else
        h.Busy = TraceBusy.None;
        h.Self = new Vector2(1000, 1000);
        var decision = h.After(100, Motion.Stationary(1050, 1000));
        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.Equal(TraceState.Following, h.Runtime.State);
    }

    [Fact]
    public void Combat_SuspendsTheTrace_AndItResumesAfterwards()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Stationary(50, 0));

        h.Busy = TraceBusy.InAction;
        var suspended = h.After(100, Motion.Stationary(50, 0));
        Assert.Equal(TraceAction.None, suspended.Action);
        Assert.Equal("suspended", suspended.Reason);
        Assert.Equal(TraceState.Suspended, h.Runtime.State);

        h.Busy = TraceBusy.None;
        var resumed = h.After(300, Motion.Stationary(50, 0));
        Assert.Equal(TraceAction.Move, resumed.Action);
        Assert.Equal(TraceState.Following, h.Runtime.State);
    }

    [Fact]
    public void Dead_SuspendsTheTrace_ButKeepsTheTarget()
    {
        var h = new TraceHarness(TraceMode.Smart) { Busy = TraceBusy.Dead };

        h.Evaluate(Motion.Stationary(50, 0));
        Assert.Equal(TraceState.Suspended, h.Runtime.State);

        h.Busy = TraceBusy.None;
        var decision = h.After(100, Motion.Stationary(50, 0));
        Assert.Equal(TraceAction.Move, decision.Action);
    }

    [Fact]
    public void HeadingOnlyMovement_SwitchesToTheNativeTraceAtOnce_AndBackOnAGroundClick()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Click(50, 0, 80, 0));
        Assert.Equal(TraceBackend.Trajectory, h.Runtime.ActiveBackend);

        // Keyboard movement or a sky click: the packet has no destination, no timer is involved
        var keys = h.After(100, Motion.Keys(55, 0, 0f, h.Now, h.Now));
        Assert.Equal(TrajectoryQuality.HeadingOnly, h.Runtime.Quality);
        Assert.Equal(TraceBackend.NativeGameTrace, h.Runtime.ActiveBackend);
        Assert.Equal(TraceAction.SendGameTrace, keys.Action);

        // Still walking with the keys: the native trace keeps running, nothing is sent again
        var still = h.After(100, Motion.Keys(60, 0, 0f, h.Now - 100, h.Now));
        Assert.Equal(TraceAction.None, still.Action);

        // Ground click: back to the trajectory, the target stays the same
        var click = h.After(100, Motion.Click(60, 0, 90, 0, h.Now));
        Assert.Equal(TraceBackend.Trajectory, h.Runtime.ActiveBackend);
        Assert.Equal(TraceAction.Move, click.Action);
        Assert.True(h.Runtime.EverResolved);
    }

    [Fact]
    public void Spinning_IsNotReliable_AndUsesTheNativeTrace()
    {
        var h = new TraceHarness(TraceMode.Smart);

        var decision = h.Evaluate(Motion.Keys(50, 0, 1f, h.Now, h.Now, spinning: true));

        Assert.Equal(TrajectoryQuality.Spinning, h.Runtime.Quality);
        Assert.Equal(TraceBackend.NativeGameTrace, h.Runtime.ActiveBackend);
        Assert.Equal(TraceAction.SendGameTrace, decision.Action);
    }

    [Fact]
    public void StopAfterKeyboardMovement_ReturnsToTheTrajectory()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Keys(50, 0, 0f, h.Now, h.Now));
        Assert.Equal(TraceBackend.NativeGameTrace, h.Runtime.ActiveBackend);

        // The server stops the target with its real position
        var stopped = h.After(100, Motion.Stationary(60, 0));

        Assert.Equal(TraceBackend.Trajectory, h.Runtime.ActiveBackend);
        Assert.Equal(TraceAction.Move, stopped.Action);
    }

    [Fact]
    public void AlternatingKeysAndClicks_DoesNotFloodNativeTraces()
    {
        var h = new TraceHarness(TraceMode.Smart);
        var traces = 0;

        for (var i = 0; i < 10; i++)
        {
            var target =
                i % 2 == 0 ? Motion.Keys(50 + i, 0, 0f, h.Now, h.Now) : Motion.Click(50 + i, 0, 90, 0, h.Now);

            if (h.After(100, target).Action == TraceAction.SendGameTrace)
                traces++;
        }

        // 1 second with at most one native trace every 500 ms
        Assert.InRange(traces, 1, 3);
    }

    [Fact]
    public void StuckFollower_UsesTheNativeTrace_UntilTheTargetHeadsSomewhereElse()
    {
        var h = new TraceHarness(TraceMode.Smart);
        var first = h.Evaluate(Motion.Stationary(50, 0));
        Assert.Equal(TraceAction.Move, first.Action);

        // The server stopped us at 20 instead of 40 (blocked terrain)
        h.Self = new Vector2(20, 0);
        h.SelfMoving = false;
        var stuck = h.After(500, Motion.Stationary(50, 0));
        Assert.True(h.Runtime.Stuck);
        Assert.Equal(TraceBackend.NativeGameTrace, h.Runtime.ActiveBackend);
        Assert.Equal(TraceAction.SendGameTrace, stuck.Action);

        // Same aim point: stay with the native trace
        h.After(100, Motion.Stationary(50, 0));
        Assert.Equal(TraceBackend.NativeGameTrace, h.Runtime.ActiveBackend);

        // The target clicks somewhere else: walk by ourselves again
        var moved = h.After(100, Motion.Click(50, 0, 90, 30, h.Now));
        Assert.False(h.Runtime.Stuck);
        Assert.Equal(TraceBackend.Trajectory, h.Runtime.ActiveBackend);
        Assert.Equal(TraceAction.Move, moved.Action);
    }

    [Fact]
    public void StandingJustBeyondTheFollowDistance_CountsAsArrived_AndDoesNotClickItsOwnPosition()
    {
        // Taken from a game log: 2.52 units away with a follow distance of 2.5
        var h = new TraceHarness(TraceMode.Smart, TraceOptions.Close()) { Self = new Vector2(30, 47.1f) };
        h.Evaluate(Motion.Stationary(44.1f, 46.8f));

        h.Self = new Vector2(41.6f, 47.1f);
        var moves = 0;
        for (var i = 0; i < 10; i++)
            if (h.After(500, Motion.Stationary(44.1f, 46.8f)).Action == TraceAction.Move)
                moves++;

        Assert.Equal(0, moves);
        Assert.Equal(TraceState.InRange, h.Runtime.State);
    }

    [Fact]
    public void ArrivingAtTheFollowPoint_IsNotStuck()
    {
        var h = new TraceHarness(TraceMode.Smart);
        h.Evaluate(Motion.Stationary(50, 0));

        h.Self = new Vector2(40, 0);
        h.After(500, Motion.Stationary(50, 0));

        Assert.False(h.Runtime.Stuck);
        Assert.Equal(TraceBackend.Trajectory, h.Runtime.ActiveBackend);
    }

    [Fact]
    public void OverlapProfile_WalksToExactlyTheClickedSpot_AndThenStays()
    {
        var h = new TraceHarness(TraceMode.Smart, TraceOptions.Overlap());

        var decision = h.Evaluate(Motion.Click(20, 0, 60, 0));
        Assert.Equal(TraceAction.Move, decision.Action);
        AssertNear(60, decision.Destination.X);
        AssertNear(0, decision.Destination.Y);

        // Arrived on the target (positions are not exact): no more clicks
        h.Self = new Vector2(60.2f, 0.1f);
        var stay = h.After(500, Motion.Stationary(60, 0));
        Assert.Equal(TraceAction.None, stay.Action);
        Assert.Equal(TraceState.InRange, h.Runtime.State);
    }

    [Fact]
    public void ClickMove_HeadsForTheClickedSpot()
    {
        var h = new TraceHarness(TraceMode.Smart, TraceOptions.Close());

        var decision = h.Evaluate(Motion.Click(20, 0, 60, 0));

        // The end of the trajectory, minus the follow distance
        Assert.Equal(TraceAction.Move, decision.Action);
        AssertNear(57.5f, decision.Destination.X);
    }

    [Fact]
    public void GameTrace_SendsTheTraceOnce_AndRepeatsItOnlyWhenIdleOutOfRange()
    {
        var h = new TraceHarness(TraceMode.GameTrace);

        var first = h.Evaluate(Motion.Stationary(50, 0));
        Assert.Equal(TraceAction.SendGameTrace, first.Action);

        var second = h.After(100, Motion.Stationary(50, 0));
        Assert.Equal(TraceAction.None, second.Action);

        var third = h.After(1600, Motion.Stationary(50, 0));
        Assert.Equal(TraceAction.SendGameTrace, third.Action);
    }

    [Fact]
    public void GameTrace_InRange_DoesNotRepeatTheTrace()
    {
        var h = new TraceHarness(TraceMode.GameTrace, TraceOptions.Close());
        h.Evaluate(Motion.Stationary(50, 0));

        h.Self = new Vector2(48, 0);
        var decision = h.After(5000, Motion.Stationary(50, 0));

        Assert.Equal(TraceAction.None, decision.Action);
        Assert.Equal(TraceState.InRange, h.Runtime.State);
    }

    [Fact]
    public void GameTrace_Respawn_SendsTheTraceAgain()
    {
        var h = new TraceHarness(TraceMode.GameTrace);
        h.Evaluate(Motion.Stationary(50, 0));
        h.After(100, null);

        var decision = h.After(600, Motion.Stationary(50, 0));

        Assert.Equal(TraceAction.SendGameTrace, decision.Action);
    }

    [Fact]
    public void GameTrace_NeverWalksByItself_EvenWithAPartyPosition()
    {
        var h = new TraceHarness(TraceMode.GameTrace);
        h.Evaluate(Motion.Stationary(50, 0));

        var decision = h.After(100, null, fallback: new Vector2(40, 0));

        Assert.Equal(TraceAction.None, decision.Action);
    }
}
