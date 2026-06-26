using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class ConservativeNavDefaultsTests
{
    [Fact]
    public void BuildDefault_UsesConservativeNavigableMechanics()
    {
        var rules = DisplayRules.BuildDefault(new RadarStyles(), showMonsters: true, watched: Array.Empty<WatchedEntry>());
        var byName = rules.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        Assert.True(byName["Breach"].Navigable);
        Assert.True(byName["Ritual"].Navigable);
        Assert.True(byName["Expedition"].Navigable);
        Assert.True(byName["Abyss · Pit"].Navigable);
        Assert.False(byName["Abyss"].Navigable);
        Assert.False(byName["Essence"].Navigable);
        Assert.False(byName["Strongbox"].Navigable);
        Assert.False(byName["Map marker"].Navigable);
        Assert.False(byName["Boss"].Navigable);
        Assert.False(byName["Monster · Rare"].Navigable);
    }

    [Fact]
    public void MigrateDisplayRules_TightensLegacyBroadNav()
    {
        var rules = DisplayRules.BuildDefault(new RadarStyles(), showMonsters: true, watched: Array.Empty<WatchedEntry>());
        foreach (var r in rules)
        {
            if (ConservativeNavDefaults.LegacyBroadNavRuleNames.Contains(r.Name)
                || r.Name.StartsWith("Strongbox", StringComparison.OrdinalIgnoreCase))
                r.Navigable = true;
        }

        var changed = ConservativeNavDefaults.MigrateDisplayRules(rules);

        Assert.True(changed);
        Assert.False(rules.First(r => r.Name == "Map marker").Navigable);
        Assert.False(rules.First(r => r.Name == "Essence").Navigable);
        Assert.True(rules.First(r => r.Name == "Breach").Navigable);
    }

    [Fact]
    public void FreshSettings_CuratedPathsOnByDefault_EntityAutoPathNeedsF3()
    {
        var s = new RadarSettings();

        Assert.True(s.ShowCuratedPaths);
        Assert.True(s.ShowEntityPaths);
        Assert.False(s.AutoPathNavigable);
        Assert.True(s.ShowPathMap);
    }
}
