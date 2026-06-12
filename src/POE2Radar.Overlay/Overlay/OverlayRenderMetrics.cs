using System.Diagnostics;

namespace POE2Radar.Overlay;

/// <summary>Smoothed timings from the ImGui overlay render thread (independent of the main tick rate).</summary>
public sealed class OverlayRenderMetrics
{
    private const double Alpha = 0.08;
    private long _lastStamp = Stopwatch.GetTimestamp();
    private double _renderFps;
    private double _renderMs;
    private double _mapMs;
    private double _pathsMs;
    private double _nameplatesMs;
    private double _navMenuMs;
    private double _atlasMs;

    public void Record(
        double renderMs,
        double mapMs,
        double pathsMs,
        double nameplatesMs,
        double navMenuMs,
        double atlasMs)
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Math.Max(0.001, (now - _lastStamp) / (double)Stopwatch.Frequency);
        _lastStamp = now;

        _renderFps = Smooth(_renderFps, 1.0 / seconds);
        _renderMs = Smooth(_renderMs, renderMs);
        _mapMs = Smooth(_mapMs, mapMs);
        _pathsMs = Smooth(_pathsMs, pathsMs);
        _nameplatesMs = Smooth(_nameplatesMs, nameplatesMs);
        _navMenuMs = Smooth(_navMenuMs, navMenuMs);
        _atlasMs = Smooth(_atlasMs, atlasMs);
    }

    public (float RenderFps, float RenderMs, float MapMs, float PathsMs, float NameplatesMs, float NavMenuMs, float AtlasMs) Snapshot()
        => ((float)_renderFps, (float)_renderMs, (float)_mapMs, (float)_pathsMs, (float)_nameplatesMs, (float)_navMenuMs, (float)_atlasMs);

    private static double Smooth(double current, double sample)
        => current <= 0 ? sample : current + (sample - current) * Alpha;
}
