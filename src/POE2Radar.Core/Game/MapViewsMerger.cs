namespace POE2Radar.Core.Game;

/// <summary>
/// Combines direct GameUi map reads (accurate screen rects / element pointers) with
/// <see cref="Poe2Live.ReadMapState"/> viewport reads (Tab toggler Shift/Zoom parity with v1.3.0).
/// </summary>
public static class MapViewsMerger
{
    /// <summary>
    /// Viewport supplies projection + Tab visibility; direct supplies element addresses and minimap screen rects.
    /// </summary>
    public static Poe2Live.MapViews Merge(Poe2Live.MapViews viewport, Poe2Live.MapViews direct)
        => new(MergeLargeMap(viewport.LargeMap, direct.LargeMap), MergeMiniMap(viewport.MiniMap, direct.MiniMap));

    public static Poe2Live.MapUi MergeLargeMap(Poe2Live.MapUi viewport, Poe2Live.MapUi direct)
    {
        var element = direct.Element != 0 ? direct.Element : viewport.Element;
        return viewport with { Element = element };
    }

    public static Poe2Live.MapUi MergeMiniMap(Poe2Live.MapUi viewport, Poe2Live.MapUi direct)
    {
        var element = direct.Element != 0 ? direct.Element : viewport.Element;
        if (!direct.HasScreenRect)
            return viewport with { Element = element };

        return viewport with
        {
            Element = element,
            CenterX = direct.CenterX,
            CenterY = direct.CenterY,
            Width = direct.Width,
            Height = direct.Height,
            PositionX = direct.PositionX,
            PositionY = direct.PositionY,
            HasScreenRect = true,
            LocalScaleMultiplier = direct.LocalScaleMultiplier,
            ScaleIndex = direct.ScaleIndex,
        };
    }
}
