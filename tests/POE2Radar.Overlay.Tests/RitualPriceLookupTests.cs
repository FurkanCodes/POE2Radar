using POE2Radar.Overlay.Pricing;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class RitualPriceLookupTests
{
    [Fact]
    public void ArtKeyVariants_IncludesThePrefixAndStrip()
    {
        var variants = RitualPriceLookup.ArtKeyVariants("The Searing Touch").ToList();
        Assert.Contains("The Searing Touch", variants);
        Assert.Contains("Searing Touch", variants);
    }

    [Fact]
    public void NormalizeKey_StripsPunctuation()
    {
        Assert.Equal("riteofpassage", RitualPriceLookup.NormalizeKey("Rite of Passage"));
    }

    [Fact]
    public void IsGenericLookupName_RejectsCharm()
    {
        Assert.True(RitualPriceLookup.IsGenericLookupName("Charm"));
        Assert.False(RitualPriceLookup.IsGenericLookupName("Divine Orb"));
    }

    [Fact]
    public void BuildNameCandidates_PrefersScoutTextAndPathBasename()
    {
        var pathNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [RitualPriceLookup.NormalizeKey("goldenuniquecharm")] = "Rite of Passage",
        };
        var candidates = RitualPriceLookup.BuildNameCandidates(
            "Item 0", "goldenuniquecharm", "Metadata/Items/Charms/goldenuniquecharm", "Scout Label", pathNames);
        Assert.Contains("Scout Label", candidates);
        Assert.Contains("Rite of Passage", candidates);
    }

    [Fact]
    public void GetDisplayPrice_ConvertsChaosToExalted()
    {
        var (value, currency) = RitualPriceLookup.GetDisplayPrice(10, RitualPriceLookup.DisplayExalted, 12, 0.5);
        Assert.Equal("ex", currency);
        Assert.Equal(20, value);
    }

    [Fact]
    public void ScoreModMatch_ExactModScoresHigher()
    {
        var item = new[] { "+25 to maximum Life" };
        var listing = new[] { "+25 to maximum Life", "+10% Fire Resistance" };
        Assert.True(RitualPriceLookup.ScoreModMatch(item, listing) >= 3);
    }
}
