using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Pricing;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private static string RitualDir => Path.Combine(AppContext.BaseDirectory, "RitualHelper");

    private bool _ritualInitialized;
    private string _ritualPricingConfigKey = "";
    private RitualPriceLabel[] _ritualLabels = [];
    private RitualPanelRow[] _ritualPanelRows = [];
    private DateTime _nextRitualRecomputeUtc = DateTime.MinValue;
    private bool _wasRitualWindowOpen;
    private readonly Dictionary<string, double> _ritualSessionStablePriceChaos = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ritualAlertedItemsThisSession = new(StringComparer.OrdinalIgnoreCase);
    private int? _pendingRitualSound;
    private Dictionary<string, string>? _ritualNameCache;
    private RitualRuntimeStatus _ritualStatus = RitualRuntimeStatus.Empty;
    private nint _ritualAreaInstance;
    private readonly PerformanceCadence _ritualIdleCadence = new();
    private bool _ritualPadConnected;
    private int _ritualReadMissStreak;
    private readonly Dictionary<nint, Poe2Live.UiRect> _ritualTileRects = new();

    private const int RitualIdleScanHz = 6;
    private const int RitualPricesWindowIdleHz = 12;
    private const int RitualTransientMissGrace = 4;
    private const int RitualPriceRecomputeMs = 500;
    private const int RitualSettingsPriceRecomputeMs = 200;

    private sealed record RitualRuntimeStatus(
        bool Open,
        string Source,
        string League,
        string Branch,
        string GridAddress,
        int SlotCount,
        int LabelCount,
        double LastScanMs,
        double LastRecomputeMs,
        DateTime LastSeenUtc,
        string Note)
    {
        public static readonly RitualRuntimeStatus Empty = new(
            false, "", "", "", "0x0", 0, 0, 0, 0, DateTime.MinValue, "");
    }

    private void RefreshRitual(LiveFrameState live, int windowWidth, int windowHeight, bool drawActive)
    {
        if (!NeedsRitualWork(drawActive))
        {
            ClearRitualWindowSession(open: false);
            _ritualPanelRows = [];
            return;
        }

        if (!live.InGame || windowWidth <= 0 || windowHeight <= 0)
        {
            ClearRitualWindowSession(open: false);
            _ritualPanelRows = [];
            return;
        }

        if (live.AreaInstance != _ritualAreaInstance)
        {
            _ritualAreaInstance = live.AreaInstance;
            _ritualReadMissStreak = 0;
            _live.InvalidateRitualUiCache();
            ClearRitualWindowSession(open: false);
            _ritualPanelRows = [];
        }

        var scanHz = RitualScanHz(_wasRitualWindowOpen, _settings.Ritual.ShowPricesWindow, _settings.LiveRefreshHz);
        if (!_ritualIdleCadence.IsDue(scanHz))
            return;

        InitializeRitualPricing(live);

        var padConnected = GamepadInput.IsConnected(_settings.GamepadUserIndex);
        if (padConnected != _ritualPadConnected)
        {
            _ritualPadConnected = padConnected;
            _ritualReadMissStreak = 0;
            _live.InvalidateRitualUiCache();
            ClearRitualWindowSession(open: false);
            _ritualPanelRows = [];
        }

        var now = DateTime.UtcNow;
        var scanStart = Stopwatch.GetTimestamp();
        var rewards = _live.ReadRitualRewards(
            live.InGameState,
            windowWidth,
            windowHeight,
            _settings.Ritual.ForceBfsFallback);
        var scanMs = ElapsedMs(scanStart);

        if (!rewards.IsOpen)
        {
            var missStreak = _ritualReadMissStreak + 1;
            if (ShouldHoldRitualReadMiss(_wasRitualWindowOpen, _ritualPanelRows.Length, _ritualLabels.Length, missStreak))
            {
                _ritualReadMissStreak = missStreak;
                _ritualStatus = _ritualStatus with
                {
                    Open = true,
                    Source = rewards.Source,
                    Branch = rewards.Branch.ToString(),
                    LastScanMs = scanMs,
                    LastSeenUtc = now,
                    Note = string.IsNullOrWhiteSpace(rewards.Note) ? "transient miss grace" : rewards.Note,
                };
                FlushPendingRitualSound();
                return;
            }

            _ritualReadMissStreak = 0;
            ClearRitualWindowSession(open: false);
            _ritualPanelRows = [];
            _ritualTileRects.Clear();
            _ritualStatus = _ritualStatus with
            {
                Open = false,
                Source = rewards.Source,
                Branch = rewards.Branch.ToString(),
                GridAddress = "0x0",
                SlotCount = 0,
                LabelCount = 0,
                LastScanMs = scanMs,
                LastRecomputeMs = 0,
                LastSeenUtc = now,
                Note = rewards.Note,
            };
            FlushPendingRitualSound();
            return;
        }

        _ritualReadMissStreak = 0;
        _wasRitualWindowOpen = true;
        PoeNinjaPriceFetcher.RefreshIfNeeded();

        var intervalMs = RitualRecomputeIntervalMs(_imguiOverlay?.IsSettingsOpen == true);
        if (now < _nextRitualRecomputeUtc && (_ritualLabels.Length > 0 || _ritualPanelRows.Length > 0))
        {
            _ritualStatus = new RitualRuntimeStatus(
                true,
                rewards.Source,
                EffectiveRitualLeague(live),
                rewards.Branch.ToString(),
                $"0x{rewards.GridAddress:X}",
                rewards.Slots.Length,
                _ritualLabels.Length,
                scanMs,
                _ritualStatus.LastRecomputeMs,
                now,
                rewards.Note);
            FlushPendingRitualSound();
            return;
        }

        _nextRitualRecomputeUtc = now.AddMilliseconds(intervalMs);

        var recomputeStart = Stopwatch.GetTimestamp();
        var panelRows = BuildRitualPanelRows(rewards);
        if (panelRows.Length > 0 || _ritualPanelRows.Length == 0)
            _ritualPanelRows = panelRows;

        if (_settings.Ritual.ShowOverlay && drawActive)
        {
            var labelSlots = MergeRitualLabelSlots(rewards, windowWidth, windowHeight);
            var labels = BuildRitualLabels(labelSlots);
            if (labels.Length > 0 || _ritualLabels.Length == 0)
                _ritualLabels = labels;
        }
        else if (_ritualLabels.Length > 0)
            _ritualLabels = [];
        var recomputeMs = ElapsedMs(recomputeStart);

        _ritualStatus = new RitualRuntimeStatus(
            true,
            rewards.Source,
            EffectiveRitualLeague(live),
            rewards.Branch.ToString(),
            $"0x{rewards.GridAddress:X}",
            rewards.Slots.Length,
            _ritualLabels.Length,
            scanMs,
            recomputeMs,
            now,
            rewards.Note);

        FlushPendingRitualSound();
    }

    internal static int RitualScanHz(bool wasWindowOpen, bool showPricesWindow, int liveRefreshHz)
    {
        _ = showPricesWindow;
        _ = liveRefreshHz;
        return wasWindowOpen ? RitualPricesWindowIdleHz : RitualIdleScanHz;
    }

    internal static int RitualRecomputeIntervalMs(bool settingsOpen)
        => settingsOpen ? RitualSettingsPriceRecomputeMs : RitualPriceRecomputeMs;

    internal static bool ShouldHoldRitualReadMiss(bool wasWindowOpen, int panelRowCount, int labelCount, int missStreak)
        => wasWindowOpen
           && (panelRowCount > 0 || labelCount > 0)
           && missStreak is > 0 and <= RitualTransientMissGrace;

    private bool NeedsRitualWork(bool drawActive)
    {
        var r = _settings.Ritual;
        if (r.DiagnosePricing || r.DebugMode) return drawActive;
        if (r.ShowPricesWindow) return true;
        if (r.ShowOverlay && drawActive) return true;
        return false;
    }

    private bool RitualShowPricesWindow()
        => _settings.Ritual.ShowPricesWindow;

    private void InitializeRitualPricing(LiveFrameState live)
    {
        Directory.CreateDirectory(RitualDir);

        var source = Math.Clamp(_settings.Ritual.PriceSource, 0, 1);
        var league = EffectiveRitualLeague(live);
        var refresh = Math.Max(1, _settings.Ritual.RefreshIntervalMin);
        var key = $"{source}|{league}|{refresh}";

        LeagueProvider.EnsureLoaded();
        RitualCurrencyIcons.Initialize(RitualDir);
        PoeNinjaPriceFetcher.Configure(source, league, refresh);

        if (!_ritualInitialized)
        {
            PoeNinjaPriceFetcher.Initialize(RitualDir);
            _ritualInitialized = true;
            _ritualPricingConfigKey = key;
            return;
        }

        if (!string.Equals(_ritualPricingConfigKey, key, StringComparison.Ordinal))
        {
            _ritualPricingConfigKey = key;
            PoeNinjaPriceFetcher.ForceRefresh(RitualDir, ignoreCooldown: true);
        }
    }

    private string EffectiveRitualLeague(LiveFrameState live)
    {
        var configured = _settings.Ritual.League?.Trim();
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

    private RitualPriceLabel[] BuildRitualLabels(Poe2Live.RitualRewardSlot[] slots)
    {
        if (slots.Length == 0)
            return [];

        var s = _settings.Ritual;
        var result = new List<RitualPriceLabel>(slots.Length);
        var baseFontSize = Math.Max(10f, _settings.UiFontSize);
        var fontSize = baseFontSize * Math.Clamp(s.PriceFontScale, 0.4f, 4f);
        var diagFontSize = fontSize * 0.8f;
        var textColor = PackColor(s.PriceTextColor);

        foreach (var slot in slots)
        {
            if (slot.Rect.W <= 1f || slot.Rect.H <= 1f)
                continue;
            if (IsRitualPlaceholderSlot(slot))
                continue;

            var itemName = ResolveRitualItemDisplayName(slot);
            var scoutText = slot.ModLines.Length > 0
                ? BuildScoutText(itemName, InferBaseTypeFromMetadataPath(slot.FullItemPath))
                : "";

            var price = PoeNinjaPriceFetcher.GetPrice(
                itemName,
                slot.ModLines,
                slot.InternalName,
                slot.FullItemPath,
                scoutText);

            var diagPos = new NumVec2(slot.Rect.X + 2f, slot.Rect.Y + 2f);
            if (price is null)
            {
                if (s.DiagnosePricing && !string.IsNullOrEmpty(slot.InternalName))
                {
                    result.Add(new RitualPriceLabel(
                        default,
                        "",
                        0,
                        0,
                        "",
                        textColor,
                        fontSize,
                        0,
                        $"{slot.Rarity} NO PRICE\nbase:{NonEmpty(slot.BaseItemName)}\nart:{NonEmpty(slot.ArtBasename)}\nname:{NonEmpty(itemName)}\nint:{slot.InternalName}",
                        diagPos,
                        diagFontSize));
                }
                continue;
            }

            var priceChaos = StabilizeRitualSessionPrice(slot.InternalName, price.PriceChaos);
            if (!RitualPriceMath.PassesMinExalted(priceChaos, s.MinDisplayExalted))
                continue;

            var display = RitualPriceMath.Format(priceChaos, s.DisplayCurrency);
            if (s.PlayValueAlert && display.DivineValue >= s.AlertMinDivine)
            {
                var alertKey = string.IsNullOrEmpty(slot.InternalName) ? itemName : slot.InternalName;
                if (_ritualAlertedItemsThisSession.Add(alertKey))
                    _pendingRitualSound = Math.Clamp(s.AlertSound, 0, 4);
            }

            var pos = new NumVec2(
                slot.Rect.X + s.PriceOffsetX,
                slot.Rect.Y + slot.Rect.H - fontSize + s.PriceOffsetY);

            result.Add(new RitualPriceLabel(
                pos,
                display.IconFile,
                fontSize,
                fontSize,
                display.ValueText,
                textColor,
                fontSize,
                0,
                s.DiagnosePricing
                    ? $"{slot.Rarity} OK\nbase:{NonEmpty(slot.BaseItemName)}\nart:{NonEmpty(slot.ArtBasename)}\nname:{NonEmpty(itemName)}\nint:{slot.InternalName}"
                    : null,
                diagPos,
                diagFontSize));
        }

        return result.Count == 0 ? [] : result.ToArray();
    }

    private RitualPanelRow[] BuildRitualPanelRows(Poe2Live.RitualRewardsRead rewards)
    {
        if (rewards.Slots.Length == 0)
            return [];

        var s = _settings.Ritual;
        var rows = new List<RitualPanelRow>(rewards.Slots.Length);
        var textColor = PackColor(s.PriceTextColor);

        foreach (var slot in rewards.Slots)
        {
            if (IsRitualPlaceholderSlot(slot))
                continue;

            var itemName = ResolveRitualItemDisplayName(slot);
            if (string.IsNullOrWhiteSpace(itemName))
                itemName = slot.InternalName;
            if (string.IsNullOrWhiteSpace(itemName))
                continue;

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
            {
                rows.Add(new RitualPanelRow(
                    itemName, "", "", textColor, 0, false, slot.Rarity.ToString()));
                continue;
            }

            var priceChaos = StabilizeRitualSessionPrice(slot.InternalName, price.PriceChaos);
            var display = RitualPriceMath.Format(priceChaos, s.DisplayCurrency);
            if (s.PlayValueAlert && display.DivineValue >= s.AlertMinDivine)
            {
                var alertKey = string.IsNullOrEmpty(slot.InternalName) ? itemName : slot.InternalName;
                if (_ritualAlertedItemsThisSession.Add(alertKey))
                    _pendingRitualSound = Math.Clamp(s.AlertSound, 0, 4);
            }

            rows.Add(new RitualPanelRow(
                itemName,
                display.ValueText,
                display.IconFile,
                textColor,
                display.DivineValue,
                true,
                slot.Rarity.ToString()));
        }

        if (rows.Count == 0)
            return [];

        rows.Sort((a, b) => b.SortDivine.CompareTo(a.SortDivine));
        return rows.ToArray();
    }

    private Poe2Live.RitualRewardSlot[] MergeRitualLabelSlots(
        Poe2Live.RitualRewardsRead rewards,
        int windowWidth,
        int windowHeight)
    {
        if (rewards.Slots.Length == 0)
            return [];

        if (rewards.GridAddress != 0)
        {
            foreach (var slot in _live.ReadRitualOverlaySlots(rewards.GridAddress, windowWidth, windowHeight))
            {
                if (slot.Rect.W > 1f && slot.Rect.H > 1f)
                    _ritualTileRects[slot.TileElement] = slot.Rect;
            }
        }

        if (_ritualTileRects.Count == 0)
            return rewards.Slots;

        var merged = new Poe2Live.RitualRewardSlot[rewards.Slots.Length];
        for (var i = 0; i < rewards.Slots.Length; i++)
        {
            var slot = rewards.Slots[i];
            merged[i] = _ritualTileRects.TryGetValue(slot.TileElement, out var rect)
                ? slot with { Rect = rect }
                : slot;
        }

        return merged;
    }

    private void ClearRitualWindowSession(bool open)
    {
        if (!open && _wasRitualWindowOpen)
        {
            _ritualAlertedItemsThisSession.Clear();
            _ritualSessionStablePriceChaos.Clear();
            _nextRitualRecomputeUtc = DateTime.MinValue;
            _live.InvalidateRitualUiCache();
        }

        _wasRitualWindowOpen = open;
        if (!open)
            _ritualReadMissStreak = 0;
        if (_ritualLabels.Length > 0)
            _ritualLabels = [];
        if (!open)
        {
            if (_ritualPanelRows.Length > 0)
                _ritualPanelRows = [];
            _ritualTileRects.Clear();
        }
    }

    private double StabilizeRitualSessionPrice(string internalNameOnly, double priceChaos)
    {
        if (string.IsNullOrWhiteSpace(internalNameOnly) || priceChaos <= 0)
            return priceChaos;

        if (_ritualSessionStablePriceChaos.TryGetValue(internalNameOnly, out var stable) && priceChaos < stable)
            return stable;

        _ritualSessionStablePriceChaos[internalNameOnly] = priceChaos;
        return priceChaos;
    }

    private void FlushPendingRitualSound()
    {
        if (_pendingRitualSound is not { } soundType) return;
        _pendingRitualSound = null;
        AlertSoundPlayer.Play(soundType);
    }

    private string ResolveRitualItemDisplayName(Poe2Live.RitualRewardSlot slot)
    {
        var name = slot.InternalName.Length > 0
            ? GetRitualPrettyName(slot.InternalName, out _)
            : "";

        if (slot.Rarity != Poe2Live.Rarity.Unique && !PoeNinjaPriceFetcher.IsGenericLookupName(slot.BaseItemName))
            name = slot.BaseItemName.Trim();

        if (slot.Rarity == Poe2Live.Rarity.Unique)
        {
            foreach (var key in ArtKeyVariants(slot.ArtBasename))
            {
                if (PoeNinjaPriceFetcher.TryResolveDisplayName(key, out var uniqueFromArt) &&
                    !PoeNinjaPriceFetcher.IsGenericLookupName(uniqueFromArt))
                    return uniqueFromArt;

                if (PoeNinjaPriceFetcher.HasPriceDataForName(key))
                    return key;
            }
        }

        if (!string.IsNullOrWhiteSpace(name) &&
            !name.StartsWith("Item ", StringComparison.Ordinal) &&
            !PoeNinjaPriceFetcher.IsGenericLookupName(name))
            return name;

        if (!PoeNinjaPriceFetcher.IsGenericLookupName(slot.BaseItemName))
            return slot.BaseItemName.Trim();

        return string.IsNullOrWhiteSpace(name) ? slot.InternalName : name;
    }

    private string GetRitualPrettyName(string internalName, out bool isMapped)
    {
        isMapped = false;
        EnsureRitualNameCache();
        TrySplitRuneforgeSuffix(internalName, out var baseInternalName, out var suffix);

        if (_ritualNameCache!.TryGetValue(baseInternalName, out var pretty))
        {
            isMapped = true;
            return string.IsNullOrEmpty(suffix) ? pretty : $"{pretty} {suffix}";
        }

        if (PoeNinjaPriceFetcher.TryResolveDisplayName(internalName, out var scoutName))
        {
            isMapped = true;
            return scoutName;
        }

        var clean = Regex.Replace(internalName, "([A-Z])", " $1").Trim();
        clean = clean.Replace("Four ", string.Empty, StringComparison.Ordinal);
        clean = Regex.Replace(clean, @"\d+", string.Empty).Trim();
        return PoeNinjaPriceFetcher.IsGenericLookupName(clean) ? internalName : clean;
    }

    private void EnsureRitualNameCache()
    {
        if (_ritualNameCache is not null) return;

        var path = Path.Combine(RitualDir, "item_names.json");
        if (File.Exists(path))
        {
            try
            {
                _ritualNameCache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _ritualNameCache = new Dictionary<string, string>(_ritualNameCache, StringComparer.OrdinalIgnoreCase);
                return;
            }
            catch { }
        }

        _ritualNameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TrySplitRuneforgeSuffix(string internalName, out string baseInternalName, out string suffix)
    {
        baseInternalName = internalName;
        suffix = "";

        if (internalName.EndsWith("Runeforged", StringComparison.OrdinalIgnoreCase))
        {
            baseInternalName = internalName[..^"Runeforged".Length];
            suffix = "Runeforged";
            return true;
        }

        if (internalName.EndsWith("Runemastered", StringComparison.OrdinalIgnoreCase))
        {
            baseInternalName = internalName[..^"Runemastered".Length];
            suffix = "Runemastered";
            return true;
        }

        if (internalName.EndsWith("reforged", StringComparison.OrdinalIgnoreCase))
        {
            baseInternalName = internalName[..^"reforged".Length];
            suffix = "Runeforged";
            return true;
        }

        return false;
    }

    private static IEnumerable<string> ArtKeyVariants(string artBasename)
    {
        if (string.IsNullOrWhiteSpace(artBasename)) yield break;
        yield return artBasename;
        if (artBasename.StartsWith("The", StringComparison.OrdinalIgnoreCase) && artBasename.Length > 3)
            yield return artBasename[3..];
        else
            yield return "The" + artBasename;
    }

    private static string BuildScoutText(string itemName, string? baseType)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return "";
        if (string.IsNullOrWhiteSpace(baseType)) return itemName.Trim();
        return $"{itemName.Trim()} {baseType.Trim()}";
    }

    private static string InferBaseTypeFromMetadataPath(string metadataPath)
    {
        if (string.IsNullOrWhiteSpace(metadataPath)) return "";
        var parts = metadataPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return "";

        var parent = parts[^2];
        var spaced = Regex.Replace(parent, "([a-z])([A-Z])", "$1 $2");
        spaced = Regex.Replace(spaced, "([A-Z]+)([A-Z][a-z])", "$1 $2");
        return spaced.Trim();
    }

    private static bool IsRitualPlaceholderSlot(Poe2Live.RitualRewardSlot slot)
        => slot.InternalName.StartsWith("HiddenItem", StringComparison.OrdinalIgnoreCase);

    private static string NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? "<none>" : value;

    private object RitualApiJson()
    {
        var lastFetch = PoeNinjaPriceFetcher.LastFetchUtc;
        return new
        {
            settings = _settings.Ritual,
            status = _ritualStatus,
            source = _settings.Ritual.PriceSource == PoeNinjaPriceFetcher.SourcePoeNinja ? "poe.ninja" : "poe2scout",
            currentLeague = _ritualStatus.League.Length > 0 ? _ritualStatus.League : _settings.Ritual.League,
            loadedPriceCount = PoeNinjaPriceFetcher.LoadedItemCount,
            isFetching = PoeNinjaPriceFetcher.IsFetching,
            lastFetchUtc = lastFetch == DateTime.MinValue ? (DateTime?)null : lastFetch,
            lastFetchAgeSeconds = lastFetch == DateTime.MinValue ? (double?)null : Math.Max(0, (DateTime.UtcNow - lastFetch).TotalSeconds),
            chaosPerDivine = PoeNinjaPriceFetcher.GetChaosPerDivine(),
            divineToExaltedRate = PoeNinjaPriceFetcher.DivineToExaltedRate,
            leagues = LeagueProvider.Leagues.ToArray(),
        };
    }

    private void ApplyRitualApi(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("settings", out var nested) && nested.ValueKind == JsonValueKind.Object)
                root = nested;

            var r = _settings.Ritual;
            if (TryGetBool(root, "showOverlay", out var show)) r.ShowOverlay = show;
            if (TryGetBool(root, "showPricesWindow", out var win)) r.ShowPricesWindow = win;
            if (TryGetBool(root, "playValueAlert", out var alert)) r.PlayValueAlert = alert;
            if (TryGetBool(root, "debugMode", out var debug)) r.DebugMode = debug;
            if (TryGetBool(root, "diagnosePricing", out var diag)) r.DiagnosePricing = diag;
            if (TryGetBool(root, "forceBfsFallback", out var bfs)) r.ForceBfsFallback = bfs;
            if (TryGetInt(root, "priceSource", out var source)) r.PriceSource = Math.Clamp(source, 0, 1);
            if (TryGetInt(root, "refreshIntervalMin", out var refresh)) r.RefreshIntervalMin = Math.Max(1, refresh);
            if (TryGetInt(root, "displayCurrency", out var currency)) r.DisplayCurrency = Math.Clamp(currency, 0, 2);
            if (TryGetInt(root, "alertSound", out var sound)) r.AlertSound = Math.Clamp(sound, 0, 4);
            if (TryGetFloat(root, "alertMinDivine", out var alertMin)) r.AlertMinDivine = Math.Clamp(alertMin, 0.001f, 1000f);
            if (TryGetFloat(root, "priceFontScale", out var fontScale)) r.PriceFontScale = Math.Clamp(fontScale, 0.4f, 4f);
            if (TryGetFloat(root, "priceOffsetX", out var ox)) r.PriceOffsetX = Math.Clamp(ox, -500f, 500f);
            if (TryGetFloat(root, "priceOffsetY", out var oy)) r.PriceOffsetY = Math.Clamp(oy, -500f, 500f);
            if (TryGetFloat(root, "minDisplayExalted", out var minEx)) r.MinDisplayExalted = Math.Clamp(minEx, 0f, 100000f);
            if (TryGetString(root, "league", out var league)) r.League = string.IsNullOrWhiteSpace(league) ? "Runes of Aldur" : league.Trim();
            if (TryGetString(root, "priceTextColor", out var color) && IsHexColor(color)) r.PriceTextColor = color;

            _ritualPricingConfigKey = "";
            _settings.Save();
        }
        catch (JsonException) { }
    }

    private static bool TryGetBool(JsonElement root, string name, out bool value)
    {
        value = false;
        return root.TryGetProperty(name, out var el) && (el.ValueKind switch
        {
            JsonValueKind.True => (value = true) || true,
            JsonValueKind.False => true,
            _ => false,
        });
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var el)) return false;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value)) return true;
        return el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetFloat(JsonElement root, string name, out float value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var el)) return false;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetSingle(out value)) return true;
        return el.ValueKind == JsonValueKind.String && float.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString() ?? "";
        return true;
    }

    private static bool IsHexColor(string value)
        => value.Length == 7 && value[0] == '#'
           && byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, null, out _)
           && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, null, out _)
           && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, null, out _);
}
