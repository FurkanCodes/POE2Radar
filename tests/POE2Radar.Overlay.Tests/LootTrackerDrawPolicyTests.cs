using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class LootTrackerDrawPolicyTests
{
    [Fact]
    public void BarMode_EnabledOutsideMap_UsesCompactSessionBar()
    {
        var mode = LootTrackerDrawPolicy.BarMode(
            viewEnabled: true,
            settingsEnabled: true,
            onMap: false);

        Assert.Equal(LootTrackerBarMode.Compact, mode);
    }

    [Fact]
    public void BarMode_EnabledOnMap_UsesMapBar()
    {
        var mode = LootTrackerDrawPolicy.BarMode(
            viewEnabled: true,
            settingsEnabled: true,
            onMap: true);

        Assert.Equal(LootTrackerBarMode.Map, mode);
    }

    [Fact]
    public void BarMode_Disabled_HidesBar()
    {
        Assert.Equal(
            LootTrackerBarMode.None,
            LootTrackerDrawPolicy.BarMode(
                viewEnabled: true,
                settingsEnabled: false,
                onMap: true));
    }
}
