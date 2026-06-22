using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class AtlasPanelResolverTests
{
    [Fact]
    public void ScoreCandidate_PrefersExpectedChildCount()
    {
        var perfect = AtlasPanelResolver.ScoreCandidate(Poe2.AtlasPanel.UiRootChildIndex, Poe2.AtlasPanel.ExpectedChildCount, 0);
        var off = AtlasPanelResolver.ScoreCandidate(Poe2.AtlasPanel.UiRootChildIndex, Poe2.AtlasPanel.ExpectedChildCount + 6, 0);
        Assert.True(perfect > off);
    }

    [Fact]
    public void ScoreCandidate_MapPanelBeatsEndgameShellWhenBothToggled()
    {
        var mapOpen = AtlasPanelResolver.ScoreCandidate(17, 8, toggleCount: 1, visibleNow: true);
        var shell = AtlasPanelResolver.ScoreCandidate(12, 24, toggleCount: 1, visibleNow: false);
        Assert.True(mapOpen > shell);
    }

    [Fact]
    public void ScoreCandidate_ToggleHistoryWinsOverChildCount()
    {
        var toggled = AtlasPanelResolver.ScoreCandidate(5, 12, toggleCount: 2);
        var perfectNoToggle = AtlasPanelResolver.ScoreCandidate(22, Poe2.AtlasPanel.ExpectedChildCount, 0);
        Assert.True(toggled > perfectNoToggle);
    }

    [Fact]
    public void ScoreCandidate_VisibleNowBoostsFirstOpenDiscovery()
    {
        var visible = AtlasPanelResolver.ScoreCandidate(17, 8, 0, visibleNow: true);
        var hidden = AtlasPanelResolver.ScoreCandidate(17, 8, 0, visibleNow: false);
        Assert.True(visible > hidden);
    }

    [Fact]
    public void PickBestIndex_SelectsHighestScoringCandidate()
    {
        var candidates = new List<(int Index, int ChildCount, int ToggleCount, bool VisibleNow)>
        {
            (12, 24, 1, false),
            (17, 8, 1, true),
            (10, 4, 0, false),
        };
        Assert.Equal(17, AtlasPanelResolver.PickBestIndex(candidates));
    }

    [Fact]
    public void PickBestIndex_FallsBackToHardcodedWhenOnlyItScores()
    {
        var candidates = new List<(int Index, int ChildCount, int ToggleCount, bool VisibleNow)>
        {
            (Poe2.AtlasPanel.UiRootChildIndex, Poe2.AtlasPanel.ExpectedChildCount, 0, true),
            (3, 4, 0, false),
        };
        Assert.Equal(Poe2.AtlasPanel.UiRootChildIndex, AtlasPanelResolver.PickBestIndex(candidates));
    }

    [Fact]
    public void PickBestIndex_ReturnsNegativeWhenEmpty()
    {
        Assert.Equal(-1, AtlasPanelResolver.PickBestIndex(Array.Empty<(int, int, int, bool)>()));
    }
}
