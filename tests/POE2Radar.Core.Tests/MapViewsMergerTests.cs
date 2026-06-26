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
    public void MergeMiniMap_KeepsDirectScreenRect_WithViewportProjection()
    {
        var viewport = Map(visible: false, shiftX: 1, shiftY: 2, zoom: 0.4f, element: 10);
        var direct = Map(
            visible: true,
            shiftX: 888,
            shiftY: -888,
            zoom: 3f,
            element: 20,
            rect: true,
            posX: 1650,
            posY: 90,
            width: 250,
            height: 250);

        var merged = MapViewsMerger.MergeMiniMap(viewport, direct, windowWidth: 1920, windowHeight: 1080);

        Assert.Equal(1f, merged.ShiftX);
        Assert.Equal(2f, merged.ShiftY);
        Assert.Equal(0.4f, merged.Zoom);
        Assert.Equal(20, merged.Element);
        Assert.True(merged.HasScreenRect);
        Assert.Equal(1650f, merged.PositionX);
        Assert.Equal(90f, merged.PositionY);
        Assert.Equal(250f, merged.Width);
        Assert.Equal(250f, merged.Height);
    }

    [Fact]
    public void Merge_ProducesTabOpenLargeMapProjection_FromViewport()
    {
        var viewport = new Poe2Live.MapViews(
            LargeMap: Map(visible: true, shiftX: 14, shiftY: 173, zoom: 0.5f, element: 1),
            MiniMap: Map(visible: false, shiftX: 0, shiftY: 0, zoom: 0.25f, element: 3));
        var direct = new Poe2Live.MapViews(
            LargeMap: Map(visible: false, shiftX: 500, shiftY: -200, zoom: 1.5f, element: 2),
            MiniMap: Map(
                visible: true,
                shiftX: 100,
                shiftY: 100,
                zoom: 0.8f,
                element: 4,
                rect: true,
                posX: 1650,
                posY: 90,
                width: 180,
                height: 180));

        var merged = MapViewsMerger.Merge(viewport, direct, windowWidth: 1920, windowHeight: 1080);

        Assert.True(merged.LargeMap.IsVisible);
        Assert.Equal(14f, merged.LargeMap.ShiftX);
        Assert.Equal(173f, merged.LargeMap.ShiftY);
        Assert.Equal(0.5f, merged.LargeMap.Zoom);
        Assert.True(merged.MiniMap.HasScreenRect);
        Assert.Equal(1650f, merged.MiniMap.PositionX);
        Assert.Equal(0.25f, merged.MiniMap.Zoom);
    }

    [Fact]
    public void MergeMiniMap_RejectsMidScreenDirectRect_KeepsViewportFrame()
    {
        var viewport = Map(
            visible: false,
            shiftX: 1,
            shiftY: 2,
            zoom: 0.4f,
            element: 10,
            rect: true,
            posX: 1650,
            posY: 90,
            width: 220,
            height: 220);
        var direct = Map(
            visible: true,
            shiftX: 888,
            shiftY: -888,
            zoom: 3f,
            element: 20,
            rect: true,
            posX: 1719.9f,
            posY: 720f,
            width: 225,
            height: 225);

        var merged = MapViewsMerger.MergeMiniMap(viewport, direct, windowWidth: 3440, windowHeight: 1440);

        Assert.Equal(1650f, merged.PositionX);
        Assert.Equal(90f, merged.PositionY);
        Assert.Equal(220f, merged.Width);
        Assert.Equal(20, merged.Element);
        Assert.True(merged.HasScreenRect);
    }

    [Fact]
    public void ShouldUseDirectMinimapRect_RejectsMidScreenLayoutRect()
    {
        var midScreen = Map(
            visible: true,
            shiftX: 0,
            shiftY: 0,
            zoom: 0.5f,
            element: 1,
            rect: true,
            posX: 1719.9f,
            posY: 720f,
            width: 225,
            height: 225);

        Assert.False(MapViewsMerger.ShouldUseDirectMinimapRect(midScreen, 3440, 1440));
    }
}
