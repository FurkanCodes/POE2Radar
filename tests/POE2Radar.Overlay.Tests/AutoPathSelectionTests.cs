using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class AutoPathSelectionTests
{
    [Fact]
    public void StopAutomaticTargetDiscovery_PreservesExistingRoutes()
    {
        var selected = new List<string> { "e:30", "t:exit" };
        var automatic = new HashSet<string> { "e:30", "t:exit" };

        RadarApp.StopAutomaticTargetDiscovery(automatic);

        Assert.Equal(["e:30", "t:exit"], selected);
        Assert.Empty(automatic);
    }
}
