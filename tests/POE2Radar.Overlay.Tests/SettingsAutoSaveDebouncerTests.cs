using System.Diagnostics;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class SettingsAutoSaveDebouncerTests
{
    [Fact]
    public void ShouldFlush_WaitsForDebounce()
    {
        var debouncer = new SettingsAutoSaveDebouncer();
        debouncer.SeedIfNeeded("{\"a\":0}");
        var t0 = Stopwatch.GetTimestamp();
        debouncer.Touch("{\"a\":1}", t0);

        var early = t0 + (long)(Stopwatch.Frequency * 0.1);
        var late = t0 + (long)(Stopwatch.Frequency * 0.4);

        Assert.False(debouncer.ShouldFlush(early, force: false));
        Assert.True(debouncer.ShouldFlush(late, force: false));
    }

    [Fact]
    public void ShouldFlush_ForceBypassesDebounce()
    {
        var debouncer = new SettingsAutoSaveDebouncer();
        var t0 = Stopwatch.GetTimestamp();
        debouncer.Touch("{\"a\":1}", t0);

        Assert.True(debouncer.ShouldFlush(t0, force: true));
    }

    [Fact]
    public void NoteSaved_ClearsPendingFlush()
    {
        var debouncer = new SettingsAutoSaveDebouncer();
        var t0 = Stopwatch.GetTimestamp();
        debouncer.Touch("{\"a\":1}", t0);
        debouncer.NoteSaved("{\"a\":1}");

        Assert.False(debouncer.ShouldFlush(t0 + Stopwatch.Frequency, force: false));
    }

    [Fact]
    public void Touch_IgnoresUnchangedSnapshot()
    {
        var debouncer = new SettingsAutoSaveDebouncer();
        debouncer.SeedIfNeeded("{\"a\":1}");
        var t0 = Stopwatch.GetTimestamp();
        debouncer.Touch("{\"a\":1}", t0);

        Assert.False(debouncer.ShouldFlush(t0 + Stopwatch.Frequency, force: false));
    }
}
