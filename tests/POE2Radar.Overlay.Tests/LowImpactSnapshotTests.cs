using System.Reflection;
using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class LowImpactSnapshotTests
{
    [Fact]
    public void WorldSnapshot_PublishesArrayBackedCollections()
    {
        var properties = typeof(WorldSnapshot).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.Equal(typeof(Poe2Live.EntityDot[]), Property(properties, nameof(WorldSnapshot.Entities)).PropertyType);
        Assert.Equal(typeof(Poe2Live.EntityDot[]), Property(properties, nameof(WorldSnapshot.SekhemaEntities)).PropertyType);
        Assert.Equal(typeof(POE2Radar.Core.Pathfinding.PathCell[]), Property(properties, nameof(WorldSnapshot.DoorOverrides)).PropertyType);
        Assert.Equal(typeof(Poe2Live.Landmark[]), Property(properties, nameof(WorldSnapshot.Landmarks)).PropertyType);
        Assert.Equal(typeof(Poe2Live.ServerMinimapIcon[]), Property(properties, nameof(WorldSnapshot.ServerIcons)).PropertyType);
        Assert.Equal(typeof(MapEntityRenderItem[]), Property(properties, nameof(WorldSnapshot.MapServerIcons)).PropertyType);
        Assert.Equal(typeof(NavTarget[]), Property(properties, nameof(WorldSnapshot.NavTargets)).PropertyType);
        Assert.Equal(typeof(HpBarSpec[]), Property(properties, nameof(WorldSnapshot.HpSpecs)).PropertyType);
        Assert.Equal(typeof(LegendEntry[]), Property(properties, nameof(WorldSnapshot.Legend)).PropertyType);
        Assert.Equal(typeof(string[]), Property(properties, nameof(WorldSnapshot.SelectedIds)).PropertyType);
        Assert.Equal(typeof(SelectedPath[]), Property(properties, nameof(WorldSnapshot.SelectedPaths)).PropertyType);
    }

    [Fact]
    public void SelectedPathSnapshot_RemainsStableAfterSourceListClears()
    {
        var source = new List<SelectedPath>
        {
            new(0, "t:boss", "Boss", false, NavTargetStatus.Live, 10f, 20f, [(1, 2), (3, 4)], [(0, 0), (1, 2), (3, 4)], (5, 6)),
        };
        var snapshot = source.ToArray();

        source.Clear();

        Assert.Single(snapshot);
        Assert.Equal("t:boss", snapshot[0].TargetId);
        Assert.Equal([(1, 2), (3, 4)], snapshot[0].Points);
        Assert.Empty(source);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShouldDrawOverlay_RequiresManualToggleAndGameFocus(
        bool renderingEnabled,
        bool gameFocused,
        bool expected)
        => Assert.Equal(expected, RadarApp.ShouldDrawOverlay(renderingEnabled, gameFocused));

    [Fact]
    public void ShouldDrawOverlay_RemainsVisibleWhileItsOwnMenuHasForegroundFocus()
    {
        Assert.True(RadarApp.ShouldShowOverlayWindow(
            inGame: true,
            gameFocused: false,
            overlayFocused: true));
        Assert.True(RadarApp.ShouldDrawOverlay(
            renderingEnabled: true,
            gameFocused: false,
            overlayFocused: true));
    }

    [Fact]
    public void ShouldShowOverlayWindow_HidesForARealAltTab()
        => Assert.False(RadarApp.ShouldShowOverlayWindow(
            inGame: true,
            gameFocused: false,
            overlayFocused: false));

    private static PropertyInfo Property(PropertyInfo[] properties, string name)
        => properties.Single(p => p.Name == name);
}
