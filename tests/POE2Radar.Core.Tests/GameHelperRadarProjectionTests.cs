using System.Numerics;
using POE2Radar.Core.Pathfinding;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class GameHelperRadarProjectionTests
{
    [Fact]
    public void LargeMapCenter_MatchesPinnedGameHelperRadar()
    {
        var center = GameHelperRadarProjection.LargeMapCenter(
            mapCenter: new Vector2(1720f, 720f),
            shift: new Vector2(14f, 173f),
            defaultShift: new Vector2(0f, -20f),
            userOffset: new Vector2(3f, -4f));

        Assert.Equal(1737.6f, center.X, 3);
        Assert.Equal(869.3f, center.Y, 3);
    }

    [Fact]
    public void MiniMapCenter_ResolvedFrameIgnoresFullscreenPanAndDefaultShift()
    {
        var center = GameHelperRadarProjection.MiniMapCenter(
            position: new Vector2(3069.9f, 9f),
            size: new Vector2(361.8f, 361.8f),
            userXOffset: 0f);

        Assert.Equal(3245.8f, center.X, 3);
        Assert.Equal(189.9f, center.Y, 3);
    }

    [Fact]
    public void LargeMapScale_MatchesPinnedGameHelperDiagonalAndBaseline()
    {
        const float mapHeight = 1440f;
        const float zoom = 0.5f;
        const float multiplier = 1.1f;
        var baseDiagonal = MathF.Sqrt((2560f * 2560f) + (1600f * 1600f));
        var diagonal = baseDiagonal * mapHeight / 1600f;
        var expected = diagonal * (multiplier * zoom * 0.187812f) / 240f;

        var actual = GameHelperRadarProjection.LargeMapScale(mapHeight, zoom, multiplier);

        Assert.Equal(expected, actual, 5);
    }

    [Fact]
    public void MiniMapScale_MatchesPinnedGameHelperDiagonalAndBaseline()
    {
        const float mapHeight = 362f;
        const float zoom = 0.5f;
        const float multiplier = 1.2f;
        var baseDiagonal = MathF.Sqrt((2560f * 2560f) + (1600f * 1600f));
        var diagonal = baseDiagonal * mapHeight / 1600f;
        var expected = diagonal * (zoom * multiplier * 0.748f) / 240f;

        var actual = GameHelperRadarProjection.MiniMapScale(mapHeight, zoom, multiplier);

        Assert.Equal(expected, actual, 5);
    }
}
