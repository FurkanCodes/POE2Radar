using POE2Radar.Overlay;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class GameAttachTests
{
    [Fact]
    public void Probe_WhenPoENotRunning_ReturnsPoENotRunning()
    {
        // CI/dev machines typically don't have PoE2 running.
        var result = AttachResult.Probe();
        if (result.Status == AttachStatus.PoENotRunning)
        {
            Assert.Equal("PoE2 not running (no matching process found).", result.StatusTitle);
            Assert.False(result.CanStart);
        }
        else
        {
            Assert.True(
                result.Status is AttachStatus.NotInZone or AttachStatus.Ready or AttachStatus.AccessDenied,
                $"Unexpected attach status on machine with PoE running: {result.Status}");
        }

        result.Dispose();
    }

    [Fact]
    public void Take_ThrowsWhenNotReady()
    {
        var result = new AttachResult { Status = AttachStatus.PoENotRunning };
        Assert.Throws<InvalidOperationException>(() => result.Take());
    }
}
