using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Pricing;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private Poe2RitualShop _ritualShop = null!;
    private RitualPriceBook _ritualPriceBook = null!;
    private readonly PerformanceCadence _ritualCadence = new();
    private readonly Dictionary<string, double> _ritualSessionPrices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ritualAlerted = new(StringComparer.OrdinalIgnoreCase);
    private bool _ritualWasOpen;
    private long _ritualRenderSignature;
    private int _ritualRenderSettingsHash;
    private long _ritualLabelRebuilds;
    private long _ritualLabelCacheHits;
    private long _ritualWindowCacheHits;
    private long _ritualRewardReads;
    private double _ritualRewardReadLastMs;
    private double _ritualLabelBuildLastMs;
    private long _lootFrameTicks;
    private double _lootFrameLastMs;
    private int _lootFrameItemLabels;
    private int _lootFrameLootTagSpecs;
    private int _lootFrameLootTagHits;
    private DateTime _ritualNextLabelRecomputeUtc = DateTime.MinValue;
    private volatile RitualRender _ritualRender = RitualRender.Empty;

    private sealed record RitualRender(bool Open, IReadOnlyList<RitualRewardLabel> Labels, string PathKind, int TileCount)
    {
        public static readonly RitualRender Empty = new(false, Array.Empty<RitualRewardLabel>(), "Closed", 0);
    }

    private void InitRitualHelper()
    {
        _ritualShop = new Poe2RitualShop(_reader, _live);
        _panelCatalog = new Poe2PanelCatalog(_ritualShop, _runeforgeLive, _atlas);
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

    private void UpdateRitualHelperLive(GameContextSnapshot game, UiContextSnapshot ui)
    {
        var ritualStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var cfg = _settings.RitualHelper;
        if (!cfg.Enabled)
        {
            if (_ritualRender.Open) _ritualRender = RitualRender.Empty;
            _featurePerf.RecordRitual(FeaturePerfAccumulator.ElapsedMs(ritualStart));
            return;
        }

        if (!_ritualCadence.IsDue(PerformanceCadence.ClampHz(cfg.ReadHz, 1, 20)))
            return;

        var allowLocate = cfg.ForceBfsFallback || _ritualShop.PanelOpen || _ritualShop.LastIdleProbeFastPathHit;
        var preferController = ui.Valid && ui.PreferController;
        var panels = _panelCatalog.Capture(game, ui, allowLocate, preferController);
        var ritual = panels.Ritual;
        var open = ritual.Open;
        var pathKind = $"{_ritualShop.LastIdleProbeKind} fast={ritual.FastPathHit}";

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
            _featurePerf.RecordRitual(FeaturePerfAccumulator.ElapsedMs(ritualStart));
            return;
        }

        var now = DateTime.UtcNow;
        var itemSignature = ritual.ItemSignature;
        var settingsHash = RitualRenderSettingsHash(cfg);
        if (_ritualRender.Open
            && itemSignature != 0
            && itemSignature == _ritualRenderSignature
            && settingsHash == _ritualRenderSettingsHash
            && now < _ritualNextLabelRecomputeUtc)
        {
            _ritualLabelCacheHits++;
            _ritualWindowCacheHits++;
            _ritualRender = new RitualRender(
                true,
                _ritualRender.Labels,
                pathKind,
                ritual.InBoundsTiles);
            _featurePerf.RecordRitual(FeaturePerfAccumulator.ElapsedMs(ritualStart));
            return;
        }

        var winW = ui.Valid ? ui.WindowWidth : _windowWidth;
        var winH = ui.Valid ? ui.WindowHeight : _windowHeight;

        var forceRewardRefresh = itemSignature != 0
            && (itemSignature != _ritualRenderSignature || now >= _ritualNextLabelRecomputeUtc);
        var rewardStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var rewards = _ritualShop.ReadRewardsFromCachedWindow(winW, winH, forceRewardRefresh);
        _ritualRewardReadLastMs = RitualElapsedMs(rewardStart);
        _ritualRewardReads++;

        _ritualNextLabelRecomputeUtc = now.AddMilliseconds(120);
        _ritualRenderSignature = itemSignature;
        _ritualRenderSettingsHash = settingsHash;
        _ritualLabelRebuilds++;
        var labelStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var labels = BuildRitualLabels(rewards, cfg);
        _ritualLabelBuildLastMs = RitualElapsedMs(labelStart);

        _ritualRender = new RitualRender(
            open,
            labels.Count > 0 ? labels : Array.Empty<RitualRewardLabel>(),
            pathKind,
            rewards.Count);
        _featurePerf.RecordRitual(FeaturePerfAccumulator.ElapsedMs(ritualStart));
    }

    private static double RitualElapsedMs(long startTimestamp)
        => (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0
           / System.Diagnostics.Stopwatch.Frequency;

    private List<RitualRewardLabel> BuildRitualLabels(
        IReadOnlyList<Poe2Live.RitualReward> rewards,
        RitualHelperSettings cfg)
    {
        var labels = new List<RitualRewardLabel>();
        foreach (var reward in rewards)
        {
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
                        var alertKey = string.IsNullOrEmpty(reward.Art) ? lookupName : reward.Art;
                        _ritualAlerted.Add(alertKey);
                    }
                }

                labels.Add(new RitualRewardLabel(
                    reward.X, reward.Y, reward.W, reward.H, valueText, currency, false,
                    cfg.DiagnosePricing ? BuildRitualDiag(reward, lookupName, true) : null));
            }
            else if (diagOnly)
            {
                labels.Add(new RitualRewardLabel(
                    reward.X, reward.Y, reward.W, reward.H, "", "", true,
                    BuildRitualDiag(reward, lookupName, false)));
            }
        }
        return labels;
    }

    private static int RitualRenderSettingsHash(RitualHelperSettings cfg)
        => HashCode.Combine(
            cfg.ShowPrices,
            cfg.DiagnosePricing,
            cfg.DisplayCurrency,
            cfg.MinDisplayExalted,
            cfg.PlayValueAlert,
            cfg.AlertMinDivine,
            cfg.PriceSource,
            cfg.League);

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
        _ritualSessionPrices.Clear();
        _ritualAlerted.Clear();
        _ritualRenderSignature = 0;
        _ritualRenderSettingsHash = 0;
        _ritualNextLabelRecomputeUtc = DateTime.MinValue;
        if (_ritualRender.Open) _ritualRender = RitualRender.Empty;
    }

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
        signatureDetected = _ritualShop.PanelOpen || _ritualShop.LastIdleProbeKind != Poe2RitualShop.IdleProbeKind.None,
        itemSignature = _ritualShop.LastItemSignature,
        labelRebuilds = _ritualLabelRebuilds,
        labelCacheHits = _ritualLabelCacheHits,
        windowCacheHits = _ritualWindowCacheHits,
        rewardReads = _ritualRewardReads,
        rewardReadElapsedMs = _ritualRewardReadLastMs,
        labelBuildElapsedMs = _ritualLabelBuildLastMs,
        perf = _ritualShop.PerfSnapshot,
        featurePerf = _featurePerfSnapshot,
        nonRitualProfile = new
        {
            lootFrameTicks = _lootFrameTicks,
            lootFrameElapsedMs = _lootFrameLastMs,
            itemLabels = _lootFrameItemLabels,
            lootTagSpecs = _lootFrameLootTagSpecs,
            lootTagHits = _lootFrameLootTagHits,
        },
    };

    public void RefreshRitualPriceBook()
    {
        var cfg = _settings.RitualHelper;
        _ritualPriceBook.SetLeagueOverride(string.IsNullOrWhiteSpace(cfg.League) ? null : cfg.League.Trim());
        _ritualPriceBook.PriceSource = cfg.PriceSource;
        _ritualPriceBook.ForceRefresh();
    }
}
