// Source-port of RitualHelper GPLv3 behavior; see RitualHelper.GPLv3.LICENSE.txt in this folder.
using System.Globalization;

namespace POE2Radar.Overlay.Pricing;

internal readonly record struct RitualDisplayPrice(
    double Value,
    string Currency,
    string ValueText,
    string IconFile,
    double DivineValue);

internal static class RitualPriceMath
{
    public static bool PassesMinExalted(double priceChaos, float minDisplayExalted)
    {
        if (minDisplayExalted <= 0f) return true;
        var (exValue, _) = PoeNinjaPriceFetcher.GetDisplayPrice(
            new PoeNinjaPrice { PriceChaos = priceChaos },
            1);
        return exValue >= minDisplayExalted;
    }

    public static RitualDisplayPrice Format(double priceChaos, int displayCurrency)
    {
        var (displayValue, displayCurrencyName) = PoeNinjaPriceFetcher.GetDisplayPrice(
            new PoeNinjaPrice { PriceChaos = priceChaos },
            Math.Clamp(displayCurrency, 0, 2));

        var valueText = displayCurrencyName switch
        {
            "divine" => displayValue.ToString("0.000", CultureInfo.InvariantCulture),
            "chaos" => displayValue.ToString("0.#", CultureInfo.InvariantCulture),
            _ => displayValue.ToString("0.#", CultureInfo.InvariantCulture),
        };
        var iconFile = displayCurrencyName switch
        {
            "divine" => "divine.png",
            "chaos" => "chaos.png",
            _ => "exalted.png",
        };
        var divineValue = priceChaos / Math.Max(PoeNinjaPriceFetcher.GetChaosPerDivine(), 1.0);
        return new RitualDisplayPrice(displayValue, displayCurrencyName, valueText, iconFile, divineValue);
    }
}
