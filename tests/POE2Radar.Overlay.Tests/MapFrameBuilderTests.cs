using POE2Radar.Overlay;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class MapFrameBuilderTests
{
    [Fact]
    public void LargeMapProjection_MatchesSikakaCenterAndScale()
    {
        const int w = 1920;
        const int h = 1080;
        var (center, scale) = MapFrameBuilder.LargeMapProjection(w, h, 14f, 173f, 0.5f, 0f, 0f, 1f);

        Assert.Equal(w * 0.5f + 14f, center.X, 3);
        Assert.Equal(h * 0.5f + 173f - 20f, center.Y, 3);
        Assert.Equal(0.5f * (h / MapFrameBuilder.MapScaleDivisor), scale, 4);
    }

    [Fact]
    public void MiniMapProjection_UsesClipCenterNotWindowCenter()
    {
        var (center, scale) = MapFrameBuilder.MiniMapProjection(
            1920, 1080,
            shiftX: 1f, shiftY: 2f, zoom: 0.4f,
            scaleMul: 1f,
            clipLeft: 1500f, clipTop: 40f, clipRight: 1860f, clipBottom: 400f);

        Assert.Equal(1680f, center.X, 2);
        Assert.Equal(220f, center.Y, 2);
        Assert.Equal(0.4f * (360f / MapFrameBuilder.MapScaleDivisor), scale, 4);
    }
}
