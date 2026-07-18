using ImGuiNET;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class LootTrackerDrawPolicyTests
{
    [Fact]
    public void BarMode_EnabledOutsideActiveRun_HidesBar()
    {
        var mode = LootTrackerDrawPolicy.BarMode(
            viewEnabled: true,
            settingsEnabled: true,
            onMap: false);

        Assert.Equal(LootTrackerBarMode.None, mode);
    }

    [Fact]
    public void BarMode_EnabledDuringActiveRun_UsesDetailedSessionBar()
    {
        var mode = LootTrackerDrawPolicy.BarMode(
            viewEnabled: true,
            settingsEnabled: true,
            onMap: true);

        Assert.Equal(LootTrackerBarMode.Compact, mode);
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

    [Theory]
    [InlineData("MapLostTowers", 80, 123u, true)]
    [InlineData("Sanctum_1_Foyer_1", 80, 123u, true)]
    [InlineData("P1_Town", 1, 123u, false)]
    [InlineData("G_Endgame_Town", 65, 123u, false)]
    [InlineData("My_Hideout", 80, 123u, false)]
    [InlineData("MapLostTowers", 80, 0u, false)]
    public void ActiveRunArea_IncludesMissionsButExcludesTownsAndHideouts(
        string areaCode,
        int areaLevel,
        uint areaHash,
        bool expected)
    {
        Assert.Equal(expected, RadarApp.IsLootTrackerRunArea(areaCode, areaLevel, areaHash));
    }
}
