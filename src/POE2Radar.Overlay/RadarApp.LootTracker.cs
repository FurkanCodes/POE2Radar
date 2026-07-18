using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Pricing;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private static string LootTrackerDir => Path.Combine(AppContext.BaseDirectory, "LootTracker");

    private readonly PerformanceCadence _lootTrackerCadence = new();
    private bool _lootTrackerInitialized;
    private string _lootTrackerPricingConfigKey = "";
    private DateTime _lootLastTickUtc = DateTime.MinValue;
    private LootMapRun? _lootCurrent;
    private readonly List<LootMapRun> _lootCompleted = new();
    private Dictionary<string, long>? _lootBaseline;
    private Dictionary<string, long>? _lootPrevSnapshot;
    private bool _lootBaselinePending;
    private readonly Dictionary<string, Poe2Live.LootInventoryItem> _lootFacts = new(StringComparer.Ordinal);
    private readonly Dictionary<nint, LootMonsterTally> _lootMonsterTallies = new();
    private readonly Dictionary<nint, LootGoldLabelSeen> _lootGoldLabels = new();
    private readonly List<LootPickupToast> _lootActiveToasts = new();
    private readonly Queue<LootPickupToast> _lootPendingToasts = new();
    private Dictionary<string, string>? _lootMetaArt;
    private LootTrackerView _lootTrackerView = LootTrackerView.Empty;

    private const int LootTrackerScanHz = 2;
    private const int LootGoldLabelScanHz = 8;
    private const int LootTrackerMaxVisibleToasts = 3;
    private const int LootTrackerMaxPendingToasts = 30;
    private static readonly Regex LootGoldRegex = new(
        @"(?ix)
          (?:
            (?<amount>\d[\d,\.\s]*)\s*(?:x\s*)?gold\b
            |
            \bgold\s*(?:x\s*)?(?<amount>\d[\d,\.\s]*)
          )",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly PerformanceCadence _lootGoldLabelCadence = new();

    private sealed class LootMapRun
    {
        public string Name { get; set; } = "";
        public string Hash { get; set; } = "";
        public int AreaLevel { get; set; }
        public TimeSpan ActiveTime { get; set; }
        public long GoldGained { get; set; }
        public Dictionary<string, long> Gained { get; set; } = new(StringComparer.Ordinal);
        public int[] Kills { get; set; } = new int[4];
        public int LedgerRevision { get; set; }
        public int ValuedRevision { get; set; } = -1;
        public DateTime ValuedUtc { get; set; } = DateTime.MinValue;
        public double CachedValueEx { get; set; }
        public LootValuedItem[] CachedItems { get; set; } = [];
        public int CachedRowsRevision { get; set; } = -1;
        public int CachedRowsCurrencyKey { get; set; } = -1;
        public long CachedRowsGold { get; set; } = -1;
        public LootTrackerItemRow[] CachedRows { get; set; } = [];
    }

    internal readonly record struct LootValuedItem(
        string Label,
        long Count,
        double UnitEx,
        double TotalEx,
        bool Priced);

    private sealed class LootMonsterTally
    {
        public int RarityIndex;
        public bool SeenAlive;
        public bool Tallied;
    }

    private sealed class LootGoldLabelSeen
    {
        public long Amount;
        public DateTime LastSeenUtc;
        public bool Credited;
    }

    private sealed class LootPickupToast
    {
        public string Label { get; set; } = "";
        public long Count { get; set; }
        public double TotalEx { get; set; }
        public DateTime ShownUtc { get; set; }
    }

    private void RefreshLootTracker(LiveFrameState live, WorldSnapshot snap)
    {
        if (!_settings.LootTracker.Enabled || !live.InGame)
        {
            PauseLootTracker();
            _lootTrackerView = LootTrackerView.Empty;
            return;
        }

        var now = DateTime.UtcNow;
        var onMap = IsLootTrackerRunArea(snap.AreaCode, snap.AreaLevel, snap.AreaHash);
        if (!onMap)
        {
            PauseLootTracker();
            UpdateLootToasts(now);
            _lootTrackerView = BuildLootTrackerView(now);
            return;
        }

        InitializeLootTrackerPricing(live);
        PoeNinjaPriceFetcher.RefreshIfNeeded();
        EnterOrResumeLootMap(snap, now);

        if (_lootCurrent != null)
            BankLootActiveTime(now);

        ScanLootKills(snap);
        if (_lootGoldLabelCadence.IsDue(LootGoldLabelScanHz))
            UpdateLootGoldLabels(live.InGameState, now);

        if (_lootTrackerCadence.IsDue(LootTrackerScanHz))
        {
            var inv = live.AreaInstance == 0
                ? Poe2Live.LootInventorySnapshot.Failed
                : _live.ReadLootInventorySnapshot(live.AreaInstance);
            UpdateLootInventory(inv, onMap);
        }

        UpdateLootToasts(now);
        TrimLootHistory();
        _lootTrackerView = BuildLootTrackerView(now);
    }

    private void StartNewLootTrackerSession()
    {
        _lootCompleted.Clear();
        _lootCurrent = null;
        _lootBaseline = null;
        _lootPrevSnapshot = null;
        _lootBaselinePending = false;
        _lootMonsterTallies.Clear();
        _lootGoldLabels.Clear();
        _lootActiveToasts.Clear();
        _lootPendingToasts.Clear();
        _lootLastTickUtc = DateTime.MinValue;
        _lootTrackerView = LootTrackerView.Empty;
    }

    private void PauseLootTracker()
    {
        BankLootActiveTime(DateTime.UtcNow);
        if (_lootCurrent != null && !_lootCompleted.Contains(_lootCurrent))
            _lootCompleted.Add(_lootCurrent);
        _lootCurrent = null;
        _lootBaseline = null;
        _lootPrevSnapshot = null;
        _lootBaselinePending = false;
        _lootMonsterTallies.Clear();
        _lootGoldLabels.Clear();
    }

    private void EnterOrResumeLootMap(WorldSnapshot snap, DateTime now)
    {
        var hash = snap.AreaHash.ToString("X8");
        if (_lootCurrent?.Hash == hash) return;

        var latestHash = _lootCurrent?.Hash
                         ?? (_lootCompleted.Count > 0 ? _lootCompleted[^1].Hash : null);
        if (ShouldResetLootSessionForMap(
                hasSession: _lootCurrent != null || _lootCompleted.Count > 0,
                latestHash,
                hash))
            StartNewLootTrackerSession();
        else
            PauseLootTracker();

        var existingIndex = _lootCompleted.FindIndex(r => string.Equals(r.Hash, hash, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            _lootCurrent = _lootCompleted[existingIndex];
            _lootCompleted.RemoveAt(existingIndex);
        }
        else
        {
            _lootCurrent = new LootMapRun
            {
                Name = FriendlyLootAreaName(snap.AreaCode),
                Hash = hash,
                AreaLevel = snap.AreaLevel,
            };
        }

        _lootBaselinePending = true;
        _lootLastTickUtc = now;
        _lootMonsterTallies.Clear();
        _lootGoldLabels.Clear();
    }

    internal static bool ShouldResetLootSessionForMap(
        bool hasSession,
        string? latestHash,
        string nextHash)
        => hasSession
           && !string.IsNullOrEmpty(latestHash)
           && !string.Equals(latestHash, nextHash, StringComparison.Ordinal);

    private void BankLootActiveTime(DateTime now)
    {
        if (_lootCurrent == null || _lootLastTickUtc == DateTime.MinValue)
        {
            _lootLastTickUtc = now;
            return;
        }

        var delta = now - _lootLastTickUtc;
        if (delta > TimeSpan.Zero && delta < TimeSpan.FromSeconds(10))
            _lootCurrent.ActiveTime += delta;
        _lootLastTickUtc = now;
    }

    private void UpdateLootInventory(Poe2Live.LootInventorySnapshot inv, bool onMap)
    {
        if (!inv.Ok)
            return;

        var snap = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in inv.Items)
        {
            _lootFacts[item.Key] = item;
            snap.TryGetValue(item.Key, out var count);
            snap[item.Key] = count + Math.Max(1, item.Count);
        }

        if (!onMap || _lootCurrent == null)
        {
            _lootPrevSnapshot = snap;
            return;
        }

        if (_lootBaselinePending || _lootBaseline == null)
        {
            _lootBaseline = new Dictionary<string, long>(snap, StringComparer.Ordinal);
            _lootPrevSnapshot = new Dictionary<string, long>(snap, StringComparer.Ordinal);
            _lootBaselinePending = false;
            return;
        }

        if (_settings.LootTracker.ShowPickupToasts && _lootPrevSnapshot != null)
            DetectLootPickups(snap, _lootPrevSnapshot);

        if (_lootPrevSnapshot != null &&
            AccumulatePositiveLootDeltas(_lootCurrent.Gained, snap, _lootPrevSnapshot))
        {
            _lootCurrent.LedgerRevision++;
        }
        _lootPrevSnapshot = snap;
    }

    private void ScanLootKills(WorldSnapshot snap)
    {
        if (_lootCurrent == null || !_settings.LootTracker.ShowKills) return;

        foreach (var entity in snap.Entities)
        {
            if (entity.Category != Poe2Live.EntityCategory.Monster || entity.IsFriendly)
                continue;
            if ((int)entity.Rarity is < 0 or > 3)
                continue;

            if (!_lootMonsterTallies.TryGetValue(entity.Address, out var tally))
            {
                tally = new LootMonsterTally
                {
                    RarityIndex = (int)entity.Rarity,
                    SeenAlive = entity.IsAlive,
                };
                _lootMonsterTallies[entity.Address] = tally;
                continue;
            }

            if (tally.Tallied) continue;
            if (entity.IsAlive)
            {
                tally.SeenAlive = true;
            }
            else if (tally.SeenAlive)
            {
                _lootCurrent.Kills[tally.RarityIndex]++;
                tally.Tallied = true;
            }
        }
    }

    private void UpdateLootGoldLabels(nint inGameState, DateTime now)
    {
        if (_lootCurrent == null || inGameState == 0) return;

        var visible = new HashSet<nint>();
        foreach (var (el, text) in _live.ScanLootLabels(inGameState, maxNodes: 6000))
        {
            if (!TryParseGoldLabel(text, out var amount) || amount <= 0)
                continue;

            visible.Add(el);
            if (!_lootGoldLabels.TryGetValue(el, out var seen))
            {
                seen = new LootGoldLabelSeen();
                _lootGoldLabels[el] = seen;
            }

            seen.Amount = amount;
            seen.LastSeenUtc = now;
        }

        foreach (var kv in _lootGoldLabels.ToArray())
        {
            if (visible.Contains(kv.Key)) continue;

            var seen = kv.Value;
            if (!seen.Credited && seen.Amount > 0 && now - seen.LastSeenUtc <= TimeSpan.FromSeconds(2))
            {
                _lootCurrent.GoldGained += seen.Amount;
                seen.Credited = true;
            }

            if (now - seen.LastSeenUtc > TimeSpan.FromSeconds(4))
                _lootGoldLabels.Remove(kv.Key);
        }
    }

    private void DetectLootPickups(Dictionary<string, long> now, Dictionary<string, long> before)
    {
        foreach (var kv in now)
        {
            before.TryGetValue(kv.Key, out var old);
            var gained = kv.Value - old;
            if (gained <= 0) continue;
            if (!TryPriceLootItem(kv.Key, out var unitEx, out var label) || unitEx <= 0) continue;

            var total = unitEx * gained;
            if (total < _settings.LootTracker.NotifyMinEx) continue;
            EnqueueLootToast(label, gained, total);
        }
    }

    private void EnqueueLootToast(string label, long count, double totalEx)
    {
        var now = DateTime.UtcNow;
        foreach (var t in _lootActiveToasts)
        {
            if (!string.Equals(t.Label, label, StringComparison.Ordinal)) continue;
            t.Count += count;
            t.TotalEx += totalEx;
            t.ShownUtc = now;
            return;
        }

        foreach (var t in _lootPendingToasts)
        {
            if (!string.Equals(t.Label, label, StringComparison.Ordinal)) continue;
            t.Count += count;
            t.TotalEx += totalEx;
            return;
        }

        var toast = new LootPickupToast { Label = label, Count = count, TotalEx = totalEx };
        if (_lootActiveToasts.Count < LootTrackerMaxVisibleToasts)
        {
            toast.ShownUtc = now;
            _lootActiveToasts.Add(toast);
        }
        else if (_lootPendingToasts.Count < LootTrackerMaxPendingToasts)
        {
            _lootPendingToasts.Enqueue(toast);
        }
    }

    private void UpdateLootToasts(DateTime now)
    {
        var life = Math.Clamp(_settings.LootTracker.NotifyDurationSec, 1f, 6f);
        _lootActiveToasts.RemoveAll(t => (now - t.ShownUtc).TotalSeconds >= life);
        while (_lootActiveToasts.Count < LootTrackerMaxVisibleToasts && _lootPendingToasts.Count > 0)
        {
            var t = _lootPendingToasts.Dequeue();
            t.ShownUtc = now;
            _lootActiveToasts.Add(t);
        }
    }

    private LootTrackerView BuildLootTrackerView(DateTime now)
    {
        var settings = _settings.LootTracker;
        var activeEx = _lootCurrent == null ? 0 : EnsureLootValuation(_lootCurrent, now);
        var activeGold = _lootCurrent == null
            ? 0
            : GoldOfLoot(_lootCurrent.Gained) + _lootCurrent.GoldGained;
        var sessionRuns = LootSessionRuns();
        SessionTotals(sessionRuns, now, out var totalTime, out var totalEx, out var totalGold);
        var maps = sessionRuns.Length;
        var perHour = totalTime.TotalHours > 0 ? totalEx / totalTime.TotalHours : 0;
        var avgTime = maps > 0 ? TimeSpan.FromTicks(totalTime.Ticks / maps) : TimeSpan.Zero;
        var avgProfit = maps > 0 ? totalEx / maps : 0;

        var recent = _lootCompleted
            .AsEnumerable()
            .Reverse()
            .Take(Math.Clamp(settings.HistorySize, 1, 50))
            .Select(r =>
            {
                var profit = EnsureLootValuation(r, now);
                return new LootTrackerRunRow(r.Name, FormatLootDuration(r.ActiveTime), FormatLootValue(profit), profit);
            })
            .ToArray();

        var sessionItems = AggregateSessionValuedItems(sessionRuns.Select(run =>
        {
            EnsureLootValuation(run, now);
            return (IReadOnlyList<LootValuedItem>)run.CachedItems;
        }));
        var breakdownItems = BuildLootBreakdownRows(sessionItems, totalGold);
        var breakdownEx = sessionItems.Sum(i => i.TotalEx);
        var breakdownItemCount = sessionItems.Sum(i => i.Count);
        var sessionMapCount = sessionRuns.Length;

        var life = Math.Clamp(settings.NotifyDurationSec, 1f, 6f);
        var toasts = _lootActiveToasts
            .Select(t => new LootTrackerToast(
                t.Label,
                t.Count,
                FormatLootValue(t.TotalEx),
                LootToastAlpha(t, now, life)))
            .ToArray();

        var kills = _lootCurrent?.Kills ?? [0, 0, 0, 0];
        return new LootTrackerView(
            true,
            _lootCurrent != null,
            _lootCurrent?.Name ?? "",
            FormatLootDuration(_lootCurrent?.ActiveTime ?? TimeSpan.Zero),
            FormatLootValue(activeEx, signed: true),
            activeEx,
            activeGold,
            FormatGold(activeGold, signed: true),
            totalGold,
            FormatGold(totalGold),
            kills[0],
            kills[1],
            kills[2],
            kills[3],
            maps,
            FormatLootDuration(avgTime),
            FormatLootValue(avgProfit),
            FormatLootValue(totalEx),
            FormatLootValue(perHour),
            FormatLootDuration(totalTime),
            recent,
            sessionMapCount == 0
                ? ""
                : $"Session loot · {sessionMapCount:N0} {(sessionMapCount == 1 ? "map" : "maps")}",
            FormatLootValue(breakdownEx),
            breakdownEx,
            totalGold,
            breakdownItemCount,
            _lootCurrent != null,
            breakdownItems,
            toasts,
            _lootPrevSnapshot?.Count ?? 0,
            _lootPrevSnapshot != null,
            EffectiveLootTrackerLeague(_liveFrame));
    }

    private LootMapRun[] LootSessionRuns()
        => _lootCurrent == null
            ? _lootCompleted.ToArray()
            : [.. _lootCompleted, _lootCurrent];

    private void SessionTotals(
        IReadOnlyList<LootMapRun> sessionRuns,
        DateTime now,
        out TimeSpan activeTime,
        out double totalEx,
        out long totalGold)
    {
        activeTime = TimeSpan.Zero;
        totalEx = 0;
        totalGold = 0;
        foreach (var run in sessionRuns)
        {
            activeTime += run.ActiveTime;
            totalEx += EnsureLootValuation(run, now);
            totalGold += GoldOfLoot(run.Gained) + run.GoldGained;
        }
    }

    private double EnsureLootValuation(LootMapRun run, DateTime now)
    {
        if (run.ValuedRevision == run.LedgerRevision &&
            now - run.ValuedUtc < TimeSpan.FromSeconds(30))
        {
            return run.CachedValueEx;
        }

        var items = new List<LootValuedItem>(run.Gained.Count);
        double totalEx = 0;
        foreach (var kv in run.Gained)
        {
            if (kv.Value <= 0 || IsGoldLootKey(kv.Key)) continue;

            var priced = TryPriceLootItem(kv.Key, out var unitEx, out var label) && unitEx > 0;
            var itemTotal = priced ? unitEx * kv.Value : 0;
            totalEx += itemTotal;
            items.Add(new LootValuedItem(label, kv.Value, unitEx, itemTotal, priced));
        }

        run.CachedItems = items
            .OrderByDescending(i => i.Priced)
            .ThenByDescending(i => i.TotalEx)
            .ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        run.CachedValueEx = totalEx;
        run.ValuedRevision = run.LedgerRevision;
        run.ValuedUtc = now;
        run.CachedRowsRevision = -1;
        return totalEx;
    }

    internal static LootValuedItem[] AggregateSessionValuedItems(
        IEnumerable<IReadOnlyList<LootValuedItem>> runs)
    {
        var totals = new Dictionary<string, (long Count, double TotalEx, bool Priced)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var items in runs)
        {
            foreach (var item in items)
            {
                if (item.Count <= 0) continue;
                totals.TryGetValue(item.Label, out var total);
                totals[item.Label] = (
                    total.Count + item.Count,
                    total.TotalEx + item.TotalEx,
                    total.Priced || item.Priced);
            }
        }

        return totals
            .Select(kv =>
            {
                var (count, totalEx, priced) = kv.Value;
                var unitEx = priced && count > 0 ? totalEx / count : 0;
                return new LootValuedItem(kv.Key, count, unitEx, totalEx, priced);
            })
            .OrderByDescending(i => i.Priced)
            .ThenByDescending(i => i.TotalEx)
            .ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private LootTrackerItemRow[] BuildLootBreakdownRows(
        IReadOnlyList<LootValuedItem> items,
        long gold)
    {
        var rows = new List<LootTrackerItemRow>(items.Count + 1);
        rows.AddRange(items.Select(i =>
        {
            var rowCurrency = EffectiveLootDisplayCurrency(i.TotalEx);
            return new LootTrackerItemRow(
                i.Label,
                i.Count,
                i.Priced ? FormatLootValue(i.UnitEx, modeOverride: rowCurrency) : "—",
                i.Priced ? FormatLootValue(i.TotalEx, modeOverride: rowCurrency) : "Unpriced",
                i.TotalEx,
                i.Priced);
        }));

        if (gold > 0)
            rows.Add(new LootTrackerItemRow("Gold", gold, "—", "Not market-priced", 0, false));
        return rows.ToArray();
    }

    private long GoldOfLoot(Dictionary<string, long> gained)
    {
        long gold = 0;
        foreach (var kv in gained)
        {
            if (kv.Value <= 0 || !IsGoldLootKey(kv.Key)) continue;
            gold += kv.Value;
        }
        return gold;
    }

    private bool TryPriceLootItem(string key, out double unitEx, out string label)
    {
        unitEx = 0;
        label = LabelForLootKey(key);
        ParseLootKey(key, out var rarity, out var metadata, out var renderArt);
        var internalName = LastPathSegment(metadata);
        var metaArt = LootMetaArt();

        var artCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(renderArt)) artCandidates.Add(renderArt);
        if (metaArt.TryGetValue(internalName, out var mappedArt)) artCandidates.Add(mappedArt);
        artCandidates.Add(internalName);
        artCandidates.Add($"{internalName}{rarity}");

        foreach (var art in artCandidates)
        {
            if (PoeNinjaPriceFetcher.TryGetExaltedByArtId(art, out unitEx) && unitEx > 0)
            {
                if (PoeNinjaPriceFetcher.TryResolveDisplayName(art, out var display))
                    label = display;
                return true;
            }
        }

        if (_lootFacts.TryGetValue(key, out var fact))
        {
            foreach (var name in new[] { fact.BaseName, fact.InternalName, label })
            {
                if (PoeNinjaPriceFetcher.TryGetExaltedByName(name, out unitEx) && unitEx > 0)
                {
                    label = string.IsNullOrWhiteSpace(fact.BaseName) ? label : fact.BaseName;
                    return true;
                }
            }
        }

        var price = PoeNinjaPriceFetcher.GetPrice(label, internalPathBasename: internalName, fullItemPath: metadata);
        if (price != null && PoeNinjaPriceFetcher.GetChaosPerExalted() > 0)
        {
            unitEx = price.PriceChaos / PoeNinjaPriceFetcher.GetChaosPerExalted();
            return unitEx > 0;
        }

        return false;
    }

    private string LabelForLootKey(string key)
    {
        if (_lootFacts.TryGetValue(key, out var fact))
        {
            if (!PoeNinjaPriceFetcher.IsGenericLookupName(fact.BaseName)) return fact.BaseName;
            if (PoeNinjaPriceFetcher.TryResolveDisplayName(fact.RenderArt, out var artName)) return artName;
            return fact.InternalName;
        }

        ParseLootKey(key, out _, out var metadata, out var renderArt);
        if (PoeNinjaPriceFetcher.TryResolveDisplayName(renderArt, out var display)) return display;
        return LastPathSegment(metadata);
    }

    private Dictionary<string, string> LootMetaArt()
    {
        if (_lootMetaArt != null) return _lootMetaArt;
        try
        {
            var path = Path.Combine(LootTrackerDir, "metaArt.json");
            _lootMetaArt = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _lootMetaArt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        return _lootMetaArt;
    }

    private void InitializeLootTrackerPricing(LiveFrameState live)
    {
        Directory.CreateDirectory(LootTrackerDir);

        var source = Math.Clamp(_settings.LootTracker.PriceSource, 0, 1);
        var league = EffectiveLootTrackerLeague(live);
        var refresh = Math.Max(1, _settings.LootTracker.RefreshIntervalMin);
        var key = $"{source}|{league}|{refresh}";

        LeagueProvider.EnsureLoaded();
        PoeNinjaPriceFetcher.Configure(source, league, refresh);

        if (!_lootTrackerInitialized)
        {
            PoeNinjaPriceFetcher.Initialize(LootTrackerDir);
            _lootTrackerInitialized = true;
            _lootTrackerPricingConfigKey = key;
            return;
        }

        if (!string.Equals(_lootTrackerPricingConfigKey, key, StringComparison.Ordinal))
        {
            _lootTrackerPricingConfigKey = key;
            PoeNinjaPriceFetcher.ForceRefresh(LootTrackerDir, ignoreCooldown: true);
        }
    }

    private string EffectiveLootTrackerLeague(LiveFrameState live)
    {
        var configured = _settings.LootTracker.League?.Trim();
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

    private void TrimLootHistory()
    {
        var max = Math.Clamp(_settings.LootTracker.HistorySize, 1, 200);
        while (_lootCompleted.Count > max)
            _lootCompleted.RemoveAt(0);
    }

    private string FormatLootValue(double ex, bool signed = false, int? modeOverride = null)
    {
        var prefix = signed && ex > 0 ? "+" : "";
        var mode = modeOverride ?? EffectiveLootDisplayCurrency(ex);
        if (mode == LootTrackerSettings.CurrencyDivine)
        {
            var rate = PoeNinjaPriceFetcher.DivineToExaltedRate;
            if (rate > 0) return $"{prefix}{ex / rate:0.##} div";
        }
        else if (mode == LootTrackerSettings.CurrencyChaos)
        {
            var chaosPerEx = PoeNinjaPriceFetcher.GetChaosPerExalted();
            if (chaosPerEx > 0) return $"{prefix}{ex * chaosPerEx:0.##} chaos";
        }

        return Math.Abs(ex) >= 100 ? $"{prefix}{ex:0} ex" : $"{prefix}{ex:0.#} ex";
    }

    private int EffectiveLootDisplayCurrency(double ex)
    {
        var selected = Math.Clamp(
            _settings.LootTracker.DisplayCurrency,
            LootTrackerSettings.CurrencyAuto,
            LootTrackerSettings.CurrencyChaos);

        // Backward compatibility for configs written before DisplayCurrency existed.
        if (_settings.LootTracker.ShowPricesInDivineOnly &&
            selected == LootTrackerSettings.CurrencyExalted)
        {
            return LootTrackerSettings.CurrencyDivine;
        }

        if (selected != LootTrackerSettings.CurrencyAuto)
            return selected;

        var divRate = PoeNinjaPriceFetcher.DivineToExaltedRate;
        if (divRate > 0 && ex >= divRate)
            return LootTrackerSettings.CurrencyDivine;
        if (ex < 1 && PoeNinjaPriceFetcher.GetChaosPerExalted() > 0)
            return LootTrackerSettings.CurrencyChaos;
        return LootTrackerSettings.CurrencyExalted;
    }

    private static string FormatGold(long gold, bool signed = false)
    {
        var prefix = signed && gold > 0 ? "+" : "";
        return $"{prefix}{gold:N0}";
    }

    internal static bool TryParseGoldLabel(string text, out long amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = LootGoldRegex.Match(text);
        if (!match.Success) return false;
        var raw = match.Groups["amount"].Value;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 0 && long.TryParse(digits, out amount) && amount > 0;
    }

    internal static bool AccumulatePositiveLootDeltas(
        Dictionary<string, long> ledger,
        IReadOnlyDictionary<string, long> now,
        IReadOnlyDictionary<string, long> before)
    {
        var changed = false;
        foreach (var kv in now)
        {
            before.TryGetValue(kv.Key, out var oldCount);
            var gained = kv.Value - oldCount;
            if (gained <= 0) continue;
            ledger.TryGetValue(kv.Key, out var accumulated);
            ledger[kv.Key] = accumulated + gained;
            changed = true;
        }
        return changed;
    }

    private static void ParseLootKey(string key, out Poe2Live.Rarity rarity, out string metadata, out string renderArt)
    {
        rarity = Poe2Live.Rarity.Normal;
        metadata = "";
        renderArt = "";
        var parts = key.Split('\x1F');
        if (parts.Length > 0 && int.TryParse(parts[0], out var r) && r is >= 0 and <= 3)
            rarity = (Poe2Live.Rarity)r;
        if (parts.Length > 1) metadata = parts[1];
        if (parts.Length > 2) renderArt = parts[2];
    }

    private bool IsGoldLootKey(string key)
    {
        ParseLootKey(key, out _, out var metadata, out var renderArt);
        var internalName = LastPathSegment(metadata);
        if (string.Equals(internalName, "GoldCoin", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(renderArt, "CoinPileTier2", StringComparison.OrdinalIgnoreCase))
            return true;
        if (_lootFacts.TryGetValue(key, out var fact))
        {
            if (string.Equals(fact.InternalName, "GoldCoin", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(fact.RenderArt, "CoinPileTier2", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static bool IsLootTrackerRunArea(string areaCode, int areaLevel, uint areaHash)
    {
        if (areaHash == 0) return false;
        if (string.IsNullOrWhiteSpace(areaCode)) return false;
        if (ZoneGuide.Shared.Area(areaCode) is { Town: true }) return false;
        if (areaCode.Contains("town", StringComparison.OrdinalIgnoreCase)) return false;
        if (areaCode.Contains("hideout", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string FriendlyLootAreaName(string areaCode)
    {
        var name = ZoneGuide.Shared.FriendlyName(areaCode);
        return string.IsNullOrWhiteSpace(name) ? areaCode : name;
    }

    private static string FormatLootDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        return $"{t.Minutes:D2}:{t.Seconds:D2}";
    }

    private static string LastPathSegment(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var slash = path.LastIndexOf('/');
        return slash >= 0 && slash < path.Length - 1 ? path[(slash + 1)..] : path;
    }

    private static float LootToastAlpha(LootPickupToast toast, DateTime now, float life)
    {
        const float fadeSec = 0.6f;
        var age = (float)(now - toast.ShownUtc).TotalSeconds;
        if (age <= life - fadeSec) return 1f;
        return Math.Clamp((life - age) / fadeSec, 0f, 1f);
    }
}
