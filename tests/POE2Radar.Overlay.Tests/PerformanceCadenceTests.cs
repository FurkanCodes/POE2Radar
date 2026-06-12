using System.Diagnostics;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class PerformanceCadenceTests
{
    [Fact]
    public void IsDue_FiresOnlyAfterConfiguredInterval()
    {
        var cadence = new PerformanceCadence();
        var start = Stopwatch.Frequency * 10;
        var interval = Stopwatch.Frequency / 10;

        Assert.True(cadence.IsDue(start, 10));
        Assert.False(cadence.IsDue(start + interval - 1, 10));
        Assert.True(cadence.IsDue(start + interval, 10));
    }

    [Theory]
    [InlineData(0, 1, 60, 1)]
    [InlineData(120, 1, 60, 60)]
    [InlineData(30, 1, 60, 30)]
    public void ClampHz_ConstrainsCadenceSettings(int input, int min, int max, int expected)
        => Assert.Equal(expected, PerformanceCadence.ClampHz(input, min, max));

    [Fact]
    public void SleepMillisecondsForHz_NeverReturnsZero()
    {
        Assert.Equal(1000, PerformanceCadence.SleepMillisecondsForHz(0));
        Assert.Equal(1, PerformanceCadence.SleepMillisecondsForHz(2000));
    }
}
