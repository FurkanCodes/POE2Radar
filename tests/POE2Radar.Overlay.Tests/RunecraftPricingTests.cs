using POE2Radar.Overlay.Pricing;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class RunecraftPricingTests
{
    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 12)]
    public void PanelScanHz_IsBoundedForClosedAndOpenPanels(bool wasOpen, int expected)
        => Assert.Equal(expected, RadarApp.RunecraftPanelScanHz(wasOpen));

    [Theory]
    [InlineData(true, 6, 3, 1, true)]
    [InlineData(true, 6, 3, 4, true)]
    [InlineData(true, 6, 3, 5, false)]
    [InlineData(false, 6, 3, 1, false)]
    [InlineData(true, 0, 0, 1, false)]
    public void ShortPanelReadMisses_PreservePopulatedControllerSessions(
        bool wasOpen,
        int priorRows,
        int labels,
        int missStreak,
        bool expected)
        => Assert.Equal(expected, RadarApp.ShouldHoldRunecraftReadMiss(wasOpen, priorRows, labels, missStreak));

    [Theory]
    [InlineData("6x Armourer's Scrap", 6, "Armourer's Scrap")]
    [InlineData("Деталь доспеха (6)", 6, "Деталь доспеха")]
    [InlineData("Orb of Alchemy", 1, "Orb of Alchemy")]
    public void ParseNameAndCount_HandlesLeadingAndTrailingCounts(string raw, int count, string name)
    {
        RunecraftPriceMath.ParseNameAndCount(raw, out var parsedCount, out var parsedName);
        Assert.Equal(count, parsedCount);
        Assert.Equal(name, parsedName);
    }

    [Fact]
    public void ArtIdFromDdsPath_StripsDirectoryAndExtension()
    {
        Assert.Equal("CurrencyUpgradeToRare", RunecraftPriceMath.ArtIdFromDdsPath("Art/2DItems/Currency/CurrencyUpgradeToRare.dds"));
    }

    [Fact]
    public void LastMetaSegment_ReturnsFinalPathSegment()
    {
        Assert.Equal("CurrencyUpgradeMagicToRare2", RunecraftPriceMath.LastMetaSegment("Metadata/Items/Currency/CurrencyUpgradeMagicToRare2"));
    }

    [Theory]
    [InlineData("CurrencySetKalguuranSkillGemLevel9", 9)]
    [InlineData("CurrencyUpgradeMagicToRare2", -1)]
    [InlineData("SkillGemUncut19", -1)]
    public void LevelFromMetaId_ParsesLevelMarkerOnly(string metaId, int level)
    {
        Assert.Equal(level, RunecraftPriceMath.LevelFromMetaId(metaId));
    }

    [Theory]
    [InlineData("SkillGemUncut19", 19)]
    [InlineData("SkillGemUncutQuest", -1)]
    public void UncutGemLevel_ParsesTrailingDigits(string metaId, int level)
    {
        Assert.Equal(level, RunecraftPriceMath.UncutGemLevel(metaId));
    }

    [Fact]
    public void FormatExalted_KeepsAtLeastOneDecimalForSmallValues()
    {
        var text = RunecraftPriceMath.FormatExalted(1.0);
        Assert.Contains("ex", text);
        Assert.Contains(".", text);
    }

    [Fact]
    public void PickColor_AbsoluteMode_UsesThresholds()
    {
        Assert.Equal(0xFF55FF55u, RunecraftPriceMath.PickColor(6, 0, RunecraftColorMode.Absolute));
        Assert.Equal(0xFF4040FFu, RunecraftPriceMath.PickColor(0.2, 0, RunecraftColorMode.Absolute));
    }
}
