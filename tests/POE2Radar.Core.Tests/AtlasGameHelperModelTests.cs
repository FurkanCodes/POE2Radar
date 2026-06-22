using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class AtlasGameHelperModelTests
{
    [Theory]
    [InlineData(0x00, false, false)]
    [InlineData(0x01, true, false)]
    [InlineData(0x02, false, true)]
    [InlineData(0x03, true, true)]
    public void FlagsFromStatus_DecodesAccessibleAndCompletedBits(byte status, bool accessible, bool completed)
    {
        var flags = Poe2Atlas.AtlasNodeState.FlagsFromStatus(status);
        Assert.Equal(accessible, (flags & 0x01) != 0);
        Assert.Equal(completed, (flags & 0x02) != 0);
    }

    [Fact]
    public void MultiSourceShortestPath_PicksNearestAccessibleFrontier()
    {
        var graph = new Dictionary<(int X, int Y), List<(int X, int Y)>>
        {
            [(0, 0)] = [(1, 0)],
            [(1, 0)] = [(0, 0), (2, 0)],
            [(2, 0)] = [(1, 0), (3, 0)],
            [(3, 0)] = [(2, 0), (4, 0)],
            [(4, 0)] = [(3, 0)],
        };

        var path = Poe2Atlas.MultiSourceShortestPath(graph, [(0, 0), (3, 0)], (4, 0));

        Assert.NotNull(path);
        Assert.Equal([(3, 0), (4, 0)], path);
    }

    [Fact]
    public void MultiSourceShortestPath_ReturnsNullWhenNoFrontierIsInGraph()
    {
        var graph = new Dictionary<(int X, int Y), List<(int X, int Y)>>
        {
            [(0, 0)] = [(1, 0)],
            [(1, 0)] = [(0, 0)],
        };

        Assert.Null(Poe2Atlas.MultiSourceShortestPath(graph, [(9, 9)], (1, 0)));
    }
}
