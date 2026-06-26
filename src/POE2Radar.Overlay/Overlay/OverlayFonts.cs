using ClickableTransparentOverlay;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay;

/// <summary>Loads overlay fonts — GameHelper defaults (Microsoft YaHei 18px + ChineseSimplifiedCommon).</summary>
internal static class OverlayFonts
{
    internal const string DefaultGameHelperPath = @"C:\Windows\Fonts\msyh.ttc";
    internal const string SegoeUiPath = @"C:\Windows\Fonts\segoeui.ttf";
    internal const int DefaultSize = 18;

    internal static string LastResolvedPath { get; private set; } = "";
    internal static string LastResolvedLabel { get; private set; } = "";

    internal static bool Apply(ClickableTransparentOverlay.Overlay overlay, RadarSettings settings)
    {
        var path = ResolveFontPath(settings.UiFontPath);
        var size = Math.Clamp(settings.UiFontSize, 13, 40);
        var range = MapGlyphRange(settings.UiFontGlyphRange);
        var ok = overlay.ReplaceFont(path, size, range);
        if (ok)
        {
            LastResolvedPath = path;
            LastResolvedLabel = Path.GetFileNameWithoutExtension(path);
        }
        return ok;
    }

    internal static string ResolveFontPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        if (File.Exists(DefaultGameHelperPath))
            return DefaultGameHelperPath;

        if (File.Exists(SegoeUiPath))
            return SegoeUiPath;

        var bundled = Path.Combine(AppContext.BaseDirectory, "fonts", "DejaVuSans.ttf");
        if (File.Exists(bundled))
            return bundled;

        return string.IsNullOrWhiteSpace(configured) ? DefaultGameHelperPath : configured;
    }

    internal static FontGlyphRangeType MapGlyphRange(UiFontGlyphRange range)
        => range switch
        {
            UiFontGlyphRange.ChineseSimplifiedCommon => FontGlyphRangeType.ChineseSimplifiedCommon,
            UiFontGlyphRange.ChineseFull => FontGlyphRangeType.ChineseFull,
            UiFontGlyphRange.Japanese => FontGlyphRangeType.Japanese,
            UiFontGlyphRange.Korean => FontGlyphRangeType.Korean,
            UiFontGlyphRange.Thai => FontGlyphRangeType.Thai,
            UiFontGlyphRange.Vietnamese => FontGlyphRangeType.Vietnamese,
            UiFontGlyphRange.Cyrillic => FontGlyphRangeType.Cyrillic,
            _ => FontGlyphRangeType.English,
        };
}
