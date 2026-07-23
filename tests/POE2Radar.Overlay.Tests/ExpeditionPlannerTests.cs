using POE2Radar.Core.Game;
using POE2Radar.Overlay.Runecraft;
using System.Diagnostics;
using Xunit;
using Vector2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Tests;

public sealed class ExpeditionPlannerTests
{
    [Fact]
    public void NearbyTarget_IsCoveredByOnePlacement()
    {
        var plan = ExpeditionPlanner.Build(
            OpenTerrain(24, 24),
            new Vector2(2, 2),
            [new ExpeditionTarget(1, new Vector2(8, 2), 0, 50, ExpeditionTargetKind.RewardMarker, "Currency")],
            chargeBudget: 1,
            placementDistance: 10,
            blastRadius: 3);

        var placement = Assert.Single(plan.Placements);
        Assert.False(placement.Bridge);
        Assert.Equal(1, plan.CapturedCount);
        Assert.InRange(Vector2.Distance(placement.Grid, new Vector2(8, 2)), 0, 3.001f);
        Assert.InRange(Vector2.Distance(placement.Grid, new Vector2(2, 2)), 0, 9.001f);
    }

    [Fact]
    public void DistantTarget_UsesBridgeThenCapture()
    {
        var plan = ExpeditionPlanner.Build(
            OpenTerrain(40, 16),
            new Vector2(2, 2),
            [new ExpeditionTarget(1, new Vector2(20, 2), 0, 50, ExpeditionTargetKind.RewardMarker, "Logbook")],
            chargeBudget: 2,
            placementDistance: 10,
            blastRadius: 4);

        Assert.Equal(2, plan.Placements.Length);
        Assert.True(plan.Placements[0].Bridge);
        Assert.False(plan.Placements[1].Bridge);
        Assert.Equal(1, plan.CapturedCount);
        Assert.InRange(Vector2.Distance(plan.Placements[0].Grid, new Vector2(2, 2)), 0, 9.001f);
        Assert.InRange(Vector2.Distance(plan.Placements[1].Grid, plan.Placements[0].Grid), 0, 10.001f);
    }

    [Fact]
    public void NonPositiveRemnant_IsNotPromotedToARouteAnchor()
    {
        var targets = new[]
        {
            new ExpeditionTarget(1, new Vector2(10, 2), 0, 100, ExpeditionTargetKind.RewardMarker, "High reward"),
            new ExpeditionTarget(2, new Vector2(10, 2), 0, -200, ExpeditionTargetKind.Remnant, "Build breaker"),
            new ExpeditionTarget(3, new Vector2(2, 10), 0, 60, ExpeditionTargetKind.RewardMarker, "Safe reward"),
        };
        var plan = ExpeditionPlanner.Build(
            OpenTerrain(24, 24), new Vector2(2, 2), targets,
            chargeBudget: 1, placementDistance: 12, blastRadius: 2);

        var placement = Assert.Single(plan.Placements);
        Assert.Equal("High reward", placement.Label);
        Assert.Equal(100, plan.CapturedWeight);
    }

    [Fact]
    public void RunestonePrimary_DrivesTheSpineBeforeHigherWeightSecondaryMarkers()
    {
        var targets = new[]
        {
            new ExpeditionTarget(
                1, new Vector2(26, 2), 0, 50,
                ExpeditionTargetKind.Monolith, "Runestone", Primary: true),
            new ExpeditionTarget(
                2, new Vector2(2, 8), 0, 1_000,
                ExpeditionTargetKind.RewardMarker, "Nearby marker", Primary: false),
        };

        var plan = ExpeditionPlanner.Build(
            OpenTerrain(40, 20), new Vector2(2, 2), targets,
            chargeBudget: 3, placementDistance: 10, blastRadius: 2);

        var first = Assert.IsType<ExpeditionPlacement>(plan.Placements.First());
        Assert.True(
            first.Grid.X > first.Grid.Y,
            $"First placement {first.Grid} followed a secondary marker instead of the runestone spine.");
        Assert.Contains(plan.Placements, p => p.Label.Contains("Runestone", StringComparison.Ordinal));
    }

    [Fact]
    public void GameHelperGrandRewardWeights_IncludeRuneMarkersAndAllowOverrides()
    {
        Assert.Equal(1f, RadarApp.ExpeditionGrandRewardWeight("RewardChestRunes", null));
        Assert.Equal(25f, RadarApp.ExpeditionGrandRewardWeight("RewardChestCurrency", null));
        Assert.Equal(40f, RadarApp.ExpeditionGrandRewardWeight("RewardChestCurrencyRare", null));

        var overrides = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["RewardChestRunes"] = 75f,
        };
        Assert.Equal(75f, RadarApp.ExpeditionGrandRewardWeight("RewardChestRunes", overrides));
    }

    [Fact]
    public void NonPositiveTarget_DoesNotSuppressAPositiveRouteAnchor()
    {
        var targets = new[]
        {
            new ExpeditionTarget(1, new Vector2(8, 2), 0, 50, ExpeditionTargetKind.RewardMarker, "Reward"),
            new ExpeditionTarget(2, new Vector2(8, 2), 0, -100, ExpeditionTargetKind.Remnant, "Build breaker"),
        };
        var plan = ExpeditionPlanner.Build(
            OpenTerrain(20, 12), new Vector2(2, 2), targets,
            chargeBudget: 1, placementDistance: 10, blastRadius: 3);

        Assert.Equal(1, plan.CapturedCount);
        Assert.Contains(plan.Placements, p => !p.Bridge);
    }

    [Fact]
    public void DensePlan_NeverRecommendsTheSamePlacementTwice()
    {
        var random = new Random(7731);
        var targets = Enumerable.Range(1, 80)
            .Select(id => new ExpeditionTarget(
                (uint)id,
                new Vector2(random.Next(8, 152), random.Next(8, 152)),
                0,
                random.Next(1, 80),
                ExpeditionTargetKind.RewardMarker,
                $"Reward {id}"))
            .ToArray();

        var plan = ExpeditionPlanner.Build(
            OpenTerrain(160, 160),
            new Vector2(4, 4),
            targets,
            chargeBudget: 20,
            placementDistance: 18,
            blastRadius: 7);

        for (var i = 0; i < plan.Placements.Length; i++)
        for (var j = i + 1; j < plan.Placements.Length; j++)
            Assert.True(
                Vector2.Distance(plan.Placements[i].Grid, plan.Placements[j].Grid) >= 1f,
                $"Placements {i + 1} and {j + 1} overlap at {plan.Placements[i].Grid}.");
    }

    [Fact]
    public void DensePlan_CompletesWithinInteractiveBudget()
    {
        var random = new Random(7731);
        var targets = Enumerable.Range(1, 80)
            .Select(id => new ExpeditionTarget(
                (uint)id,
                new Vector2(random.Next(8, 152), random.Next(8, 152)),
                0,
                random.Next(1, 80),
                ExpeditionTargetKind.RewardMarker,
                $"Reward {id}"))
            .ToArray();
        var stopwatch = Stopwatch.StartNew();

        _ = ExpeditionPlanner.Build(
            OpenTerrain(160, 160),
            new Vector2(4, 4),
            targets,
            chargeBudget: 20,
            placementDistance: 18,
            blastRadius: 7);

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(750),
            $"Dense plan took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public void PlacingAnExplosive_DoesNotRecalculateTheLockedFullRoute()
    {
        Assert.False(RadarApp.ShouldStartExpeditionPlan(
            manualRunRequested: false,
            taskRunning: false));
    }

    [Fact]
    public void ManualRun_RecalculatesALockedPlan()
    {
        Assert.True(RadarApp.ShouldStartExpeditionPlan(
            manualRunRequested: true,
            taskRunning: false));
    }

    [Fact]
    public void EncounterWithoutAPlan_WaitsForTheGameHelperStyleRunButton()
    {
        Assert.False(RadarApp.ShouldStartExpeditionPlan(
            manualRunRequested: false,
            taskRunning: false));
        Assert.False(RadarApp.ShouldStartExpeditionPlan(
            manualRunRequested: true,
            taskRunning: true));
    }

    [Fact]
    public void RemovingAllExplosives_ResetsNextToTheFirstPointOfTheLockedRoute()
    {
        Assert.Equal(3, RadarApp.ExpeditionNextRouteIndex(placed: 3, routeLength: 8));
        Assert.Equal(0, RadarApp.ExpeditionNextRouteIndex(placed: 0, routeLength: 8));
    }

    [Fact]
    public void DisconnectedTargets_DoNotTriggerAFullFloodForEveryTarget()
    {
        const int width = 360;
        const int height = 240;
        var walkable = Enumerable.Repeat((byte)1, width * height).ToArray();
        for (var y = 0; y < height; y++)
            walkable[y * width + width / 2] = 0;
        var terrain = new Poe2Live.TerrainData(walkable, width, height);
        var targets = Enumerable.Range(1, 120)
            .Select(id => new ExpeditionTarget(
                (uint)id,
                new Vector2(width / 2 + 10 + id % 120, 10 + id * 17 % (height - 20)),
                0,
                10,
                ExpeditionTargetKind.RewardMarker,
                $"Blocked {id}"))
            .ToArray();
        var stopwatch = Stopwatch.StartNew();

        var plan = ExpeditionPlanner.Build(
            terrain,
            new Vector2(20, height / 2),
            targets,
            chargeBudget: 20,
            placementDistance: 18,
            blastRadius: 7);

        stopwatch.Stop();
        Assert.Equal(0, plan.CapturedCount);
        Assert.All(plan.Placements, placement => Assert.True(placement.Bridge));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
            $"Disconnected plan took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public void ExplicitComputeBudget_ReturnsPromptlyWithHonestPartialStatus()
    {
        var target = new ExpeditionTarget(
            1,
            new Vector2(580, 580),
            0,
            100,
            ExpeditionTargetKind.RewardMarker,
            "Far reward");
        var stopwatch = Stopwatch.StartNew();

        var plan = ExpeditionPlanner.Build(
            OpenTerrain(600, 600),
            new Vector2(2, 2),
            [target],
            chargeBudget: 20,
            placementDistance: 18,
            blastRadius: 7,
            computeBudget: TimeSpan.FromMilliseconds(1));

        stopwatch.Stop();
        Assert.Contains("budget", plan.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(250),
            $"Budgeted plan took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }

    [Theory]
    [InlineData("Metadata/MiscellaneousObjects/Expedition/ExpeditionDetonator")]
    [InlineData("Metadata/MiscellaneousObjects/Expedition/ExpeditionMarker")]
    [InlineData("Metadata/Chests/LeaguesExpedition/ExpeditionChest1")]
    [InlineData("Metadata/Terrain/Leagues/Expedition2/Objects/Expedition2Encounter")]
    public void ExpeditionMetadata_IsRetainedOutsideNormalDrawRadius(string metadata)
        => Assert.True(RadarApp.IsExpeditionPlannerEntity(metadata));

    private static Poe2Live.TerrainData OpenTerrain(int width, int height)
        => new(Enumerable.Repeat((byte)1, width * height).ToArray(), width, height);
}
