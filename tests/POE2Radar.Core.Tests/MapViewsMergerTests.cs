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
        float height = 0,
        float? centerX = null,
        float? centerY = null)
        => new(
            IsVisible: visible,
            ShiftX: shiftX,
            ShiftY: shiftY,
            DefaultShiftX: 0,
            DefaultShiftY: -20,
            Zoom: zoom,
            Element: element,
            CenterX: centerX ?? (posX + width * 0.5f),
            CenterY: centerY ?? (posY + height * 0.5f),
            Width: width,
            Height: height,
            PositionX: posX,
            PositionY: posY,
            LocalScaleMultiplier: 1,
            ScaleIndex: 0,
            HasScreenRect: rect);

    [Fact]
    public void MergeLargeMap_UsesDirectSemanticVisibilityAndProjection()
    {
        var viewport = Map(visible: true, shiftX: 14, shiftY: 173, zoom: 0.5f, element: 1);
        var direct = Map(visible: false, shiftX: 999, shiftY: -500, zoom: 2f, element: 2);

        var merged = MapViewsMerger.MergeLargeMap(viewport, direct);

        Assert.False(merged.IsVisible);
        Assert.Equal(999f, merged.ShiftX);
        Assert.Equal(-500f, merged.ShiftY);
        Assert.Equal(2f, merged.Zoom);
        Assert.Equal(2, merged.Element);
    }

    [Fact]
    public void MergeLargeMap_DirectTabTogglerOverridesReversedTreeClassification()
    {
        var fallback = Map(visible: false, shiftX: 14, shiftY: 173, zoom: 0.5f, element: 1);
        var direct = Map(visible: true, shiftX: 999, shiftY: -500, zoom: 2f, element: 2);

        var merged = MapViewsMerger.MergeLargeMap(fallback, direct);

        Assert.True(merged.IsVisible);
        Assert.Equal(999f, merged.ShiftX);
        Assert.Equal(-500f, merged.ShiftY);
        Assert.Equal(2f, merged.Zoom);
        Assert.Equal(2, merged.Element);
    }

    [Fact]
    public void MergeLargeMap_PreservesDirectLiveAnchorAndProjection()
    {
        var viewport = Map(
            visible: false,
            shiftX: 0,
            shiftY: 0,
            zoom: 0.5f,
            element: 1);
        var direct = Map(
            visible: true,
            shiftX: 14,
            shiftY: 173,
            zoom: 0.75f,
            element: 2,
            rect: true,
            centerX: 1276.2f,
            centerY: 720f);

        var merged = MapViewsMerger.MergeLargeMap(viewport, direct);

        Assert.True(merged.IsVisible);
        Assert.Equal(14f, merged.ShiftX);
        Assert.Equal(173f, merged.ShiftY);
        Assert.Equal(0.75f, merged.Zoom);
        Assert.Equal(1276.2f, merged.CenterX, 1);
        Assert.Equal(720f, merged.CenterY, 1);
        Assert.True(merged.HasScreenRect);
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
            posX: 12,
            posY: 34,
            width: 250,
            height: 250);

        var merged = MapViewsMerger.MergeMiniMap(viewport, direct);

        Assert.Equal(1f, merged.ShiftX);
        Assert.Equal(2f, merged.ShiftY);
        Assert.Equal(0.4f, merged.Zoom);
        Assert.Equal(20, merged.Element);
        Assert.True(merged.IsVisible);
        Assert.True(merged.HasScreenRect);
        Assert.Equal(12f, merged.PositionX);
        Assert.Equal(34f, merged.PositionY);
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
            LargeMap: Map(visible: true, shiftX: 500, shiftY: -200, zoom: 1.5f, element: 2),
            MiniMap: Map(
                visible: true,
                shiftX: 100,
                shiftY: 100,
                zoom: 0.8f,
                element: 4,
                rect: true,
                posX: 5,
                posY: 6,
                width: 180,
                height: 180));

        var merged = MapViewsMerger.Merge(viewport, direct);

        Assert.True(merged.LargeMap.IsVisible);
        Assert.Equal(500f, merged.LargeMap.ShiftX);
        Assert.Equal(-200f, merged.LargeMap.ShiftY);
        Assert.Equal(1.5f, merged.LargeMap.Zoom);
        Assert.True(merged.MiniMap.HasScreenRect);
        Assert.Equal(5f, merged.MiniMap.PositionX);
        Assert.Equal(0.25f, merged.MiniMap.Zoom);
    }

    [Fact]
    public void Merge_UsesDirectMapParentMode_WhenTreeClassificationIsReversed()
    {
        var viewport = new Poe2Live.MapViews(
            LargeMap: Map(visible: false, shiftX: -130, shiftY: 537, zoom: 0.5f, element: 1),
            MiniMap: Map(
                visible: true,
                shiftX: -130,
                shiftY: 537,
                zoom: 0.5f,
                element: 3,
                rect: true,
                posX: 3158.4f,
                posY: 117f,
                width: 273.6f,
                height: 273.6f));
        var direct = new Poe2Live.MapViews(
            LargeMap: Map(visible: false, shiftX: 0, shiftY: 0, zoom: 0.5f, element: 2),
            MiniMap: Map(
                visible: true,
                shiftX: 0,
                shiftY: 0,
                zoom: 1f,
                element: 4,
                rect: true,
                posX: 3069.9f,
                posY: 9f,
                width: 361.8f,
                height: 361.8f));

        var merged = MapViewsMerger.Merge(viewport, direct);

        Assert.False(merged.LargeMap.IsVisible);
        Assert.True(merged.MiniMap.IsVisible);
    }

    [Fact]
    public void Merge_TabOpen_UsesDirectTogglerVisibilityAndProjection()
    {
        var viewport = new Poe2Live.MapViews(
            // Live 3440x1440 failure: size-based discovery reverses the roles. The always-local-
            // visible shared content is reported as the large map with corner-map pan, while the
            // real MapParent TAB toggler is reported as the minimap.
            LargeMap: Map(
                visible: false,
                shiftX: 134,
                shiftY: -695,
                zoom: 0.5f,
                element: 0xB),
            MiniMap: Map(
                visible: true,
                shiftX: 0,
                shiftY: 0,
                zoom: 0.5f,
                element: 0xA));
        var direct = new Poe2Live.MapViews(
            // The direct MapParent pair has the semantic roles and hierarchical visibility right.
            LargeMap: Map(
                visible: true,
                shiftX: 0,
                shiftY: 0,
                zoom: 0.5f,
                element: 0xA,
                rect: true,
                centerX: 1719.9f,
                centerY: 720f),
            MiniMap: Map(
                visible: false,
                shiftX: 0,
                shiftY: 0,
                zoom: 1f,
                element: 0xC,
                rect: true,
                posX: 3069.9f,
                posY: 9f,
                width: 361.8f,
                height: 361.8f));

        var merged = MapViewsMerger.Merge(viewport, direct);

        Assert.True(merged.LargeMap.IsVisible);
        Assert.False(merged.MiniMap.IsVisible);
        Assert.Equal(0f, merged.LargeMap.ShiftX);
        Assert.Equal(0f, merged.LargeMap.ShiftY);
        Assert.Equal(0.5f, merged.LargeMap.Zoom);
        Assert.Equal(1719.9f, merged.LargeMap.CenterX, 1);
        Assert.Equal(720f, merged.LargeMap.CenterY, 1);
    }
}
