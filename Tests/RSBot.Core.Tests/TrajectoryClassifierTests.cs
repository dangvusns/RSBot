using System.Numerics;
using RSBot.Core.Components.Tracing;
using Xunit;

namespace RSBot.Core.Tests;

public class TrajectoryClassifierTests
{
    [Fact]
    public void StandingTarget_IsReliable()
    {
        Assert.Equal(TrajectoryQuality.Reliable, TrajectoryClassifier.Classify(Motion.Stationary(1, 2)));
    }

    [Fact]
    public void GroundClick_IsReliable()
    {
        Assert.Equal(TrajectoryQuality.Reliable, TrajectoryClassifier.Classify(Motion.Click(0, 0, 10, 10)));
    }

    [Fact]
    public void KeyboardMovementOrSkyClick_IsHeadingOnly()
    {
        Assert.Equal(TrajectoryQuality.HeadingOnly, TrajectoryClassifier.Classify(Motion.Keys(0, 0, 0f, 0, 0)));
    }

    [Fact]
    public void TurningOnTheSpot_IsSpinning()
    {
        var state = Motion.Keys(0, 0, 0f, 0, 0, spinning: true);

        Assert.Equal(TrajectoryQuality.Spinning, TrajectoryClassifier.Classify(state));
    }

    [Fact]
    public void NotFiniteDestination_IsInvalid()
    {
        var state = new TargetMotionState(
            1,
            Vector2.Zero,
            new Vector2(float.NaN, 0),
            true,
            true,
            0f,
            5,
            0,
            0
        );

        Assert.Equal(TrajectoryQuality.InvalidDestination, TrajectoryClassifier.Classify(state));
    }
}
