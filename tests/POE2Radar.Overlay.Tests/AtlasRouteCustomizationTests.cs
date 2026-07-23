using System.Text.Json;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class AtlasRouteCustomizationTests
{
    [Fact]
    public void Defaults_PreserveCurrentAtlasRouteAppearance()
    {
        var settings = new RadarSettings();

        Assert.Equal("#3BDBFF", settings.AtlasManualRouteColor);
        Assert.Equal("#FFFFFF", settings.AtlasSearchRouteColor);
        Assert.Equal(0.95f, settings.AtlasRouteOpacity);
    }

    [Fact]
    public void RouteGroupPatch_SanitizesColorsAndBounds()
    {
        using var document = JsonDocument.Parse(
            """
            [
              {
                "name": "  Boss targets  ",
                "builtInKey": "bosses",
                "color": "#12ab34",
                "lineThickness": 99,
                "maxHops": 900,
                "entries": [
                  {
                    "name": " Arbiter ",
                    "match": " name:Arbiter ",
                    "color": "invalid",
                    "maxHops": -20
                  }
                ]
              }
            ]
            """);

        var parsed = ApiServer.TryParseAtlasRouteGroups(document.RootElement, out var groups);

        Assert.True(parsed);
        var group = Assert.Single(groups);
        Assert.Equal("Boss targets", group.Name);
        Assert.Equal("#12AB34", group.Color);
        Assert.Equal(8f, group.LineThickness);
        Assert.Equal(500, group.MaxHops);
        var entry = Assert.Single(group.Entries);
        Assert.Equal("Arbiter", entry.Name);
        Assert.Equal("name:Arbiter", entry.Match);
        Assert.Equal("#12AB34", entry.Color);
        Assert.Equal(0, entry.MaxHops);
    }

    [Fact]
    public void Dashboard_ExposesAtlasRouteColorEditors()
    {
        var page = DashboardHtml.Page;

        Assert.Contains("data-set=\"atlasManualRouteColor\"", page);
        Assert.Contains("data-set=\"atlasSearchRouteColor\"", page);
        Assert.Contains("data-set=\"atlasRouteOpacity\"", page);
        Assert.Contains("id=\"atlasRouteColors\"", page);
        Assert.DoesNotContain("{{H.AtlasManualRouteColor}}", page);
        Assert.DoesNotContain("{{H.AtlasRouteGroupColor}}", page);
    }
}
