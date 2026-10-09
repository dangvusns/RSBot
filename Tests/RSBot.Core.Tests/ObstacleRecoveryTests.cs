using RSBot.Core.Components;
using Xunit;

namespace RSBot.Core.Tests;

public class ObstacleRecoveryTests
{
    [Fact]
    public void RecoveryHasThreeSpacedAttemptsAndCooldown()
    {
        var recovery = new ObstacleRecovery();
        Assert.True(recovery.Start(100, 10, 20));
        Assert.False(recovery.Start(101, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Attempt, recovery.Update(100, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Waiting, recovery.Update(2099, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Attempt, recovery.Update(2100, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Attempt, recovery.Update(4100, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Exhausted, recovery.Update(6100, 10, 20));
        Assert.False(recovery.Start(21099, 10, 20));
        Assert.True(recovery.Start(21100, 10, 20));
    }

    [Fact]
    public void RetryAndTimeoutContinueAcrossMaskedTickWrap()
    {
        var recovery = new ObstacleRecovery();
        var tick = int.MaxValue - 1000;
        Assert.True(recovery.Start(tick, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Attempt, recovery.Update(tick, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Attempt, recovery.Update(unchecked(tick + 2000) & int.MaxValue, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Attempt, recovery.Update(unchecked(tick + 4000) & int.MaxValue, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Exhausted, recovery.Update(unchecked(tick + 6000) & int.MaxValue, 10, 20));
    }

    [Fact]
    public void ServerPositionProgressEndsRecoveryAndTickWrapIsSafe()
    {
        var recovery = new ObstacleRecovery();
        var tick = int.MaxValue - 1000;
        Assert.True(recovery.Start(tick, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Attempt, recovery.Update(tick, 10, 20));
        Assert.Equal(ObstacleRecovery.Step.Waiting, recovery.Update(unchecked(tick + 1500) & int.MaxValue, 10.5f, 20));
        Assert.Equal(ObstacleRecovery.Step.Recovered, recovery.Update(unchecked(tick + 2000) & int.MaxValue, 11, 20));
        Assert.False(recovery.Active);
        Assert.False(recovery.Start(unchecked(tick + 16999) & int.MaxValue, 11, 20));
        Assert.True(recovery.Start(unchecked(tick + 17000) & int.MaxValue, 11, 20));
    }

    [Fact]
    public void CooldownContinuesAcrossMaskedTickWrap()
    {
        var recovery = new ObstacleRecovery();
        var tick = int.MaxValue - 1000;
        Assert.True(recovery.Start(tick, 10, 20));
        recovery.Finish(tick);
        Assert.False(recovery.Start(unchecked(tick + 14999) & int.MaxValue, 10, 20));
        Assert.True(recovery.Start(unchecked(tick + 15000) & int.MaxValue, 10, 20));
    }
}
