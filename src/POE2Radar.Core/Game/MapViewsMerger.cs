namespace POE2Radar.Core.Game;

/// <summary>
/// Combines the semantic MapParent pair with the UI-tree viewport fallback. MapParent identifies the
/// actual TAB toggler and corner frame, so its hierarchical visibility and fullscreen projection are
/// authoritative. Tree discovery remains useful for the corner minimap's shared-content pan/zoom.
/// </summary>
public static class MapViewsMerger
{
    /// <summary>Merge the direct semantic pair with projection details discovered from the UI tree.</summary>
    public static Poe2Live.MapViews Merge(Poe2Live.MapViews viewport, Poe2Live.MapViews direct)
        => new(MergeLargeMap(viewport.LargeMap, direct.LargeMap), MergeMiniMap(viewport.MiniMap, direct.MiniMap));

    public static Poe2Live.MapUi MergeLargeMap(Poe2Live.MapUi viewport, Poe2Live.MapUi direct)
        => direct.Element != 0 ? direct : viewport;

    public static Poe2Live.MapUi MergeMiniMap(Poe2Live.MapUi viewport, Poe2Live.MapUi direct)
        => MergeDirectUiState(viewport, direct);

    private static Poe2Live.MapUi MergeDirectUiState(Poe2Live.MapUi viewport, Poe2Live.MapUi direct)
    {
        if (direct.Element == 0) return viewport;
        return viewport with
        {
            IsVisible = direct.IsVisible,
            Element = direct.Element,
            CenterX = direct.CenterX,
            CenterY = direct.CenterY,
            Width = direct.Width,
            Height = direct.Height,
            PositionX = direct.PositionX,
            PositionY = direct.PositionY,
            LocalScaleMultiplier = direct.LocalScaleMultiplier,
            ScaleIndex = direct.ScaleIndex,
            HasScreenRect = direct.HasScreenRect,
        };
    }
}
