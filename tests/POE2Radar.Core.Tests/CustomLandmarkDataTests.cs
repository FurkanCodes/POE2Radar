using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class CustomLandmarkDataTests
{
    [Fact]
    public void Load_ParsesEmbeddedJson()
    {
        var data = CustomLandmarkData.Load();
        Assert.NotEmpty(data);
        Assert.True(data.ContainsKey("G1_2"));
        Assert.True(data.ContainsKey("G1_4"));
    }

    [Fact]
    public void TryMatch_G1_4_ReturnsCuratedTransitionLabel()
    {
        const string tile = "Metadata/Terrain/Woods/AreaTransitions/OldForestToGrimTangle_02_metatile.tdtx:6-y:2";
        var label = CustomLandmarkData.TryMatch("G1_4", tile);
        Assert.Equal("The Grim Tangle", label);
    }

    [Fact]
    public void TryMatch_GlobalBucket_ResolvesStarKey()
    {
        const string tile = "Metadata/Terrain/Woods/Woods/AzmeriLeague/Features/arenaTransition_01.tdtx:0-y:0";
        var label = CustomLandmarkData.TryMatch("G1_99", tile);
        Assert.Equal("Boss stronghold", label);
    }
}
