using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class SekhemaClassificationTests
{
    [Theory]
    [InlineData("Caverns_Arena_02", "Hourglass")]
    [InlineData("Ruins_Lair_01", "Chalice")]
    [InlineData("Depths_Explore_03", "Escape")]
    [InlineData("Caverns_Gauntlet_02", "Gauntlet")]
    public void RoomTypeMapping_MatchesUpstreamDataRowAliases(string id, string expected)
        => Assert.Equal(expected, Poe2Live.ExtractSekhemaRoomType(id));

    [Theory]
    [InlineData("Caverns_TreasureKeyGold", "Gold Key")]
    [InlineData("Ruins_TreasureChestSilver", "Silver Cache")]
    [InlineData("Depths_TreasureWaterMajor", "Large Fountain")]
    [InlineData("Caverns_TreasureMerchant_01", "Merchant")]
    [InlineData("Ruins_TreasureLegendHonor", "Honour")]
    [InlineData("Depths_TreasureLegendBoon", "Boon")]
    public void RewardMapping_MatchesUpstreamTreasureIds(string id, string expected)
        => Assert.Equal(expected, Poe2Live.MapSekhemaReward(id));
}
