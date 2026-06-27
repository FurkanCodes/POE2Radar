using NumVec2 = System.Numerics.Vector2;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Pricing;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private PoeNinjaPriceBook _priceBook = null!;
    private readonly RuneMonolithCatalog _monoCatalog = RuneMonolithCatalog.Instance;

    private readonly record struct LootTagSpec(nint El, string TagText, string Value, bool Highlight);
    private sealed record LootTagRender(IReadOnlyList<LootTagSpec> Specs)
    {
        public static readonly LootTagRender Empty = new(Array.Empty<LootTagSpec>());
    }
    private volatile LootTagRender _lootTags = LootTagRender.Empty;
    private readonly List<LootTagLabel> _lootTagFrame = new();
    private readonly List<ItemLabel> _itemFrame = new();

    private sealed record MonolithRender(uint AreaHash, IReadOnlyList<MonolithMarker> Markers)
    {
        public static readonly MonolithRender Empty = new(0, Array.Empty<MonolithMarker>());
    }
    private volatile MonolithRender _monoRender = MonolithRender.Empty;

    private DateTime _nextLootScanUtc = DateTime.MinValue;
    private const int LootScanThrottleMs = 2000;
    private int _windowWidth = 1920;
    private int _windowHeight = 1080;

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
        ritual = RitualPriceBookStatus(),
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

    private void UpdateLootWorld(GameContextSnapshot game, EntityContextSnapshot entities, int winW, int winH)
    {
        _windowWidth = winW;
        _windowHeight = winH;
        if (!game.Valid) return;
        SyncPriceBookLeague(game.AreaInstance);
        UpdateLootTags(game.InGameState);
        UpdateMonoliths(entities);
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

    private bool LeagueValueOverlaysEnabled()
        => (_settings.GroundItems.Enabled || _settings.Monoliths.Enabled) && _priceBook.IsLoaded;

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

        var tags = _worldLive.ScanLootLabels(inGameState, maxNodes: 3000);
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

    private void UpdateMonoliths(EntityContextSnapshot entities)
    {
        var cfg = _settings.Monoliths;
        if (!cfg.Enabled || !_priceBook.IsLoaded || !_monoCatalog.IsLoaded || !entities.Valid)
        {
            if (_monoRender.Markers.Count > 0) _monoRender = MonolithRender.Empty;
            return;
        }

        var areaHash = entities.AreaHash;
        var areaLevel = entities.AreaLevel;
        var entityList = entities.Entities;
        var markers = new List<MonolithMarker>();
        foreach (var e in entityList)
        {
            if (e.Metadata.IndexOf("Expedition2Encounter", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var m = _worldLive.ReadMonolith(e.Address);
            if (!m.Resolved) continue;
            if (cfg.HideCollected && m.Collected) continue;

            var offers = _monoCatalog.Offers(m.AnchorIdx, m.AnchorPos, m.HoleCount, m.IsUnique, areaLevel);
            var rewards = new List<MonolithReward>(offers.Count);
            double best = 0; var bestName = "";
            foreach (var o in offers)
            {
                var ex = o.Name.Length > 0 && _priceBook.TryByName(o.Name) is { } pr ? pr.Exalted * Math.Max(1, o.Count) : 0;
                if (ex < cfg.MinRewardEx) continue;
                rewards.Add(new MonolithReward(o.Name.Length > 0 ? o.Name : o.Description, o.Count, ex, o.Size, o.Runes));
                if (ex > best) { best = ex; bestName = o.Name; }
            }
            if (cfg.MinValueEx > 0 && best < cfg.MinValueEx) continue;
            rewards.Sort((a, b) => b.Ex.CompareTo(a.Ex));

            var anchor = m.IsUnique ? "Unique" : m.AnchorIdx >= 0 ? _monoCatalog.RuneName(m.AnchorIdx) : "?";
            markers.Add(new MonolithMarker(
                e.Grid, m.HoleCount, m.IsUnique, m.Collected, anchor, best, bestName,
                LootValueLogic.MonolithColor(best, cfg.HighlightMinEx), rewards));
        }
        _monoRender = new MonolithRender(areaHash, markers);
    }

    private void RefreshLootRenderFrames(WorldSnapshot snap, GameContextSnapshot game, UiContextSnapshot ui,
        int windowWidth, int windowHeight, bool inGame)
    {
        var profileStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var itemLabelCount = 0;
        var lootTagSpecs = 0;
        var lootTagHits = 0;

        _windowWidth = windowWidth;
        _windowHeight = windowHeight;
        _itemFrame.Clear();
        if (inGame && game.Valid)
        {
            SyncRitualPriceBook(game.AreaInstance);
            UpdateRitualHelperLive(game, ui);
        }

        if (inGame)
        {
            foreach (var s in snap.ItemLabels)
            {
                if (!_live.TryLiveBar(s.Entity, out var w, out _, out _, out _, out _)) continue;
                _itemFrame.Add(new ItemLabel(w, s.Name, s.Value, s.Highlight, s.ShowName));
                itemLabelCount++;
            }

            _lootTagFrame.Clear();
            var lt = _lootTags;
            foreach (var s in lt.Specs)
            {
                lootTagSpecs++;
                if (!UiProjector.TryRect(_reader, s.El, ui, out var rx, out var ry, out var rw, out var rh, s.TagText))
                    continue;
                _lootTagFrame.Add(new LootTagLabel(rx, ry, rw, rh, s.Value, s.Highlight));
                lootTagHits++;
            }
        }
        else
        {
            _lootTagFrame.Clear();
        }

        _lootFrameTicks++;
        _lootFrameItemLabels = itemLabelCount;
        _lootFrameLootTagSpecs = lootTagSpecs;
        _lootFrameLootTagHits = lootTagHits;
        _lootFrameLastMs = (System.Diagnostics.Stopwatch.GetTimestamp() - profileStart) * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
    }

    private (IReadOnlyList<ItemLabel> items, IReadOnlyList<LootTagLabel>? lootTags,
        IReadOnlyList<MonolithMarker>? monoliths) BuildLootRenderPayload(uint areaHash, bool inGame)
    {
        if (!inGame)
            return (Array.Empty<ItemLabel>(), null, null);

        var mr = _monoRender;
        var items = _itemFrame.Count > 0 ? (IReadOnlyList<ItemLabel>)_itemFrame.ToArray() : Array.Empty<ItemLabel>();
        var lootTags = _lootTagFrame.Count > 0 ? (IReadOnlyList<LootTagLabel>)_lootTagFrame.ToArray() : null;
        var monoliths = mr.AreaHash == areaHash && mr.Markers.Count > 0 ? mr.Markers : null;
        return (items, lootTags, monoliths);
    }

    private void ResetLootSession()
    {
        if (!ReferenceEquals(_lootTags, LootTagRender.Empty)) _lootTags = LootTagRender.Empty;
        if (_monoRender.Markers.Count > 0) _monoRender = MonolithRender.Empty;
        _ritualProbeHint = Poe2UiAnchors.BranchKind.None;
        _panelCatalog?.ResetRitualSession();
        ResetRitualSession();
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
