using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Navigation;
using NumVec2 = System.Numerics.Vector2;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class NavigationPathBuilderTests
{
    [Fact]
    public void BuildForwardPath_PreservesTrackerRouteOrder()
    {
        var player = new NumVec2(28f, 0f);
        var waypoints = new List<(int x, int y)> { (0, 0), (10, 0), (20, 10), (30, 10), (40, 0) };

        var fwd = NavigationPathBuilder.BuildForwardPath(player, waypoints, (40, 0));

        Assert.Equal(waypoints, fwd);
    }

    [Fact]
    public void BuildForwardPath_AppendsResolvedGoalOnlyAfterWaypoints()
    {
        var waypoints = new List<(int x, int y)> { (0, 0), (10, 0), (20, 0) };

        var fwd = NavigationPathBuilder.BuildForwardPath(new NumVec2(5, 5), waypoints, (25, 0));

        Assert.Equal([(0, 0), (10, 0), (20, 0), (25, 0)], fwd);
    }

    [Fact]
    public void BuildForwardPath_DoesNotCreateLiveGoalFallbackWhenNoWaypoints()
    {
        var fwd = NavigationPathBuilder.BuildForwardPath(new NumVec2(5, 5), [], (12, 18));

        Assert.Empty(fwd);
    }

    [Fact]
    public void BuildDrawPolyline_EmptyWaypointsPlusLiveGoalProducesNoStraightLine()
    {
        var poly = NavigationPathBuilder.BuildDrawPolyline(new NumVec2(5, 5), [], (12, 18));

        Assert.Empty(poly);
    }

    [Fact]
    public void HasDrawablePath_FalseWithOnlyLiveGoal()
        => Assert.False(NavigationPathBuilder.HasDrawablePath([], (1, 2), RoutePlanStatus.Planned));

    [Fact]
    public void HasDrawablePath_FalseWhenRouteIsNotPlanned()
        => Assert.False(NavigationPathBuilder.HasDrawablePath([(1, 2)], (3, 4), RoutePlanStatus.NoPath));

    [Fact]
    public void DecimateForWorldDisplay_UsesStrideAndKeepsLastPoint()
    {
        var points = new List<(int x, int y)>();
        for (var i = 0; i < 80; i++)
            points.Add((i, 0));

        var decimated = NavigationPathBuilder.DecimateForWorldDisplay(points, maxDots: 40);

        Assert.Equal(0, decimated[0].x);
        Assert.Equal(79, decimated[^1].x);
        Assert.True(decimated.Count <= 41);
    }

    [Fact]
    public void AppendResolvedGoal_AddsTerminalWhenMissing()
    {
        var withGoal = NavigationPathBuilder.AppendResolvedGoal([(0, 0), (10, 0)], (25, 0));
        Assert.Equal((25, 0), withGoal[^1]);
    }
}
