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
    public void Shared_LoadsGameHelperMapMetadata()
    {
        var map = AtlasCatalog.Shared.Map("MapAzmerianRanges");

        Assert.NotNull(map);
        Assert.Equal("map", map?.Group);
        Assert.Contains("craft", map?.Tags ?? []);
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
        Assert.Equal("Br", info?.Abbrev);
    }

    [Fact]
    public void Shared_ExposesGameHelperMapContentCatalog()
    {
        var beast = AtlasCatalog.Shared.MapContents.FirstOrDefault(c => c.Name == "Great Beast");

        Assert.Equal("Great Beast", beast.Name);
        Assert.Contains("Hilda", beast.Description);
        Assert.NotNull(AtlasCatalog.Shared.MapContentInfoFor("Great Beast"));
    }

    [Fact]
    public void MapContentNameForBadgeId_ResolvesGreatBeast()
    {
        Assert.Equal("Great Beast", AtlasCatalog.Shared.MapContentNameForBadgeId(0x0002006F));
    }
}
