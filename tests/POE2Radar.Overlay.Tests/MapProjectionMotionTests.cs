using NumVec2 = System.Numerics.Vector2;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class MapProjectionMotionTests
{
    [Fact]
    public void LargeMapReference_SmoothingEnabled_UsesRawPlayerGridToPreserveIconAlignment()
    {
        var frame = Frame(isMinimap: false);
        var smoothed = new NumVec2(104.12f, 198.94f);
        var raw = new NumVec2(104.5f, 198.5f);

        var reference = MapProjectionMotion.PlayerReference(
            frame,
            smoothingEnabled: true,
            smoothed,
            raw);

        Assert.Equal(raw, reference);
    }

    [Fact]
    public void LargeMapReference_SmoothingDisabled_UsesRawPlayerGrid()
    {
        var frame = Frame(isMinimap: false);
        var smoothed = new NumVec2(104.12f, 198.94f);
        var raw = new NumVec2(104.5f, 198.5f);

        var reference = MapProjectionMotion.PlayerReference(
            frame,
            smoothingEnabled: false,
            smoothed,
            raw);

        Assert.Equal(raw, reference);
    }

    [Fact]
    public void MiniMapReference_UsesRawPlayerGridToPreserveCornerMapAlignment()
    {
        var smoothed = new NumVec2(103.75f, 199.25f);
        var raw = new NumVec2(104f, 199f);
        var frame = Frame(isMinimap: true);

        var reference = MapProjectionMotion.PlayerReference(
            frame,
            smoothingEnabled: true,
            smoothed,
            raw);

        Assert.Equal(raw, reference);
    }

    private static MapFrame Frame(bool isMinimap)
        => new(
            Center: new NumVec2(1720f, 720f),
            Scale: 1f,
            Width: isMinimap ? 362f : 3440f,
            Height: isMinimap ? 362f : 1440f,
            MapElement: isMinimap ? 0x2000 : 0x1000,
            PlayerTerrainHeight: 12f,
            Position: isMinimap ? new NumVec2(3070f, 9f) : NumVec2.Zero,
            IsMinimap: isMinimap);
}
