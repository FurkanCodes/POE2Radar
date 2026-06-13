using POE2Radar.Core.Game;

namespace POE2Radar.Overlay;

/// <summary>Which map layer to draw — v1.3.0 parity: Tab overlay OR corner minimap, never both, and
/// never project the full window from a closed Tab map element.</summary>
public static class MapOverlayDrawPolicy
{
    /// <summary>Fullscreen Tab overlay map — only when the game reports the large map visible.
    /// Do NOT draw from HasScreenRect alone: the Tab map element keeps a layout rect while closed, and
    /// its Shift/Zoom are stale — projecting entities to the full window then misaligns every marker.</summary>
    public static bool ShouldDrawLargeMap(Poe2Live.MapUi map)
        => map.Element != 0 && map.IsVisible;

    /// <summary>Corner minimap when the Tab overlay is closed (mutually exclusive with the large map).</summary>
    public static bool ShouldDrawMinimap(Poe2Live.MapUi largeMap, Poe2Live.MapUi miniMap)
    {
        if (largeMap.IsVisible) return false;
        if (miniMap.Element == 0) return false;
        if (miniMap.IsVisible) return true;
        return miniMap.HasScreenRect && miniMap.Width >= 32f && miniMap.Height >= 32f;
    }
}
