using System.Diagnostics;
using System.Globalization;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Pricing;
using POE2Radar.Overlay.StashUtility;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private static string StashValueDir => Path.Combine(AppContext.BaseDirectory, "StashValue");

    private bool _stashValueInitialized;
    private string _stashValuePricingConfigKey = "";
    private StashValueLabel[] _stashValueLabels = [];
    private StashUtilityHighlight[] _stashUtilityHighlights = [];
    private HoveredTabletTierView? _hoveredTabletTier;
    private Poe2Live.StashValueSlot[] _stashValueSlots = [];
    private HashSet<nint> _stashInventoryEntities = [];
    private StashValueRuntimeStatus _stashValueStatus = StashValueRuntimeStatus.Empty;
    private readonly PerformanceCadence _stashValueCadence = new();
    private bool _stashUtilityPadConnected;
    private bool _stashUtilityForceScan;
    private int _stashUtilityReadMissStreak;

    private const int StashValueScanHz = 8;
    private const int StashUtilityTransientMissGrace = 4;

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

        var padConnected = GamepadInput.IsConnected(_settings.GamepadUserIndex);
        if (padConnected != _stashUtilityPadConnected)
        {
            _stashUtilityPadConnected = padConnected;
            _stashUtilityForceScan = true;
            _stashUtilityReadMissStreak = 0;
            _live.InvalidateStashValueUiCache();
        }

        if (!_stashUtilityForceScan && !_stashValueCadence.IsDue(StashValueScanHz))
            return;
        _stashUtilityForceScan = false;

        if (_settings.StashValue.ShowOverlay || _settings.StashValue.ShowInventoryOverlay || _settings.StashValue.ShowDebugInfo)
        {
            InitializeStashValuePricing(live);
            PoeNinjaPriceFetcher.RefreshIfNeeded();
        }

        TryGetCursorClient(out var cursor);
        var scanStart = Stopwatch.GetTimestamp();
        var read = _live.ReadStashValueSlots(live.InGameState, windowWidth, windowHeight, cursor);
        if (ShouldHoldStashUtilityReadMiss(read.Slots.Length, _stashUtilityHighlights.Length, _stashUtilityReadMissStreak))
        {
            _stashUtilityReadMissStreak++;
            return;
        }
        _stashUtilityReadMissStreak = read.Slots.Length == 0 ? _stashUtilityReadMissStreak + 1 : 0;

        _stashValueSlots = read.Slots;
        if (_settings.WaystoneAlchemy.Enabled)
        {
            var inventory = _live.ReadLootInventorySnapshot(live.AreaInstance);
            _stashInventoryEntities = inventory.Ok
                ? inventory.Items.Select(i => i.ItemEntity).ToHashSet()
                : [];
        }
        else if (_stashInventoryEntities.Count > 0)
        {
            _stashInventoryEntities = [];
        }

        var labels = BuildStashValueLabels(read);
        var highlights = BuildStashUtilityHighlights(read);
        _hoveredTabletTier = BuildHoveredTabletTier(read);
        var scanMs = ElapsedMs(scanStart);

        _stashValueLabels = labels;
        _stashUtilityHighlights = highlights;
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
        return s.ShowOverlay || s.ShowInventoryOverlay || s.ShowDebugInfo
            || StashUtilityRules.IsEnabled(_settings.StashUtility)
            || _settings.StashUtility.ShowTabletTiersOnHover
            || _settings.WaystoneAlchemy.Enabled;
    }

    private void ClearStashValue()
    {
        if (_stashValueLabels.Length > 0)
            _stashValueLabels = [];
        if (_stashUtilityHighlights.Length > 0)
            _stashUtilityHighlights = [];
        _hoveredTabletTier = null;
        if (_stashValueSlots.Length > 0)
            _stashValueSlots = [];
        if (_stashInventoryEntities.Count > 0)
            _stashInventoryEntities = [];
        _stashUtilityReadMissStreak = 0;
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

    private StashUtilityHighlight[] BuildStashUtilityHighlights(Poe2Live.StashValueRead read)
    {
        if (read.Slots.Length == 0 || !StashUtilityRules.IsEnabled(_settings.StashUtility)) return [];
        var s = _settings.StashUtility;
        var result = new List<StashUtilityHighlight>();

        foreach (var slot in read.Slots)
        {
            if (!StashUtilityRules.TryEvaluate(slot, s, out var evaluation)) continue;
            var tablet = evaluation.Kind == StashUtilityKind.Tablet;
            var border = PackColor(tablet
                ? (evaluation.Bad ? s.TabletBadColor : s.TabletGoodColor)
                : (evaluation.Bad ? s.WaystoneBadColor : s.WaystoneGoodColor));
            var great = PackColor(tablet ? s.TabletGreatColor : s.WaystoneGreatColor);
            var rarity = PackColor(slot.Rarity switch
            {
                Poe2Live.Rarity.Magic => "#6699FF",
                Poe2Live.Rarity.Rare => "#FFD900",
                Poe2Live.Rarity.Unique => "#FF8000",
                _ => "#CCCCCC",
            });

            result.Add(new StashUtilityHighlight(
                new NumVec2(slot.Rect.X, slot.Rect.Y),
                new NumVec2(slot.Rect.W, slot.Rect.H),
                border,
                great,
                rarity,
                Math.Clamp(s.BorderThickness, 1f, 10f),
                Math.Clamp(s.BorderMargin, 0f, 20f),
                evaluation.Bad ? Math.Clamp(s.BadBorderStyle, 0, 2) : Math.Clamp(s.GoodBorderStyle, 0, 2),
                evaluation.Great,
                s.ShowGreatArrow,
                Math.Clamp(s.GreatArrowSize, 5f, 60f),
                Math.Clamp(s.GreatArrowCorner, 0, 3),
                s.ShowRarityCorner,
                Math.Clamp(s.RarityCornerSize, 3f, 30f),
                evaluation.Summary));
        }

        return result.Count == 0 ? [] : result.ToArray();
    }

    private HoveredTabletTierView? BuildHoveredTabletTier(Poe2Live.StashValueRead read)
    {
        if (!_settings.StashUtility.ShowTabletTiersOnHover)
            return null;

        foreach (var slot in read.Slots)
        {
            if (!TabletTierHover.TryBuild(slot, out var summary))
                continue;

            return new HoveredTabletTierView(
                new NumVec2(slot.Rect.X, slot.Rect.Y),
                new NumVec2(slot.Rect.W, slot.Rect.H),
                summary.TabletType,
                summary.OverallTier,
                PackColor(summary.OverallColor),
                summary.Modifiers
                    .Select(modifier => new TabletTierLineView(
                        modifier.Tier,
                        PackColor(modifier.Color),
                        modifier.Modifier,
                        modifier.Roll))
                    .ToArray());
        }

        return null;
    }

    internal static bool ShouldHoldStashUtilityReadMiss(int slotCount, int priorHighlightCount, int missStreak)
        => slotCount == 0 && priorHighlightCount > 0 && missStreak < StashUtilityTransientMissGrace;
}
