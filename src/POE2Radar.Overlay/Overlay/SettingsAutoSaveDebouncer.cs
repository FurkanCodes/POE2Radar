using System.Diagnostics;

namespace POE2Radar.Overlay;

/// <summary>Debounces <see cref="RadarSettings"/> JSON snapshots before persisting to disk.</summary>
internal sealed class SettingsAutoSaveDebouncer
{
    public const double DebounceSeconds = 0.35;

    private string? _snapshot;
    private long _dueStamp;

    public void SeedIfNeeded(string snapshot) => _snapshot ??= snapshot;

    public void Touch(string snapshot, long nowStamp)
    {
        if (snapshot == _snapshot) return;
        _snapshot = snapshot;
        _dueStamp = nowStamp;
    }

    public bool ShouldFlush(long nowStamp, bool force)
    {
        if (_dueStamp == 0) return false;
        if (force) return true;
        return Stopwatch.GetElapsedTime(_dueStamp, nowStamp).TotalSeconds >= DebounceSeconds;
    }

    public void NoteSaved(string snapshot)
    {
        _snapshot = snapshot;
        _dueStamp = 0;
    }
}
