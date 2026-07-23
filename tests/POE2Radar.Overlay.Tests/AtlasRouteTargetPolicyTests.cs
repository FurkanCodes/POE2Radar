using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class AtlasRouteTargetPolicyTests
{
    [Fact]
    public void ActiveSearch_GatesUnrelatedCategoryRoutesLikeAtlas2()
    {
        var search = AtlasSearch.Parse("Relinquary");

        Assert.True(RadarApp.AtlasCategoryTargetAllowed(
            search, "Relinquary", null, Array.Empty<string>(), Array.Empty<string>()));
        Assert.False(RadarApp.AtlasCategoryTargetAllowed(
            search, "Savannah", null, Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void EmptySearch_DoesNotGateCategoryRoutes()
    {
        var search = AtlasSearch.Parse("");

        Assert.True(RadarApp.AtlasCategoryTargetAllowed(
            search, "Savannah", null, Array.Empty<string>(), Array.Empty<string>()));
    }
}
