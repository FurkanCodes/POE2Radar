using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class ChestDisplayPolicyTests
{
    [Theory]
    [InlineData("Chest · Rare")]
    [InlineData("Chest · Unique")]
    [InlineData("ServerIcon · Chest")]
    public void IsPlainChestRule_MatchesGenericChests(string name)
        => Assert.True(ChestDisplayPolicy.IsPlainChestRule(name));

    [Theory]
    [InlineData("Strongbox")]
    [InlineData("Strongbox · Unique")]
    [InlineData("ServerIcon · Strongbox")]
    public void IsPlainChestRule_RejectsStrongboxes(string name)
        => Assert.False(ChestDisplayPolicy.IsPlainChestRule(name));

    [Fact]
    public void ApplyIconOnlyDefaults_OnlyAffectsPlainChests()
    {
        var chest = new DisplayRule { Name = "Chest · Rare", Label = "Chest · Rare", Navigable = true };
        var box = new DisplayRule { Name = "Strongbox · Jeweller", Label = "Strongbox · Jeweller", Navigable = true };

        Assert.True(ChestDisplayPolicy.ApplyIconOnlyDefaults(chest));
        Assert.False(chest.Navigable);
        Assert.Null(chest.Label);

        Assert.False(ChestDisplayPolicy.ApplyIconOnlyDefaults(box));
        Assert.True(box.Navigable);
        Assert.Equal("Strongbox · Jeweller", box.Label);
    }

    [Fact]
    public void IsPlainChestEntity_MatchesMetadataChestsPath()
    {
        var e = new Poe2Live.EntityDot(
            Id: 1,
            Address: 0,
            Grid: default,
            World: default,
            TerrainHeight: 0f,
            Category: Poe2Live.EntityCategory.Object,
            Metadata: "Metadata/Chests/Leagues/Petrosphere/PetrosphereCluster01A",
            HpCur: 0,
            HpMax: 0,
            Poi: false,
            Reaction: 0,
            Rarity: Poe2Live.Rarity.Normal,
            Opened: false);

        Assert.True(ChestDisplayPolicy.IsPlainChestEntity(e));
    }

    [Fact]
    public void IsPlainChestEntity_RejectsStrongboxMetadata()
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

        Assert.False(ChestDisplayPolicy.IsPlainChestEntity(e));
    }

    [Fact]
    public void ApplyStrongboxDefaults_RestoresNavAndLabel()
    {
        var rule = new DisplayRule { Name = "Strongbox", Navigable = false, Label = null };

        Assert.True(ChestDisplayPolicy.ApplyStrongboxDefaults(rule));
        Assert.True(rule.Navigable);
        Assert.Equal("Strongbox", rule.Label);
    }
}
