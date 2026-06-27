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
    private bool _ritualWasOpen;
    private volatile RitualRender _ritualRender = RitualRender.Empty;

    private sealed record RitualRender(bool Open, IReadOnlyList<RitualRewardLabel> Labels, string PathKind, int TileCount)
    {
        public static readonly RitualRender Empty = new(false, Array.Empty<RitualRewardLabel>(), "Closed", 0);
    }

    private void InitRitualHelper()
    {
        _ritualRewards = new Poe2RitualRewards(_reader);
        var cfg = _settings.RitualHelper;
        _ritualPriceBook = new RitualPriceBook(
            Path.Combine(ConfigDir, "ritual_prices.json"),
            string.IsNullOrWhiteSpace(cfg.League) ? null : cfg.League)
        {
            PriceSource = cfg.PriceSource,
            RefreshIntervalMinutes = Math.Clamp(cfg.RefreshIntervalMin, 1, 120),
        };
        _ritualPriceBook.RefreshIfDue();
    }

  private void SyncRitualPriceBook(nint areaInstance)
    {
        var cfg = _settings.RitualHelper;
        _ritualPriceBook.SetLeagueOverride(string.IsNullOrWhiteSpace(cfg.League) ? null : cfg.League.Trim());
        _ritualPriceBook.PriceSource = cfg.PriceSource;
        _ritualPriceBook.RefreshIntervalMinutes = Math.Clamp(cfg.RefreshIntervalMin, 1, 120);
        var detected = _worldLive.LeagueName(areaInstance);
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

        var tiles = _ritualRewards.ReadRewards(
            inGameState, winW, winH, cfg.ForceBfsFallback, _ritualPriceBook.TryPrettyName);
        var open = _ritualRewards.PanelOpen;

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
        foreach (var tile in tiles)
        {
            var item = tile.Item;
            var lookupName = ResolveRitualLookupName(item);
            var chaos = _ritualPriceBook.GetPriceChaos(
                lookupName, item.ModLines, item.InternalBasename, item.FullPath);
            var diagOnly = cfg.DiagnosePricing && chaos is null or <= 0;

            if (chaos is > 0)
            {
                chaos = _ritualPriceBook.StabilizeSessionPrice(item.InternalBasename, chaos.Value, _ritualSessionPrices);
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
                        var alertKey = string.IsNullOrEmpty(item.InternalBasename) ? lookupName : item.InternalBasename;
                        _ritualAlerted.Add(alertKey);
                    }
                }

                labels.Add(new RitualRewardLabel(
                    tile.X, tile.Y, tile.W, tile.H, valueText, currency, false,
                    cfg.DiagnosePricing ? BuildRitualDiag(item, lookupName, true) : null));
            }
            else if (diagOnly && !string.IsNullOrEmpty(item.InternalBasename))
            {
                labels.Add(new RitualRewardLabel(
                    tile.X, tile.Y, tile.W, tile.H, "", "", true,
                    BuildRitualDiag(item, lookupName, false)));
            }
        }

        _ritualRender = new RitualRender(
            open,
            labels.Count > 0 ? labels : Array.Empty<RitualRewardLabel>(),
            _ritualRewards.LastPathKind.ToString(),
            tiles.Count);
    }

    private string ResolveRitualLookupName(Poe2RitualItemReader.RitualItemIdentity item)
    {
        if (item.Rarity == Poe2RitualItemReader.ItemRarity.Unique)
        {
            var fromArt = _ritualPriceBook.TryResolveArtName(item.ArtBasename);
            if (!string.IsNullOrWhiteSpace(fromArt) && !RitualPriceLookup.IsGenericLookupName(fromArt))
                return fromArt;
        }
        if (!string.IsNullOrWhiteSpace(item.BaseName) && item.Rarity != Poe2RitualItemReader.ItemRarity.Unique)
            return item.BaseName;
        return item.DisplayName;
    }

    private static string BuildRitualDiag(Poe2RitualItemReader.RitualItemIdentity item, string lookupName, bool ok)
    {
        var status = ok ? "OK" : "NO PRICE";
        return $"{item.Rarity} {status}\nbase:{item.BaseName ?? " "}\nart:{item.ArtBasename ?? " "}\nname:{lookupName}\nint:{item.InternalBasename}";
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
        count = _ritualPriceBook.ItemCount,
        chaosPerDivine = _ritualPriceBook.ChaosPerDivine,
        chaosPerExalted = _ritualPriceBook.ChaosPerExalted,
        lastFetchUtc = _ritualPriceBook.LastFetchUtc,
        status = _ritualPriceBook.Status,
        pathKind = _ritualRender.PathKind,
        tileCount = _ritualRender.TileCount,
        labelCount = _ritualRender.Labels.Count,
    };

    public void RefreshRitualPriceBook()
    {
        var cfg = _settings.RitualHelper;
        _ritualPriceBook.SetLeagueOverride(string.IsNullOrWhiteSpace(cfg.League) ? null : cfg.League.Trim());
        _ritualPriceBook.PriceSource = cfg.PriceSource;
        _ritualPriceBook.ForceRefresh();
    }
}
