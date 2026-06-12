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
}
