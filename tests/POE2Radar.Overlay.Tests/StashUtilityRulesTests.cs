using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.StashUtility;
using System.Text.Json;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class StashUtilityRulesTests
{
    [Theory]
    [InlineData("Waystone Tier 15", 15)]
    [InlineData("Metadata/Items/Maps/Waystone16", 16)]
    [InlineData("Tablet", 0)]
    public void ParseTier_SupportsDisplayNameAndMetadata(string text, int expected)
        => Assert.Equal(expected, StashUtilityRules.ParseTier(text));

    [Fact]
    public void Waystone_PassesNumericalFiltersAndGreatThresholds()
    {
        var settings = new StashUtilitySettings
        {
            FilterMinPackSize = true,
            MinPackSize = 20,
            GreatByDropChance = true,
            GreatDropChance = 100,
        };
        var slot = Waystone(
            stats: new Poe2Live.StashItemStats(40, 25, 20, 15, 95, 20, true));

        Assert.True(StashUtilityRules.TryEvaluate(slot, settings, out var result));
        Assert.False(result.Bad);
        Assert.True(result.Great);
        Assert.Equal(115, result.DropChance);
    }

    [Fact]
    public void Waystone_SelectedBadModOverridesOtherwiseGoodItem()
    {
        var settings = new StashUtilitySettings
        {
            BadWaystoneMods = ["MapPlayerMaximumResists"],
            GoodWaystoneMods = ["MapMonsterAdditionalProjectiles"],
        };
        var slot = Waystone(mods:
        [
            new Poe2Live.StashItemMod("MapPlayerMaximumResists", 10, float.NaN, true),
            new Poe2Live.StashItemMod("MapMonsterAdditionalProjectiles", 2, float.NaN, true),
        ]);

        Assert.True(StashUtilityRules.TryEvaluate(slot, settings, out var result));
        Assert.True(result.Bad);
        Assert.Equal(1, result.GoodMods);
    }

    [Fact]
    public void Waystone_SelectedRequiredJuiceFiltersOutNonMatchingItems()
    {
        var settings = new StashUtilitySettings
        {
            GoodWaystoneMods =
            [
                "MapMonsterDamageAsCold",
                "MapPlayerEnfeeble",
            ],
        };

        Assert.False(StashUtilityRules.TryEvaluate(
            Waystone(mods:
            [
                new Poe2Live.StashItemMod("MapMonsterAdditionalProjectiles", 2, float.NaN, true),
            ]),
            settings,
            out _));
        Assert.True(StashUtilityRules.TryEvaluate(
            Waystone(mods:
            [
                new Poe2Live.StashItemMod("MapMonsterDamageAsCold", 14, float.NaN, true),
            ]),
            settings,
            out var result));
        Assert.Equal(1, result.GoodMods);
    }

    [Fact]
    public void Waystone_RequireAllSelectedJuicesNeedsEveryMatch()
    {
        var settings = new StashUtilitySettings
        {
            GoodWaystoneMods =
            [
                "MapMonsterDamageAsCold",
                "MapPlayerEnfeeble",
            ],
            RequireAllGoodWaystoneMods = true,
        };

        Assert.False(StashUtilityRules.TryEvaluate(
            Waystone(mods:
            [
                new Poe2Live.StashItemMod("MapMonsterDamageAsCold", 14, float.NaN, true),
            ]),
            settings,
            out _));
        Assert.True(StashUtilityRules.TryEvaluate(
            Waystone(mods:
            [
                new Poe2Live.StashItemMod("MapMonsterDamageAsCold", 14, float.NaN, true),
                new Poe2Live.StashItemMod("MapPlayerEnfeeble", 16, float.NaN, true),
            ]),
            settings,
            out _));
    }

    [Fact]
    public void Waystone_OnlyEnabledRarityAndMonsterEffectivenessFiltersApply()
    {
        var settings = new StashUtilitySettings
        {
            FilterMinItemRarity = true,
            MinItemRarity = 30,
            FilterMinMonsterEffectiveness = true,
            MinMonsterEffectiveness = 20,
        };

        Assert.True(StashUtilityRules.TryEvaluate(
            Waystone(stats: new Poe2Live.StashItemStats(30, 0, 0, 20, 0, 0, true)),
            settings,
            out _));
        Assert.False(StashUtilityRules.TryEvaluate(
            Waystone(stats: new Poe2Live.StashItemStats(30, 100, 100, 19, 400, 0, true)),
            settings,
            out _));
    }

    [Fact]
    public void Waystone_GreatAndBadJuicesFilterAndClassifyIndependently()
    {
        var settings = new StashUtilitySettings
        {
            GreatWaystoneMods = ["MapMonsterDamageAsCold"],
            BadWaystoneMods = ["MapPlayerMaximumResists"],
        };

        Assert.False(StashUtilityRules.TryEvaluate(
            Waystone(mods:
            [
                new Poe2Live.StashItemMod("MapMonsterAdditionalProjectiles", 2, float.NaN, true),
            ]),
            settings,
            out _));
        Assert.True(StashUtilityRules.TryEvaluate(
            Waystone(mods:
            [
                new Poe2Live.StashItemMod("MapMonsterDamageAsCold", 14, float.NaN, true),
            ]),
            settings,
            out var great));
        Assert.True(great.Great);
        Assert.False(great.Bad);
        Assert.True(StashUtilityRules.TryEvaluate(
            Waystone(mods:
            [
                new Poe2Live.StashItemMod("MapPlayerMaximumResists", 10, float.NaN, true),
            ]),
            settings,
            out var bad));
        Assert.True(bad.Bad);
        Assert.False(bad.Great);
    }

    [Fact]
    public void Tablet_MinimumRollControlsGoodClassification()
    {
        var settings = new StashUtilitySettings
        {
            GoodTabletMods = ["TowerPackSizeIncrease"],
            TabletMinimumRolls = new() { ["TowerPackSizeIncrease"] = 12f },
        };

        Assert.False(StashUtilityRules.TryEvaluate(
            Tablet(new Poe2Live.StashItemMod("TowerPackSizeIncrease", 10, float.NaN, true)),
            settings,
            out _));
        Assert.True(StashUtilityRules.TryEvaluate(
            Tablet(new Poe2Live.StashItemMod("TowerPackSizeIncrease", 15, float.NaN, true)),
            settings,
            out var result));
        Assert.Equal(1, result.GoodMods);
    }

    [Fact]
    public void Tablet_GodModIsAlwaysGreat()
    {
        var settings = new StashUtilitySettings
        {
            GodTabletMods = ["TowerRitualAdditionalReroll"],
            TabletMinimumRolls = new() { ["TowerRitualAdditionalReroll"] = 2f },
        };

        Assert.True(StashUtilityRules.TryEvaluate(
            Tablet(new Poe2Live.StashItemMod("TowerRitualAdditionalReroll", 3, float.NaN, true)),
            settings,
            out var result));
        Assert.True(result.Great);
        Assert.False(result.Bad);
    }

    [Fact]
    public void Tablet_SelectedRequiredJuicesExcludeUnrelatedJuice()
    {
        var settings = new StashUtilitySettings
        {
            GoodTabletMods =
            [
                "TowerDroppedItemRarityIncrease",
                "TowerMonsterEffectiveness",
            ],
        };

        Assert.False(StashUtilityRules.TryEvaluate(
            Tablet(new Poe2Live.StashItemMod("TowerPackSizeIncrease", 15, float.NaN, true)),
            settings,
            out _));
        Assert.True(StashUtilityRules.TryEvaluate(
            Tablet(new Poe2Live.StashItemMod("TowerDroppedItemRarityIncrease", 12, float.NaN, true)),
            settings,
            out _));
        Assert.True(StashUtilityRules.TryEvaluate(
            Tablet(new Poe2Live.StashItemMod("TowerMonsterEffectiveness", 15, float.NaN, true)),
            settings,
            out _));
    }

    [Fact]
    public void Tablet_BadPriorityIsNotClearedByMatchingGreatJuice()
    {
        var settings = new StashUtilitySettings
        {
            GodTabletMods = ["TowerDroppedItemRarityIncrease"],
            BadTabletMods = ["TowerMonsterEffectiveness"],
            RedTakesPriority = true,
        };

        Assert.True(StashUtilityRules.TryEvaluate(
            Tablet(
                new Poe2Live.StashItemMod("TowerDroppedItemRarityIncrease", 12, float.NaN, true),
                new Poe2Live.StashItemMod("TowerMonsterEffectiveness", 15, float.NaN, true)),
            settings,
            out var result));
        Assert.True(result.Great);
        Assert.True(result.Bad);
    }

    [Fact]
    public void Tablet_GreatJuiceCanOverrideBadWhenBadPriorityIsDisabled()
    {
        var settings = new StashUtilitySettings
        {
            GodTabletMods = ["TowerDroppedItemRarityIncrease"],
            BadTabletMods = ["TowerMonsterEffectiveness"],
            RedTakesPriority = false,
            HideBadTablets = true,
        };

        Assert.True(StashUtilityRules.TryEvaluate(
            Tablet(
                new Poe2Live.StashItemMod("TowerDroppedItemRarityIncrease", 12, float.NaN, true),
                new Poe2Live.StashItemMod("TowerMonsterEffectiveness", 15, float.NaN, true)),
            settings,
            out var result));
        Assert.True(result.Great);
        Assert.False(result.Bad);
    }

    [Fact]
    public void Tablet_GreatClassificationUsesOnlySelectedGreatJuices()
    {
        var settings = new StashUtilitySettings
        {
            GoodTabletMods =
            [
                "TowerDroppedItemRarityIncrease",
                "TowerMonsterEffectiveness",
            ],
        };

        Assert.True(StashUtilityRules.TryEvaluate(
            Tablet(
                new Poe2Live.StashItemMod("TowerDroppedItemRarityIncrease", 12, float.NaN, true),
                new Poe2Live.StashItemMod("TowerMonsterEffectiveness", 15, float.NaN, true)),
            settings,
            out var result));
        Assert.False(result.Great);
    }

    [Theory]
    [InlineData(0, 1, 0, true)]
    [InlineData(0, 1, 3, true)]
    [InlineData(0, 1, 4, false)]
    [InlineData(1, 1, 0, false)]
    [InlineData(0, 0, 0, false)]
    public void TransientMissGrace_PreservesControllerTransition(
        int slots, int previous, int streak, bool expected)
        => Assert.Equal(expected, RadarApp.ShouldHoldStashUtilityReadMiss(slots, previous, streak));

    [Fact]
    public void ControllerHint_PrioritizesControllerStashRootWithoutDroppingKbmFallback()
    {
        Span<nint> roots = stackalloc nint[6];
        var count = UiBranchCandidates.Fill(
            roots,
            gameUi: 0x1000,
            controllerGameUi: 0x2000,
            uiRoot: 0x3000,
            fixedRoot: 0x4000,
            hint: Poe2UiAnchors.BranchKind.Controller);

        Assert.Equal((nint)0x2000, roots[0]);
        Assert.Contains((nint)0x1000, roots[..count].ToArray());
    }

    [Fact]
    public void Settings_ModRulesRoundTripThroughJson()
    {
        var settings = new RadarSettings();
        settings.StashUtility.BadWaystoneMods.Add("MapPlayerMaximumResists");
        settings.StashUtility.GreatWaystoneMods.Add("MapMonsterDamageAsCold");
        settings.StashUtility.GodTabletMods.Add("TowerRitualAdditionalReroll");
        settings.StashUtility.TabletMinimumRolls["TowerRitualAdditionalReroll"] = 2f;

        var restored = JsonSerializer.Deserialize<RadarSettings>(JsonSerializer.Serialize(settings));

        Assert.NotNull(restored);
        Assert.Contains("MapPlayerMaximumResists", restored.StashUtility.BadWaystoneMods);
        Assert.Contains("MapMonsterDamageAsCold", restored.StashUtility.GreatWaystoneMods);
        Assert.Contains("TowerRitualAdditionalReroll", restored.StashUtility.GodTabletMods);
        Assert.Equal(2f, restored.StashUtility.TabletMinimumRolls["TowerRitualAdditionalReroll"]);
    }

    [Fact]
    public void TabletGroups_UseTheSevenCurrentInGameBaseNames()
    {
        Assert.Equal(
            [
                "Irradiated Tablet",
                "Overseer Tablet",
                "Abyss Tablet",
                "Breach Tablet",
                "Delirium Tablet",
                "Ritual Tablet",
                "Temple Tablet",
            ],
            StashUtilityCatalog.TabletGroups.Select(group => group.Name));
        Assert.DoesNotContain(
            StashUtilityCatalog.TabletGroups,
            group => string.Equals(group.Name, "Expedition Tablet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TabletGroups_PutExpeditionUsefulRollsUnderIrradiated()
    {
        var irradiated = Assert.Single(
            StashUtilityCatalog.TabletGroups,
            group => group.Name == "Irradiated Tablet");
        var expeditionMods = StashUtilityCatalog.TabletMods
            .Where(definition => definition.Category == "Expedition")
            .Select(definition => definition.Id)
            .ToArray();

        Assert.NotEmpty(expeditionMods);
        Assert.All(
            expeditionMods,
            id => Assert.Contains(StashUtilityCatalog.TabletModsFor(irradiated), definition => definition.Id == id));
    }

    [Fact]
    public void TabletGroups_ExposeEveryConfiguredModifier()
    {
        var groupedIds = StashUtilityCatalog.TabletGroups
            .SelectMany(StashUtilityCatalog.TabletModsFor)
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(StashUtilityCatalog.TabletMods, definition => Assert.Contains(definition.Id, groupedIds));
    }

    [Fact]
    public void TabletCatalog_CoversAllCurrentAffixesWithCanonicalText()
    {
        Assert.Equal(83, StashUtilityCatalog.TabletMods.Length);
        Assert.Equal(
            StashUtilityCatalog.TabletMods.Length,
            StashUtilityCatalog.TabletMods.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(
            "Map has (1-2) additional random Modifiers",
            TabletMod("TowerMapAdditionalModifier").Name);
        Assert.Equal(
            "(5-7)% increased Pack Size in Map",
            TabletMod("TowerPackSizeIncrease").Name);
        Assert.Equal(
            "Ritual Altars in Map allow rerolling Favours (1-3) additional times",
            TabletMod("TowerRitualAdditionalReroll").Name);
    }

    [Fact]
    public void TabletCatalog_CanonicalTextFingerprint_DoesNotDriftSilently()
    {
        var canonicalText = string.Join(
            '\n',
            StashUtilityCatalog.TabletMods.Select(definition => $"{definition.Id}|{definition.Name}"));
        var fingerprint = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalText)));

        Assert.Equal("10051255067F88D109F0E406F46769FC2387BFFED47E9E2AA4BA387959C90E00", fingerprint);
    }

    [Fact]
    public void TabletSearch_MatchesAllWordsInExactModifierText()
    {
        var additionalModifiers = TabletMod("TowerMapAdditionalModifier");

        Assert.True(StashUtilityCatalog.MatchesTabletSearch(additionalModifiers, "Additional Modifiers"));
        Assert.True(StashUtilityCatalog.MatchesTabletSearch(additionalModifiers, "map random"));
        Assert.False(StashUtilityCatalog.MatchesTabletSearch(additionalModifiers, "Ritual Tribute"));
    }

    [Fact]
    public void TabletMarketTiers_HighlightPriceDefiningModifiers()
    {
        var ritualRerolls = TabletMod("TowerRitualAdditionalReroll");
        var additionalMapMods = TabletMod("TowerMapAdditionalModifier");

        Assert.Equal("S", ritualRerolls.MarketTier);
        Assert.Equal("#FFD166", ritualRerolls.TierColor);
        Assert.Equal("A", additionalMapMods.MarketTier);
        Assert.Equal("#6EEB87", additionalMapMods.TierColor);
        Assert.All(
            StashUtilityCatalog.TabletMods,
            definition => Assert.Contains(definition.MarketTier, new[] { "S", "A", "B", "C", "D" }));
    }

    [Fact]
    public void TabletTierHover_DetectsTypeAndRanksEveryExplicitModifier()
    {
        var slot = Slot(
            "Metadata/Items/Tablet/PrecursorTabletRitual",
            "Ritual Tablet",
            [
                new Poe2Live.StashItemMod("TowerPackSizeIncrease", 7, float.NaN, true),
                new Poe2Live.StashItemMod("TowerRitualAdditionalReroll", 3, float.NaN, true),
                new Poe2Live.StashItemMod("TowerRitualOmenChance", 62, float.NaN, true),
            ],
            default) with { Hovered = true };

        Assert.True(TabletTierHover.TryBuild(slot, out var hover));
        Assert.Equal("Ritual Tablet", hover.ItemType);
        Assert.Equal("S", hover.OverallTier);
        Assert.Equal("#FFD166", hover.OverallColor);
        Assert.Equal(["S", "A", "C"], hover.Modifiers.Select(modifier => modifier.Tier));
        Assert.Equal("3", hover.Modifiers[0].Roll);
        Assert.Equal("62%", hover.Modifiers[1].Roll);
        Assert.Equal("7%", hover.Modifiers[2].Roll);
    }

    [Fact]
    public void TabletTierHover_OnlyAppearsForHoveredTablets()
    {
        var tablet = Tablet(new Poe2Live.StashItemMod("TowerRitualAdditionalReroll", 3, float.NaN, true));
        Assert.False(TabletTierHover.TryBuild(tablet, out _));
        Assert.False(TabletTierHover.TryBuild(Waystone() with { Hovered = true }, out _));
    }

    [Fact]
    public void WaystoneMarketTiers_UsePerModifierDropChanceContribution()
    {
        Assert.Equal("S", WaystoneMod("MapMonsterSpeedIncrease").MarketTier);
        Assert.Equal("A", WaystoneMod("MapMonsterDamageIncrease").MarketTier);
        Assert.Equal("B", WaystoneMod("MapMonsterDamageAsCold").MarketTier);
        Assert.Equal("C", WaystoneMod("MapMonstersBaseSelfCriticalMultiplier").MarketTier);
        Assert.Equal("D", StashUtilityCatalog.WaystoneRewardTier(5));
    }

    [Fact]
    public void WaystoneTierHover_RanksEveryExplicitModifierByReward()
    {
        var slot = Waystone(
            mods:
            [
                new Poe2Live.StashItemMod("MapMonstersBaseSelfCriticalMultiplier", 25, float.NaN, true),
                new Poe2Live.StashItemMod("MapMonsterDamageAsCold", 14, float.NaN, true),
                new Poe2Live.StashItemMod("MapMonsterDamageIncrease", 20, float.NaN, true),
                new Poe2Live.StashItemMod("MapMonsterSpeedIncrease", 18, float.NaN, true),
            ]) with { Hovered = true };

        Assert.True(WaystoneTierHover.TryBuild(slot, out var hover));
        Assert.Equal("Waystone Tier 15", hover.ItemType);
        Assert.Equal("S", hover.OverallTier);
        Assert.Equal(["S", "A", "B", "C"], hover.Modifiers.Select(modifier => modifier.Tier));
        Assert.Contains("danger is build-specific", hover.TierNote);
        var metrics = hover.Metrics.ToDictionary(metric => metric.Label);
        Assert.Equal(("+14%", "D"), (metrics["Item rarity"].Value, metrics["Item rarity"].Tier));
        Assert.Equal(("+9%", "D"), (metrics["Pack size"].Value, metrics["Pack size"].Tier));
        Assert.Equal(("+43%", "A"), (metrics["Monster rarity"].Value, metrics["Monster rarity"].Tier));
        Assert.Equal(
            ("+0%", "D"),
            (metrics["Monster effectiveness"].Value, metrics["Monster effectiveness"].Tier));
        Assert.Equal(
            ("+70%", "D"),
            (metrics["Waystone drop chance"].Value, metrics["Waystone drop chance"].Tier));
        Assert.Equal(("2", "B"), (metrics["Revives available"].Value, metrics["Revives available"].Tier));
        Assert.All(hover.Metrics, metric => Assert.StartsWith("#", metric.Color));
    }

    [Fact]
    public void WaystoneAggregateRewardTiers_UseIndependentMarketBreakpoints()
    {
        AssertTierBands(
            WaystoneTierHover.RewardMetric.ItemRarity,
            (65, "S"), (50, "A"), (40, "B"), (20, "C"), (19, "D"));
        AssertTierBands(
            WaystoneTierHover.RewardMetric.PackSize,
            (42, "S"), (30, "A"), (20, "B"), (10, "C"), (9, "D"));
        AssertTierBands(
            WaystoneTierHover.RewardMetric.MonsterRarity,
            (50, "S"), (40, "A"), (30, "B"), (20, "C"), (19, "D"));
        AssertTierBands(
            WaystoneTierHover.RewardMetric.MonsterEffectiveness,
            (40, "S"), (30, "A"), (20, "B"), (10, "C"), (9, "D"));
        AssertTierBands(
            WaystoneTierHover.RewardMetric.WaystoneDropChance,
            (145, "S"), (120, "A"), (100, "B"), (75, "C"), (74, "D"));
        AssertTierBands(
            WaystoneTierHover.RewardMetric.Revives,
            (0, "S"), (1, "A"), (2, "B"), (3, "C"), (4, "D"));
    }

    [Fact]
    public void WaystoneTierHover_OnlyAppearsForHoveredWaystones()
    {
        Assert.False(WaystoneTierHover.TryBuild(Waystone(), out _));
        Assert.False(WaystoneTierHover.TryBuild(
            Tablet(new Poe2Live.StashItemMod("TowerPackSizeIncrease", 7, float.NaN, true))
                with { Hovered = true },
            out _));
    }

    [Theory]
    [InlineData("Metadata/Items/Tablet/PrecursorTabletIncursion", "Temple Tablet")]
    [InlineData("Metadata/Items/Tablet/PrecursorTabletBoss", "Overseer Tablet")]
    [InlineData("Metadata/Items/Tablet/PrecursorTabletGeneric", "Irradiated Tablet")]
    public void TabletTierHover_DetectsMetadataOnlyTabletFamilies(string path, string expected)
    {
        var slot = Slot(path, "", [], default);

        Assert.Equal(expected, TabletTierHover.DetectTabletType(slot));
    }

    [Theory]
    [InlineData("Abyss Tablet")]
    [InlineData("Breach Tablet")]
    [InlineData("Delirium Tablet")]
    [InlineData("Irradiated Tablet")]
    [InlineData("Overseer Tablet")]
    [InlineData("Ritual Tablet")]
    [InlineData("Temple Tablet")]
    public void TabletEvaluation_RecognizesCurrentBaseNames(string baseName)
    {
        var settings = new StashUtilitySettings
        {
            GoodTabletMods = ["TowerPackSizeIncrease"],
        };
        var slot = Slot(
            $"Metadata/Items/TowerAugment/{baseName.Replace(" ", "")}",
            baseName,
            [new Poe2Live.StashItemMod("TowerPackSizeIncrease", 10, float.NaN, true)],
            default);

        Assert.True(StashUtilityRules.TryEvaluate(slot, settings, out var result));
        Assert.Equal(StashUtilityKind.Tablet, result.Kind);
    }

    private static Poe2Live.StashValueSlot Waystone(
        Poe2Live.StashItemMod[]? mods = null,
        Poe2Live.StashItemStats? stats = null)
        => Slot(
            "Metadata/Items/Maps/Waystone15",
            "Waystone Tier 15",
            mods ?? [],
            stats ?? new Poe2Live.StashItemStats(0, 0, 0, 0, 0, 0, false));

    private static Poe2Live.StashValueSlot Tablet(params Poe2Live.StashItemMod[] mods)
        => Slot("Metadata/Items/Tablet/TowerAugment", "Precursor Tablet", mods, default);

    private static StashUtilityModDefinition TabletMod(string id)
        => Assert.Single(StashUtilityCatalog.TabletMods, definition => definition.Id == id);

    private static StashUtilityModDefinition WaystoneMod(string id)
        => Assert.Single(StashUtilityCatalog.WaystoneMods, definition => definition.Id == id);

    private static void AssertTierBands(
        WaystoneTierHover.RewardMetric metric,
        params (int Value, string Tier)[] bands)
    {
        foreach (var band in bands)
            Assert.Equal(band.Tier, WaystoneTierHover.AggregateRewardTier(metric, band.Value));
    }

    private static Poe2Live.StashValueSlot Slot(
        string path,
        string name,
        Poe2Live.StashItemMod[] mods,
        Poe2Live.StashItemStats stats)
        => new(
            0x1000,
            0x2000,
            new Poe2Live.UiRect(10, 10, 52, 52),
            Poe2Live.StashValuePanel.Stash,
            false,
            path,
            Path.GetFileName(path),
            name,
            Poe2Live.Rarity.Rare,
            "",
            1,
            true,
            false,
            mods.Select(m => m.Id).ToArray(),
            mods,
            stats);
}
