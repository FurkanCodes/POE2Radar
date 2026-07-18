using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public class AtlasSearchTests
{
    [Theory]
    [InlineData("Moor of the skies")]
    [InlineData("moor skies")]
    [InlineData("Fallen Skies")]
    [InlineData("ExpeditionLogBook_Heath")]
    public void Matches_MoorOfFallenSkies_LoosePhrases(string query)
    {
        var q = AtlasSearch.Parse(query);
        Assert.False(q.IsEmpty);
        Assert.True(q.Matches("Moor of Fallen Skies", "ExpeditionLogBook_Heath"));
        Assert.True(q.Matches(null, "ExpeditionLogBook_Heath"));
    }

    [Fact]
    public void Matches_CommaOr_EitherSide()
    {
        var q = AtlasSearch.Parse("Jade Isles, Moor skies");
        Assert.True(q.Matches("The Jade Isles", "MapUberBoss_JadeCitadel"));
        Assert.True(q.Matches("Moor of Fallen Skies", "ExpeditionLogBook_Heath"));
        Assert.False(q.Matches("Ravine", "MapRavine"));
    }

    [Fact]
    public void Matches_EmptyQuery_MatchesEverything()
    {
        var q = AtlasSearch.Parse("  ");
        Assert.True(q.IsEmpty);
        Assert.True(q.Matches("Anything", "MapX"));
    }

    [Fact]
    public void SignificantTokens_DropsStopWords()
    {
        var tokens = AtlasSearch.SignificantTokens("Moor of the skies");
        Assert.Contains("Moor", tokens);
        Assert.Contains("skies", tokens);
        Assert.DoesNotContain("of", tokens);
        Assert.DoesNotContain("the", tokens);
    }
}
