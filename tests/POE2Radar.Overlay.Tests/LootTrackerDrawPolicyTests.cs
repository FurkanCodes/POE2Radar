using ImGuiNET;
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

    [Fact]
    public void CompactWindow_IsMovableAutoSizedAndKeepsItsDraggedPosition()
    {
        var window = LootTrackerDrawPolicy.CompactWindow;

        Assert.Equal(ImGuiCond.FirstUseEver, window.PositionCondition);
        Assert.False(window.ForceSize);
        Assert.True(window.ShowLootButton);
        Assert.True(window.Flags.HasFlag(ImGuiWindowFlags.AlwaysAutoResize));
        Assert.False(window.Flags.HasFlag(ImGuiWindowFlags.NoMove));
        Assert.False(window.Flags.HasFlag(ImGuiWindowFlags.NoSavedSettings));
    }

    [Fact]
    public void BreakdownWindow_IsMovableAndKeepsItsDraggedPosition()
    {
        var window = LootTrackerDrawPolicy.BreakdownWindow;

        Assert.Equal(ImGuiCond.FirstUseEver, window.PositionCondition);
        Assert.False(window.Flags.HasFlag(ImGuiWindowFlags.NoMove));
        Assert.False(window.Flags.HasFlag(ImGuiWindowFlags.NoSavedSettings));
    }
}
