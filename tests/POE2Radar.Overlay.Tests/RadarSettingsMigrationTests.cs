using POE2Radar.Overlay.Config;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class RadarSettingsMigrationTests
{
    [Fact]
    public void Migrate_FoldsLegacyShowPathIntoLayerToggles()
    {
        var s = new RadarSettings
        {
            ShowPath = true,
            PathTogglesMigrated = false,
            ShowPathWorld = false,
            ShowPathMap = false,
            ShowPathMinimap = false,
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.PathTogglesMigrated);
        Assert.True(s.ShowPathWorld);
        Assert.True(s.ShowPathMap);
        Assert.True(s.ShowPathMinimap);
        Assert.True(s.ShowGroundWaypoints);
    }

    [Fact]
    public void SetAllPathLayers_EnablesEveryLayer()
    {
        var s = new RadarSettings();
        s.SetAllPathLayers(false);
        s.SetAllPathLayers(true);

        Assert.True(s.ShowPath);
        Assert.True(s.ShowPathWorld);
        Assert.True(s.ShowGroundWaypoints);
        Assert.True(s.ShowPathMap);
        Assert.True(s.ShowPathMinimap);
        Assert.True(s.AnyPathLayerEnabled);
    }

    [Fact]
    public void SetPathGroundEnabled_TogglesBothGroundFlags()
    {
        var s = new RadarSettings();
        s.SetPathGroundEnabled(false);
        Assert.False(s.ShowPathWorld);
        Assert.False(s.ShowGroundWaypoints);

        s.SetPathGroundEnabled(true);
        Assert.True(s.ShowPathWorld);
        Assert.True(s.ShowGroundWaypoints);
    }

    [Fact]
    public void Migrate_LegacyShowPathOff_TurnsAllLayersOff()
    {
        var s = new RadarSettings { ShowPath = false, PathTogglesMigrated = false };

        s.Migrate();

        Assert.False(s.ShowPathWorld);
        Assert.False(s.ShowPathMap);
        Assert.False(s.ShowPathMinimap);
    }

    [Fact]
    public void Migrate_AppliesLowImpactDefaultsOnce()
    {
        var s = new RadarSettings
        {
            PerformanceDefaultsMigrated = false,
            FpsCap = 144,
            ShowFpsOverlay = true,
            LiveRefreshHz = 99,
            WorldRefreshHz = 99,
            HpBarRefreshHz = 29,
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.LowImpactMode);
        Assert.True(s.PerformanceDefaultsMigrated);
        Assert.Equal(45, s.FpsCap);
        Assert.False(s.ShowFpsOverlay);
        Assert.Equal(30, s.LiveRefreshHz);
        Assert.Equal(12, s.WorldRefreshHz);
        Assert.Equal(8, s.HpBarRefreshHz);
        Assert.True(s.SmoothOverlayMotion);
        Assert.Equal(45, s.OverlaySmoothingMs);
        Assert.Equal(70, s.ChipSmoothingMs);
        Assert.True(s.PixelSnapLabels);
        Assert.True(s.OverlayVSync);

        s.FpsCap = 120;
        s.LiveRefreshHz = 60;
        changed = s.Migrate();

        Assert.False(changed);
        Assert.Equal(120, s.FpsCap);
        Assert.Equal(60, s.LiveRefreshHz);
    }

    [Fact]
    public void Migrate_PreservesHpVisualTogglesForExistingConfig()
    {
        var s = new RadarSettings
        {
            PerformanceDefaultsMigrated = false,
            HpBarNormal = true,
            HpBarMagic = true,
            HpBarRare = false,
            HpBarUnique = false,
        };

        s.Migrate();

        Assert.True(s.HpBarNormal);
        Assert.True(s.HpBarMagic);
        Assert.False(s.HpBarRare);
        Assert.False(s.HpBarUnique);
    }

    [Fact]
    public void Migrate_AppliesUiFontDefaultsOnce()
    {
        var s = new RadarSettings
        {
            UiFontDefaultsMigrated = false,
            UiFontSize = 13,
            UiFontPath = @"C:\temp\missing.ttf",
            UiFontGlyphRange = UiFontGlyphRange.English,
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.UiFontDefaultsMigrated);
        Assert.Equal(18, s.UiFontSize);
        Assert.Equal(@"C:\Windows\Fonts\msyh.ttc", s.UiFontPath);
        Assert.Equal(UiFontGlyphRange.ChineseSimplifiedCommon, s.UiFontGlyphRange);

        changed = s.Migrate();
        Assert.False(changed);
        Assert.Equal(18, s.UiFontSize);
    }

    [Fact]
    public void Migrate_LargeMapScaleStubDefault_CopiesScaleMul()
    {
        var s = new RadarSettings
        {
            LargeMapScaleWiredMigrated = false,
            LargeMapScaleMultiplier = 0.1738f,
            ScaleMul = 1.25f,
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.LargeMapScaleWiredMigrated);
        Assert.Equal(1.25f, s.LargeMapScaleMultiplier);
    }

    [Fact]
    public void NewWorldPathSettings_DefaultToCurrentBehavior()
    {
        var s = new RadarSettings();

        Assert.True(s.ShowGroundWaypoints);
        Assert.Equal(1.0f, s.LargeMapScaleMultiplier);
    }

    [Fact]
    public void IslandRumours_DefaultOffToPreserveAtlasPerformance()
    {
        var s = new RadarSettings();

        Assert.False(s.AtlasShowIslandRumours);
        Assert.True(s.AtlasShowIslandRumourBadges);
        Assert.NotEmpty(s.AtlasIslandRumourPriorityFilter);
        Assert.Equal("#FFD166", s.AtlasIslandRumourPriorityColor);
    }

    [Fact]
    public void NewLootTrackerSettings_KeepCompletedSessionVisible()
    {
        var settings = new LootTrackerSettings();

        Assert.True(settings.KeepVisibleAfterRun);
    }

    [Fact]
    public void Migrate_AppliesAtlasGhStyleDefaultsOnce()
    {
        var s = new RadarSettings
        {
            AtlasGhStyleMigrated = false,
            AtlasCleanMvpMigrated = true, // isolate GH-style step from the later clean-MVP override
            Atlas2QolMigrated = true,
            AtlasHideCompletedMaps = false,
            AtlasHideNotAccessibleMaps = false,
            AtlasHideAvailableMaps = false,
            AtlasShowNodeSprites = true,
            AtlasRouteLineThickness = 3.5f,
            AtlasRouteChevronSpacing = 28f,
            AtlasAnchorNudgeY = 0f,
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.AtlasGhStyleMigrated);
        Assert.True(s.AtlasHideCompletedMaps);
        Assert.True(s.AtlasHideNotAccessibleMaps);
        Assert.True(s.AtlasHideAvailableMaps);
        Assert.False(s.AtlasShowNodeSprites);
        Assert.Equal(1f, s.AtlasRouteLineThickness);
        Assert.Equal(8f, s.AtlasRouteChevronSpacing);
        Assert.Equal(28f, s.AtlasAnchorNudgeY);

        Assert.False(s.Migrate());
    }

    [Fact]
    public void Migrate_AppliesAtlasCleanMvpDefaultsOnce()
    {
        var s = new RadarSettings
        {
            AtlasGhStyleMigrated = true,
            AtlasCleanMvpMigrated = false,
            Atlas2QolMigrated = true, // isolate from Atlas2 QoL step
            AtlasShowOnScreenNodes = false,
            AtlasRevealFog = false,
            AtlasHideCompletedMaps = true,
            AtlasHideNotAccessibleMaps = true,
            AtlasHideAvailableMaps = true,
            AtlasDrawLinesSearchQuery = false,
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.AtlasCleanMvpMigrated);
        Assert.True(s.AtlasShowOnScreenNodes);
        Assert.True(s.AtlasRevealFog);
        Assert.False(s.AtlasHideCompletedMaps);
        Assert.False(s.AtlasHideNotAccessibleMaps);
        Assert.False(s.AtlasHideAvailableMaps);
        Assert.True(s.AtlasDrawLinesSearchQuery);

        Assert.False(s.Migrate());
    }

    [Fact]
    public void Migrate_AppliesAtlas2QolDefaultsOnce()
    {
        var s = new RadarSettings
        {
            AtlasGhStyleMigrated = true,
            AtlasCleanMvpMigrated = true,
            Atlas2QolMigrated = false,
            AtlasRouteTargetsGhParityMigrated = true,
            AtlasHideCompletedMaps = false,
            AtlasShowBiomeBorders = false,
            AtlasRouteGroups = [],
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.Atlas2QolMigrated);
        Assert.True(s.AtlasHideCompletedMaps);
        Assert.True(s.AtlasShowBiomeBorders);
        Assert.Contains(s.AtlasRouteGroups, g => g.BuiltInKey == "search");
        Assert.Contains(s.AtlasRouteGroups, g => g.BuiltInKey == "expedition");
        Assert.False(s.Migrate());
    }

    [Fact]
    public void Migrate_ReconcilesAtlasBuiltInTargetsToGameHelperList()
    {
        var s = new RadarSettings
        {
            AtlasRouteTargetsGhParityMigrated = false,
            Atlas2QolMigrated = true, // keep legacy Map Targets reconcile path
            AtlasRouteGroups =
            [
                new AtlasRouteGroupSettings
                {
                    Name = "Map Targets",
                    Locked = true,
                    LineThickness = 3.5f,
                    Entries =
                    [
                        new AtlasRouteEntrySettings { Name = "Old", Match = "map:Jade Citadel" },
                    ],
                },
                new AtlasRouteGroupSettings
                {
                    Name = "Great Beast",
                    Entries =
                    [
                        new AtlasRouteEntrySettings { Name = "Great Beast", Match = "content:Great Beast" },
                    ],
                },
            ],
        };

        var changed = s.Migrate();
        var builtIn = s.AtlasRouteGroups.Single(g => g.Locked);

        Assert.True(changed);
        Assert.True(s.AtlasRouteTargetsGhParityMigrated);
        Assert.Equal(1.5f, builtIn.LineThickness);
        Assert.Contains(builtIn.Entries, e => e.Name == "The Jade Isles" && e.Match == "id:MapUberBoss_JadeCitadel");
        Assert.Contains(builtIn.Entries, e => e.Name == "Sprawling Jungle" && e.Match == "id:ExpeditionSubArea_MedvedBoss");
        Assert.Contains(s.AtlasRouteGroups, g => !g.Locked && g.Name == "Great Beast");
        Assert.False(s.Migrate());
    }

    [Fact]
    public void Migrate_DisablesAutoShowMonolithWithGamepadOnce()
    {
        var s = new RadarSettings
        {
            RunecraftAutoMonolithCpuMigrated = false,
            Runecraft = { AutoShowMonolithWithGamepad = true },
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.RunecraftAutoMonolithCpuMigrated);
        Assert.False(s.Runecraft.AutoShowMonolithWithGamepad);
        Assert.False(s.Migrate());
    }

    [Fact]
    public void NewRunecraftSettings_AutoShowMonolithWithGamepadDefaultsOff()
    {
        var s = new RadarSettings();
        s.Migrate();
        Assert.False(s.Runecraft.AutoShowMonolithWithGamepad);
    }

    [Fact]
    public void Migrate_RepairsIncompleteSekhemaProfilesAndCollections()
    {
        var s = new RadarSettings
        {
            Sekhema =
            {
                CurrentProfile = "removed-profile",
                Profiles = new Dictionary<string, SekhemaProfileSettings>
                {
                    ["Default"] = new()
                    {
                        RoomTypeWeights = [],
                        AfflictionWeights = [],
                        RewardWeights = [],
                    },
                },
                ChestPriorityOrder = [],
                ChestDisabledContent = new HashSet<string>(StringComparer.Ordinal) { "generic" },
            },
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.Equal("Default", s.Sekhema.CurrentProfile);
        Assert.Contains("No-Hit", s.Sekhema.Profiles.Keys);
        Assert.NotEmpty(s.Sekhema.Profiles["Default"].RoomTypeWeights);
        Assert.NotEmpty(s.Sekhema.Profiles["Default"].AfflictionWeights);
        Assert.NotEmpty(s.Sekhema.Profiles["Default"].RewardWeights);
        Assert.NotEmpty(s.Sekhema.ChestPriorityOrder);
        Assert.Contains("GENERIC", s.Sekhema.ChestDisabledContent);
    }

    [Fact]
    public void Migrate_EnablesRitualPricesWindowOnce()
    {
        var s = new RadarSettings
        {
            RitualPricesWindowMigrated = false,
            Ritual = { ShowPricesWindow = false },
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.RitualPricesWindowMigrated);
        Assert.True(s.Ritual.ShowPricesWindow);
        Assert.False(s.Migrate());
    }

    [Fact]
    public void Migrate_MovesGlobalRenderingToggleToF3()
    {
        var s = new RadarSettings
        {
            RenderingHotkeyMigrated = false,
            ToggleRenderingHotkey = 0x71,
            AutoPathToggleHotkey = 0x72,
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.RenderingHotkeyMigrated);
        Assert.Equal(0x72, s.ToggleRenderingHotkey);
        Assert.Equal(0x71, s.AutoPathToggleHotkey);
        Assert.False(s.Migrate());
    }

    [Fact]
    public void Migrate_PrefersAlchemyOnMagicWaystones()
    {
        var s = new RadarSettings
        {
            WaystoneAlchemyPreferAlchemyMigrated = false,
            WaystoneAlchemy = { UseRegalOnMagic = true },
        };

        var changed = s.Migrate();

        Assert.True(changed);
        Assert.True(s.WaystoneAlchemyPreferAlchemyMigrated);
        Assert.False(s.WaystoneAlchemy.UseRegalOnMagic);
        Assert.False(s.Migrate());
    }

}
