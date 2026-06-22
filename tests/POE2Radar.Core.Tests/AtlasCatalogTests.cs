using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class AtlasCatalogTests
{
    [Fact]
    public void Shared_LoadsEmbeddedMapNames()
    {
        var name = AtlasCatalog.Shared.MapName("MapRavine");
        Assert.Equal("Ravine", name);
    }

    [Fact]
    public void Shared_ClassifiesCitadelAsArbiter()
    {
        var map = AtlasCatalog.Shared.Maps.FirstOrDefault(m => m.Name.Contains("Citadel"));
        Assert.Contains("arbiter", map.Tags);
    }

    [Fact]
    public void Shared_ExposesDefaultTargetRoutes()
    {
        Assert.Contains(AtlasCatalog.Shared.DefaultRouteTargets, t => t.Name.Contains("Patriarch Halls"));
        Assert.Contains(AtlasCatalog.Shared.DefaultRouteTargets, t => t.Match == "type:unique");
    }

    [Fact]
    public void ContentInfoFor_MatchesKnownMechanic()
    {
        var info = AtlasCatalog.Shared.ContentInfoFor("map_atlas_node_has_breach");
        Assert.NotNull(info);
        Assert.Equal("Br", info?.ShortLabel);
    }
}
