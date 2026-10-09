using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects;
using RSBot.NavMeshApi;

namespace RSBot.Training.Bot;

internal static partial class NavigationManager
{
    private static bool TryBuildLocalPath(Position from, Position target, out List<Position> route)
    {
        route = null;
        // shortcut: local outdoor detours only; use the recorded/graph route for dungeon or inter-world travel.
        if (from.Region.IsDungeon || target.Region.IsDungeon || from.WorldId != target.WorldId || from.LayerId != target.LayerId)
            return false;

        var player = Game.Player;
        var clock = Stopwatch.StartNew();
        bool Cancelled() => clock.ElapsedMilliseconds >= 2000 || !Game.Ready || Game.Player != player || !Kernel.Bot.Running;
        var resolved = new Dictionary<Vector2, (Position Position, NavMeshTransform Transform)>();

        bool Resolve(Vector2 point)
        {
            if (resolved.ContainsKey(point))
                return true;
            var position = new Position(point.X, point.Y)
            {
                ZOffset = from.ZOffset,
                WorldId = from.WorldId,
                LayerId = from.LayerId,
            };
            if (!position.TryGetNavMeshTransform(out var transform))
                return false;
            position.ZOffset = transform.Offset.Y;
            resolved[point] = (position, transform);
            return true;
        }

        bool CanMove(Vector2 start, Vector2 end) => !Cancelled() && Resolve(start) && Resolve(end)
            && (start == end || NavMeshManager.Raycast(new NavMeshTransform(resolved[start].Transform),
                new NavMeshTransform(resolved[end].Transform), NavMeshRaycastType.Move));

        var path = LocalPathfinder.Find(new Vector2(from.X, from.Y), new Vector2(target.X, target.Y), CanMove, Cancelled);
        if (path == null)
            return false;
        route = new List<Position>(path.Count);
        foreach (var point in path)
            route.Add(resolved[point].Position);
        return true;
    }
}
