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
