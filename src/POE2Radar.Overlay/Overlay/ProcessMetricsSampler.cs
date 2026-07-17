using System.Diagnostics;

namespace POE2Radar.Overlay;

/// <summary>CPU%, GPU%, and RAM for a single process — intended for <see cref="Environment.ProcessId"/> (the overlay exe).</summary>
public sealed class ProcessMetricsSampler : IDisposable
{
    private const double CpuSampleSeconds = 1.0; // match Task Manager ~1s refresh
    private const float Unavailable = -1f;

    private readonly Process _process;
    private readonly int _processorCount;
    private long _lastCpuStamp;
    private TimeSpan _lastCpu;
    private double _cpuPct;
    private float _memoryMb;
    private float _gpuPct = Unavailable;
    private float _gpuMemoryMb = Unavailable;
    private bool _gpuInitAttempted;
    private PerformanceCounterCategory? _gpuEngineCategory;
    private PerformanceCounterCategory? _gpuMemoryCategory;
    private List<PerformanceCounter> _gpuUtilCounters = new();
    private List<PerformanceCounter> _gpuMemCounters = new();
    private DateTime _lastGpuCounterRefresh = DateTime.MinValue;
    private DateTime _lastGpuSample = DateTime.MinValue;

    public static ProcessMetricsSampler ForOverlay() => new(Environment.ProcessId);

    public ProcessMetricsSampler(int processId)
    {
        _process = Process.GetProcessById(processId);
        _process.Refresh();
        _processorCount = Math.Max(1, Environment.ProcessorCount);
        _lastCpu = _process.TotalProcessorTime;
        _lastCpuStamp = Stopwatch.GetTimestamp();
        _memoryMb = ReadMemoryMb(_process);
    }

    public void Sample()
        => Sample(enabled: true, metricsRefreshHz: 1, gpuMetricsRefreshSeconds: 3);

    public void Sample(bool enabled, int metricsRefreshHz, int gpuMetricsRefreshSeconds, bool includeGpu = true)
    {
        if (!enabled) return;

        var cpuStamp = Stopwatch.GetTimestamp();
        var now = DateTime.UtcNow;

        var wall = (cpuStamp - _lastCpuStamp) / (double)Stopwatch.Frequency;
        var cpuSampleSeconds = 1.0 / Math.Max(1, metricsRefreshHz);
        if (wall >= Math.Max(CpuSampleSeconds, cpuSampleSeconds))
        {
            try
            {
                _process.Refresh();
                _memoryMb = ReadMemoryMb(_process);
                var cpu = _process.TotalProcessorTime;
                // Same normalization as Task Manager: % of total CPU capacity across logical processors.
                _cpuPct = ClampPercent(100.0 * (cpu - _lastCpu).TotalSeconds / (wall * _processorCount));
                _lastCpuStamp = cpuStamp;
                _lastCpu = cpu;
            }
            catch
            {
                _cpuPct = 0;
                _lastCpuStamp = cpuStamp;
            }
        }

        if (includeGpu && (now - _lastGpuSample).TotalSeconds >= Math.Max(1, gpuMetricsRefreshSeconds))
        {
            _lastGpuSample = now;
            SampleGpu(now, Math.Max(1, gpuMetricsRefreshSeconds));
        }
    }

    public float CpuPercent => (float)_cpuPct;

    /// <summary>Working set (MB) — matches Task Manager working-set / Memory column for most apps.</summary>
    public float WorkingSetMb => _memoryMb;

    public float GpuPercent => _gpuPct;

    public float GpuMemoryMb => _gpuMemoryMb;

    private static float ReadMemoryMb(Process process)
        => (float)(process.WorkingSet64 / (1024.0 * 1024.0));

    private bool IsOurProcessGpuInstance(string instance)
    {
        var head = $"pid_{_process.Id}";
        if (!instance.StartsWith(head, StringComparison.OrdinalIgnoreCase)) return false;
        return instance.Length == head.Length || instance[head.Length] == '_';
    }

    private void SampleGpu(DateTime now, int gpuMetricsRefreshSeconds)
    {
        if (!_gpuInitAttempted)
        {
            _gpuInitAttempted = true;
            try
            {
                if (PerformanceCounterCategory.Exists("GPU Engine"))
                    _gpuEngineCategory = new PerformanceCounterCategory("GPU Engine");
                if (PerformanceCounterCategory.Exists("GPU Process Memory"))
                    _gpuMemoryCategory = new PerformanceCounterCategory("GPU Process Memory");
            }
            catch
            {
                /* counters optional */
            }
        }

        if (_gpuEngineCategory is null && _gpuMemoryCategory is null)
        {
            _gpuPct = Unavailable;
            _gpuMemoryMb = Unavailable;
            return;
        }

        if ((now - _lastGpuCounterRefresh).TotalSeconds > gpuMetricsRefreshSeconds)
            RefreshGpuCounters();

        if (_gpuUtilCounters.Count > 0)
        {
            var peak = Unavailable;
            foreach (var counter in _gpuUtilCounters)
            {
                try
                {
                    var sample = ClampOptionalPercent(counter.NextValue());
                    if (sample >= 0) peak = Math.Max(peak, sample);
                }
                catch { /* stale instance */ }
            }
            _gpuPct = peak;
        }
        else
            _gpuPct = Unavailable;

        if (_gpuMemCounters.Count > 0)
        {
            double totalBytes = 0;
            foreach (var counter in _gpuMemCounters)
            {
                try
                {
                    var sample = counter.NextValue();
                    if (float.IsFinite(sample) && sample > 0)
                        totalBytes += sample;
                }
                catch { /* ignore */ }
            }
            _gpuMemoryMb = totalBytes > 0 ? BytesToMb(totalBytes) : Unavailable;
        }
        else
            _gpuMemoryMb = Unavailable;
    }

    private void RefreshGpuCounters()
    {
        _lastGpuCounterRefresh = DateTime.UtcNow;
        DisposeGpuCounters();

        if (_gpuEngineCategory is not null)
        {
            try
            {
                foreach (var inst in _gpuEngineCategory.GetInstanceNames())
                {
                    if (!IsOurProcessGpuInstance(inst)) continue;
                    try
                    {
                        var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                        c.NextValue();
                        _gpuUtilCounters.Add(c);
                    }
                    catch { /* stale instance */ }
                }
            }
            catch { /* category unavailable */ }
        }

        if (_gpuMemoryCategory is not null)
        {
            try
            {
                foreach (var inst in _gpuMemoryCategory.GetInstanceNames())
                {
                    if (!IsOurProcessGpuInstance(inst)) continue;
                    try
                    {
                        var c = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", inst, true);
                        c.NextValue();
                        _gpuMemCounters.Add(c);
                    }
                    catch { /* dedicated missing */ }
                }
            }
            catch { /* category unavailable */ }
        }
    }

    private void DisposeGpuCounters()
    {
        foreach (var c in _gpuUtilCounters) c.Dispose();
        foreach (var c in _gpuMemCounters) c.Dispose();
        _gpuUtilCounters.Clear();
        _gpuMemCounters.Clear();
    }

    public void Dispose()
    {
        DisposeGpuCounters();
        _process.Dispose();
    }

    private static double ClampPercent(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0.0, 100.0) : 0.0;

    private static float ClampOptionalPercent(float value)
        => float.IsFinite(value) && value >= 0f ? Math.Min(value, 100f) : Unavailable;

    private static float BytesToMb(double bytes)
        => (float)(bytes / (1024.0 * 1024.0));
}
