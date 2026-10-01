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
            var decision = h.After(100, Motion.Click(30 + i * 10, 0, 500, 0));
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
            var decision = h.After(50, Motion.Click(30 + i * 3, 0, 200, 0));
            if (decision.Action == TraceAction.Move)
                moves++;
        }

        // 1 second with a minimum of 250 ms between two commands
        Assert.InRange(moves, 1, 5);
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
    public void KeyWalking_SwitchesToTheNativeTraceAfterTheDelay_AndBackWhenTheTargetClicks()
    {
        var h = new TraceHarness(TraceMode.Smart);

        // Just started to walk with the keys: not switched yet
        var first = h.Evaluate(Motion.Keys(50, 0, 0f, h.Now, h.Now));
        Assert.Equal(TracePhase.BotFollow, h.Runtime.Phase);
        Assert.Equal(TraceAction.Move, first.Action);

        var second = h.After(100, Motion.Keys(55, 0, 0f, h.Now - 100, h.Now));
        Assert.Equal(TracePhase.BotFollow, h.Runtime.Phase);
        Assert.Equal(TraceAction.None, second.Action);

        // Still walking with the keys after the switch delay: the native trace takes over
        var third = h.After(300, Motion.Keys(70, 0, 0f, h.Now - 400, h.Now));
        Assert.Equal(TracePhase.GameFollow, h.Runtime.Phase);
        Assert.Equal(TraceAction.SendGameTrace, third.Action);

        // The target clicks the ground again: the bot walks by itself
        var fourth = h.After(100, Motion.Click(70, 0, 90, 0, h.Now));
        Assert.Equal(TracePhase.BotFollow, h.Runtime.Phase);
        Assert.Equal(TraceAction.Move, fourth.Action);
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

        var decision = h.After(100, Motion.Stationary(50, 0));

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
