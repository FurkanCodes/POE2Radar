using NumVec2 = System.Numerics.Vector2;
using POE2Radar.Core.Pathfinding;

namespace POE2Radar.Overlay.Navigation;

/// <summary>
/// Render-rate navigation path assembly — unit-tested, no game reads.
/// Draws tracker-owned route points in route order. It never treats a target point as a path.
/// </summary>
public static class NavigationPathBuilder
{
    public static (int x, int y) PlayerCell(NumVec2 playerGrid)
        => ((int)MathF.Round(playerGrid.X), (int)MathF.Round(playerGrid.Y));

    /// <summary>True when there is anything to draw for this target.</summary>
    public static bool HasDrawablePath(
        IReadOnlyList<(int x, int y)> waypoints,
        (int x, int y)? liveGoal,
        RoutePlanStatus routeStatus = RoutePlanStatus.Planned)
        => routeStatus == RoutePlanStatus.Planned && waypoints.Count > 0;

    /// <summary>
    /// First waypoint index that is not behind the player relative to the goal bearing.
    /// Returns <c>waypoints.Count</c> when every stored vertex is behind (draw straight to goal).
    /// </summary>
    public static int FindForwardWaypointIndex(
        NumVec2 playerGrid,
        IReadOnlyList<(int x, int y)> waypoints,
        (int x, int y)? liveGoal)
    {
        if (waypoints.Count == 0) return 0;

        var goal = liveGoal.HasValue ? ToVec(liveGoal.Value) : ToVec(waypoints[^1]);
        var toGoal = goal - playerGrid;
        if (toGoal.LengthSquared() < 1e-4f) return waypoints.Count - 1;

        var goalDir = NumVec2.Normalize(toGoal);
        const float behindCells = 2f; // grid cells of backward tolerance at corners
        for (var i = 0; i < waypoints.Count; i++)
        {
            var ahead = NumVec2.Dot(ToVec(waypoints[i]) - playerGrid, goalDir);
            if (ahead > -behindCells) return i;
        }

        return waypoints.Count;
    }

    /// <summary>
    /// Waypoints plus optional resolved drawable goal. The route tracker owns cursor advancement, so
    /// this preserves route order instead of trimming by final-goal bearing.
    /// </summary>
    public static List<(int x, int y)> BuildForwardPath(
        NumVec2 playerGrid,
        IReadOnlyList<(int x, int y)> waypoints,
        (int x, int y)? liveGoal)
    {
        var result = new List<(int x, int y)>();
        foreach (var waypoint in waypoints)
        {
            if (result.Count > 0 && result[^1] == waypoint) continue;
            result.Add(waypoint);
        }

        if (result.Count > 0 && liveGoal is { } g && result[^1] != g)
            result.Add(g);

        return result;
    }

    /// <summary>Grid polyline for map projection: live player cell + forward path.</summary>
    public static List<(int x, int y)> BuildDrawPolyline(
        NumVec2 playerGrid,
        IReadOnlyList<(int x, int y)> waypoints,
        (int x, int y)? liveGoal)
    {
        var fwd = BuildForwardPath(playerGrid, waypoints, liveGoal);
        if (fwd.Count == 0) return fwd;

        var poly = new List<(int x, int y)>(fwd.Count + 1) { PlayerCell(playerGrid) };
        foreach (var p in fwd)
        {
            if (poly[^1] == p) continue;
            poly.Add(p);
        }
        return poly;
    }

    /// <summary>Ensure the resolved walkable goal cell is the terminal vertex (for world goal ring).</summary>
    public static List<(int x, int y)> AppendResolvedGoal(
        IReadOnlyList<(int x, int y)> points,
        (int x, int y)? resolvedGoal)
    {
        if (points.Count == 0) return new List<(int x, int y)>();
        var result = new List<(int x, int y)>(points.Count + 1);
        foreach (var p in points)
        {
            if (result.Count > 0 && result[^1] == p) continue;
            result.Add(p);
        }
        if (resolvedGoal is { } g && (result.Count == 0 || result[^1] != g))
            result.Add(g);
        return result;
    }

    /// <summary>
    /// v1.3.0-style display decimation: stride <c>max(1, count / maxDots)</c>, always includes the last point.
    /// </summary>
    public static List<(int x, int y)> DecimateForWorldDisplay(
        IReadOnlyList<(int x, int y)> points,
        int maxDots = 40)
    {
        if (points.Count <= 2) return points is List<(int x, int y)> list ? list : points.ToList();
        var stride = Math.Max(1, points.Count / Math.Max(1, maxDots));
        var result = new List<(int x, int y)>(Math.Min(points.Count, maxDots + 2));
        for (var i = 0; i < points.Count; i += stride)
            result.Add(points[i]);
        if (result[^1] != points[^1])
            result.Add(points[^1]);
        return result;
    }

    private static NumVec2 ToVec((int x, int y) c) => new(c.x, c.y);
}
