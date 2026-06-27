using POE2Radar.Overlay.Pricing;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class LootValueLogicTests
{
    [Theory]
    [InlineData("UniqueWeapons", "Uniques")]
    [InlineData("UniqueArmours", "Uniques")]
    [InlineData("Currency", "Currency")]
    [InlineData("PrecursorTablets", "Tablets")]
    [InlineData("LineageSupportGems", "Gems")]
    [InlineData("SomethingElse", "Other")]
    public void CategoryGroup_maps_ninja_types(string input, string expected)
        => Assert.Equal(expected, LootValueLogic.CategoryGroup(input));

    [Theory]
    [InlineData("Uniques", 5, 1, 1, 5)]
    [InlineData("Currency", 5, 1, 1, 1)]
    [InlineData("Runes", 5, 1, 1, 1)]
    public void GroundFloor_uses_bucket(string group, double u, double c, double o, double expected)
        => Assert.Equal(expected, LootValueLogic.GroundFloor(group, u, c, o));

    [Theory]
    [InlineData("5x Chaos Orb", "Chaos Orb")]
    [InlineData("Chaos Orb", "Chaos Orb")]
    [InlineData("  12X Divine Orb ", "Divine Orb")]
    public void StripCount_removes_stack_prefix(string raw, string expected)
        => Assert.Equal(expected, LootValueLogic.StripCount(raw));

    [Theory]
    [InlineData("3x Exalted Orb", "Exalted Orb")]
    [InlineData("Greater Orb of Augmentation", "Greater Orb of Augmentation")]
    public void NameLookupCandidates_includes_stripped(string input, string expected)
    {
        Assert.Contains(expected, LootValueLogic.NameLookupCandidates(input).ToList());
    }

    [Fact]
    public void NameLookupCandidates_strips_level_suffix()
    {
        var keys = LootValueLogic.NameLookupCandidates("Uncut Spirit Gem (Level 19)").ToList();
        Assert.Contains("Uncut Spirit Gem", keys);
    }

}
