using POE2Radar.Overlay;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class ImGuiThemeTests
{
    [Fact]
    public void TooltipWrapWidth_ScalesWithFontSize()
    {
        Assert.Equal(630f, ImGuiTheme.TooltipWrapWidth(18f));
        Assert.Equal(455f, ImGuiTheme.TooltipWrapWidth(13f));
    }

    [Fact]
    public void ResolveFontPath_PrefersConfiguredWhenExists()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var fontsDir = Path.Combine(system, "..", "Fonts");
        fontsDir = Path.GetFullPath(fontsDir);
        var segoe = Path.Combine(fontsDir, "segoeui.ttf");
        if (!File.Exists(segoe)) return;

        var resolved = OverlayFonts.ResolveFontPath(segoe);
        Assert.Equal(segoe, resolved);
    }

    [Fact]
    public void StashUtilityModifierNames_AreNotPassedToImGuiAsFormatStrings()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var overlaySource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "POE2Radar.Overlay",
                "Overlay",
                "ImGuiRadarOverlay.cs"));

        Assert.DoesNotContain(
            "ImGui.TextColored(new Vector4(tierRgb, 1f), definition.Name);",
            overlaySource);
        Assert.DoesNotContain(
            "ImGui.TextWrapped(modifier.Modifier);",
            overlaySource);
    }

    [Fact]
    public void CampaignWidget_DoesNotUseUnboundedHorizontalAutoResize()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var overlaySource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "POE2Radar.Overlay",
                "Overlay",
                "ImGuiRadarOverlay.Campaign.cs"));
        var start = overlaySource.IndexOf(
            "private void DrawCampaignWidget",
            StringComparison.Ordinal);
        var end = overlaySource.IndexOf(
            "private void DrawCampaignDragHandle",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var campaignWidgetSource = overlaySource[start..end];

        Assert.Contains(
            "ImGui.SetNextWindowSizeConstraints(",
            campaignWidgetSource,
            StringComparison.Ordinal);
        Assert.Contains("var widgetWidth = 360f * scale;", campaignWidgetSource, StringComparison.Ordinal);
        Assert.Contains("CampaignCheckbox(\"##done\"", campaignWidgetSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ImGui.Checkbox(\"##done\"", campaignWidgetSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ImGui.SetNextWindowSize(new NumVec2(352f * scale, 0f), ImGuiCond.Once);",
            campaignWidgetSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CampaignGuide_ExposesWebsiteParityControls()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var overlaySource = File.ReadAllText(
            Path.Combine(
                repositoryRoot,
                "src",
                "POE2Radar.Overlay",
                "Overlay",
                "ImGuiRadarOverlay.Campaign.cs"));

        Assert.Contains("Required only##GuideMode", overlaySource, StringComparison.Ordinal);
        Assert.Contains("Full clear##GuideMode", overlaySource, StringComparison.Ordinal);
        Assert.Contains("\"##BrowserZoneRequired\"", overlaySource, StringComparison.Ordinal);
        Assert.Contains("ImGui.TextUnformatted(\"Mark required\")", overlaySource, StringComparison.Ordinal);
        Assert.Contains("Export progress", overlaySource, StringComparison.Ordinal);
        Assert.Contains("Import progress", overlaySource, StringComparison.Ordinal);
        Assert.Contains("Reset chapter", overlaySource, StringComparison.Ordinal);
        Assert.Contains("ACT REWARDS & UNLOCKS", overlaySource, StringComparison.Ordinal);
        Assert.DoesNotContain("ImGui.TextDisabled(objective.Note)", overlaySource, StringComparison.Ordinal);
    }
}
