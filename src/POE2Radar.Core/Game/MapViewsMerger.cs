namespace POE2Radar.Core.Game;

/// <summary>
/// Combines direct GameUi map reads (accurate element pointers) with
/// <see cref="Poe2Live.ReadMapState"/> viewport reads (Tab toggler Shift/Zoom parity with v1.3.0).
/// </summary>
public static class MapViewsMerger
{
    /// <summary>Viewport supplies projection + Tab visibility; direct supplies element addresses.</summary>
    public static Poe2Live.MapViews Merge(Poe2Live.MapViews viewport, Poe2Live.MapViews direct)
    {
        var toggler = direct.CornerTogglerElement != 0 ? direct.CornerTogglerElement : viewport.CornerTogglerElement;
        return new(MergeLargeMap(viewport.LargeMap, direct.LargeMap), toggler);
    }

    public static Poe2Live.MapUi MergeLargeMap(Poe2Live.MapUi viewport, Poe2Live.MapUi direct)
    {
        var element = direct.Element != 0 ? direct.Element : viewport.Element;
        return viewport with { Element = element };
    }
}
