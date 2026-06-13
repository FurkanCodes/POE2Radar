using POE2Radar.Core.Game;
using POE2Radar.Overlay;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class MapOverlayDrawPolicyTests
{
    private static Poe2Live.MapUi Map(bool visible, bool rect, nint element = 1)
        => new(
            IsVisible: visible,
            ShiftX: 0,
            ShiftY: 0,
            DefaultShiftX: 0,
            DefaultShiftY: -20,
            Zoom: 0.5f,
            Element: element,
            CenterX: 0,
            CenterY: 0,
            Width: rect ? 800 : 0,
            Height: rect ? 600 : 0,
            PositionX: 0,
            PositionY: 0,
            LocalScaleMultiplier: 1,
            ScaleIndex: 0,
            HasScreenRect: rect);

    [Fact]
    public void LargeMap_DrawsOnlyWhenVisible()
    {
        Assert.True(MapOverlayDrawPolicy.ShouldDrawLargeMap(Map(visible: true, rect: true)));
        Assert.False(MapOverlayDrawPolicy.ShouldDrawLargeMap(Map(visible: false, rect: true)));
        Assert.False(MapOverlayDrawPolicy.ShouldDrawLargeMap(Map(visible: false, rect: false, element: 0)));
    }

    [Fact]
    public void Minimap_DrawsWhenTabClosed_AndMutuallyExclusiveWithLargeMap()
    {
        var tabOpen = Map(visible: true, rect: true, element: 1);
        var mini = Map(visible: true, rect: true, element: 2);

        Assert.False(MapOverlayDrawPolicy.ShouldDrawMinimap(tabOpen, mini));

        var tabClosed = Map(visible: false, rect: true, element: 1);
        Assert.True(MapOverlayDrawPolicy.ShouldDrawMinimap(tabClosed, mini));
    }
}
