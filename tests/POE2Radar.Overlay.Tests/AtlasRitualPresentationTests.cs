using POE2Radar.Core.Game;
using Xunit;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Tests;

public sealed class AtlasRitualPresentationTests
{
    [Fact]
    public void BuildRows_KeepsPredictionWhenRoutePointsAreUnavailable()
    {
        var chain = new AtlasRitualPlanner.Chain(
            "line",
            [(1, 1), (2, 2), (3, 3)],
            [
                new AtlasRitualPlanner.Reward("First reward", ""),
                new AtlasRitualPlanner.Reward("Second reward", ""),
            ],
            "One > Two > Three",
            "First reward - Second reward",
            0);
        var names = new Dictionary<(int X, int Y), string>
        {
            [(1, 1)] = "One",
            [(2, 2)] = "Two",
            [(3, 3)] = "Three",
        };
        var centers = new Dictionary<(int X, int Y), NumVec2>
        {
            [(1, 1)] = new(100, 100),
            [(2, 2)] = new(200, 200),
            // The third node is outside the currently projected Atlas viewport.
        };

        var rows = AtlasRitualPresentation.BuildRows([chain], names, centers);

        var row = Assert.Single(rows);
        Assert.Equal(["One", "Two", "Three"], row.MapNames);
        Assert.Equal(["First reward", "Second reward"], row.Rewards);
        Assert.Empty(row.Points);
    }

    [Fact]
    public void ResolveRoutePoints_UsesCurrentAtlasFrame()
    {
        var chain = new AtlasRitualPlanner.Chain(
            "line",
            [(1, 1), (2, 2)],
            [new AtlasRitualPlanner.Reward("Reward", "")],
            "One > Two",
            "Reward",
            0);
        var names = new Dictionary<(int X, int Y), string>
        {
            [(1, 1)] = "One",
            [(2, 2)] = "Two",
        };
        var initial = new Dictionary<(int X, int Y), NumVec2>
        {
            [(1, 1)] = new(100, 100),
            [(2, 2)] = new(200, 200),
        };
        var panned = new Dictionary<(int X, int Y), NumVec2>
        {
            [(1, 1)] = new(350, 120),
            [(2, 2)] = new(450, 220),
        };
        var row = Assert.Single(AtlasRitualPresentation.BuildRows([chain], names, initial));

        var points = AtlasRitualPresentation.ResolveRoutePoints(row, panned);

        Assert.Equal([new NumVec2(350, 120), new NumVec2(450, 220)], points);
    }

    [Fact]
    public void FindRewardMatches_MapsFullModifierSearchToDestinationNode()
    {
        const string fullModifier = "Rerolling Favours costs 20% reduced Tribute";
        var chain = new AtlasRitualPlanner.Chain(
            "line",
            [(1, 1), (2, 2)],
            [new AtlasRitualPlanner.Reward(fullModifier, "")],
            "One > Two",
            "-Reroll Cost",
            0);
        var names = new Dictionary<(int X, int Y), string>
        {
            [(1, 1)] = "One",
            [(2, 2)] = "Two",
        };
        var centers = new Dictionary<(int X, int Y), NumVec2>
        {
            [(1, 1)] = new(100, 100),
            [(2, 2)] = new(200, 200),
        };
        var row = Assert.Single(AtlasRitualPresentation.BuildRows([chain], names, centers));

        var match = Assert.Single(AtlasRitualPresentation.FindRewardMatches(
            [row],
            "rerolling favours costs 20% reduced tribute"));

        Assert.Equal(new AtlasRitualGridNode(2, 2), match.Grid);
        Assert.Equal("-Reroll Cost", match.Label);
    }
}
