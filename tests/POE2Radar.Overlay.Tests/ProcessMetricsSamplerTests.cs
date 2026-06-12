using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class ProcessMetricsSamplerTests
{
    [Fact]
    public void Sample_Disabled_DoesNotRefreshMetrics()
    {
        using var sampler = new ProcessMetricsSampler(Environment.ProcessId);
        var cpu = sampler.CpuPercent;
        var memory = sampler.WorkingSetMb;
        var gpu = sampler.GpuPercent;
        var gpuMemory = sampler.GpuMemoryMb;

        sampler.Sample(enabled: false, metricsRefreshHz: 10, gpuMetricsRefreshSeconds: 1);

        Assert.Equal(cpu, sampler.CpuPercent);
        Assert.Equal(memory, sampler.WorkingSetMb);
        Assert.Equal(gpu, sampler.GpuPercent);
        Assert.Equal(gpuMemory, sampler.GpuMemoryMb);
    }
}
