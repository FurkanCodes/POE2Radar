using POE2Radar.Overlay.Navigation;
using NumVec2 = System.Numerics.Vector2;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class AtlasRoutePolylineBuilderTests
{
    [Fact]
    public void IsDrawableEdge_RequiresAtLeastOneEndpointOnScreen()
    {
        Assert.False(AtlasRoutePolylineBuilder.IsDrawableEdge(
            new(-200, -200), new(-100, -100), 100, 100, margin: 0));
        Assert.True(AtlasRoutePolylineBuilder.IsDrawableEdge(
            new(-100, -100), new(50, 50), 100, 100, margin: 0));
        Assert.True(AtlasRoutePolylineBuilder.IsDrawableEdge(
            new(50, 50), new(150, 50), 100, 100, margin: 0));
        Assert.False(AtlasRoutePolylineBuilder.IsDrawableEdge(
            new(150, 50), new(250, 50), 100, 100, margin: 0));
    }

    [Fact]
    public void BuildSegments_ReturnsEmptyForMissingPath()
    {
        var segments = AtlasRoutePolylineBuilder.BuildSegments(null, new Dictionary<(int, int), NumVec2>(), "Target", "#ffffff", 3);

        Assert.Empty(segments);
    }

    [Fact]
    public void BuildSegments_SplitsAtMissingCentersInsteadOfDrawingLongChord()
    {
        var path = new List<(int X, int Y)> { (0, 0), (1, 0), (2, 0), (3, 0), (4, 0) };
        var centers = new Dictionary<(int, int), NumVec2>
        {
            [(0, 0)] = new(0, 0),
            [(1, 0)] = new(10, 0),
            [(3, 0)] = new(30, 0),
            [(4, 0)] = new(40, 0),
        };

        var segments = AtlasRoutePolylineBuilder.BuildSegments(path, centers, "Target", "#ff00ff", 4);

        Assert.Equal(2, segments.Count);
        Assert.Equal(new NumVec2(0, 0), segments[0].Points[0]);
        Assert.Equal(new NumVec2(10, 0), segments[0].Points[^1]);
        Assert.Equal("", segments[0].Label);
        Assert.Equal(0, segments[0].Hops);
        Assert.Equal(new NumVec2(30, 0), segments[1].Points[0]);
        Assert.Equal(new NumVec2(40, 0), segments[1].Points[^1]);
        Assert.Equal("Target", segments[1].Label);
        Assert.Equal(4, segments[1].Hops);
    }

    [Fact]
    public void BuildSegments_DiscardsSinglePointFragments()
    {
        var path = new List<(int X, int Y)> { (0, 0), (1, 0), (2, 0) };
        var centers = new Dictionary<(int, int), NumVec2>
        {
            [(0, 0)] = new(0, 0),
            [(2, 0)] = new(20, 0),
        };

        var segments = AtlasRoutePolylineBuilder.BuildSegments(path, centers, "Target", "#ff00ff", 2);

        Assert.Empty(segments);
    }
}
