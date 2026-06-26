using POE2Radar.Core.Game;

namespace POE2Radar.Overlay;

/// <summary>Which map layer to draw — fullscreen Tab overlay only when the game reports it open.
/// Do NOT draw from HasScreenRect alone: the Tab map element keeps a layout rect while closed, and
/// its Shift/Zoom are stale — projecting entities to the full window then misaligns every marker.</summary>
public static class MapOverlayDrawPolicy
{
    /// <summary>Fullscreen Tab overlay map — only when the game reports the large map visible.</summary>
    public static bool ShouldDrawLargeMap(Poe2Live.MapUi map)
        => map.Element != 0 && map.IsVisible;
}
