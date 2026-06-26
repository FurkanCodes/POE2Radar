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
}
