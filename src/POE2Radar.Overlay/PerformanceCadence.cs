using System.Diagnostics;

namespace POE2Radar.Overlay;

public sealed class PerformanceCadence
{
    private long _nextStamp;

    public PerformanceCadence()
    {
        _nextStamp = 0;
    }

    public bool IsDue(int hz)
        => IsDue(Stopwatch.GetTimestamp(), hz);

    public bool IsDue(long nowStamp, int hz)
    {
        hz = Math.Max(1, hz);
        if (nowStamp < _nextStamp) return false;
        _nextStamp = nowStamp + Stopwatch.Frequency / hz;
        return true;
    }

    public static int ClampHz(int hz, int min, int max)
        => Math.Clamp(hz, min, max);

    public static int SleepMillisecondsForHz(int hz)
        => Math.Max(1, 1000 / Math.Max(1, hz));
}
