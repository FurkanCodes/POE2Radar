using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class SharedContextSnapshotTests
{
    [Fact]
    public void GameContext_Invalid_IsNotValid()
    {
        Assert.False(GameContextSnapshot.Invalid.Valid);
    }

    [Fact]
    public void EntityContext_FromWorld_CopiesIdentity()
    {
        var dots = new List<Poe2Live.EntityDot>();
        var source = new WorldEntitySource(true, 0x1000, 42, 80, dots);
        var snap = EntityContextSnapshot.FromWorld(source, generation: 7);
        Assert.True(snap.Valid);
        Assert.Equal(0x1000, snap.AreaInstance);
        Assert.Equal(42u, snap.AreaHash);
        Assert.Equal(80, snap.AreaLevel);
        Assert.Equal(7, snap.Generation);
        Assert.Same(dots, snap.Entities);
    }

    [Fact]
    public void FeaturePerfAccumulator_SmoothsSamples()
    {
        var acc = new FeaturePerfAccumulator();
        acc.RecordGameContext(10);
        acc.RecordGameContext(10);
        var snap = acc.Snapshot;
        Assert.True(snap.GameContextMs > 0);
        Assert.Equal(2, snap.GameContextTicks);
    }

    [Fact]
    public void PanelCatalog_Invalid_Default()
    {
        Assert.False(PanelCatalogSnapshot.Invalid.Valid);
    }
}
