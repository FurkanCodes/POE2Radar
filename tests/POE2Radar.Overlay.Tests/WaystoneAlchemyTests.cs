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
        Assert.Equal(0, settings.TargetType);
        Assert.Equal(2, settings.DesiredTabletExplicitMods);
        Assert.False(settings.AutoModeAcknowledged);
        Assert.False(settings.TabletAlchemyUnlocked);
        Assert.Equal(0, settings.RunHotkey);
    }

    [Theory]
    [InlineData(Poe2Live.Rarity.Normal, true, 0, "ALCHEMY", "CurrencyUpgradeToRare")]
    [InlineData(Poe2Live.Rarity.Magic, false, 2, "IDENTIFY", "CurrencyIdentification")]
    [InlineData(Poe2Live.Rarity.Magic, true, 2, "ALCHEMY", "CurrencyUpgradeToRare")]
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
            new WaystoneAlchemySettings { Recipe = 0, DesiredExplicitMods = 6, UseRegalOnMagic = false });

        Assert.NotNull(result);
        Assert.Equal(action, result.Value.Name);
        Assert.Equal(currency, result.Value.CurrencyToken);
    }

    [Fact]
    public void UpgradeRecipe_RegalOnMagic_OnlyWhenPreferRegalEnabled()
    {
        var waystone = Waystone(Poe2Live.Rarity.Magic, identified: true, corrupted: false, explicitMods: 2);
        Assert.Equal("REGAL", RadarApp.DetermineAlchemyAction(
            waystone,
            new WaystoneAlchemySettings { Recipe = 0, UseRegalOnMagic = true })?.Name);
        Assert.Equal("ALCHEMY", RadarApp.DetermineAlchemyAction(
            waystone,
            new WaystoneAlchemySettings { Recipe = 0, UseRegalOnMagic = false })?.Name);
    }

    [Fact]
    public void CurrencyMatch_AlchemyDoesNotMatchChanceOrBindingOrb()
    {
        Assert.True(RadarApp.MatchesAlchemyCurrencyToken(
            Currency("Metadata/Items/Currency/CurrencyUpgradeToRare", "CurrencyUpgradeToRare", "Orb of Alchemy"),
            "CurrencyUpgradeToRare"));
        Assert.True(RadarApp.MatchesAlchemyCurrencyToken(
            Currency("Metadata/Items/Currency/CurrencyUpgradeToRare2", "CurrencyUpgradeToRare2", "Perfect Orb of Alchemy"),
            "CurrencyUpgradeToRare"));

        Assert.False(RadarApp.MatchesAlchemyCurrencyToken(
            Currency("Metadata/Items/Currency/CurrencyUpgradeRandomly", "CurrencyUpgradeRandomly", "Orb of Chance"),
            "CurrencyUpgradeToRare"));
        Assert.False(RadarApp.MatchesAlchemyCurrencyToken(
            Currency(
                "Metadata/Items/Currency/CurrencyUpgradeToRareAndSetSockets",
                "CurrencyUpgradeToRareAndSetSockets",
                "Orb of Binding"),
            "CurrencyUpgradeToRare"));
    }

    private static Poe2Live.StashValueSlot Currency(string path, string internalName, string baseName)
        => new(
            0x5000,
            0x5001,
            new Poe2Live.UiRect(10, 10, 40, 40),
            Poe2Live.StashValuePanel.Inventory,
            false,
            path,
            internalName,
            baseName,
            Poe2Live.Rarity.Normal,
            "",
            10,
            true,
            false,
            [],
            [],
            default);

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

    [Theory]
    [InlineData(Poe2Live.Rarity.Normal, true, 0, 2, "TRANSMUTE", "CurrencyUpgradeToMagic")]
    [InlineData(Poe2Live.Rarity.Magic, true, 1, 2, "AUGMENT", "CurrencyAddModToMagic")]
    [InlineData(Poe2Live.Rarity.Magic, true, 2, 3, "REGAL", "CurrencyUpgradeMagicToRare")]
    [InlineData(Poe2Live.Rarity.Rare, true, 3, 4, "EXALTED", "CurrencyAddModToRare")]
    public void TabletUpgrade_UsesTabletCraftingSequence(
        Poe2Live.Rarity rarity,
        bool identified,
        int explicitMods,
        int desiredMods,
        string action,
        string currency)
    {
        var result = RadarApp.DetermineAlchemyAction(
            Tablet(rarity, identified, corrupted: false, explicitMods),
            new WaystoneAlchemySettings
            {
                TargetType = 1,
                Recipe = 0,
                DesiredTabletExplicitMods = desiredMods,
            });

        Assert.NotNull(result);
        Assert.Equal(action, result.Value.Name);
        Assert.Equal(currency, result.Value.CurrencyToken);
    }

    [Theory]
    [InlineData(Poe2Live.Rarity.Magic, 2, 2)]
    [InlineData(Poe2Live.Rarity.Rare, 3, 3)]
    [InlineData(Poe2Live.Rarity.Rare, 4, 4)]
    public void TabletUpgrade_StopsAtConfiguredUnlockLimit(
        Poe2Live.Rarity rarity,
        int explicitMods,
        int desiredMods)
    {
        var result = RadarApp.DetermineAlchemyAction(
            Tablet(rarity, identified: true, corrupted: false, explicitMods),
            new WaystoneAlchemySettings
            {
                TargetType = 1,
                Recipe = 0,
                DesiredTabletExplicitMods = desiredMods,
            });

        Assert.Null(result);
    }

    [Fact]
    public void TabletCorruption_UsesAncientInfuserAndRejectsUnsafeTargets()
    {
        var settings = new WaystoneAlchemySettings
        {
            TargetType = 1,
            Recipe = 1,
            DesiredTabletExplicitMods = 4,
        };

        var action = RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Rare, identified: true, corrupted: false, explicitMods: 4),
            settings);
        Assert.Equal("ANCIENT INFUSER", action?.Name);
        Assert.Equal("CurrencyIncursionCorruptTablet", action?.CurrencyToken);
        Assert.Null(RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Rare, true, corrupted: true, explicitMods: 4),
            settings));
        Assert.Null(RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Unique, true, corrupted: false, explicitMods: 4),
            settings));
    }

    [Fact]
    public void TabletAlchemy_TargetsNormalOrMagicLikeWaystones()
    {
        var settings = new WaystoneAlchemySettings
        {
            TargetType = 1,
            Recipe = 2,
            DesiredTabletExplicitMods = 4,
        };

        Assert.Equal("ALCHEMY", RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Normal, identified: true, corrupted: false, explicitMods: 0),
            settings)?.Name);
        Assert.Equal("ALCHEMY", RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Magic, identified: true, corrupted: false, explicitMods: 2),
            settings)?.Name);
        Assert.Null(RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Rare, true, corrupted: false, explicitMods: 4),
            settings));
        Assert.Null(RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Normal, true, corrupted: true, explicitMods: 0),
            settings));
        Assert.Null(RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Unique, true, corrupted: false, explicitMods: 0),
            settings));
    }

    [Fact]
    public void TabletUpgrade_DoesNotAugmentMagicAtTwoExplicits()
    {
        var settings = new WaystoneAlchemySettings
        {
            TargetType = 1,
            Recipe = 0,
            DesiredTabletExplicitMods = 2,
        };

        Assert.Equal("AUGMENT", RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Magic, true, false, explicitMods: 1), settings)?.Name);
        Assert.Null(RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Magic, true, false, explicitMods: 2), settings));
    }

    [Fact]
    public void WaystoneMinimumTier_DoesNotRejectTablets()
    {
        var action = RadarApp.DetermineAlchemyAction(
            Tablet(Poe2Live.Rarity.Normal, identified: true, corrupted: false, explicitMods: 0),
            new WaystoneAlchemySettings
            {
                TargetType = 1,
                Recipe = 0,
                MinimumTier = 16,
            });

        Assert.Equal("TRANSMUTE", action?.Name);
    }

    [Fact]
    public void SelectNext_SkipsFinishedTabletsAndPicksEligibleNormal()
    {
        // 8 finished magic tablets (2 mods @ target 2) + 2 white tablets needing Transmute.
        var slots = new List<Poe2Live.StashValueSlot>();
        for (var i = 0; i < 8; i++)
            slots.Add(Tablet(Poe2Live.Rarity.Magic, true, false, explicitMods: 2, entity: 0x1000 + i, x: i * 10f));
        slots.Add(Tablet(Poe2Live.Rarity.Normal, true, false, 0, entity: 0x2001, x: 80f));
        slots.Add(Tablet(Poe2Live.Rarity.Normal, true, false, 0, entity: 0x2002, x: 90f));

        var settings = new WaystoneAlchemySettings
        {
            TargetType = 1,
            Recipe = 0,
            DesiredTabletExplicitMods = 2,
        };
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CurrencyUpgradeToMagic",
        };

        Assert.True(RadarApp.TrySelectNextAlchemyChoice(
            slots,
            settings,
            processed: new HashSet<nint>(),
            failed: new HashSet<nint>(),
            available,
            static (slot, _) => IsTestTablet(slot),
            out var choice,
            out var failure));

        Assert.Null(failure);
        Assert.Equal(0x2001, choice.TargetEntity);
        Assert.Equal("TRANSMUTE", choice.Name);
    }

    [Fact]
    public void SelectNext_SkipsMissingCurrencyAndUsesLaterAvailableAction()
    {
        var slots = new[]
        {
            Tablet(Poe2Live.Rarity.Normal, true, false, 0, entity: 0x1, x: 10f), // needs Transmute (missing)
            Tablet(Poe2Live.Rarity.Magic, true, false, 1, entity: 0x2, x: 20f),  // needs Augment (available)
        };
        var settings = new WaystoneAlchemySettings
        {
            TargetType = 1,
            Recipe = 0,
            DesiredTabletExplicitMods = 2,
        };
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CurrencyAddModToMagic",
        };

        Assert.True(RadarApp.TrySelectNextAlchemyChoice(
            slots,
            settings,
            new HashSet<nint>(),
            new HashSet<nint>(),
            available,
            static (slot, _) => IsTestTablet(slot),
            out var choice,
            out var failure));

        Assert.Null(failure);
        Assert.Equal(0x2, choice.TargetEntity);
        Assert.Equal("AUGMENT", choice.Name);
    }

    [Fact]
    public void SelectNext_SkipsFailedItemsAndReportsMissingWhenNothingRunnable()
    {
        var slots = new[]
        {
            Tablet(Poe2Live.Rarity.Normal, true, false, 0, entity: 0x1, x: 10f),
            Tablet(Poe2Live.Rarity.Normal, true, false, 0, entity: 0x2, x: 20f),
        };
        var settings = new WaystoneAlchemySettings
        {
            TargetType = 1,
            Recipe = 0,
            DesiredTabletExplicitMods = 2,
        };
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CurrencyUpgradeToMagic",
        };

        Assert.True(RadarApp.TrySelectNextAlchemyChoice(
            slots,
            settings,
            new HashSet<nint>(),
            failed: new HashSet<nint> { 0x1 },
            available,
            static (slot, _) => IsTestTablet(slot),
            out var choice,
            out _));
        Assert.Equal(0x2, choice.TargetEntity);

        Assert.False(RadarApp.TrySelectNextAlchemyChoice(
            slots,
            settings,
            new HashSet<nint>(),
            failed: new HashSet<nint> { 0x1, 0x2 },
            available,
            static (slot, _) => IsTestTablet(slot),
            out _,
            out var failure));
        Assert.Equal("Complete · no remaining eligible actions", failure);

        Assert.False(RadarApp.TrySelectNextAlchemyChoice(
            slots,
            settings,
            new HashSet<nint>(),
            new HashSet<nint>(),
            availableCurrencyTokens: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            static (slot, _) => IsTestTablet(slot),
            out _,
            out var missing));
        Assert.Equal("Stopped: missing TRANSMUTE", missing);
    }

    private static bool IsTestTablet(Poe2Live.StashValueSlot slot)
        => slot.FullItemPath.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ||
           slot.FullItemPath.Contains("TowerAugment", StringComparison.OrdinalIgnoreCase);

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

    private static Poe2Live.StashValueSlot Tablet(
        Poe2Live.Rarity rarity,
        bool identified,
        bool corrupted,
        int explicitMods,
        nint entity = 0x3000,
        float x = 100f)
    {
        var mods = Enumerable.Range(0, explicitMods)
            .Select(i => new Poe2Live.StashItemMod($"TabletMod{i}", i, float.NaN, true))
            .ToArray();
        return new Poe2Live.StashValueSlot(
            entity + 0x1000,
            entity,
            new Poe2Live.UiRect(x, 100, 52, 52),
            Poe2Live.StashValuePanel.Inventory,
            false,
            "Metadata/Items/Tablet/TowerAugment",
            "TowerAugment",
            "Irradiated Tablet",
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
