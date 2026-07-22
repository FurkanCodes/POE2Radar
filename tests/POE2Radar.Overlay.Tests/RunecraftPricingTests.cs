using POE2Radar.Overlay.Pricing;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class RunecraftPricingTests
{
    private static RunecraftRecipeCatalog LoadCatalog()
    {
        var catalog = new RunecraftRecipeCatalog();
        var path = Path.Combine(AppContext.BaseDirectory, "Runecraft", "expedition2_recipes.json");
        Assert.True(catalog.TryLoad(path), $"RuneCraft catalog was not copied to {path}");
        return catalog;
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 12)]
    public void PanelScanHz_IsBoundedForClosedAndOpenPanels(bool wasOpen, int expected)
        => Assert.Equal(expected, RadarApp.RunecraftPanelScanHz(wasOpen));

    [Fact]
    public void MapPriceOverlay_RequestsMonolithScansWithoutOtherRunecraftWindows()
    {
        var settings = new Config.RunecraftSettings
        {
            ShowOverlay = false,
            ShowMapLabels = true,
            ShowMonolithWindow = false,
            ShowExpeditionPlanner = false,
        };

        Assert.True(RadarApp.RunecraftNeedsMonolithScans(settings, monolithWindowActive: false));
    }

    [Theory]
    [InlineData("Metadata/Terrain/Leagues/Expedition2/Expedition2Encounter", true)]
    [InlineData("Metadata/Terrain/Leagues/Expedition2/Objects/Expedition2Encounter", true)]
    [InlineData("Metadata/Effects/Spells/Expedition2EncounterCrack", false)]
    [InlineData("", false)]
    public void MonolithScan_RejectsTransientExpeditionEffects(string metadata, bool expected)
        => Assert.Equal(expected, RadarApp.IsRunecraftMonolithMetadata(metadata));

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

    [Fact]
    public void BestPanelReward_UsesTotalStackValueAndHighlightsTies()
    {
        var totals = new[] { 4.5, 18.0, 18.0, 7.0 };

        var best = RunecraftPriceMath.BestOf(totals);

        Assert.Equal(18.0, best);
        Assert.False(RunecraftPriceMath.IsBest(4.5, best));
        Assert.True(RunecraftPriceMath.IsBest(18.0, best));
    }

    [Fact]
    public void Catalog_BuildsAnchoredAndUniqueMonolithOffers()
    {
        var catalog = LoadCatalog();
        static double UnitPrice(RunecraftRecipeCatalog.RecipeRow recipe)
            => recipe.reward?.name == "Exalted Orb" ? 3.0 : 0.0;

        var anchored = new RunecraftRecipeCatalog.MonolithView
        {
            HoleCount = 2,
            AnchorIdx = 7,
            AnchorPos = 0,
        };
        catalog.BuildCandidates(anchored, areaLevel: 80, UnitPrice);

        Assert.Contains(anchored.Candidates, c => c.Reward == "Exalted Orb");
        Assert.Equal(3.0, anchored.Best);

        var unique = new RunecraftRecipeCatalog.MonolithView
        {
            HoleCount = 2,
            IsUnique = true,
        };
        catalog.BuildCandidates(unique, areaLevel: 80, UnitPrice);

        Assert.True(unique.Candidates.Count > anchored.Candidates.Count);
        Assert.Equal(3.0, unique.Best);
    }

    [Fact]
    public void Catalog_RerolledDynamicRewardKeepsItsDescription()
    {
        var catalog = LoadCatalog();
        var rerolled = new RunecraftRecipeCatalog.MonolithView
        {
            HoleCount = 2,
            IsRerolled = true,
            SelectedRecipeId = "2SlotUncutSkillGem1",
        };

        catalog.BuildCandidates(rerolled, areaLevel: 80, _ => 0);

        var candidate = Assert.Single(rerolled.Candidates);
        Assert.Equal("Uncut Skill Gem", candidate.Reward);
    }

    [Fact]
    public void Catalog_MapsMetadataIdsToEnglishNames()
    {
        var catalog = LoadCatalog();

        Assert.Equal("Exalted Orb", catalog.EnglishNameForMetaId("CurrencyAddModToRare"));
    }

    [Fact]
    public void LockedReward_NameFallbackWorksWithoutMetadataId()
    {
        var monolith = new RunecraftRecipeCatalog.MonolithView
        {
            PanelOpen = true,
            IsRerolled = true,
            SelectedRecipeId = "2SlotUncutSkillGem1",
            Candidates =
            {
                new RunecraftRecipeCatalog.Candidate
                {
                    MetaId = "",
                    Reward = "Uncut Skill Gem",
                },
            },
        };

        var keys = RadarApp.RunecraftLockedRewardKeys([monolith]);

        Assert.Equal("", keys.MetaId);
        Assert.Equal("Uncut Skill Gem", keys.Name);
    }

    [Fact]
    public void LargeMapPrice_UsesWorkingMapFrameWithoutUiScreenRect()
    {
        var frame = new Overlay.MapFrame(
            Center: new System.Numerics.Vector2(1720, 720),
            Scale: 1.0f,
            Width: 3440,
            Height: 1440,
            MapElement: 0x1234,
            PlayerTerrainHeight: 0,
            Position: System.Numerics.Vector2.Zero,
            IsMinimap: false);

        var screen = RadarApp.ProjectRunecraftMapLabel(
            grid: new System.Numerics.Vector2(246.5f, 805.5f),
            terrainHeight: 0,
            playerGrid: new System.Numerics.Vector2(174.5f, 808.5f),
            playerHeight: 0,
            mapFrame: frame,
            scaleMultiplier: 1,
            xOffset: 0,
            yOffset: 0);

        Assert.True(float.IsFinite(screen.X));
        Assert.True(float.IsFinite(screen.Y));
        Assert.True(screen.X > frame.Center.X);
    }
}
