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
    private volatile RitualRender _ritualRender = RitualRender.Empty;

    private sealed record RitualRender(bool Open, IReadOnlyList<RitualRewardLabel> Labels, string PathKind, int TileCount)
    {
        public static readonly RitualRender Empty = new(false, Array.Empty<RitualRewardLabel>(), "Closed", 0);
    }

    private void InitRitualHelper()
    {
        _ritualShop = new Poe2RitualShop(_reader, _live);
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

    private void UpdateRitualHelperLive(nint inGameState, int winW, int winH)
    {
        var cfg = _settings.RitualHelper;
        if (!cfg.Enabled)
        {
            if (_ritualRender.Open) _ritualRender = RitualRender.Empty;
            return;
        }

        if (!_ritualCadence.IsDue(PerformanceCadence.ClampHz(cfg.ReadHz, 1, 20)))
            return;

        var allowLocate = cfg.ForceBfsFallback || _ritualShop.ShouldAllowFullLocate;
        var preferController = _settings.GamepadHotkeysEnabled
            || GamepadInput.IsConnected(_settings.GamepadUserIndex);
        var read = _ritualShop.ReadPanelState(inGameState, winW, winH, allowLocate, preferController);
        var open = read.PanelOpen;
        var rewards = read.Rewards;
        var pathKind = $"{_ritualShop.LastIdleProbeKind} fast={_ritualShop.LastIdleProbeFastPathHit}";

        if (!open && _ritualWasOpen)
        {
            _ritualSessionPrices.Clear();
            _ritualAlerted.Clear();
        }
        _ritualWasOpen = open;

        if (!open || !cfg.ShowPrices && !cfg.DiagnosePricing)
        {
            if (_ritualRender.Open) _ritualRender = RitualRender.Empty;
            return;
        }

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

        _ritualRender = new RitualRender(
            open,
            labels.Count > 0 ? labels : Array.Empty<RitualRewardLabel>(),
            pathKind,
            rewards.Count);
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
        _ritualSessionPrices.Clear();
        _ritualAlerted.Clear();
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
    };

    public void RefreshRitualPriceBook()
    {
        var cfg = _settings.RitualHelper;
        _ritualPriceBook.SetLeagueOverride(string.IsNullOrWhiteSpace(cfg.League) ? null : cfg.League.Trim());
        _ritualPriceBook.PriceSource = cfg.PriceSource;
        _ritualPriceBook.ForceRefresh();
    }
}
