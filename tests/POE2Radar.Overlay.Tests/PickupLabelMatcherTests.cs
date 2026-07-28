using POE2Radar.Overlay.Pickup;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class PickupLabelMatcherTests
{
    [Fact]
    public void Match_UsesGlobalAssignmentToKeepBothCrowdedItems()
    {
        var items = new[]
        {
            new PickupMatchItem(0, "Chaos Orb", 50f, 0f),
            new PickupMatchItem(1, "Chaos Orb", 0f, 0f),
        };
        var labels = new[]
        {
            Label(0, "Chaos Orb", 0f, 0f),
            Label(1, "Chaos Orb", 100f, 0f),
        };

        var matches = PickupLabelMatcher.Match(items, labels, maxScreenDistance: 75f);

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, x => x.ItemIndex == 0 && x.LabelIndex == 1);
        Assert.Contains(matches, x => x.ItemIndex == 1 && x.LabelIndex == 0);
    }

    [Fact]
    public void Match_RejectsImplausiblyDistantLabels()
    {
        var items = new[] { new PickupMatchItem(0, "Divine Orb", 0f, 0f) };
        var labels = new[] { Label(0, "Divine Orb", 500f, 0f) };

        var matches = PickupLabelMatcher.Match(items, labels, maxScreenDistance: 200f);

        Assert.Empty(matches);
    }

    [Fact]
    public void Match_DoesNotPairDifferentVisibleItemNames()
    {
        var items = new[] { new PickupMatchItem(0, "Divine Orb", 10f, 10f) };
        var labels = new[] { Label(0, "Exalted Orb", 10f, 10f) };

        var matches = PickupLabelMatcher.Match(items, labels, maxScreenDistance: 200f);

        Assert.Empty(matches);
    }

    [Fact]
    public void Match_RejectsNonFiniteProjectionCoordinates()
    {
        var items = new[] { new PickupMatchItem(0, "Divine Orb", float.NaN, 10f) };
        var labels = new[] { Label(0, "Divine Orb", 10f, 10f) };

        var matches = PickupLabelMatcher.Match(items, labels, maxScreenDistance: 200f);

        Assert.Empty(matches);
    }

    [Fact]
    public void Match_NeverAssignsOneMultiLineLabelToTwoItems()
    {
        var items = new[]
        {
            new PickupMatchItem(0, "Divine Orb", 10f, 10f),
            new PickupMatchItem(1, "Exalted Orb", 12f, 10f),
        };
        var lines = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Divine Orb",
            "Exalted Orb",
        };
        var labels = new[] { new PickupMatchLabel(0, 10f, 10f, lines) };

        var matches = PickupLabelMatcher.Match(items, labels, maxScreenDistance: 200f);

        Assert.Single(matches);
    }

    private static PickupMatchLabel Label(int index, string line, float x, float y)
        => new(index, x, y, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { line });
}
