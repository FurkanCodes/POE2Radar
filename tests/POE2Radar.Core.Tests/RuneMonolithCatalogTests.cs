using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class RuneMonolithCatalogTests
{
    [Fact]
    public void Catalog_loads_from_embedded_json()
    {
        var cat = RuneMonolithCatalog.Instance;
        Assert.True(cat.IsLoaded);
    }

    [Fact]
    public void Offers_filters_by_anchor_and_holes()
    {
        var cat = RuneMonolithCatalog.Instance;
        if (!cat.IsLoaded) return;
        var offers = cat.Offers(anchorIdx: 0, anchorPos: 0, holeCount: 3, isUnique: false, areaLevel: 80);
        Assert.All(offers, o => Assert.True(o.Size <= 3));
        Assert.All(offers, o => Assert.False(string.IsNullOrWhiteSpace(o.Name) && string.IsNullOrWhiteSpace(o.Description)));
    }

    [Fact]
    public void Offers_unique_branch_includes_anchorless()
    {
        var cat = RuneMonolithCatalog.Instance;
        if (!cat.IsLoaded) return;
        var offers = cat.Offers(-1, -1, 2, isUnique: true, areaLevel: 80);
        Assert.NotEmpty(offers);
    }
}
