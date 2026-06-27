using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Pricing;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private Poe2RitualRewards _ritualRewards = null!;
    private RitualPriceBook _ritualPriceBook = null!;
    private readonly PerformanceCadence _ritualCadence = new();
    private readonly Dictionary<string, double> _ritualSessionPrices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ritualAlerted = new(StringComparer.OrdinalIgnoreCase);
    private Poe2UiAnchors.BranchKind _ritualProbeHint;
    private bool _ritualWasOpen;
    private long _ritualRenderSignature;
    private int _ritualRenderSettingsHash;
    private long _ritualLabelRebuilds;
    private long _ritualLabelCacheHits;
    private long _ritualRewardReads;
    private double _ritualProbeLastMs;
    private double _ritualRewardReadLastMs;
    private double _ritualLabelBuildLastMs;
    private DateTime _ritualNextLabelRecomputeUtc = DateTime.MinValue;
    private volatile RitualRender _ritualRender = RitualRender.Empty;

    private sealed record RitualRender(bool Open, IReadOnlyList<RitualRewardLabel> Labels, string PathKind, int TileCount)
    {
        public static readonly RitualRender Empty = new(false, Array.Empty<RitualRewardLabel>(), "Closed", 0);
    }

    private void InitRitualHelper()
    {
        _ritualRewards = new Poe2RitualRewards(_reader, _live);
        MigrateRitualHelperSettings();
        var cfg = _settings.RitualHelper;
        _ritualPriceBook = new RitualPriceBook(
            Path.Combine(ConfigDir, "ritual_prices.json"),
            string.IsNullOrWhiteSpace(cfg.League) ? null : cfg.League)
        {
            PriceSource = cfg.PriceSource,
            RefreshIntervalMinutes = Math.Clamp(cfg.RefreshIntervalMin, 1, 120),
        };
        _ritualPriceBook.ReloadCacheFromDisk();
        if (string.IsNullOrWhiteSpace(cfg.League) && !string.IsNullOrWhiteSpace(_ritualPriceBook.League))
        {
            cfg.League = _ritualPriceBook.League.Trim();
            _settings.Save();
        }
        _ritualPriceBook.RefreshIfDue();
    }

    private void MigrateRitualHelperSettings()
    {
        if (_settings.RitualHelperHzMigrated) return;
        var rh = _settings.RitualHelper;
        if (rh.OpenReadHz <= 0) rh.OpenReadHz = Math.Clamp(rh.ReadHz, 1, 20);
        if (rh.ClosedProbeHz <= 0) rh.ClosedProbeHz = 1;
        _settings.RitualHelperHzMigrated = true;
        _settings.Save();
    }

    private void SyncRitualPriceBook(nint areaInstance)
    {
        var cfg = _settings.RitualHelper;
        var detected = _live.LeagueName(areaInstance);
        if (string.IsNullOrWhiteSpace(cfg.League) && !string.IsNullOrWhiteSpace(detected))
        {
            cfg.League = detected.Trim();
            _settings.Save();
        }

        _ritualPriceBook.SetLeagueOverride(string.IsNullOrWhiteSpace(cfg.League) ? null : cfg.League.Trim());
        _ritualPriceBook.PriceSource = cfg.PriceSource;
        _ritualPriceBook.RefreshIntervalMinutes = Math.Clamp(cfg.RefreshIntervalMin, 1, 120);
        if (!string.IsNullOrWhiteSpace(detected))
            _ritualPriceBook.SetDetectedLeague(detected);
        _ritualPriceBook.RefreshIfDue();
    }

    private void TickRitualHelper(nint inGameState, nint areaInstance, int winW, int winH)
    {
        var cfg = _settings.RitualHelper;
        if (!cfg.Enabled || !cfg.ShowPrices && !cfg.DiagnosePricing)
        {
            if (_ritualRender.Open) _ritualRender = RitualRender.Empty;
            return;
        }

        SyncRitualPriceBook(areaInstance);

        var hz = _ritualRewards.PanelOpen
            ? PerformanceCadence.ClampHz(cfg.OpenReadHz, 1, 20)
            : PerformanceCadence.ClampHz(cfg.ClosedProbeHz, 1, 4);
        if (!_ritualCadence.IsDue(hz)) return;

        UpdateRitualHelperLive(inGameState, winW, winH);
    }

    private void UpdateRitualHelperLive(nint inGameState, int winW, int winH)
    {
        var cfg = _settings.RitualHelper;
        var probeHint = _ritualProbeHint;
        var window = _ritualRewards.ReadWindowState(
            inGameState, winW, winH, allowFullLocate: true, probeHint, cfg.ForceBfsFallback);
        _ritualProbeLastMs = _ritualRewards.Perf.LastProbeMs;

        if (window.PanelOpen && _ritualRewards.LastBranchKind != Poe2UiAnchors.BranchKind.None)
            _ritualProbeHint = _ritualRewards.LastBranchKind;

        var open = window.PanelOpen;
        var pathKind = $"{window.PathKind} fast={window.FastPathHit}";

        if (!open && _ritualWasOpen)
        {
            _ritualSessionPrices.Clear();
            _ritualAlerted.Clear();
            _ritualRenderSignature = 0;
            _ritualRenderSettingsHash = 0;
            _ritualNextLabelRecomputeUtc = DateTime.MinValue;
        }
        _ritualWasOpen = open;

        if (!open || !cfg.ShowPrices && !cfg.DiagnosePricing)
        {
            if (_ritualRender.Open) _ritualRender = RitualRender.Empty;
            return;
        }

        var now = DateTime.UtcNow;
        var itemSignature = window.ItemSignature;
        var settingsHash = RitualRenderSettingsHash(cfg);
        if (_ritualRender.Open
            && itemSignature != 0
            && itemSignature == _ritualRenderSignature
            && settingsHash == _ritualRenderSettingsHash
            && now < _ritualNextLabelRecomputeUtc)
        {
            _ritualLabelCacheHits++;
            _ritualRender = new RitualRender(true, _ritualRender.Labels, pathKind, window.InBoundsTiles);
            return;
        }

        var forceRewardRefresh = itemSignature != 0
            && (itemSignature != _ritualRenderSignature || now >= _ritualNextLabelRecomputeUtc);
        var rewardStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var tiles = _ritualRewards.ReadRewardsFromCachedWindow(
            winW, winH, forceRewardRefresh, _ritualPriceBook.TryPrettyName);
        _ritualRewardReadLastMs = RitualElapsedMs(rewardStart);
        if (forceRewardRefresh) _ritualRewardReads++;

        _ritualNextLabelRecomputeUtc = now.AddMilliseconds(120);
        _ritualRenderSignature = itemSignature;
        _ritualRenderSettingsHash = settingsHash;
        _ritualLabelRebuilds++;

        var labelStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var labels = BuildRitualLabels(tiles, cfg);
        _ritualLabelBuildLastMs = RitualElapsedMs(labelStart);

        _ritualRender = new RitualRender(
            open,
            labels.Count > 0 ? labels : Array.Empty<RitualRewardLabel>(),
            pathKind,
            tiles.Count);
    }

    private static double RitualElapsedMs(long startTimestamp)
        => (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0
           / System.Diagnostics.Stopwatch.Frequency;

    private static int RitualRenderSettingsHash(RitualHelperSettings cfg)
    {
        var h = HashCode.Combine(
            cfg.ShowPrices,
            cfg.DiagnosePricing,
            cfg.DisplayCurrency,
            cfg.MinDisplayExalted,
            cfg.PlayValueAlert,
            cfg.AlertMinDivine,
            cfg.PriceSource,
            cfg.League);
        return HashCode.Combine(h, cfg.PriceFontScale, cfg.PriceOffsetX, cfg.PriceOffsetY);
    }

    private List<RitualRewardLabel> BuildRitualLabels(
        IReadOnlyList<Poe2RitualRewards.RitualRewardTile> tiles,
        RitualHelperSettings cfg)
    {
        var labels = new List<RitualRewardLabel>(tiles.Count);
        var ox = cfg.PriceOffsetX;
        var oy = cfg.PriceOffsetY;
        foreach (var tile in tiles)
        {
            var reward = tile.Reward;
            var lookupName = ResolveRitualLookupName(reward);
            var chaos = _ritualPriceBook.GetPriceChaos(
                lookupName, null, reward.Art, reward.Name);
            var diagOnly = cfg.DiagnosePricing && chaos is null or <= 0;

            if (chaos is > 0)
            {
                var stableKey = reward.Art ?? reward.Name ?? lookupName;
                chaos = _ritualPriceBook.StabilizeSessionPrice(stableKey, chaos.Value, _ritualSessionPrices);
                var (exValue, _) = RitualPriceLookup.GetDisplayPrice(
                    chaos.Value, RitualPriceLookup.DisplayExalted, _ritualPriceBook.ChaosPerDivine, _ritualPriceBook.ChaosPerExalted);
                if (!cfg.DiagnosePricing && cfg.MinDisplayExalted > 0 && exValue < cfg.MinDisplayExalted)
                    continue;

                var (displayValue, currency) = RitualPriceLookup.GetDisplayPrice(
                    chaos.Value, cfg.DisplayCurrency, _ritualPriceBook.ChaosPerDivine, _ritualPriceBook.ChaosPerExalted);
                var valueText = RitualPriceLookup.FormatDisplayValue(displayValue, currency);

                if (cfg.PlayValueAlert)
                {
                    var divine = chaos.Value / Math.Max(_ritualPriceBook.ChaosPerDivine, 1);
                    if (divine >= cfg.AlertMinDivine)
                    {
                        var alertKey = reward.Art ?? reward.Name ?? lookupName;
                        _ritualAlerted.Add(alertKey);
                    }
                }

                labels.Add(new RitualRewardLabel(
                    reward.X + ox, reward.Y + oy, reward.W, reward.H, valueText, currency, false,
                    cfg.DiagnosePricing ? BuildRitualDiag(reward, lookupName, true) : null));
            }
            else if (diagOnly)
            {
                labels.Add(new RitualRewardLabel(
                    reward.X + ox, reward.Y + oy, reward.W, reward.H, "", "", true,
                    BuildRitualDiag(reward, lookupName, false)));
            }
        }
        return labels;
    }

    private string ResolveRitualLookupName(Poe2Live.RitualReward reward)
    {
        if (reward.Rarity == Poe2Live.Rarity.Unique)
        {
            var fromArt = _ritualPriceBook.TryResolveArtName(reward.Art);
            if (!string.IsNullOrWhiteSpace(fromArt) && !RitualPriceLookup.IsGenericLookupName(fromArt))
                return fromArt;
        }
        if (!string.IsNullOrWhiteSpace(reward.Name) && reward.Rarity != Poe2Live.Rarity.Unique)
            return reward.Name;
        return reward.Name ?? reward.Art ?? "";
    }

    private static string BuildRitualDiag(Poe2Live.RitualReward reward, string lookupName, bool ok)
    {
        var status = ok ? "OK" : "NO PRICE";
        return $"{reward.Rarity} {status}\nname:{reward.Name ?? " "}\nart:{reward.Art ?? " "}\nlookup:{lookupName}";
    }

    private void ResetRitualSession()
    {
        _ritualWasOpen = false;
        _ritualProbeHint = Poe2UiAnchors.BranchKind.None;
        _ritualSessionPrices.Clear();
        _ritualAlerted.Clear();
        _ritualRenderSignature = 0;
        _ritualRenderSettingsHash = 0;
        _ritualNextLabelRecomputeUtc = DateTime.MinValue;
        _ritualRewards.ResetSession();
        if (_ritualRender.Open) _ritualRender = RitualRender.Empty;
    }

    public object RitualPerfStatus() => new
    {
        probeMs = _ritualProbeLastMs,
        rewardReadMs = _ritualRewardReadLastMs,
        labelBuildMs = _ritualLabelBuildLastMs,
        labelRebuilds = _ritualLabelRebuilds,
        labelCacheHits = _ritualLabelCacheHits,
        rewardReads = _ritualRewardReads,
        fullReads = _ritualRewards.Perf.FullReads,
        cacheHits = _ritualRewards.Perf.CacheHits,
        fastChainHits = _ritualRewards.Perf.FastChainHits,
        bfsHits = _ritualRewards.Perf.BfsHits,
        branch = _ritualRewards.LastBranchKind.ToString(),
        pathKind = _ritualRender.PathKind,
        tileCount = _ritualRender.TileCount,
    };

    public object RitualPriceBookStatus() => new
    {
        loaded = _ritualPriceBook.IsLoaded,
        league = _ritualPriceBook.League,
        effectiveLeague = _ritualPriceBook.EffectiveLeague,
        detectedLeague = _ritualPriceBook.DetectedLeague,
        fetching = _ritualPriceBook.IsFetching,
        count = _ritualPriceBook.ItemCount,
        chaosPerDivine = _ritualPriceBook.ChaosPerDivine,
        chaosPerExalted = _ritualPriceBook.ChaosPerExalted,
        lastFetchUtc = _ritualPriceBook.LastFetchUtc,
        status = _ritualPriceBook.Status,
        pathKind = _ritualRender.PathKind,
        tileCount = _ritualRender.TileCount,
        labelCount = _ritualRender.Labels.Count,
        perf = RitualPerfStatus(),
    };

    public void RefreshRitualPriceBook()
    {
        var cfg = _settings.RitualHelper;
        _ritualPriceBook.SetLeagueOverride(string.IsNullOrWhiteSpace(cfg.League) ? null : cfg.League.Trim());
        _ritualPriceBook.PriceSource = cfg.PriceSource;
        _ritualPriceBook.ForceRefresh();
    }
}
