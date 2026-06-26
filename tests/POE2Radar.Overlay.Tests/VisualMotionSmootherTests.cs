using System.Diagnostics;
using POE2Radar.Core.Game;
using NumVec2 = System.Numerics.Vector2;
using NumVec3 = System.Numerics.Vector3;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class VisualMotionSmootherTests
{
    [Fact]
    public void Update_SmoothingEnabled_ApproachesTargetWithoutJumping()
    {
        var smoother = new VisualMotionSmoother();
        var start = Stopwatch.Frequency;
        var first = Sample(new NumVec2(0, 0), new NumVec3(0, 0, 0), new NumVec2(100, 100), 1f);
        var next = Sample(new NumVec2(10, 0), new NumVec3(20, 0, 0), new NumVec2(120, 100), 1.1f);

        var initial = smoother.Update(start, enabled: true, smoothingMs: 100, first);
        var smoothed = smoother.Update(start + Stopwatch.Frequency / 120, enabled: true, smoothingMs: 100, next);

        Assert.Equal(first.PlayerGrid, initial.PlayerGrid);
        Assert.True(smoothed.PlayerGrid.X > 0f);
        Assert.True(smoothed.PlayerGrid.X < next.PlayerGrid.X);
        Assert.Equal(next.MapFrame.Center, smoothed.MapFrame.Center);
    }

    [Fact]
    public void Update_Disabled_UsesRawTarget()
    {
        var smoother = new VisualMotionSmoother();
        var start = Stopwatch.Frequency;
        smoother.Update(start, enabled: true, smoothingMs: 100, Sample(new NumVec2(0, 0), new NumVec3(0, 0, 0), new NumVec2(100, 100), 1f));

        var target = Sample(new NumVec2(10, 0), new NumVec3(20, 0, 0), new NumVec2(120, 100), 1.1f);
        var raw = smoother.Update(start + Stopwatch.Frequency / 120, enabled: false, smoothingMs: 100, target);

        Assert.Equal(target.PlayerGrid, raw.PlayerGrid);
        Assert.Equal(target.PlayerWorld, raw.PlayerWorld);
        Assert.Equal(target.MapFrame.Center, raw.MapFrame.Center);
    }

    [Fact]
    public void Update_AreaChange_ResetsImmediately()
    {
        var smoother = new VisualMotionSmoother();
        var start = Stopwatch.Frequency;
        smoother.Update(start, enabled: true, smoothingMs: 100, Sample(new NumVec2(0, 0), new NumVec3(0, 0, 0), new NumVec2(100, 100), 1f, areaHash: 1));

        var target = Sample(new NumVec2(10, 0), new NumVec3(20, 0, 0), new NumVec2(120, 100), 1.1f, areaHash: 2);
        var reset = smoother.Update(start + Stopwatch.Frequency / 120, enabled: true, smoothingMs: 100, target);

        Assert.Equal(target.PlayerGrid, reset.PlayerGrid);
        Assert.Equal(target.MapFrame.Center, reset.MapFrame.Center);
    }

    private static LiveVisualSample Sample(
        NumVec2 playerGrid,
        NumVec3 playerWorld,
        NumVec2 mapCenter,
        float scale,
        uint areaHash = 1)
    {
        var map = new Poe2Live.MapUi(
            true,
            0,
            0,
            0,
            0,
            scale,
            new nint(100),
            mapCenter.X,
            mapCenter.Y,
            1920,
            1080,
            0,
            0,
            1,
            0,
            true);
        var frame = new MapFrame(mapCenter, scale, 1920, 1080, map.Element, 0);
        return new LiveVisualSample(
            true,
            areaHash,
            1920,
            1080,
            playerGrid,
            playerWorld,
            0,
            map,
            frame,
            AtlasOpen: false,
            CameraMatrix: null);
    }
}
