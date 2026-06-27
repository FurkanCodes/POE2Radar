using System.Text.Json;
using POE2Radar.Overlay;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class CuratedDisplayRulesSnapshotTests
{
  private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Regenerate embedded default: set POE2RADAR_REGEN_DEFAULTS=1 and run this test.</summary>
    [Fact]
    public void RegenEmbeddedDisplayRulesDefaults()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("POE2RADAR_REGEN_DEFAULTS"), "1", StringComparison.Ordinal))
            return;

        var json = DisplayRules.SerializeDefaultForEmbed(new RadarStyles(), showMonsters: true, watched: []);
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "POE2Radar.Overlay", "Web", "display_rules.default.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        Assert.True(File.Exists(path), $"Regen failed to write {path}");
    }

    [Fact]
    public void BuildDefault_NavigableFlags_MatchCuratedPolicy()
    {
        var rules = DisplayRules.BuildDefault(new RadarStyles(), showMonsters: true, watched: []);
        foreach (var r in rules)
        {
            if (CuratedDefaults.DefaultNavigableRuleNames.Contains(r.Name))
                Assert.True(r.Navigable, $"Expected navigable: {r.Name}");
            else if (CuratedDefaults.DefaultHiddenNavRuleNames.Contains(r.Name))
                Assert.False(r.Navigable, $"Expected non-navigable: {r.Name}");
        }
    }

    [Fact]
    public void EmbeddedDefault_MatchesBuildDefault_NavigableAndNames()
    {
        var built = DisplayRules.BuildDefault(new RadarStyles(), showMonsters: true, watched: []);
        var embedded = DisplayRules.LoadEmbeddedDefault();
        Assert.NotNull(embedded);
        Assert.Equal(built.Count, embedded!.Count);

        for (var i = 0; i < built.Count; i++)
        {
            Assert.Equal(built[i].Name, embedded[i].Name);
            Assert.Equal(built[i].Navigable, embedded[i].Navigable);
            Assert.Equal(built[i].Enabled, embedded[i].Enabled);
            Assert.Equal(built[i].Hide, embedded[i].Hide);
        }
    }
}
