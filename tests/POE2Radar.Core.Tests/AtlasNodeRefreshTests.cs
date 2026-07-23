using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class AtlasNodeRefreshTests
{
    [Theory]
    [InlineData(2288, 2288, false, false)]
    [InlineData(2288, 2288, true, true)]
    [InlineData(2288, 2287, false, true)]
    public void PositionRefresh_ForcesFullReadWhenCanvasNodeSetChanged(
        int snapshotCount,
        int knownNodesSeen,
        bool sawUnknownValidNode,
        bool expected)
    {
        var actual = Poe2Atlas.NeedsFullNodeRefresh(snapshotCount, knownNodesSeen, sawUnknownValidNode);

        Assert.Equal(expected, actual);
    }
}
