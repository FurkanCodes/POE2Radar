using NumVec2 = System.Numerics.Vector2;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class MapProjectionMotionTests
{
    [Fact]
    public void PlayerReference_SmoothingEnabled_PreservesRawGameAlignment()
    {
        var smoothed = new NumVec2(101.25f, 202.5f);
        var raw = new NumVec2(104f, 199f);

        var player = MapProjectionMotion.PlayerReference(
            smoothingEnabled: true,
            smoothed,
            raw);

        Assert.Equal(raw, player);
    }

    [Fact]
    public void PlayerReference_SmoothingDisabled_UsesRawGrid()
    {
        var smoothed = new NumVec2(101.25f, 202.5f);
        var raw = new NumVec2(104f, 199f);

        var player = MapProjectionMotion.PlayerReference(
            smoothingEnabled: false,
            smoothed,
            raw);

        Assert.Equal(raw, player);
    }
}
