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
    public static Poe2Live.MapViews Merge(Poe2Live.MapViews viewport, Poe2Live.MapViews direct, int windowWidth = 0, int windowHeight = 0)
        => new(MergeLargeMap(viewport.LargeMap, direct.LargeMap), MergeMiniMap(viewport.MiniMap, direct.MiniMap, windowWidth, windowHeight));

    public static Poe2Live.MapUi MergeLargeMap(Poe2Live.MapUi viewport, Poe2Live.MapUi direct)
    {
        var element = direct.Element != 0 ? direct.Element : viewport.Element;
        return viewport with { Element = element };
    }

    public static Poe2Live.MapUi MergeMiniMap(Poe2Live.MapUi viewport, Poe2Live.MapUi direct, int windowWidth = 0, int windowHeight = 0)
    {
        var element = direct.Element != 0 ? direct.Element : viewport.Element;
        if (!ShouldUseDirectMinimapRect(direct, windowWidth, windowHeight))
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

    /// <summary>
    /// Direct MapParent minimap reads often resolve a mid-screen layout rect for the 0×0 corner widget.
    /// Only trust direct rects that land in the live top-right minimap band.
    /// </summary>
    public static bool ShouldUseDirectMinimapRect(Poe2Live.MapUi direct, int windowWidth, int windowHeight)
    {
        if (!direct.HasScreenRect || direct.Width < 32f || direct.Height < 32f)
            return false;
        if (windowWidth <= 0 || windowHeight <= 0)
            return true;

        return MapViewportLogic.IsTopRightMinimapRect(
            direct.PositionX,
            direct.PositionY,
            direct.PositionX + direct.Width,
            direct.PositionY + direct.Height,
            windowWidth,
            windowHeight);
    }
}
