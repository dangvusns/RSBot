using System.Numerics;
using RSBot.Core.Components;
using Xunit;

namespace RSBot.Core.Tests;

public class LocalPathfinderTests
{
    [Fact]
    public void DetoursAroundWallAndEverySegmentIsPassable()
    {
        bool CanMove(Vector2 from, Vector2 to)
        {
            if ((from.X < 20 && to.X >= 20) || (to.X < 20 && from.X >= 20))
            {
                var y = from.Y + (to.Y - from.Y) * (20 - from.X) / (to.X - from.X);
                return y >= 20 || y <= -20;
            }
            return true;
        }
        var path = LocalPathfinder.Find(Vector2.Zero, new Vector2(40, 0), CanMove, () => false);
        Assert.NotNull(path);
        Assert.Equal(Vector2.Zero, path[0]);
        Assert.Equal(new Vector2(40, 0), path[^1]);
        for (var i = 1; i < path.Count; i++)
            Assert.True(CanMove(path[i - 1], path[i]));
    }

    [Fact]
    public void CancellationAndBudgetCannotReturnPartialRoute()
    {
        Assert.Null(LocalPathfinder.Find(Vector2.Zero, Vector2.One, (_, _) => true, () => true));
        Assert.Null(LocalPathfinder.Find(Vector2.Zero, new Vector2(40, 0), (_, _) => false, () => false, 1));
        Assert.Null(LocalPathfinder.Find(Vector2.Zero, new Vector2(1001, 0), (_, _) => true, () => false));
    }

    [Fact]
    public void CancellingDuringSearchStopsFurtherMovementChecks()
    {
        var checks = 0;
        Assert.Null(LocalPathfinder.Find(Vector2.Zero, new Vector2(40, 0), (_, _) => ++checks > 1, () => checks >= 3));
        Assert.Equal(3, checks);
    }
}
