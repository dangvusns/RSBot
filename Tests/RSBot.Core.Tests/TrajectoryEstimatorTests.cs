using System.Numerics;
using RSBot.Core.Components.Tracing;
using Xunit;

namespace RSBot.Core.Tests;

public class TrajectoryEstimatorTests
{
    private const int Now = 10000;

    private static TraceOptions Enabled()
    {
        return new TraceOptions { PredictionEnabled = true, PredictionTime = 200 };
    }

    [Fact]
    public void EstimateCurrent_IsTheInterpolatedPositionOfTheSnapshot()
    {
        var state = Motion.Click(3, 4, 30, 40);

        Assert.Equal(new Vector2(3, 4), TrajectoryEstimator.EstimateCurrent(state));
    }

    [Fact]
    public void AimPoint_ClickMove_IsTheClickedDestination()
    {
        var state = Motion.Click(0, 0, 30, 40, Now);

        Assert.Equal(new Vector2(30, 40), TrajectoryEstimator.AimPoint(state, Now, new TraceOptions()));
    }

    [Fact]
    public void AimPoint_WithoutAimingAtTheDestination_IsTheCurrentPosition()
    {
        var state = Motion.Click(0, 0, 30, 40, Now);
        var options = new TraceOptions { AimAtDestination = false };

        Assert.Equal(state.Position, TrajectoryEstimator.AimPoint(state, Now, options));
    }

    [Fact]
    public void AimPoint_StandingTarget_IsItsPosition()
    {
        var state = Motion.Stationary(7, 8, Now);

        Assert.Equal(state.Position, TrajectoryEstimator.AimPoint(state, Now, new TraceOptions()));
    }

    [Fact]
    public void Predict_IsDisabledByDefault()
    {
        var state = Motion.Click(0, 0, 10, 0);

        Assert.Equal(state.Position, TrajectoryEstimator.Predict(state, Now, new TraceOptions()));
    }

    [Fact]
    public void Predict_AdvancesAlongTheDestination()
    {
        var state = Motion.Click(0, 0, 10, 0, Now, speed: 5);

        var predicted = TrajectoryEstimator.Predict(state, Now, Enabled());

        Assert.InRange(predicted.X, 0.99f, 1.01f);
        Assert.InRange(predicted.Y, -0.01f, 0.01f);
    }

    [Fact]
    public void Predict_NeverPassesTheDestination()
    {
        var state = Motion.Click(0, 0, 0.5f, 0, Now, speed: 5);

        var predicted = TrajectoryEstimator.Predict(state, Now, Enabled());

        Assert.InRange(predicted.X, 0.49f, 0.51f);
    }

    [Fact]
    public void Predict_IsCappedByTheMaximumPredictionTime()
    {
        var options = new TraceOptions
        {
            PredictionEnabled = true,
            PredictionTime = 1000,
            MaxPredictionTime = 500,
        };

        var predicted = TrajectoryEstimator.Predict(Motion.Click(0, 0, 100, 0, Now, speed: 5), Now, options);

        Assert.InRange(predicted.X, 2.49f, 2.51f);
    }

    [Fact]
    public void Predict_StationaryTarget_ReturnsTheCurrentPosition()
    {
        var state = Motion.Stationary(5, 5, Now);

        Assert.Equal(state.Position, TrajectoryEstimator.Predict(state, Now, Enabled()));
    }

    [Fact]
    public void Predict_KeyWalking_FollowsTheHeading()
    {
        var state = Motion.Keys(0, 0, 0f, Now, Now);

        var predicted = TrajectoryEstimator.Predict(state, Now, Enabled());

        Assert.InRange(predicted.X, 0.99f, 1.01f);
    }

    [Fact]
    public void Predict_KeyWalkingWithStaleMovement_ReturnsTheCurrentPosition()
    {
        // The movement last changed 10 seconds ago, much longer than the stale timeout
        var state = Motion.Keys(0, 0, 0f, 0, Now);

        Assert.True(TrajectoryEstimator.IsStale(state, Now, new TraceOptions()));
        Assert.Equal(state.Position, TrajectoryEstimator.Predict(state, Now, Enabled()));
    }

    [Fact]
    public void IsStale_OnlyAppliesToKeyWalking()
    {
        var click = Motion.Click(0, 0, 500, 0, 0);

        Assert.False(TrajectoryEstimator.IsStale(click, Now, new TraceOptions()));
    }
}
