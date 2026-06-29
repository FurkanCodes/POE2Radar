using System.Diagnostics;
using System.Globalization;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Pricing;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private static string StashValueDir => Path.Combine(AppContext.BaseDirectory, "StashValue");

    private bool _stashValueInitialized;
    private string _stashValuePricingConfigKey = "";
    private StashValueLabel[] _stashValueLabels = [];
    private StashValueRuntimeStatus _stashValueStatus = StashValueRuntimeStatus.Empty;
    private readonly PerformanceCadence _stashValueCadence = new();

    private const int StashValueScanHz = 8;

    private sealed record StashValueRuntimeStatus(
        bool Active,
        int SlotCount,
        int LabelCount,
        int ScannedNodes,
        int CandidateSlots,
        bool AnyHovered,
        double LastScanMs,
        string League)
    {
        public static readonly StashValueRuntimeStatus Empty = new(false, 0, 0, 0, 0, false, 0, "");
    }

    private void RefreshStashValue(LiveFrameState live, int windowWidth, int windowHeight, bool drawActive)
    {
        if (!NeedsStashValueWork(drawActive) || !live.InGame || windowWidth <= 0 || windowHeight <= 0)
        {
            ClearStashValue();
            return;
        }

        if (!_stashValueCadence.IsDue(StashValueScanHz))
            return;

        InitializeStashValuePricing(live);
        PoeNinjaPriceFetcher.RefreshIfNeeded();

        TryGetCursorClient(out var cursor);
        var scanStart = Stopwatch.GetTimestamp();
        var read = _live.ReadStashValueSlots(live.InGameState, windowWidth, windowHeight, cursor);
        var labels = BuildStashValueLabels(read);
        var scanMs = ElapsedMs(scanStart);

        _stashValueLabels = labels;
        _stashValueStatus = new StashValueRuntimeStatus(
            true,
            read.Slots.Length,
            labels.Length,
            read.ScannedNodes,
            read.CandidateSlots,
            read.AnyHovered,
            scanMs,
            EffectiveStashValueLeague(live));
    }

    private bool NeedsStashValueWork(bool drawActive)
    {
        var s = _settings.StashValue;
        if (!drawActive) return false;
        return s.ShowOverlay || s.ShowInventoryOverlay || s.ShowDebugInfo;
    }

    private void ClearStashValue()
    {
        if (_stashValueLabels.Length > 0)
            _stashValueLabels = [];
        if (_stashValueStatus.Active)
            _stashValueStatus = StashValueRuntimeStatus.Empty;
    }

    private void InitializeStashValuePricing(LiveFrameState live)
    {
        Directory.CreateDirectory(StashValueDir);

        var source = Math.Clamp(_settings.StashValue.PriceSource, 0, 1);
        var league = EffectiveStashValueLeague(live);
        var refresh = Math.Max(1, _settings.StashValue.RefreshIntervalMin);
        var key = $"{source}|{league}|{refresh}";

        LeagueProvider.EnsureLoaded();
        PoeNinjaPriceFetcher.Configure(source, league, refresh);

        if (!_stashValueInitialized)
        {
            PoeNinjaPriceFetcher.Initialize(StashValueDir);
            _stashValueInitialized = true;
            _stashValuePricingConfigKey = key;
            return;
        }

        if (!string.Equals(_stashValuePricingConfigKey, key, StringComparison.Ordinal))
        {
            _stashValuePricingConfigKey = key;
            PoeNinjaPriceFetcher.ForceRefresh(StashValueDir, ignoreCooldown: true);
        }
    }

    private string EffectiveStashValueLeague(LiveFrameState live)
    {
        var configured = _settings.StashValue.League?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && !string.Equals(configured, "Auto", StringComparison.OrdinalIgnoreCase))
            return configured!;

        if (live.AreaInstance != 0)
        {
            var liveLeague = _live.LeagueName(live.AreaInstance);
            if (!string.IsNullOrWhiteSpace(liveLeague))
                return liveLeague.Trim();
        }

        return "Runes of Aldur";
    }

    private StashValueLabel[] BuildStashValueLabels(Poe2Live.StashValueRead read)
    {
        if (read.Slots.Length == 0)
            return [];

        var s = _settings.StashValue;
        var result = new List<StashValueLabel>(read.Slots.Length);
        var baseFontSize = Math.Max(10f, _settings.UiFontSize);
        var fontSize = baseFontSize * Math.Clamp(s.PriceFontScale, 0.5f, 2f);
        var textColor = PackColor(s.PriceTextColor);
        var hidePrice = s.HidePriceOnHover && read.AnyHovered;

        foreach (var slot in read.Slots)
        {
            if (slot.Panel == Poe2Live.StashValuePanel.Stash && !s.ShowOverlay && !s.ShowDebugInfo)
                continue;
            if (slot.Panel == Poe2Live.StashValuePanel.Inventory && !s.ShowInventoryOverlay && !s.ShowDebugInfo)
                continue;

            if (!TryPriceStashValueSlot(slot, out var valueText))
                continue;

            var pos = new NumVec2(
                slot.Rect.X + s.PriceOffsetX,
                slot.Rect.Y + slot.Rect.H - fontSize + s.PriceOffsetY);
            var size = new NumVec2(slot.Rect.W, slot.Rect.H);
            var debug = s.ShowDebugInfo;
            var debugText = debug ? $"E: {slot.ItemEntity:X}" : "";

            result.Add(new StashValueLabel(
                pos,
                size,
                valueText,
                textColor,
                fontSize,
                debug,
                debugText,
                hidePrice));
        }

        return result.Count == 0 ? [] : result.ToArray();
    }

    private bool TryPriceStashValueSlot(Poe2Live.StashValueSlot slot, out string valueText)
    {
        valueText = "";

        var itemName = slot.BaseItemName;
        if (slot.Rarity == Poe2Live.Rarity.Unique && !string.IsNullOrEmpty(slot.ArtBasename))
        {
            foreach (var key in ArtKeyVariants(slot.ArtBasename))
            {
                if (PoeNinjaPriceFetcher.TryResolveDisplayName(key, out var uniqueName) &&
                    !PoeNinjaPriceFetcher.IsGenericLookupName(uniqueName))
                {
                    itemName = uniqueName;
                    break;
                }

                if (PoeNinjaPriceFetcher.HasPriceDataForName(key))
                {
                    itemName = key;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(itemName))
            itemName = slot.InternalName;
        if (string.IsNullOrWhiteSpace(itemName))
            return false;

        var scoutText = slot.ModLines.Length > 0
            ? BuildScoutText(itemName, InferBaseTypeFromMetadataPath(slot.FullItemPath))
            : "";

        var price = PoeNinjaPriceFetcher.GetPrice(
            itemName,
            slot.ModLines,
            slot.InternalName,
            slot.FullItemPath,
            scoutText);
        if (price is null)
            return false;

        var priceChaos = price.PriceChaos * Math.Max(1, slot.StackCount);
        var priced = new PoeNinjaPrice { PriceChaos = priceChaos };
        var (displayValue, displayCurrency) = PoeNinjaPriceFetcher.GetDisplayPrice(priced, _settings.StashValue.DisplayCurrency);
        if (_settings.StashValue.MinValueEx > 0f && displayValue < _settings.StashValue.MinValueEx)
            return false;

        valueText = FormatStashValue(displayValue, displayCurrency);
        return true;
    }

    private static string FormatStashValue(double value, string currency) => currency switch
    {
        "divine" => value.ToString("0.00", CultureInfo.InvariantCulture) + " div",
        "chaos" => value.ToString("0.#", CultureInfo.InvariantCulture) + " c",
        _ => value.ToString("0.#", CultureInfo.InvariantCulture) + " ex",
    };
}
