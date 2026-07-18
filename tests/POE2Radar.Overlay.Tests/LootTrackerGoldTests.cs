using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class LootTrackerGoldTests
{
    [Theory]
    [InlineData(false, null, "AAAAAAAA", false)]
    [InlineData(true, "AAAAAAAA", "AAAAAAAA", false)]
    [InlineData(true, "AAAAAAAA", "BBBBBBBB", true)]
    public void NewMapBoundary_ResetsOnlyWhenEnteringADifferentMap(
        bool hasSession,
        string? latestHash,
        string nextHash,
        bool expectedReset)
        => Assert.Equal(
            expectedReset,
            RadarApp.ShouldResetLootSessionForMap(hasSession, latestHash, nextHash));

    [Theory]
    [InlineData("125 Gold", 125)]
    [InlineData("1,250 Gold", 1250)]
    [InlineData("Gold x345", 345)]
    [InlineData("Gold 9 876", 9876)]
    public void TryParseGoldLabel_ReadsVisibleLootLabelAmount(string text, long expected)
    {
        Assert.True(RadarApp.TryParseGoldLabel(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("Orb of Alchemy")]
    [InlineData("Golden Charm")]
    [InlineData("Goldrim")]
    public void TryParseGoldLabel_RejectsNonGoldLoot(string text)
        => Assert.False(RadarApp.TryParseGoldLabel(text, out _));

    [Fact]
    public void AccumulatePositiveLootDeltas_KeepsItemsThatAreLaterConsumedOrStashed()
    {
        var ledger = new Dictionary<string, long>(StringComparer.Ordinal);
        var baseline = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Alchemy"] = 10,
            ["Waystone"] = 1,
        };
        var afterPickup = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Alchemy"] = 12,
            ["Waystone"] = 2,
        };
        var afterUse = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Alchemy"] = 9,
            ["Waystone"] = 1,
        };

        Assert.True(RadarApp.AccumulatePositiveLootDeltas(ledger, afterPickup, baseline));
        Assert.False(RadarApp.AccumulatePositiveLootDeltas(ledger, afterUse, afterPickup));

        Assert.Equal(2, ledger["Alchemy"]);
        Assert.Equal(1, ledger["Waystone"]);
    }

    [Fact]
    public void AccumulatePositiveLootDeltas_AddsRepeatedPickupsWithoutCountingLosses()
    {
        var ledger = new Dictionary<string, long>(StringComparer.Ordinal);
        var first = new Dictionary<string, long>(StringComparer.Ordinal) { ["Exalted"] = 1 };
        var second = new Dictionary<string, long>(StringComparer.Ordinal) { ["Exalted"] = 3 };
        var spent = new Dictionary<string, long>(StringComparer.Ordinal) { ["Exalted"] = 2 };
        var pickedAgain = new Dictionary<string, long>(StringComparer.Ordinal) { ["Exalted"] = 5 };

        RadarApp.AccumulatePositiveLootDeltas(ledger, second, first);
        RadarApp.AccumulatePositiveLootDeltas(ledger, spent, second);
        RadarApp.AccumulatePositiveLootDeltas(ledger, pickedAgain, spent);

        Assert.Equal(5, ledger["Exalted"]);
    }

    [Fact]
    public void AggregateSessionValuedItems_MergesCompletedAndActiveMapLoot()
    {
        var completed = new[]
        {
            new RadarApp.LootValuedItem("Divine Orb", 1, 80, 80, true),
            new RadarApp.LootValuedItem("Unknown Relic", 2, 0, 0, false),
        };
        var active = new[]
        {
            new RadarApp.LootValuedItem("Divine Orb", 2, 80, 160, true),
            new RadarApp.LootValuedItem("Unknown Relic", 1, 0, 0, false),
        };

        var session = RadarApp.AggregateSessionValuedItems([completed, active]);

        Assert.Collection(
            session,
            divine =>
            {
                Assert.Equal("Divine Orb", divine.Label);
                Assert.Equal(3, divine.Count);
                Assert.Equal(240, divine.TotalEx);
                Assert.True(divine.Priced);
            },
            relic =>
            {
                Assert.Equal("Unknown Relic", relic.Label);
                Assert.Equal(3, relic.Count);
                Assert.Equal(0, relic.TotalEx);
                Assert.False(relic.Priced);
            });
    }
}
