using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class LootTrackerGoldTests
{
    [Theory]
    [InlineData("125 Gold", 125)]
    [InlineData("1,250 Gold", 1250)]
    [InlineData("Gold x345", 345)]
    [InlineData("Gold 9 876", 9876)]
    public void TryParseGoldLabel_ReadsVisibleLootLabelAmount(string text, long expected)
    {
        Assert.True(RadarApp.TryParseGoldLabel(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("Orb of Alchemy")]
    [InlineData("Golden Charm")]
    [InlineData("Goldrim")]
    public void TryParseGoldLabel_RejectsNonGoldLoot(string text)
        => Assert.False(RadarApp.TryParseGoldLabel(text, out _));
}
