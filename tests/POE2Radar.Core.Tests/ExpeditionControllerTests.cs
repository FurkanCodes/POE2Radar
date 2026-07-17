using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class ExpeditionControllerTests
{
    [Fact]
    public void EmptyPlacedVector_IsValidControllerState()
    {
        var ok = Poe2Live.TryParseExpeditionController(10, 0, 0, out var total, out var placed);
        Assert.True(ok);
        Assert.Equal(10, total);
        Assert.Equal(0, placed);
    }

    [Fact]
    public void PlacedVector_UsesPointerCount()
    {
        var first = (nint)0x10000;
        var ok = Poe2Live.TryParseExpeditionController(5, first, first + 3 * IntPtr.Size, out var total, out var placed);
        Assert.True(ok);
        Assert.Equal(5, total);
        Assert.Equal(3, placed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void ImplausibleTotal_IsRejected(int rawTotal)
        => Assert.False(Poe2Live.TryParseExpeditionController(rawTotal, 0, 0, out _, out _));

    [Fact]
    public void MorePlacedThanTotal_IsRejected()
    {
        var first = (nint)0x10000;
        Assert.False(Poe2Live.TryParseExpeditionController(2, first, first + 3 * IntPtr.Size, out _, out _));
    }
}
