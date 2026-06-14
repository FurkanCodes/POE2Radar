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

    private static PropertyInfo Property(PropertyInfo[] properties, string name)
        => properties.Single(p => p.Name == name);
}
