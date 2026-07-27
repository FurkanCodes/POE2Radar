using POE2Radar.Overlay.Stealth;
using POE2Radar.Overlay.Web;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public class StealthIdentityTests
{
    [Fact]
    public void GenerateWindowTitle_UsesConfiguredLengthAndAlphabet()
    {
        var title = StealthIdentity.GenerateWindowTitle(12);
        Assert.Equal(12, title.Length);
        Assert.All(title, c => Assert.True(char.IsLetterOrDigit(c)));
    }

    [Fact]
    public void GenerateHardlinkFileName_MatchesPattern()
    {
        var name = StealthIdentity.GenerateHardlinkFileName();
        Assert.True(StealthIdentity.IsHardlinkFileName(name));
        Assert.Matches("^[0-9a-f]{12}\\.exe$", name);
    }

    [Theory]
    [InlineData("abcdef012345.exe", true)]
    [InlineData("ABCDEF012345.EXE", true)]
    [InlineData("AppHost.exe", false)]
    [InlineData("abc.exe", false)]
    [InlineData(null, false)]
    public void IsHardlinkFileName_Validates(string? name, bool expected)
        => Assert.Equal(expected, StealthIdentity.IsHardlinkFileName(name));
}

public class StealthLaunchTests
{
    [Fact]
    public void HasFlag_IsCaseInsensitive()
    {
        Assert.True(StealthLaunch.HasFlag(["--No-Stealth"], StealthLaunch.SkipMarker));
        Assert.False(StealthLaunch.HasFlag(["--other"], StealthLaunch.SkipMarker));
    }

    [Fact]
    public void BuildChildArgs_AppendsRelaunchMarkerAndDropsSkip()
    {
        var child = StealthLaunch.BuildChildArgs(["--no-stealth", "x", StealthLaunch.RelaunchMarker]);
        Assert.Equal(["x", StealthLaunch.RelaunchMarker], child);
    }
}

public class RadarStatePrivacyTests
{
    [Fact]
    public void RadarState_HasNoCharNameProperty()
    {
        var names = typeof(RadarState).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("CharName", names);
        Assert.Contains("CharLevel", names);
    }

    [Fact]
    public void RadarStateEmpty_OmitsCharacterName()
    {
        var empty = RadarState.Empty;
        Assert.Equal("", empty.AreaCode);
        Assert.Equal(0, empty.CharLevel);
    }
}
