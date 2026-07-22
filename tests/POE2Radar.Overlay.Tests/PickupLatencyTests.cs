using POE2Radar.Overlay.Input;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class PickupLatencyTests
{
    [Fact]
    public void HumanSpeedProfile_KeepsDetectionAndClickLatencyWithinOneFrameEach()
    {
        PickupTimingProfile timing = PickupTimingProfile.HumanSpeed;

        Assert.InRange(timing.ScanIntervalMs, 1, 16);
        Assert.InRange(timing.CursorSettleMs, 0, 16);
        Assert.InRange(timing.MouseDownMs, 0, 20);
        Assert.InRange(timing.PostClickMs, 0, 16);
        Assert.True(timing.InputPathDelayMs <= 40,
            $"Pickup input path takes {timing.InputPathDelayMs} ms; expected at most 40 ms.");
        Assert.True(timing.DetectionToClickBudgetMs <= 40,
            $"Pickup detection-to-click budget is {timing.DetectionToClickBudgetMs} ms; expected at most 40 ms.");
    }
}
