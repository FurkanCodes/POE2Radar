using POE2Radar.Core.Game;
using POE2Radar.Overlay.Runecraft;
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
            blastRadius: 3);

        Assert.Equal(2, plan.Placements.Length);
        Assert.True(plan.Placements[0].Bridge);
        Assert.False(plan.Placements[1].Bridge);
        Assert.Equal(1, plan.CapturedCount);
        Assert.InRange(Vector2.Distance(plan.Placements[0].Grid, new Vector2(2, 2)), 0, 9.001f);
        Assert.InRange(Vector2.Distance(plan.Placements[1].Grid, plan.Placements[0].Grid), 0, 9.001f);
    }

    [Fact]
    public void DangerousRemnant_SteersRouteTowardSaferReward()
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
        Assert.Equal("Safe reward", placement.Label);
        Assert.Equal(60, plan.CapturedWeight);
    }

    [Fact]
    public void NetNegativeBlast_IsNeverRecommendedAsCapture()
    {
        var targets = new[]
        {
            new ExpeditionTarget(1, new Vector2(8, 2), 0, 50, ExpeditionTargetKind.RewardMarker, "Reward"),
            new ExpeditionTarget(2, new Vector2(8, 2), 0, -100, ExpeditionTargetKind.Remnant, "Build breaker"),
        };
        var plan = ExpeditionPlanner.Build(
            OpenTerrain(20, 12), new Vector2(2, 2), targets,
            chargeBudget: 1, placementDistance: 10, blastRadius: 3);

        Assert.Equal(0, plan.CapturedCount);
        Assert.DoesNotContain(plan.Placements, p => !p.Bridge);
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
