using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Pricing;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class RitualPricingTests
{
    [Fact]
    public void RitualSettings_DefaultsMatchPortPlan()
    {
        var ritual = new RadarSettings().Ritual;

        Assert.True(ritual.ShowOverlay);
        Assert.Equal(PoeNinjaPriceFetcher.SourcePoe2Scout, ritual.PriceSource);
        Assert.Equal("Runes of Aldur", ritual.League);
        Assert.Equal(5, ritual.RefreshIntervalMin);
        Assert.Equal(1, ritual.DisplayCurrency);
        Assert.Equal(50f, ritual.MinDisplayExalted);
        Assert.True(ritual.PlayValueAlert);
        Assert.Equal(1f, ritual.AlertMinDivine);
        Assert.False(ritual.DiagnosePricing);
        Assert.False(ritual.DebugMode);
        Assert.False(ritual.ForceBfsFallback);
    }

    [Theory]
    [InlineData(0, "divine", "divine.png")]
    [InlineData(1, "ex", "exalted.png")]
    [InlineData(2, "chaos", "chaos.png")]
    public void Format_UsesSelectedCurrencyAndIcon(int displayCurrency, string currency, string icon)
    {
        var display = RitualPriceMath.Format(12.0, displayCurrency);

        Assert.Equal(currency, display.Currency);
        Assert.Equal(icon, display.IconFile);
        Assert.NotEmpty(display.ValueText);
        Assert.True(display.Value > 0);
    }

    [Fact]
    public void PassesMinExalted_FiltersBelowConfiguredThreshold()
    {
        Assert.True(RitualPriceMath.PassesMinExalted(5.0, 50f));
        Assert.False(RitualPriceMath.PassesMinExalted(4.9, 50f));
        Assert.True(RitualPriceMath.PassesMinExalted(0.01, 0f));
    }
}
