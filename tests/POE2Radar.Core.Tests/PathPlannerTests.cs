using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class PathPlannerTests
{
    [Fact]
    public void AStar_DoesNotCrossBlockedDiagonalCorner()
    {
        var terrain = Terrain(3, 3, blocked: [(1, 0), (0, 1)]);
        var reader = new TerrainCellReader(terrain);
        var astar = new AStar(3, 3);

        var path = astar.FindPath(reader, new PathCell(0, 0), new PathCell(2, 2), flatCost: true);

        Assert.False(path.Found);
    }

    [Fact]
    public void PathSmoother_LineOfSightDoesNotRecreateBlockedCornerShortcut()
    {
        var terrain = Terrain(3, 3, blocked: [(1, 0), (0, 1)]);
        var reader = new TerrainCellReader(terrain);

        Assert.False(PathSmoother.HasLineOfSight(reader, 0, 0, 2, 2));
    }

    [Fact]
    public void PlanToReachableTarget_BlockedCentroidRoutesToNearbyWalkableCell()
    {
        var terrain = Terrain(12, 7, blocked: [(8, 3)]);
        var planner = new PathPlanner();

        var plan = planner.PlanToReachableTarget(terrain, (1, 3), (8, 3), goalSearchRadius: 2);

        Assert.Equal(RoutePlanStatus.Planned, plan.Status);
        Assert.NotEmpty(plan.Waypoints);
        Assert.NotEqual((8, 3), plan.ResolvedGoal);
        Assert.NotNull(plan.ResolvedGoal);
        Assert.InRange(plan.ResolvedGoal!.Value.x, 6, 10);
    }

    [Fact]
    public void PlanToReachableTarget_UnreachableTargetReturnsNoPath()
    {
        var blocked = Enumerable.Range(0, 7).Select(y => (5, y)).ToArray();
        var terrain = Terrain(12, 7, blocked);
        var planner = new PathPlanner();

        var plan = planner.PlanToReachableTarget(terrain, (1, 3), (8, 3), goalSearchRadius: 2);

        Assert.Equal(RoutePlanStatus.NoPath, plan.Status);
        Assert.Empty(plan.Waypoints);
    }

    [Fact]
    public void PlanToReachableTarget_DoorOverrideCanOpenKnownDoorCell()
    {
        var blocked = Enumerable.Range(0, 7).Select(y => (5, y)).ToArray();
        var terrain = Terrain(12, 7, blocked);
        var planner = new PathPlanner();

        var plan = planner.PlanToReachableTarget(
            terrain,
            (1, 3),
            (8, 3),
            goalSearchRadius: 2,
            forcedWalkable: [new PathCell(5, 3)]);

        Assert.Equal(RoutePlanStatus.Planned, plan.Status);
        Assert.NotEmpty(plan.Waypoints);
        Assert.Contains(plan.Waypoints, p => p.x > 5);
    }

    private static Poe2Live.TerrainData Terrain(int width, int height, IReadOnlyList<(int x, int y)> blocked)
    {
        var data = Enumerable.Repeat((byte)1, width * height).ToArray();
        foreach (var (x, y) in blocked)
            data[y * width + x] = 0;
        return new Poe2Live.TerrainData(data, width, height);
    }
}
