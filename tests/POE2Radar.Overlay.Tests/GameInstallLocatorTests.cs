using POE2Radar.Overlay;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class GameInstallLocatorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(@"C:\missing\PathOfExile.exe", false)]
    public void IsValidGameExe_RejectsMissingOrInvalid(string? path, bool _)
    {
        Assert.False(GameInstallLocator.IsValidGameExe(path));
    }

    [Fact]
    public void IsValidGameExe_AcceptsTempPoE2Exe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "poe2radar-locator-test");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "PathOfExileSteam.exe");
        try
        {
            File.WriteAllText(exe, "");
            Assert.True(GameInstallLocator.IsValidGameExe(exe));
        }
        finally
        {
            try { File.Delete(exe); Directory.Delete(dir); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Discover_WithSavedPath_UsesSavedPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "poe2radar-locator-saved");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "PathOfExile.exe");
        try
        {
            File.WriteAllText(exe, "");
            var result = GameInstallLocator.Discover(exe);
            Assert.True(result.Found);
            Assert.Equal(GameInstallLocator.LocateSource.Saved, result.Source);
            Assert.Equal(Path.GetFullPath(exe), result.Path);
        }
        finally
        {
            try { File.Delete(exe); Directory.Delete(dir); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Discover_IgnoresInvalidSavedPath()
    {
        var result = GameInstallLocator.Discover(@"Z:\no-such\PathOfExile.exe");
        Assert.NotEqual(@"Z:\no-such\PathOfExile.exe", result.Path);
        if (result.Found)
            Assert.True(GameInstallLocator.IsValidGameExe(result.Path));
        else
            Assert.Equal(GameInstallLocator.LocateSource.None, result.Source);
    }
}
