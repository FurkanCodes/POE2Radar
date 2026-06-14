using System.Reflection;
using POE2Radar.Overlay.Navigation;
using NumVec2 = System.Numerics.Vector2;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class RouteTrackerTests
{
    private static void ClearReplanCooldown(RouteTracker tracker)
    {
        var field = typeof(RouteTracker).GetField("_lastReplanUtc", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(tracker, DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void Maintain_AdvancesCursorAsPlayerWalks()
    {
        var tracker = new RouteTracker();
        tracker.ApplyResult(
            [(0, 0), (10, 0), (20, 0), (30, 0), (40, 0)],
            new NumVec2(40, 0));

        tracker.Maintain(new NumVec2(25, 0));

        Assert.True(tracker.CurrentPoints.Count < 5);
        Assert.Equal((20, 0), tracker.CurrentPoints[0]);
    }

    [Fact]
    public void ShouldReplan_FiresWhenEmpty()
    {
        var tracker = new RouteTracker();
        Assert.True(tracker.ShouldReplan(new NumVec2(0, 0), new NumVec2(10, 10)));
    }

    [Fact]
    public void ShouldReplan_FiresWhenOffPath()
    {
        var tracker = new RouteTracker();
        tracker.ApplyResult(
            [(0, 0), (10, 0), (20, 0), (30, 0), (40, 0)],
            new NumVec2(40, 0));
        ClearReplanCooldown(tracker);

        Assert.True(tracker.ShouldReplan(new NumVec2(25, 20), new NumVec2(40, 0)));
    }
}
