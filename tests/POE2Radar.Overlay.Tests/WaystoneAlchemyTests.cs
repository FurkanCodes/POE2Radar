using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class WaystoneAlchemyTests
{
    [Fact]
    public void Defaults_AreManualAndInputDisabled()
    {
        var settings = new WaystoneAlchemySettings();
        Assert.False(settings.Enabled);
        Assert.Equal(0, settings.Mode);
        Assert.False(settings.AutoModeAcknowledged);
    }

    [Theory]
    [InlineData(Poe2Live.Rarity.Normal, true, 0, "ALCHEMY", "CurrencyUpgradeToRare")]
    [InlineData(Poe2Live.Rarity.Magic, false, 2, "IDENTIFY", "CurrencyIdentification")]
    [InlineData(Poe2Live.Rarity.Magic, true, 2, "REGAL", "CurrencyUpgradeMagicToRare")]
    [InlineData(Poe2Live.Rarity.Rare, false, 3, "IDENTIFY", "CurrencyIdentification")]
    [InlineData(Poe2Live.Rarity.Rare, true, 4, "EXALTED", "CurrencyAddModToRare")]
    public void UpgradeRecipe_SelectsExpectedNextCurrency(
        Poe2Live.Rarity rarity,
        bool identified,
        int explicitMods,
        string action,
        string currency)
    {
        var result = RadarApp.DetermineAlchemyAction(
            Waystone(rarity, identified, corrupted: false, explicitMods),
            new WaystoneAlchemySettings { Recipe = 0, DesiredExplicitMods = 6 });

        Assert.NotNull(result);
        Assert.Equal(action, result.Value.Name);
        Assert.Equal(currency, result.Value.CurrencyToken);
    }

    [Fact]
    public void UpgradeRecipe_StopsAtDesiredExplicitMods()
    {
        var result = RadarApp.DetermineAlchemyAction(
            Waystone(Poe2Live.Rarity.Rare, true, false, 5),
            new WaystoneAlchemySettings { Recipe = 0, DesiredExplicitMods = 5 });
        Assert.Null(result);
    }

    [Fact]
    public void CorruptionRecipe_RejectsAlreadyCorruptedWaystone()
    {
        var settings = new WaystoneAlchemySettings { Recipe = 1 };
        Assert.Equal("CORRUPT", RadarApp.DetermineAlchemyAction(
            Waystone(Poe2Live.Rarity.Rare, true, false, 6), settings)?.Name);
        Assert.Null(RadarApp.DetermineAlchemyAction(
            Waystone(Poe2Live.Rarity.Rare, true, true, 6), settings));
    }

    [Fact]
    public void MinimumTier_GatesEveryRecipe()
    {
        var result = RadarApp.DetermineAlchemyAction(
            Waystone(Poe2Live.Rarity.Normal, true, false, 0, tier: 5),
            new WaystoneAlchemySettings { Recipe = 0, MinimumTier = 10 });
        Assert.Null(result);
    }

    private static Poe2Live.StashValueSlot Waystone(
        Poe2Live.Rarity rarity,
        bool identified,
        bool corrupted,
        int explicitMods,
        int tier = 15)
    {
        var mods = Enumerable.Range(0, explicitMods)
            .Select(i => new Poe2Live.StashItemMod($"Mod{i}", i, float.NaN, true))
            .ToArray();
        return new Poe2Live.StashValueSlot(
            0x1000,
            0x2000,
            new Poe2Live.UiRect(100, 100, 52, 52),
            Poe2Live.StashValuePanel.Inventory,
            false,
            $"Metadata/Items/Maps/Waystone{tier}",
            $"Waystone{tier}",
            $"Waystone Tier {tier}",
            rarity,
            "",
            1,
            identified,
            corrupted,
            mods.Select(m => m.Id).ToArray(),
            mods,
            default);
    }
}
