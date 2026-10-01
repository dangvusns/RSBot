using System.Numerics;
using RSBot.Core.Components.Tracing;
using Xunit;

namespace RSBot.Core.Tests;

public class FollowPointSolverTests
{
    [Fact]
    public void TrySolve_PlacesThePointOnTheLineAtTheFollowDistance()
    {
        var found = FollowPointSolver.TrySolve(
            new Vector2(80, 100),
            new Vector2(100, 100),
            TraceOptions.PartyMaster(),
            out var destination
        );

        Assert.True(found);
        Assert.InRange(destination.X, 89.99f, 90.01f);
        Assert.InRange(destination.Y, 99.99f, 100.01f);
    }

    [Fact]
    public void TrySolve_WithinTheFollowDistance_ReturnsFalse()
    {
        var found = FollowPointSolver.TrySolve(
            new Vector2(0, 0),
            new Vector2(9, 0),
            TraceOptions.PartyMaster(),
            out _
        );

        Assert.False(found);
    }

    [Fact]
    public void TrySolve_LimitsTheLengthOfASingleMove()
    {
        FollowPointSolver.TrySolve(
            new Vector2(0, 0),
            new Vector2(1000, 0),
            TraceOptions.PartyMaster(),
            out var destination
        );

        Assert.InRange(destination.X, 139.99f, 140.01f);
    }

    [Theory]
    [InlineData(TraceState.Following, 13.0, true, TraceState.Following)]
    [InlineData(TraceState.Following, 11.0, true, TraceState.Following)]
    [InlineData(TraceState.Following, 11.0, false, TraceState.Following)]
    [InlineData(TraceState.Following, 9.5, true, TraceState.Following)]
    [InlineData(TraceState.Following, 9.5, false, TraceState.InRange)]
    [InlineData(TraceState.Following, 7.0, true, TraceState.InRange)]
    [InlineData(TraceState.InRange, 9.0, true, TraceState.InRange)]
    [InlineData(TraceState.InRange, 11.5, false, TraceState.InRange)]
    [InlineData(TraceState.InRange, 12.5, false, TraceState.Following)]
    [InlineData(TraceState.ResolvingTarget, 11.0, false, TraceState.Following)]
    [InlineData(TraceState.ResolvingTarget, 9.0, false, TraceState.InRange)]
    public void NextRangeState_HasHysteresis(
        TraceState current,
        double distance,
        bool selfMoving,
        TraceState expected
    )
    {
        var next = FollowPointSolver.NextRangeState(current, distance, TraceOptions.PartyMaster(), selfMoving);

        Assert.Equal(expected, next);
    }

    [Fact]
    public void Normalize_KeepsTheDistancesInOrder()
    {
        var options = new TraceOptions
        {
            FollowDistance = 10,
            StartMoveDistance = 5,
            StopMoveDistance = 20,
            MaxMoveDistance = 500,
        };

        options.Normalize();

        Assert.Equal(10, options.StartMoveDistance);
        Assert.Equal(10, options.StopMoveDistance);
        Assert.Equal(148, options.MaxMoveDistance);
    }
}
