using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Pricing;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private PoeNinjaPriceBook _priceBook = null!;

    private readonly record struct LootTagSpec(nint El, string TagText, string Value, bool Highlight);
    private sealed record LootTagRender(IReadOnlyList<LootTagSpec> Specs)
    {
        public static readonly LootTagRender Empty = new(Array.Empty<LootTagSpec>());
    }
    private volatile LootTagRender _lootTags = LootTagRender.Empty;
    private readonly List<LootTagLabel> _lootTagFrame = new();
    private readonly List<ItemLabel> _itemFrame = new();

    private DateTime _nextLootScanUtc = DateTime.MinValue;
    private const int LootScanThrottleMs = 400;

    private void InitLootValues()
    {
        _priceBook = new PoeNinjaPriceBook(
            Path.Combine(ConfigDir, "poe_ninja_prices.json"),
            string.IsNullOrWhiteSpace(_settings.GroundItems.League) ? null : _settings.GroundItems.League);
        _priceBook.RefreshIfDue();

        if (!_settings.GroundItemCategoriesMigrated)
        {
            var cur = _settings.GroundItems.Categories ?? new();
            if (cur.Count == 0)
                _settings.GroundItems.Categories = new GroundItemSettings().Categories;
            _settings.GroundItemCategoriesMigrated = true;
            _settings.Save();
        }
    }

    public object PriceBookStatus() => new
    {
        loaded = _priceBook.IsLoaded,
        league = _priceBook.League,
        count = _priceBook.ItemCount,
        exPerDivine = _priceBook.ExPerDivine,
        exPerChaos = _priceBook.ExPerChaos,
        lastFetchUtc = _priceBook.LastFetchUtc,
        status = _priceBook.Status,
    };

    private void SyncPriceBookLeague(nint areaInstance)
    {
        _priceBook.SetLeagueOverride(string.IsNullOrWhiteSpace(_settings.GroundItems.League)
            ? null : _settings.GroundItems.League.Trim());
        var detected = _worldLive.LeagueName(areaInstance);
        if (!string.IsNullOrWhiteSpace(detected))
            _priceBook.SetDetectedLeague(detected);
        _priceBook.RefreshIfDue();
    }

    private void UpdateLootWorld(nint inGameState, nint areaInstance)
    {
        SyncPriceBookLeague(areaInstance);
        UpdateLootTags(inGameState);
    }

    private List<ItemLabelSpec> BuildItemLabels(IReadOnlyList<Poe2Live.EntityDot> entities)
    {
        var labels = new List<ItemLabelSpec>();
        var cfg = _settings.GroundItems;
        if (!cfg.Enabled || !_priceBook.IsLoaded) return labels;
        var enabled = cfg.Categories is { Count: > 0 }
            ? new HashSet<string>(cfg.Categories, StringComparer.OrdinalIgnoreCase) : null;
        if (enabled is null) return labels;

        foreach (var e in entities)
        {
            if (e.ItemArt is not { Length: > 0 } && e.ItemName is not { Length: > 0 }) continue;
            var isUnique = e.Rarity == Poe2Live.Rarity.Unique;
            if (cfg.AnchorValuesToTags && !(isUnique && !e.ItemIdentified)) continue;
            var lookup = isUnique
                ? _priceBook.TryByArt(e.ItemArt)
                : (e.ItemName is { Length: > 0 } nm ? _priceBook.TryByName(nm) : null);
            if (lookup is not { } pr) continue;
            if (cfg.MinQuantity > 0 && pr.Quantity < cfg.MinQuantity) continue;
            var group = LootValueLogic.CategoryGroup(pr.Category);
            if (!enabled.Contains(group)) continue;
            if (pr.Exalted < LootValueLogic.GroundFloor(group, cfg.UniqueMinEx, cfg.CurrencyMinEx, cfg.OtherMinEx)) continue;
            if (!_worldLive.TryLiveBar(e.Address, out _, out _, out _, out _, out _)) continue;
            var showName = isUnique && !e.ItemIdentified;
            labels.Add(new ItemLabelSpec(e.Address, pr.Name, _priceBook.Format(pr.Exalted),
                pr.Exalted >= cfg.HighlightMinEx, showName));
        }
        return labels;
    }

    private void UpdateLootTags(nint inGameState)
    {
        var cfg = _settings.GroundItems;
        var enabled = cfg.Categories is { Count: > 0 }
            ? new HashSet<string>(cfg.Categories, StringComparer.OrdinalIgnoreCase) : null;
        if (!cfg.Enabled || !cfg.AnchorValuesToTags || !_priceBook.IsLoaded || enabled is null)
        {
            if (!ReferenceEquals(_lootTags, LootTagRender.Empty)) _lootTags = LootTagRender.Empty;
            return;
        }

        var now = DateTime.UtcNow;
        if (now < _nextLootScanUtc) return;
        _nextLootScanUtc = now.AddMilliseconds(LootScanThrottleMs);

        var tags = _worldLive.ScanLootLabels(inGameState);
        if (tags.Count == 0)
        {
            if (!ReferenceEquals(_lootTags, LootTagRender.Empty)) _lootTags = LootTagRender.Empty;
            return;
        }

        var specs = new List<LootTagSpec>();
        var seen = new HashSet<nint>();
        foreach (var (el, text) in tags)
        {
            if (!seen.Add(el)) continue;
            var pr = _priceBook.TryByName(text) ?? _priceBook.TryByName(LootValueLogic.StripCount(text));
            if (pr is not { } p) continue;
            if (cfg.MinQuantity > 0 && p.Quantity < cfg.MinQuantity) continue;
            var group = LootValueLogic.CategoryGroup(p.Category);
            if (!enabled.Contains(group)) continue;
            if (p.Exalted < LootValueLogic.GroundFloor(group, cfg.UniqueMinEx, cfg.CurrencyMinEx, cfg.OtherMinEx)) continue;
            specs.Add(new LootTagSpec(el, text, _priceBook.Format(p.Exalted), p.Exalted >= cfg.HighlightMinEx));
        }
        _lootTags = specs.Count > 0 ? new LootTagRender(specs) : LootTagRender.Empty;
    }

    private void RefreshLootRenderFrames(WorldSnapshot snap, int windowWidth, int windowHeight, bool inGame)
    {
        _itemFrame.Clear();

        if (inGame)
        {
            foreach (var s in snap.ItemLabels)
            {
                if (!_live.TryLiveBar(s.Entity, out var w, out _, out _, out _, out _)) continue;
                _itemFrame.Add(new ItemLabel(w, s.Name, s.Value, s.Highlight, s.ShowName));
            }

            _lootTagFrame.Clear();
            var lt = _lootTags;
            foreach (var s in lt.Specs)
            {
                if (!_live.TryUiElementRect(s.El, windowWidth, windowHeight, out var rx, out var ry, out var rw, out var rh, requireFirstLine: s.TagText)) continue;
                _lootTagFrame.Add(new LootTagLabel(rx, ry, rw, rh, s.Value, s.Highlight));
            }
        }
        else
        {
            _lootTagFrame.Clear();
        }
    }

    private (IReadOnlyList<ItemLabel> items, IReadOnlyList<LootTagLabel>? lootTags) BuildLootRenderPayload(bool inGame)
    {
        if (!inGame)
            return (Array.Empty<ItemLabel>(), null);

        var items = _itemFrame.Count > 0 ? (IReadOnlyList<ItemLabel>)_itemFrame.ToArray() : Array.Empty<ItemLabel>();
        var lootTags = _lootTagFrame.Count > 0 ? (IReadOnlyList<LootTagLabel>)_lootTagFrame.ToArray() : null;
        return (items, lootTags);
    }

    private void ResetLootSession()
    {
        if (!ReferenceEquals(_lootTags, LootTagRender.Empty)) _lootTags = LootTagRender.Empty;
        _itemFrame.Clear();
        _lootTagFrame.Clear();
    }

    public void RefreshPriceBook()
    {
        _priceBook.SetLeagueOverride(string.IsNullOrWhiteSpace(_settings.GroundItems.League)
            ? null : _settings.GroundItems.League.Trim());
        _priceBook.ForceRefresh();
    }

    public void SetPriceLeague(string? league)
    {
        _settings.GroundItems.League = league ?? "";
        _settings.Save();
        _priceBook.SetLeagueOverride(string.IsNullOrWhiteSpace(league) ? null : league.Trim());
        _priceBook.ForceRefresh();
    }
}
