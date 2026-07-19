using System.Reflection;
using POE2Radar.Overlay.Pricing;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class PoeNinjaPriceFetcherPathIndexTests
{
    [Fact]
    public void IndexPathName_MarksSharedApiIdAmbiguousInsteadOfRelabelingExaltedOrb()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        IndexPathName(index, "CurrencyAddModToRare", "Exalted Orb");
        IndexPathName(index, "CurrencyAddModToRare", "Greater Exalted Orb");
        IndexPathName(index, "CurrencyAddModToRare", "Perfect Exalted Orb");

        Assert.True(index.TryGetValue("currencyaddmodtorare", out var displayName));
        Assert.Equal(string.Empty, displayName);
    }

    private static void IndexPathName(
        Dictionary<string, string> index,
        string pathBasename,
        string displayName)
    {
        var method = typeof(PoeNinjaPriceFetcher).GetMethod(
            "IndexPathName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method.Invoke(null, [index, pathBasename, displayName]);
    }
}
