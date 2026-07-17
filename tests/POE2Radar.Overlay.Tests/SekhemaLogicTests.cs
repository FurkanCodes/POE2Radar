using System.Numerics;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Sekhema;
using Xunit;
using Vector2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Tests;

public sealed class SekhemaLogicTests
{
    [Fact]
    public void Profiles_MatchPluginDefaultsAndNoHitOverrides()
    {
        var normal = SekhemaProfileSettings.CreateDefault();
        var noHit = SekhemaProfileSettings.CreateNoHit();

        Assert.Equal(-1000, normal.RoomTypeWeights["Gauntlet"]);
        Assert.Equal(100, normal.RoomTypeWeights["Escape"]);
        Assert.Equal(200, normal.RewardWeights["Boon"]);
        Assert.Equal(-600000, noHit.AfflictionWeights["Spiked Exit"]);
        Assert.Equal(0, noHit.AfflictionWeights["Ghastly Scythe"]);
    }

    [Fact]
    public void ScoreRoom_SuppressesMerchantAndHonourAtLiveThresholds()
    {
        var settings = new SekhemaSettings();
        var merchant = Room(0, 0, reward: "Merchant");
        var honour = Room(0, 0, reward: "Honour");

        var lowWater = SekhemaLogic.ScoreRoom(
            merchant,
            settings,
            new Poe2Live.SekhemaResources(249, 0, 0, 0, 50),
            default);
        var highHonour = SekhemaLogic.ScoreRoom(
            honour,
            settings,
            new Poe2Live.SekhemaResources(500, 0, 0, 0, 81),
            default);

        Assert.Equal(999700, lowWater.Weight);
        Assert.Contains("low water", lowWater.Debug);
        Assert.Equal(999700, highHonour.Weight);
        Assert.Contains("honour 81%>80%", highHonour.Debug);
    }

    [Fact]
    public void ScoreRoom_UsesDefensiveStatsForDynamicAfflictions()
    {
        var room = Room(0, 0, affliction: "Iron Manacles");
        var score = SekhemaLogic.ScoreRoom(
            room,
            new SekhemaSettings(),
            Poe2Live.SekhemaResources.Unknown,
            new Poe2Live.SekhemaPlayerStats(3000, 0, 1000, 1000, false));

        Assert.Equal(996250, score.Weight);
        Assert.Contains("(dyn)", score.Debug);
    }

    [Fact]
    public void BestPath_UsesTotalWeightThenConnectivityTieBreak()
    {
        var layers = new[]
        {
            new[] { Room(0, 0, [0, 1]) },
            new[] { Room(1, 0, [0]), Room(1, 1, [1]) },
            new[] { Room(2, 0), Room(2, 1, [0, 1]) },
        };
        var floor = new Poe2Live.SekhemaFloorRead(
            true, true, 1, 2, 0, 0, layers,
            Poe2Live.SekhemaResources.Unknown, default, "ok");
        var weights = new Dictionary<(int, int), double>
        {
            [(0, 0)] = 0,
            [(1, 0)] = 10,
            [(1, 1)] = 10,
            [(2, 0)] = 10,
            [(2, 1)] = 10,
        };

        var path = SekhemaLogic.FindBestPath(floor, weights);

        Assert.Equal([(0, 0), (1, 1), (2, 1)], path);
    }

    [Theory]
    [InlineData("Metadata/Chests/MarakethSanctum/GoldChestGrandSpectrum3", 2, "GrandSpectrum", 3)]
    [InlineData("Metadata/Chests/MarakethSanctum/SilverChestJewels2", 1, "Jewels", 2)]
    [InlineData("Metadata/Chests/MarakethSanctum/BronzeChestGeneric", 0, "Generic", 1)]
    public void ChestParser_MatchesTierContentAndQuality(
        string metadata,
        int expectedTier,
        string expectedContent,
        int expectedQuality)
    {
        Assert.True(SekhemaLogic.TryParseChest(metadata, out var tier, out var content, out var quality));
        Assert.Equal(expectedTier, (int)tier);
        Assert.Equal(expectedContent, content);
        Assert.Equal(expectedQuality, quality);
    }

    [Fact]
    public void ChestSelection_UsesLiveKeyBudgetPriorityQualityThenDistance()
    {
        var candidates = new[]
        {
            Chest(1, "Jewels", 1, 5, 10),
            Chest(2, "GrandSpectrum", 1, 9, 20),
            Chest(3, "GrandSpectrum", 3, 9, 30),
            Chest(4, "GrandSpectrum", 3, 9, 5),
        };

        var selected = SekhemaLogic.SelectChests(candidates, bronzeKeys: 2, silverKeys: 0, goldKeys: 0);

        Assert.Equal([4u, 3u], selected.Select(chest => chest.Id));
    }

    [Fact]
    public void CrystalGrouping_SelectsClosestContiguousEntityIdGroup()
    {
        var crystals = new (uint Id, Vector2 Grid)[]
        {
            (100, new Vector2(10, 10)),
            (102, new Vector2(20, 10)),
            (200, new Vector2(500, 500)),
            (202, new Vector2(510, 500)),
        };

        var selected = SekhemaLogic.PlayerCrystalRoom(crystals, new Vector2(505, 505), maxIdGap: 10);

        Assert.Equal([2, 3], selected);
        Assert.True(SekhemaLogic.PlayerInsideCrystalRoom(
            selected.Select(index => crystals[index].Grid).ToArray(),
            new Vector2(505, 505),
            margin: 30));
    }

    [Fact]
    public void CrystalSelection_RoutesEveryActiveCrystalInCurrentRoomRegardlessOfDistance()
    {
        var crystals = new (uint Id, Vector2 Grid, bool Active)[]
        {
            (100, new Vector2(0, 0), true),
            (101, new Vector2(50, 50), true),
            (102, new Vector2(100, 100), true),
            (103, new Vector2(80, 80), false),
            (300, new Vector2(1000, 1000), true),
        };

        var selected = SekhemaLogic.SelectRouteCrystals(
            crystals,
            new Vector2(50, 50),
            new SekhemaSettings());

        Assert.Equal([0, 1, 2], selected);
    }

    [Fact]
    public void StraightCrystalRoute_VisitsNearestCrystalFirst()
    {
        var legs = SekhemaRoutePlanner.Build(
            terrain: null,
            player: Vector2.Zero,
            crystals: [new Vector2(10, 0), new Vector2(2, 0), new Vector2(6, 0)],
            forcedWalkable: [],
            followWalkable: false);

        Assert.Equal(3, legs.Length);
        Assert.False(legs[0].Walkable);
        Assert.Equal(new Vector2(2, 0), legs[0].Points[^1]);
        Assert.Equal(new Vector2(6, 0), legs[1].Points[^1]);
        Assert.Equal(new Vector2(10, 0), legs[2].Points[^1]);
    }

    [Fact]
    public void WalkableCrystalRoute_UsesAStarAroundBlockedCells()
    {
        const int width = 12;
        const int height = 7;
        var walkable = Enumerable.Repeat((byte)1, width * height).ToArray();
        for (var y = 0; y < height - 1; y++)
            walkable[y * width + 5] = 0;
        var terrain = new Poe2Live.TerrainData(walkable, width, height);

        var legs = SekhemaRoutePlanner.Build(
            terrain,
            player: new Vector2(2, 2),
            crystals: [new Vector2(9, 2)],
            forcedWalkable: [],
            followWalkable: true);

        Assert.Single(legs);
        Assert.True(legs[0].Walkable);
        Assert.True(legs[0].Points.Length >= 3);
        Assert.Contains(legs[0].Points, point => point.Y >= height - 1);
    }

    private static Poe2Live.SekhemaRoomRead Room(
        int layer,
        int index,
        int[]? connections = null,
        string reward = "",
        string affliction = "")
        => new()
        {
            Layer = layer,
            Index = index,
            NextConnections = connections ?? [],
            Reward = reward,
            Affliction = affliction,
        };

    private static SekhemaLogic.ChestCandidate Chest(
        uint id,
        string content,
        int quality,
        int priority,
        float distance)
        => new(
            id,
            SekhemaLogic.ChestTier.Bronze,
            content,
            quality,
            Vector2.Zero,
            0,
            distance,
            priority);
}
