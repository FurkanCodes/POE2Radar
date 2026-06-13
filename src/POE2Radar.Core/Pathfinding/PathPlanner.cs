using System.Diagnostics;
using POE2Radar.Core.Game;

namespace POE2Radar.Core.Pathfinding;

/// <summary>
/// Single entry point for draw-only path planning on the radar overlay. Given the live
/// terrain grid and a start/goal in grid coordinates, returns a short list of smoothed
/// grid waypoints, plus explicit planning status for diagnostics and rendering.
/// </summary>
public sealed class PathPlanner
{
    private AStar? _astar;
    private int _gridWidth;
    private int _gridHeight;

    public readonly record struct RoutePlanResult(
        RoutePlanStatus Status,
        (int x, int y)? ResolvedGoal,
        IReadOnlyList<(int x, int y)> Waypoints,
        string FailureReason,
        double PlanMilliseconds)
    {
        public static RoutePlanResult Failure(RoutePlanStatus status, string reason, double ms = 0)
            => new(status, null, Array.Empty<(int, int)>(), reason, ms);
    }

    /// <summary>
    /// Backwards-compatible simple planner. Prefer <see cref="PlanToReachableTarget"/> when the caller
    /// needs route diagnostics or target-nearby goal resolution.
    /// </summary>
    public IReadOnlyList<(int x, int y)> Plan(
        Poe2Live.TerrainData terrain, (int x, int y) start, (int x, int y) goal,
        int maxNodes = 1_000_000)
    {
        var result = PlanToReachableTarget(terrain, start, goal, goalSearchRadius: 1, maxNodes: maxNodes);
        return result.Status == RoutePlanStatus.Planned ? result.Waypoints : Array.Empty<(int, int)>();
    }

    /// <summary>
    /// Plan to a walkable candidate near the requested target. This avoids routing to a decorative
    /// centroid inside walls/scenery while keeping the marker itself at the real target location.
    /// </summary>
    public RoutePlanResult PlanToReachableTarget(
        Poe2Live.TerrainData terrain,
        (int x, int y) start,
        (int x, int y) goal,
        int goalSearchRadius = 24,
        IReadOnlyList<PathCell>? targetAnchors = null,
        IReadOnlyList<PathCell>? forcedWalkable = null,
        int maxNodes = 1_000_000)
    {
        var sw = Stopwatch.StartNew();

        if (terrain.Width <= 0 || terrain.Height <= 0 || terrain.Walkable.Length == 0)
            return PathPlanner.RoutePlanResult.Failure(RoutePlanStatus.WaitingForTerrain, "terrain unavailable");

        if (_astar is null || _gridWidth != terrain.Width || _gridHeight != terrain.Height)
        {
            _astar = new AStar(terrain.Width, terrain.Height);
            _gridWidth = terrain.Width;
            _gridHeight = terrain.Height;
        }

        var reader = new TerrainCellReader(terrain, forcedWalkable);
        var startCell = Clamp(new PathCell(start.x, start.y), terrain.Width, terrain.Height);
        if (reader.Read(startCell.X, startCell.Y) == 0)
        {
            var snapped = FindNearestWalkable(reader, startCell, maxRadius: 8);
            if (snapped is null)
                return PathPlanner.RoutePlanResult.Failure(
                    RoutePlanStatus.NoWalkableStart,
                    "player is not on reachable walkable terrain",
                    sw.Elapsed.TotalMilliseconds);
            startCell = snapped.Value;
        }

        var goalCell = Clamp(new PathCell(goal.x, goal.y), terrain.Width, terrain.Height);
        var candidates = BuildGoalCandidates(reader, goalCell, goalSearchRadius, targetAnchors);
        if (candidates.Count == 0)
            return PathPlanner.RoutePlanResult.Failure(
                RoutePlanStatus.NoReachableGoal,
                "no walkable cell near target",
                sw.Elapsed.TotalMilliseconds);

        var path = _astar.FindPathToAny(
            reader,
            startCell,
            candidates,
            goalCell,
            maxNodes,
            flatCost: true);

        if (!path.Found || path.Cells.Count == 0)
            return PathPlanner.RoutePlanResult.Failure(
                RoutePlanStatus.NoPath,
                "target area is not reachable from player",
                sw.Elapsed.TotalMilliseconds);

        var smoothed = PathSmoother.Smooth(reader, path.Cells, minWalkable: 1);
        var result = new (int x, int y)[smoothed.Count];
        for (var i = 0; i < smoothed.Count; i++)
            result[i] = (smoothed[i].X, smoothed[i].Y);

        var resolved = path.Cells[^1];
        return new RoutePlanResult(
            RoutePlanStatus.Planned,
            (resolved.X, resolved.Y),
            result,
            "",
            sw.Elapsed.TotalMilliseconds);
    }

    private static List<PathCell> BuildGoalCandidates(
        ICellReader reader,
        PathCell goal,
        int goalSearchRadius,
        IReadOnlyList<PathCell>? anchors)
    {
        goalSearchRadius = Math.Clamp(goalSearchRadius, 1, 128);
        var seen = new HashSet<int>();
        var candidates = new List<PathCell>();

        AddCandidatesAround(reader, goal, goalSearchRadius, seen, candidates);
        if (anchors is not null)
        {
            foreach (var anchor in anchors)
                AddCandidatesAround(reader, anchor, Math.Min(12, goalSearchRadius), seen, candidates);
        }

        candidates.Sort((a, b) =>
        {
            var da = DistanceSquared(a, goal);
            var db = DistanceSquared(b, goal);
            return da.CompareTo(db);
        });
        return candidates;
    }

    private static void AddCandidatesAround(
        ICellReader reader,
        PathCell center,
        int radius,
        HashSet<int> seen,
        List<PathCell> candidates)
    {
        radius = Math.Clamp(radius, 1, 128);
        var r2 = radius * radius;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                var x = center.X + dx;
                var y = center.Y + dy;
                if ((uint)x >= (uint)reader.Width || (uint)y >= (uint)reader.Height) continue;
                if (reader.Read(x, y) == 0) continue;
                var idx = y * reader.Width + x;
                if (!seen.Add(idx)) continue;
                candidates.Add(new PathCell(x, y));
            }
        }
    }

    private static PathCell? FindNearestWalkable(ICellReader reader, PathCell center, int maxRadius)
    {
        if (reader.Read(center.X, center.Y) > 0) return center;
        for (var r = 1; r <= maxRadius; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                    var x = center.X + dx;
                    var y = center.Y + dy;
                    if ((uint)x >= (uint)reader.Width || (uint)y >= (uint)reader.Height) continue;
                    if (reader.Read(x, y) > 0) return new PathCell(x, y);
                }
            }
        }
        return null;
    }

    private static PathCell Clamp(PathCell c, int width, int height)
        => new(Math.Clamp(c.X, 0, Math.Max(0, width - 1)), Math.Clamp(c.Y, 0, Math.Max(0, height - 1)));

    private static int DistanceSquared(PathCell a, PathCell b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
