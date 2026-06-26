using POE2Radar.Overlay.Navigation;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class AutoPathSelectionTests
{
    [Fact]
    public void FilterDesiredAuto_SkipsReachedAndDismissed()
    {
        var reached = new HashSet<string>(StringComparer.Ordinal) { "t:a" };
        var dismissed = new HashSet<string>(StringComparer.Ordinal) { "t:b" };

        var result = AutoPathSelection.FilterDesiredAuto(["t:a", "t:b", "t:c", "t:d"], reached, dismissed);

        Assert.Equal(["t:c", "t:d"], result);
    }

    [Fact]
    public void FilterDesiredAuto_RespectsMaxAutoCap()
    {
        var result = AutoPathSelection.FilterDesiredAuto(
            Enumerable.Range(0, 30).Select(i => $"t:{i}"),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            maxAuto: 3);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ApplyAutoDiff_AddsAndRemovesWithoutTouchingManual()
    {
        var selected = new List<string> { "t:manual", "t:old-auto" };
        var auto = new HashSet<string>(StringComparer.Ordinal) { "t:old-auto" };

        AutoPathSelection.ApplyAutoDiff(selected, auto, ["t:new-auto"]);

        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "t:manual", "t:new-auto" }, selected.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(["t:new-auto"], auto.ToList());
    }

    [Fact]
    public void CanAddManual_AllowsEightManualOnly()
    {
        var selected = new List<string>();
        var auto = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < AutoPathSelection.MaxManualTargets; i++)
            selected.Add($"m:{i}");

        Assert.False(AutoPathSelection.CanAddManual(selected, auto, "m:extra"));
    }
}
