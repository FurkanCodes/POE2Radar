namespace POE2Radar.Core.Game;

/// <summary>
/// Combines direct GameUi map reads with the UI-tree viewport fallback. The direct shared-content /
/// corner-frame layout is authoritative for visibility and screen geometry; the viewport retains
/// the validated pan/zoom projection values.
/// </summary>
public static class MapViewsMerger
{
    /// <summary>Merge direct UI state into the validated viewport projection when available.</summary>
    public static Poe2Live.MapViews Merge(Poe2Live.MapViews viewport, Poe2Live.MapViews direct)
        => new(MergeLargeMap(viewport.LargeMap, direct.LargeMap), MergeMiniMap(viewport.MiniMap, direct.MiniMap));

    public static Poe2Live.MapUi MergeLargeMap(Poe2Live.MapUi viewport, Poe2Live.MapUi direct)
        => MergeDirectUiState(viewport, direct);

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
