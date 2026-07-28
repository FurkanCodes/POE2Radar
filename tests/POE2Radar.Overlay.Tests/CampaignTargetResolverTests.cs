using POE2Radar.Core.Campaign;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Campaign;
using Xunit;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Tests;

public sealed class CampaignTargetResolverTests
{
    [Fact]
    public void Resolver_UsesFullExactGlobAndNeverFuzzyDisplayNames()
    {
        var objective = Objective(
            "Metadata/monsters/swollenmiller/swollenmiller",
            "G1_1");
        var frame = Frame(
            "G1_1",
            Entity(1, "Metadata/monsters/swollenmiller/swollenmiller", 30, 0),
            Entity(2, "Metadata/monsters/swollenmiller/swollenmillerstandalone", 2, 0),
            Entity(3, "The Bloated Miller", 1, 0));

        var result = new CampaignTargetResolver().Resolve(objective, frame);

        Assert.Equal(CampaignTargetStatus.Resolved, result.Status);
        Assert.Equal((uint)1, result.EntityId);
        Assert.Equal(1, result.CandidateCount);
    }

    [Fact]
    public void Resolver_ReportsDuplicatesAndChoosesNearestDeterministically()
    {
        var objective = Objective("Metadata/quest/*", "G1_1");
        var frame = Frame(
            "G1_1",
            Entity(7, "Metadata/quest/a", 20, 0),
            Entity(4, "Metadata/quest/b", 4, 0));

        var result = new CampaignTargetResolver().Resolve(objective, frame);

        Assert.Equal(CampaignTargetStatus.MultipleCandidates, result.Status);
        Assert.Equal((uint)4, result.EntityId);
        Assert.Equal(2, result.CandidateCount);
    }

    [Fact]
    public void Resolver_RefusesTargetsOutsideAllowedArea()
    {
        var objective = Objective("Metadata/quest/*", "G1_1");

        var result = new CampaignTargetResolver().Resolve(
            objective,
            Frame("G1_2", Entity(1, "Metadata/quest/a", 1, 0)));

        Assert.Equal(CampaignTargetStatus.WrongArea, result.Status);
        Assert.Null(result.Grid);
    }

    private static CampaignObjective Objective(string glob, string area)
        => new()
        {
            Id = "test",
            Chapter = "act1",
            Order = 1,
            Text = "test",
            Target = new CampaignTargetSpec
            {
                Kind = CampaignTargetKind.Boss,
                Label = "Target",
                AllowedAreaCodes = [area],
                MetadataGlobs = [glob],
                Validated = true,
            },
        };

    private static CampaignFrame Frame(string area, params Poe2Live.EntityDot[] entities)
        => new(
            area, 1, NumVec2.Zero, "League", "Character", entities,
            Array.Empty<Poe2Live.Landmark>(),
            Array.Empty<Poe2Live.ServerMinimapIcon>());

    internal static Poe2Live.EntityDot Entity(
        uint id,
        string metadata,
        float x,
        float y,
        int hpCur = 10,
        int hpMax = 10,
        bool opened = false,
        bool iconComplete = false)
        => new(
            id, (nint)id, new NumVec2(x, y), default, 0,
            Poe2Live.EntityCategory.Monster, metadata,
            hpCur, hpMax, true, 0, Poe2Live.Rarity.Unique,
            opened, iconComplete);
}
