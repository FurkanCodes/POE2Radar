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
    public void Migrate_AppliesAtlasGhStyleDefaultsOnce()
    {
        var s = new RadarSettings
        {
            AtlasGhStyleMigrated = false,
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
}
