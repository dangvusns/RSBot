using RSBot.Core.Components;
using Xunit;

namespace RSBot.Core.Tests;

public class EncounterOpenersTests
{
    [Fact]
    public void SelectionDoesNotConsumeOpenerAndConfirmedCastSurvivesInterruption()
    {
        var history = new EncounterOpeners();
        Assert.False(history.WasAccepted(100, 7));
        Assert.False(history.WasAccepted(100, 7)); // rejected/not sent, still eligible
        history.Confirm(100, 7);
        history.Confirm(200, 7); // fight a smaller attacker, then resume target 100
        Assert.True(history.WasAccepted(100, 7));
        Assert.False(history.WasAccepted(100, 8));
        history.Forget(100); // death/despawn permits a new encounter with a reused ID
        Assert.False(history.WasAccepted(100, 7));
        Assert.True(history.WasAccepted(200, 7));
    }

    [Fact]
    public void InactiveHistoryExpiresButCurrentEncounterIsTouched()
    {
        long now = 0;
        var history = new EncounterOpeners(() => now, lifetimeMilliseconds: 100);
        history.Confirm(100, 7);
        history.Confirm(200, 7);
        now = 90;
        Assert.True(history.WasAccepted(100, 7));
        now = 110;
        Assert.True(history.WasAccepted(100, 7));
        Assert.False(history.WasAccepted(200, 7));
    }

    [Fact]
    public void CapacityEvictsOldestAndSessionResetClearsAll()
    {
        long now = 0;
        var history = new EncounterOpeners(() => now, capacity: 2);
        history.Confirm(100, 7);
        now++;
        history.Confirm(200, 7);
        now++;
        history.Confirm(300, 7);
        Assert.False(history.WasAccepted(100, 7));
        Assert.True(history.WasAccepted(200, 7));
        Assert.True(history.WasAccepted(300, 7));
        history.Clear();
        Assert.False(history.WasAccepted(200, 7));
        Assert.False(history.WasAccepted(300, 7));
    }
}
