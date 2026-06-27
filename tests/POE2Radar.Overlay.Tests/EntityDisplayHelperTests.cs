using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class EntityDisplayHelperTests
{
    [Fact]
    public void RuleLabel_returns_empty_when_hide_label_set()
    {
        var rule = new DisplayRule { Name = "Foo", Label = "Bar", HideLabel = true };
        Assert.Equal("", EntityDisplayHelper.RuleLabel(rule));
    }

    [Fact]
    public void FormatEntityLabel_returns_empty_when_hide_label_set()
    {
        var entity = new Poe2Live.EntityDot(
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
            Rarity: Poe2Live.Rarity.Normal,
            Opened: false);

        var rule = new DisplayRule
        {
            Name = "PetrosphereCluster",
            Label = "Cluster",
            HideLabel = true,
            Match = ["PetrosphereCluster"],
        };

        Assert.Equal("", EntityDisplayHelper.FormatEntityLabel(entity, rule));
    }
}
