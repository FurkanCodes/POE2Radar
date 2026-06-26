using POE2Radar.Core.Game;
using System.Reflection;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class MapViewportLogicTests
{
    private const int W = 1920, H = 1080;

    [Fact]
    public void TrySelectLocalVisibleMini_PicksSmallestLocallyVisible()
    {
        var reads = new[]
        {
            new MapViewportLogic.MiniElementRead(false, 0, -20, 0.5f, 0, 0, W, H),
            new MapViewportLogic.MiniElementRead(true, 1, -20, 0.5f, 1600, 800, 1820, 1020),
        };

        Assert.True(MapViewportLogic.TrySelectLocalVisibleMini(reads, out var pick));
        Assert.Equal(1600f, pick.ScreenLeft);
    }

    [Fact]
    public void TrySelectLocalVisibleMini_IgnoresTogglerHistory_BothEverToggled()
    {
        var reads = new[]
        {
            new MapViewportLogic.MiniElementRead(false, 0, -20, 0.5f, 0, 0, W, H),
            new MapViewportLogic.MiniElementRead(true, 2, -20, 0.5f, 1650, 850, 1870, 1070),
        };

        Assert.True(MapViewportLogic.TrySelectLocalVisibleMini(reads, out var pick));
        Assert.True(pick.LocalVisible);
        Assert.True(pick.ScreenLeft > 1600f);
    }

    [Fact]
    public void IsTabMapOpen_CornerLocalVisibleMeansTabOpen()
    {
        Assert.True(MapViewportLogic.IsTabMapOpen(cornerLocalVisible: true));
        Assert.False(MapViewportLogic.IsTabMapOpen(cornerLocalVisible: false));
    }

    [Fact]
    public void ClassifyByIntrinsicSize_LargerElementIsTabMap()
    {
        MapViewportLogic.ClassifyByIntrinsicSize(800, 600, 250, 250, out var firstIsLarge);
        Assert.True(firstIsLarge);

        MapViewportLogic.ClassifyByIntrinsicSize(250, 250, 800, 600, out firstIsLarge);
        Assert.False(firstIsLarge);
    }

    [Fact]
    public void MapProjectionCenter_LargeMapAppliesDefaultShiftY()
    {
        var (x, y) = MapViewportLogic.MapProjectionCenter(
            W, H, shiftX: 14f, shiftY: 173f, offsetX: 0f, offsetY: 0f);

        Assert.Equal(W * 0.5f + 14f, x, 0);
        Assert.Equal(H * 0.5f + 173f + MapViewportLogic.MapDefaultShiftY, y, 0);
    }

    [Fact]
    public void ScoreMapViews_VisibleControllerBranchBeatsStaleKeyboardBranch()
    {
        var score = typeof(Poe2Live).GetMethod("ScoreMapViews", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(score);

        var visibleController = new Poe2Live.MapViews(Map(visible: true, rect: true, element: 1));
        var staleKeyboard = new Poe2Live.MapViews(Map(visible: false, rect: true, element: 3));

        var controllerScore = Assert.IsType<int>(score!.Invoke(null, [visibleController]));
        var keyboardScore = Assert.IsType<int>(score.Invoke(null, [staleKeyboard]));

        Assert.True(controllerScore > keyboardScore);
    }

    [Fact]
    public void ClampScreenRect_ScalesAndClampsToWindow()
    {
        var (l, t, r, b) = MapViewportLogic.ClampScreenRect(100, 200, 250, 250, 0.675f, W, H);

        Assert.True(MapViewportLogic.HasArea(l, t, r, b));
        Assert.Equal(100 * 0.675f, l, 3);
        Assert.Equal(200 * 0.675f, t, 3);
    }

    private static Poe2Live.MapUi Map(bool visible, bool rect, nint element)
        => new(
            visible,
            0,
            0,
            0,
            MapViewportLogic.MapDefaultShiftY,
            1,
            element,
            0,
            0,
            rect ? 220 : 0,
            rect ? 220 : 0,
            0,
            0,
            1,
            0,
            rect);
}
