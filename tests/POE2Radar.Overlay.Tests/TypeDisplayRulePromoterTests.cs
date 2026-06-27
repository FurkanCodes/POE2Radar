using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class TypeDisplayRulePromoterTests
{
    [Fact]
    public void FindRuleIndex_matches_name_token_and_legacy_type_override()
    {
        var rules = new List<DisplayRule>
        {
            new() { Name = "Hide dead monsters", Categories = ["Monster"], Life = "Dead", Hide = true },
            new() { Name = "PetrosphereCluster", Match = ["PetrosphereCluster"], Categories = ["Object"] },
            new() { Name = "Type override: Foo", Match = ["Foo"] },
        };

        Assert.Equal(1, TypeDisplayRulePromoter.FindRuleIndex(rules, "PetrosphereCluster"));
        Assert.Equal(2, TypeDisplayRulePromoter.FindRuleIndex(rules, "Foo"));
        Assert.Equal(-1, TypeDisplayRulePromoter.FindRuleIndex(rules, "Missing"));
    }

    [Fact]
    public void RuleMatchesSearch_checks_name_and_match_terms()
    {
        var rule = new DisplayRule { Name = "Chest row", Match = ["PetrosphereCluster"] };
        Assert.True(TypeDisplayRulePromoter.RuleMatchesSearch(rule, "petro"));
        Assert.True(TypeDisplayRulePromoter.RuleMatchesSearch(rule, "chest"));
        Assert.False(TypeDisplayRulePromoter.RuleMatchesSearch(rule, "ritual"));
    }

    [Fact]
    public void PromoteInsertIndex_is_after_state_hide_rules()
    {
        var rules = new List<DisplayRule>
        {
            new() { Name = "Hide dead monsters", Categories = ["Monster"], Life = "Dead", Hide = true },
            new() { Name = "Hide opened chests", Categories = ["Chest"], Chest = "Opened", Hide = true },
            new() { Name = "Boss", Categories = ["Monster"] },
        };

        Assert.Equal(2, TypeDisplayRulePromoter.PromoteInsertIndex(rules));
    }

    [Fact]
    public void BuildRule_uses_dashboard_shape_and_is_visible()
    {
        var sample = new Poe2Live.EntityDot(
            Id: 1,
            Address: 0,
            Grid: default,
            World: default,
            TerrainHeight: 0f,
            Category: Poe2Live.EntityCategory.Object,
            Metadata: "Metadata/Chests/PetrosphereCluster",
            HpCur: 0,
            HpMax: 0,
            Poi: false,
            Reaction: 0,
            Rarity: Poe2Live.Rarity.Rare,
            Opened: false);

        var styles = new RadarStyles();
        var rule = TypeDisplayRulePromoter.BuildRule(sample, "PetrosphereCluster", null, styles, "PetrosphereCluster");

        Assert.Equal("PetrosphereCluster", rule.Name);
        Assert.Equal(["PetrosphereCluster"], rule.Match);
        Assert.Equal(["Object"], rule.Categories);
        Assert.False(rule.Hide);
        Assert.Equal(styles.ChestRare.Shape, rule.Shape);
    }

    [Fact]
    public void BuildRule_never_copies_hide_from_effective_rule()
    {
        var sample = new Poe2Live.EntityDot(
            Id: 1,
            Address: 0,
            Grid: default,
            World: default,
            TerrainHeight: 0f,
            Category: Poe2Live.EntityCategory.Object,
            Metadata: "Metadata/Items/Foo",
            HpCur: 0,
            HpMax: 0,
            Poi: false,
            Reaction: 0,
            Rarity: Poe2Live.Rarity.Normal,
            Opened: false);

        var effective = new DisplayRule
        {
            Hide = true,
            Shape = "Star",
            Color = "#ff0000",
            Navigable = true,
        };

        var rule = TypeDisplayRulePromoter.BuildRule(sample, "Foo", effective, new RadarStyles(), "Foo");
        Assert.False(rule.Hide);
        Assert.Equal("Star", rule.Shape);
        Assert.True(rule.Navigable);
    }

    [Fact]
    public void Promote_inserts_rule_and_skips_duplicate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drules_{Guid.NewGuid():N}.json");
        try
        {
            var displayRules = new DisplayRules(path);
            displayRules.Replace([
                new() { Name = "Hide dead monsters", Categories = ["Monster"], Life = "Dead", Hide = true },
            ]);

            var sample = new Poe2Live.EntityDot(
                Id: 1,
                Address: 0,
                Grid: default,
                World: default,
                TerrainHeight: 0f,
                Category: Poe2Live.EntityCategory.Object,
                Metadata: "Metadata/Items/SomeThing",
                HpCur: 0,
                HpMax: 0,
                Poi: false,
                Reaction: 0,
                Rarity: Poe2Live.Rarity.Normal,
                Opened: false);

            var styles = new RadarStyles();
            var (idx1, created1) = TypeDisplayRulePromoter.Promote(
                displayRules, null, null, "SomeThing", sample, null, styles, "Some Thing");
            var (idx2, created2) = TypeDisplayRulePromoter.Promote(
                displayRules, null, null, "SomeThing", sample, null, styles, "Some Thing");

            Assert.True(created1);
            Assert.False(created2);
            Assert.Equal(idx1, idx2);
            Assert.Equal(2, displayRules.Count);
            Assert.Equal("SomeThing", displayRules.All[idx1].Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
