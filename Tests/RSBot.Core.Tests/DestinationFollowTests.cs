using System.Numerics;
using RSBot.Core.Components.Tracing;
using Xunit;

namespace RSBot.Core.Tests;

public class DestinationFollowTests
{
    private static TraceHarness Create()
    {
        var options = TraceOptions.Overlap();
        options.DestinationFollow = true;
        options.IdleRetryInterval = 500;
        options.DestinationChangeThreshold = 0.5;
        options.TargetMovementThreshold = 0.5;
        return new TraceHarness(TraceMode.Smart, options);
    }

    [Fact]
    public void StartMovesToCurrentPositionThenCopiesClickedDestination()
    {
        var h = Create();
        var target = Motion.Click(10, 0, 40, 0);
        Assert.Equal(new Vector2(10, 0), h.Evaluate(target).Destination);
        Assert.Equal(new Vector2(40, 0), h.After(500, target).Destination);
        Assert.Equal(TraceBackend.Trajectory, h.Runtime.ActiveBackend);
    }

    [Fact]
    public void TargetStartsMovingWhileOverlapping_FollowerCopiesDestination()
    {
        var h = Create();
        h.Evaluate(Motion.Stationary(0, 0));
        var decision = h.After(500, Motion.Click(0, 0, 40, 0));
        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.Equal(new Vector2(40, 0), decision.Destination);
    }

    [Fact]
    public void SmallDestinationChangeIsCopiedWhileFollowing()
    {
        var h = Create();
        h.Evaluate(Motion.Stationary(20, 0));
        h.SelfMoving = true;
        h.SelfHasDestination = true;
        h.SelfDestination = new Vector2(20, 0);
        var decision = h.After(300, Motion.Click(20, 0, 21, 0));
        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.Equal(new Vector2(21, 0), decision.Destination);
    }

    [Fact]
    public void ManualClickRestoresFollowingEvenWhileFollowerIsMoving()
    {
        var h = Create();
        var target = Motion.Stationary(20, 0);
        h.Evaluate(target);
        h.SelfMoving = true;
        h.SelfHasDestination = true;
        h.SelfDestination = new Vector2(-20, 0);
        var decision = h.After(500, target);
        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.Equal(new Vector2(20, 0), decision.Destination);
        Assert.Equal("restore follow after manual movement", decision.Reason);
    }

    [Fact]
    public void BlockedOrUnacknowledgedMoveRetriesWithoutNativeFallback()
    {
        var h = Create();
        var target = Motion.Stationary(20, 0);
        h.Evaluate(target);
        Assert.Equal(TraceAction.None, h.After(100, target).Action);
        Assert.Equal(TraceAction.Move, h.After(400, target).Action);
        Assert.Equal(TraceAction.None, h.After(100, target).Action);
        Assert.Equal(TraceAction.Move, h.After(400, target).Action);
        Assert.Equal(TraceBackend.Trajectory, h.Runtime.ActiveBackend);
    }

    [Fact]
    public void HeadingOnlyAndSpinningTargetsNeverActivateNativeTrace()
    {
        var h = Create();
        Assert.Equal(TraceAction.Move, h.Evaluate(Motion.Keys(20, 0, 0, h.Now, h.Now)).Action);
        var decision = h.After(500, Motion.Keys(25, 0, 1, h.Now, h.Now, spinning: true));
        Assert.Equal(TraceAction.None, decision.Action);
        Assert.Equal(TraceBackend.Trajectory, h.Runtime.ActiveBackend);
    }

    [Fact]
    public void MissingTargetWaitsAndReappearanceStartsAtCurrentPosition()
    {
        var h = Create();
        h.Evaluate(Motion.Stationary(20, 0));
        Assert.Equal(TraceAction.None, h.After(500, null).Action);
        var decision = h.After(500, Motion.Click(30, 0, 50, 0));
        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.Equal(new Vector2(30, 0), decision.Destination);
    }

    [Fact]
    public void FreshRuntimeAfterRestartDoesNotReusePriorDestination()
    {
        var previous = Create();
        previous.Evaluate(Motion.Stationary(20, 0));
        previous.After(500, Motion.Click(20, 0, 50, 0));
        var restarted = Create();
        var decision = restarted.Evaluate(Motion.Stationary(30, 0));
        Assert.Equal(TraceAction.Move, decision.Action);
        Assert.Equal(new Vector2(30, 0), decision.Destination);
        Assert.Equal(TraceBackend.Trajectory, restarted.Runtime.ActiveBackend);
    }

    [Fact]
    public void LegitimateActionSuspendsFollowingAndCompletionRestoresIt()
    {
        var h = Create();
        h.Busy = TraceBusy.InAction;
        Assert.Equal(TraceAction.None, h.Evaluate(Motion.Stationary(20, 0)).Action);
        Assert.Equal(TraceState.Suspended, h.Runtime.State);
        h.Busy = TraceBusy.None;
        Assert.Equal(TraceAction.Move, h.After(500, Motion.Stationary(20, 0)).Action);
    }
}
