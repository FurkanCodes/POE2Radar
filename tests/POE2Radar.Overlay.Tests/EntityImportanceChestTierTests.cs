using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class EntityImportanceChestTierTests
{
    [Fact]
    public void Classify_plain_chest_metadata_is_chest_tier_not_other()
    {
        var e = new Poe2Live.EntityDot(
            Id: 1,
            Address: 0,
            Grid: default,
            World: default,
            TerrainHeight: 0f,
            Category: Poe2Live.EntityCategory.Object,
            Metadata: "Metadata/Chests/Leagues/Petrosphere/PetrosphereCluster03A",
            HpCur: 0,
            HpMax: 0,
            Poi: false,
            Reaction: 0,
            Rarity: Poe2Live.Rarity.Rare,
            Opened: false);

        var tier = EntityImportanceHelper.Classify(e, new RadarStyles());
        Assert.Equal(EntityImportance.Chest, tier);
    }

    [Fact]
    public void Classify_strongbox_stays_chest_tier()
    {
        var e = new Poe2Live.EntityDot(
            Id: 2,
            Address: 0,
            Grid: default,
            World: default,
            TerrainHeight: 0f,
            Category: Poe2Live.EntityCategory.Chest,
            Metadata: "Metadata/Chests/StrongBoxes/SomeBox",
            HpCur: 0,
            HpMax: 0,
            Poi: false,
            Reaction: 0,
            Rarity: Poe2Live.Rarity.Rare,
            Opened: false);

        var tier = EntityImportanceHelper.Classify(e, new RadarStyles());
        Assert.Equal(EntityImportance.Chest, tier);
    }
}
