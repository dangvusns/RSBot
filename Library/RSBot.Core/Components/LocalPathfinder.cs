using System;
using System.Collections.Generic;
using System.Numerics;

namespace RSBot.Core.Components;

/// <summary>Bounded local detours; the caller supplies resolved movement checks and cancellation.</summary>
public static class LocalPathfinder
{
    public static List<Vector2> Find(
        Vector2 start,
        Vector2 target,
        Func<Vector2, Vector2, bool> canMove,
        Func<bool> cancelled,
        int maximumNodes = 4096
    )
    {
        if (!float.IsFinite(start.X) || !float.IsFinite(start.Y)
            || !float.IsFinite(target.X) || !float.IsFinite(target.Y)
            || Vector2.Distance(start, target) > 1000 || cancelled())
            return null;

        if (canMove(start, target))
            return cancelled() ? null : new List<Vector2> { start, target };

        const float spacing = 10;
        const float margin = 80;
        var minimum = Vector2.Min(start, target) - new Vector2(margin);
        var maximum = Vector2.Max(start, target) + new Vector2(margin);
        var origin = (X: 0, Y: 0);
        var open = new PriorityQueue<(int X, int Y), float>();
        var costs = new Dictionary<(int X, int Y), float> { [origin] = 0 };
        var previous = new Dictionary<(int X, int Y), (int X, int Y)>();
        var visited = new HashSet<(int X, int Y)>();
        open.Enqueue(origin, Vector2.Distance(start, target));

        while (open.TryDequeue(out var current, out _) && visited.Count < maximumNodes)
        {
            if (cancelled())
                return null;
            if (!visited.Add(current))
                continue;
            var position = start + new Vector2(current.X, current.Y) * spacing;
            if (Vector2.Distance(position, target) <= spacing * 1.5f && canMove(position, target))
            {
                var route = new List<Vector2> { target, position };
                while (previous.TryGetValue(current, out var parent))
                {
                    current = parent;
                    route.Add(start + new Vector2(current.X, current.Y) * spacing);
                }
                route.Reverse();
                return cancelled() ? null : route;
            }

            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (cancelled())
                    return null;
                if (x == 0 && y == 0)
                    continue;
                var next = (X: current.X + x, Y: current.Y + y);
                var destination = start + new Vector2(next.X, next.Y) * spacing;
                if (visited.Contains(next) || destination.X < minimum.X || destination.X > maximum.X
                    || destination.Y < minimum.Y || destination.Y > maximum.Y)
                    continue;
                var cost = costs[current] + Vector2.Distance(position, destination);
                if (costs.TryGetValue(next, out var known) && known <= cost)
                    continue;
                if (!canMove(position, destination))
                    continue;
                costs[next] = cost;
                previous[next] = current;
                open.Enqueue(next, cost + Vector2.Distance(destination, target));
            }
        }
        return null;
    }
}
