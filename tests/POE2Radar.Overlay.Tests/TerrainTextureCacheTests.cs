using POE2Radar.Overlay;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class TerrainTextureCacheTests
{
    [Fact]
    public void SekhemaTerrain_IsScaledWithinSafeGpuTextureBudget()
    {
        var dimensions = TerrainTextureCache.GetTextureDimensions(4486, 11546);

        Assert.True(dimensions.Width <= 4096);
        Assert.True(dimensions.Height <= 4096);
        Assert.True(dimensions.Width > 0);
        Assert.True(dimensions.Height > 0);
        Assert.InRange(
            dimensions.Width / (double)dimensions.Height,
            4486d / 11546d - 0.001,
            4486d / 11546d + 0.001);
    }
}
