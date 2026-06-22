using POE2Radar.Overlay.Pricing;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class PoeNinjaPriceBookTests
{
    [Fact]
    public void Format_uses_ex_or_div()
    {
        var book = new PoeNinjaPriceBook(Path.Combine(Path.GetTempPath(), $"poe2radar_test_{Guid.NewGuid():N}.json"));
        Assert.Equal("1.5 ex", book.Format(1.5));
    }

    [Fact]
    public void TryByName_misses_unknown()
    {
        var book = new PoeNinjaPriceBook(Path.Combine(Path.GetTempPath(), $"poe2radar_test_{Guid.NewGuid():N}.json"));
        Assert.Null(book.TryByName("Definitely Not A Real Item Name 9999"));
    }
}
