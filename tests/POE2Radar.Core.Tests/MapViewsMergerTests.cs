using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class MapViewsMergerTests
{
    private static Poe2Live.MapUi Map(
        bool visible,
        float shiftX,
        float shiftY,
        float zoom,
        nint element,
        bool rect = false,
        float posX = 0,
        float posY = 0,
        float width = 0,
        float height = 0)
        => new(
            IsVisible: visible,
            ShiftX: shiftX,
            ShiftY: shiftY,
            DefaultShiftX: 0,
            DefaultShiftY: -20,
            Zoom: zoom,
            Element: element,
            CenterX: 0,
            CenterY: 0,
            Width: width,
            Height: height,
            PositionX: posX,
            PositionY: posY,
            LocalScaleMultiplier: 1,
            ScaleIndex: 0,
            HasScreenRect: rect);

    [Fact]
    public void MergeLargeMap_UsesViewportProjection_WhenDirectDiffers()
    {
        var viewport = Map(visible: true, shiftX: 14, shiftY: 173, zoom: 0.5f, element: 1);
        var direct = Map(visible: false, shiftX: 999, shiftY: -500, zoom: 2f, element: 2);

        var merged = MapViewsMerger.MergeLargeMap(viewport, direct);

        Assert.True(merged.IsVisible);
        Assert.Equal(14f, merged.ShiftX);
        Assert.Equal(173f, merged.ShiftY);
        Assert.Equal(0.5f, merged.Zoom);
        Assert.Equal(2, merged.Element);
    }

    [Fact]
    public void Merge_ProducesTabOpenLargeMapProjection_FromViewport()
    {
        var viewport = new Poe2Live.MapViews(
            Map(visible: true, shiftX: 14, shiftY: 173, zoom: 0.5f, element: 1),
            CornerTogglerElement: 3);
        var direct = new Poe2Live.MapViews(
            Map(visible: false, shiftX: 500, shiftY: -200, zoom: 1.5f, element: 2),
            CornerTogglerElement: 4);

        var merged = MapViewsMerger.Merge(viewport, direct);

        Assert.True(merged.LargeMap.IsVisible);
        Assert.Equal(14f, merged.LargeMap.ShiftX);
        Assert.Equal(173f, merged.LargeMap.ShiftY);
        Assert.Equal(0.5f, merged.LargeMap.Zoom);
        Assert.Equal(4, merged.CornerTogglerElement);
    }
}
