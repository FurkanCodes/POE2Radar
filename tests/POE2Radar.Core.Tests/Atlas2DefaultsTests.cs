using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public class Atlas2DefaultsTests
{
    [Fact]
    public void Categories_IncludeSearchAndExpeditionWithMoor()
    {
        Assert.Contains(Atlas2Defaults.Categories, c => c.BuiltInKey == "search" && c.DrawPath);
        var exp = Assert.Single(Atlas2Defaults.Categories, c => c.BuiltInKey == "expedition");
        Assert.Contains(exp.Targets, t => t.Name == "Moor of Fallen Skies" && t.Enabled);
    }

    [Fact]
    public void RitualMods_PoolLoadsFromEmbeddedJson()
    {
        Assert.NotEmpty(AtlasRitualPrediction.Mods);
        Assert.Contains(AtlasRitualPrediction.Mods, m => m.Text.Contains("Tribute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RitualLineMode_CurrentLiveValueOne_IsActive()
    {
        Assert.True(AtlasRitualPrediction.IsLineModeActive(1));
        Assert.False(AtlasRitualPrediction.IsLineModeActive(0));
    }

    [Fact]
    public void TinyMt_SeedAndJump_MatchesAtlas2GoldenVectors()
    {
        Assert.Equal(0x8D15DD1Eu, AtlasRitualPrediction.TinyMt32.SeedAndJump(1, 2, 3, 4));
        Assert.Equal(0x99BC8397u, AtlasRitualPrediction.TinyMt32.SeedAndJump(0x12345678, 0, 0, 0));
        Assert.Equal(0x4C3BA38Bu, AtlasRitualPrediction.TinyMt32.SeedAndJump(99, 5, 2, 0));
        Assert.Equal(0x135B77D4u, AtlasRitualPrediction.TinyMt32.SeedAndJump(99, 5, 2, 0x91DA3AD9));
    }

    [Fact]
    public void FilterPool_RequiresLiveConditionStat()
    {
        var unconditional = AtlasRitualPrediction.FilterPool(new Dictionary<int, int>());
        Assert.All(unconditional, mod => Assert.Equal(0, mod.Cond));

        var conditional = AtlasRitualPrediction.Mods.First(mod => mod.Cond != 0);
        var enabled = AtlasRitualPrediction.FilterPool(new Dictionary<int, int> { [conditional.Cond] = 1 });
        Assert.Contains(enabled, mod => mod.Row == conditional.Row);
    }

    [Fact]
    public void RitualPlanner_RejectsBranchesThatCannotReachFullLength()
    {
        var candidates = new Dictionary<(int X, int Y), List<(int X, int Y)>>
        {
            [(0, 0)] = [(1, 0), (0, 1)],
            [(1, 0)] = [(2, 0)],
            [(2, 0)] = [(3, 0)],
            [(0, 1)] = [],
        };
        var state = new Poe2Atlas.RitualLineSnapshot(
            123,
            Array.Empty<(int, int)>(),
            Array.Empty<(int, int)>(),
            candidates,
            new Dictionary<int, int>());
        var nodes = new[]
        {
            new AtlasRitualPlanner.NodeInfo(0, 0, "Start", true, false),
            new AtlasRitualPlanner.NodeInfo(1, 0, "One", false, false),
            new AtlasRitualPlanner.NodeInfo(2, 0, "Two", false, false),
            new AtlasRitualPlanner.NodeInfo(3, 0, "Three", false, false),
            new AtlasRitualPlanner.NodeInfo(0, 1, "Dead End", false, false),
        };

        var plan = AtlasRitualPlanner.BuildChains(
            state,
            nodes,
            lineLength: 4,
            AtlasRitualPrediction.FilterPool(null),
            secondChance: 0,
            maxPaths: 32);

        var chain = Assert.Single(plan.Chains);
        Assert.Equal([(0, 0), (1, 0), (2, 0), (3, 0)], chain.Nodes);
        Assert.DoesNotContain((0, 1), chain.Nodes);
    }

    [Fact]
    public void RitualPlanner_SelectDisplayChains_ReturnsSixCompleteChoices()
    {
        var chains = Enumerable.Range(1, 9)
            .Select(index => new AtlasRitualPlanner.Chain(
                $"line-{index}",
                Enumerable.Range(1, 6).Select(step => (index, step)).ToArray(),
                Enumerable.Range(1, 5)
                    .Select(step => new AtlasRitualPlanner.Reward($"Reward {index}.{step}", ""))
                    .ToArray(),
                $"Line {index}",
                $"Rewards {index}",
                100 - index))
            .ToArray();
        var plan = new AtlasRitualPlanner.Plan(chains, 9, 9, false, 6);

        var displayed = AtlasRitualPlanner.SelectDisplayChains(plan, 6);

        Assert.Equal(6, displayed.Count);
        Assert.All(displayed, chain => Assert.Equal(6, chain.Nodes.Count));
        Assert.Equal(chains.Take(6).Select(chain => chain.Key), displayed.Select(chain => chain.Key));
    }

    [Fact]
    public void RitualPlanner_SelectDisplayChains_PrioritizesCurrentViewport()
    {
        var chains = Enumerable.Range(1, 8)
            .Select(index => new AtlasRitualPlanner.Chain(
                $"line-{index}",
                [(index, 0), (index, 1)],
                [new AtlasRitualPlanner.Reward($"Reward {index}", "")],
                $"Line {index}",
                $"Reward {index}",
                0))
            .ToArray();
        var plan = new AtlasRitualPlanner.Plan(chains, 8, 8, false, 2);

        var leftViewport = AtlasRitualPlanner.SelectDisplayChains(
            plan,
            6,
            priorityNodes: new HashSet<(int X, int Y)> { (2, 0) });
        var rightViewport = AtlasRitualPlanner.SelectDisplayChains(
            plan,
            6,
            priorityNodes: new HashSet<(int X, int Y)> { (8, 0) });

        Assert.Equal("line-2", leftViewport[0].Key);
        Assert.Equal("line-8", rightViewport[0].Key);
        Assert.NotEqual(
            leftViewport.Select(chain => chain.Key),
            rightViewport.Select(chain => chain.Key));
    }

    [Fact]
    public void RitualPlanner_RewardQuery_MatchesFullUnshortenedModifier()
    {
        const string fullModifier = "Rerolling Favours costs 20% reduced Tribute";
        var chain = new AtlasRitualPlanner.Chain(
            "line",
            [(1, 1), (2, 2)],
            [new AtlasRitualPlanner.Reward(fullModifier, "")],
            "One > Two",
            "-Reroll Cost",
            0);

        Assert.True(AtlasRitualPlanner.MatchesRewardQuery(
            chain,
            "rerolling favours costs 20% reduced tribute"));
    }

    [Fact]
    public void IslandRumours_BuildsDecodedManifestAndAggregatesDuplicates()
    {
        var nodes = new[]
        {
            AtlasNode(65, -42, "Moor of Fallen Skies"),
            AtlasNode(69, -44, "Moor of Fallen Skies"),
            AtlasNode(78, -40, "Castaway"),
            AtlasNode(82, -40, "Steppe"),
            AtlasNode(17, 17, "Frigid Bluffs"),
        };

        var manifests = AtlasIslandRumours.Build(nodes);

        var primary = Assert.Single(manifests, manifest => manifest.ChunkX == 4 && manifest.ChunkY == -3);
        Assert.Equal(3, primary.TotalIslands);
        Assert.True(primary.HasMoorOfFallenSkies);
        var castaway = Assert.Single(primary.Rows, row => row.Definition.Destination == "Castaway");
        Assert.Equal("All that glitters...", castaway.Definition.Rumour);
        Assert.Equal("A", castaway.Definition.Tier);
        Assert.Equal(1, castaway.Count);
        var fallenSkies = Assert.Single(
            primary.Rows,
            row => row.Definition.Destination == "Moor of Fallen Skies");
        Assert.Equal("Fallen stars...", fallenSkies.Definition.Rumour);
        Assert.Equal("S+", fallenSkies.Definition.Tier);
        Assert.Equal(2, fallenSkies.Count);
        Assert.DoesNotContain(primary.Rows, row => row.Definition.Destination == "Steppe");
    }

    [Fact]
    public void IslandRumours_CatalogCoversAllTwentyKnownRumours()
    {
        Assert.Equal(20, AtlasIslandRumours.Definitions.Count);
        Assert.Equal(
            AtlasIslandRumours.Definitions.Count,
            AtlasIslandRumours.Definitions.Select(definition => definition.Rumour)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(AtlasIslandRumours.Definitions, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Tier));
            Assert.Matches("^#[0-9A-Fa-f]{6}$", definition.TierColor);
            Assert.False(string.IsNullOrWhiteSpace(definition.Preparation.Investment));
            Assert.False(string.IsNullOrWhiteSpace(definition.Preparation.Tablets));
            Assert.False(string.IsNullOrWhiteSpace(definition.Preparation.Waystone));
        });
        var moor = Assert.Single(AtlasIslandRumours.Definitions, definition => definition.IsMoorPriority);
        Assert.Equal("Moor of Fallen Skies", moor.Destination);
        Assert.Equal("MAX JUICE", moor.Preparation.Investment);
        Assert.Matches("^#[0-9A-Fa-f]{6}$", AtlasIslandRumours.MoorPriorityColor);
        Assert.True(AtlasIslandRumours.TryGetDefinition("Grazed Prairie", out var warm));
        Assert.Equal("Warm but risky...", warm.Rumour);
        Assert.Equal("B", warm.Tier);
        Assert.Contains("Experience", warm.Summary);
        Assert.True(AtlasIslandRumours.TryGetDefinition("Lush Isle", out var wild));
        Assert.Equal("Wild roaming free...", wild.Rumour);
        Assert.Equal("D", wild.Tier);
        Assert.Contains("Azmeri", wild.Summary);
        Assert.True(AtlasIslandRumours.TryGetDefinition("Exhumed Ruins", out var exhumed));
        Assert.Equal("Unknown ruins...", exhumed.Rumour);
        Assert.Equal("B", exhumed.Tier);
    }

    private static Poe2Atlas.AtlasNodeLive AtlasNode(int gridX, int gridY, string mapName)
        => new(
            Element: 0,
            Id: 0,
            Content: 0,
            State: 0,
            Biome: 0,
            Flags: 0,
            Completion: 0,
            X: 0,
            Y: 0,
            W: 32,
            H: 32,
            Scale: 1,
            Visible: false,
            IconType: 0,
            ScreenX: 0,
            ScreenY: 0,
            ScreenW: 32,
            ScreenH: 32,
            GridX: gridX,
            GridY: gridY,
            MapName: mapName,
            Tags: Array.Empty<string>());
}
