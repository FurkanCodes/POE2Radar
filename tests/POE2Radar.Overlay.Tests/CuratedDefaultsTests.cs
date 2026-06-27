using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class CuratedDefaultsTests
{
    [Fact]
    public void IsVisible_BreachMechanic_True()
    {
        var e = SampleEntity(Poe2Live.EntityCategory.Object, "Metadata/Leagues/Breach/BreachObject", Poe2Live.Rarity.NonMonster);
        var rule = new DisplayRule { Name = "Breach", Hide = false };
        Assert.True(CuratedDefaults.IsVisibleWhenImportantOnly(e, rule, new RadarStyles()));
    }

    [Fact]
    public void IsVisible_GenericRareChest_False()
    {
        var e = SampleEntity(Poe2Live.EntityCategory.Chest, "Metadata/Chests/Chest", Poe2Live.Rarity.Rare);
        var rule = new DisplayRule { Name = "Chest · Rare", Hide = false };
        Assert.False(CuratedDefaults.IsVisibleWhenImportantOnly(e, rule, new RadarStyles()));
    }

    [Fact]
    public void IsVisible_StrongboxSubtype_True()
    {
        var e = SampleEntity(Poe2Live.EntityCategory.Chest, "Metadata/Chests/StrongBoxes/ArmoryStrongbox", Poe2Live.Rarity.NonMonster);
        var rule = new DisplayRule { Name = "Strongbox · Armourer", Hide = false };
        Assert.True(CuratedDefaults.IsVisibleWhenImportantOnly(e, rule, new RadarStyles()));
    }

    [Fact]
    public void IsVisible_Player_AlwaysTrue()
    {
        var e = SampleEntity(Poe2Live.EntityCategory.Player, "Metadata/Characters/Player", Poe2Live.Rarity.NonMonster);
        Assert.True(CuratedDefaults.IsVisibleWhenImportantOnly(e, null, new RadarStyles()));
    }

    [Fact]
    public void IsVisible_Waypoint_False()
    {
        var e = SampleEntity(Poe2Live.EntityCategory.Object, "Metadata/MiscellaneousObjects/Waypoint", Poe2Live.Rarity.NonMonster);
        var rule = new DisplayRule { Name = "Waypoint", Hide = false };
        Assert.False(CuratedDefaults.IsVisibleWhenImportantOnly(e, rule, new RadarStyles()));
    }

    [Fact]
    public void MigrateDisplayRules_SetsNavigableFlags()
    {
        var rules = new List<DisplayRule>
        {
            new() { Name = "Breach", Navigable = false },
            new() { Name = "Waypoint", Navigable = true },
            new() { Name = "Boss", Navigable = false },
            new() { Name = "Monster · Normal", Navigable = true },
        };

        Assert.True(CuratedDefaults.MigrateDisplayRules(rules));
        Assert.True(rules[0].Navigable);
        Assert.False(rules[1].Navigable);
        Assert.True(rules[2].Navigable);
        Assert.False(rules[3].Navigable);
    }

    [Fact]
    public void DisplayRuleEngine_ImportantOnly_HidesGenericChest()
    {
        var global = new DisplayRules(Path.Combine(Path.GetTempPath(), $"poe2radar-cd-{Guid.NewGuid():N}.json"));
        global.Replace(DisplayRules.BuildDefault(new RadarStyles(), showMonsters: true, watched: []));
        var zones = new ZoneEntityOverrides(Path.Combine(Path.GetTempPath(), $"poe2radar-cdz-{Guid.NewGuid():N}.json"));
        var engine = new DisplayRuleEngine(global, zones, () => new RadarStyles());

        var chest = SampleEntity(Poe2Live.EntityCategory.Chest, "Metadata/Chests/Chest", Poe2Live.Rarity.Rare);
        var hidden = engine.Resolve(chest, "Map", importantOnly: true);
        Assert.NotNull(hidden);
        Assert.True(hidden!.Hide);

        var shown = engine.Resolve(chest, "Map", importantOnly: false);
        Assert.NotNull(shown);
        Assert.False(shown!.Hide);
    }

    private static Poe2Live.EntityDot SampleEntity(
        Poe2Live.EntityCategory cat, string metadata, Poe2Live.Rarity rarity)
        => new(
            Id: 1, Address: 0, Grid: default, World: default, TerrainHeight: 0f,
            Category: cat, Metadata: metadata,
            HpCur: 0, HpMax: 0, Poi: false, Reaction: 0, Rarity: rarity,
            Opened: false, IconComplete: false, IsSleeping: false);
}
