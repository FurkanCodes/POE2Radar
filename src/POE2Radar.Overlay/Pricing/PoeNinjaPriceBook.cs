using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace POE2Radar.Overlay.Pricing;

/// <summary>
/// Result of a price lookup. <see cref="Exalted"/> is the item's value in Exalted Orbs.
/// <see cref="Quantity"/> is listing count / trade volume — low-volume rows are often mislisted.
/// </summary>
public readonly record struct PriceResult(string Name, double Exalted, int Quantity, string Category)
{
    public bool LowConfidence(int minQty) => Quantity < minQty;
}

/// <summary>
/// Centralized poe.ninja PoE2 price source: fetches exchange + stash item overviews, converts to Exalted,
/// indexes by normalized name and 2D-art basename, caches to disk, refreshes on a TTL.
/// </summary>
public sealed class PoeNinjaPriceBook
{
    private static readonly string[] UniqueTypes =
    {
        "UniqueWeapons", "UniqueArmours", "UniqueAccessories", "UniqueFlasks", "UniqueJewels",
        "UniqueTablets", "PrecursorTablets",
    };

    private static readonly string[] ExchangeTypes =
    {
        "Currency", "Runes", "Fragments", "Essences", "Expedition", "Verisium", "Breach", "Ritual",
        "Delirium", "UncutGems", "Abyss", "SoulCores", "LineageSupportGems", "Idols",
    };

    private const string NinjaExchange = "https://poe.ninja/poe2/api/economy/exchange/current/overview";
    private const string NinjaStashItem = "https://poe.ninja/poe2/api/economy/stash/current/item/overview";

    private static readonly HttpClient Http = CreateHttp();
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _cachePath;
    private readonly object _gate = new();

    private volatile Dictionary<string, PricedItem> _byArt = new(StringComparer.OrdinalIgnoreCase);
    private volatile Dictionary<string, PricedItem> _byName = new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _fetching;
    private DateTime _lastFetchUtc = DateTime.MinValue;
    private string _league = "";
    private string? _leagueOverride;
    private volatile string? _detectedLeague;

    public double ExPerDivine { get; private set; } = 1;
    public double ExPerChaos { get; private set; } = 1;
    public bool IsLoaded => _byName.Count > 0 || _byArt.Count > 0;
    public int ItemCount => _byArt.Count + _byName.Count;
    public string League => _league;
    public string Status { get; private set; } = "not started";
    public DateTime LastFetchUtc => _lastFetchUtc;
    public int RefreshIntervalMinutes { get; set; } = 30;

    private sealed record PricedItem(string Name, double Exalted, int Quantity, string Category);

    public PoeNinjaPriceBook(string cachePath, string? leagueOverride = null)
    {
        _cachePath = cachePath;
        _leagueOverride = string.IsNullOrWhiteSpace(leagueOverride) ? null : leagueOverride.Trim();
        TryLoadCache();
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("POE2Radar-PriceBook");
        return c;
    }

    public void SetLeagueOverride(string? league)
    {
        var v = string.IsNullOrWhiteSpace(league) ? null : league.Trim();
        if (v == _leagueOverride) return;
        _leagueOverride = v;
        _lastFetchUtc = DateTime.MinValue;
    }

    public void SetDetectedLeague(string? league)
    {
        var v = string.IsNullOrWhiteSpace(league) ? null : league.Trim();
        if (v == _detectedLeague) return;
        _detectedLeague = v;
        if (_leagueOverride == null) _lastFetchUtc = DateTime.MinValue;
    }

    public void RefreshIfDue()
    {
        if (_fetching) return;
        if (DateTime.UtcNow - _lastFetchUtc < TimeSpan.FromMinutes(RefreshIntervalMinutes)) return;
        _fetching = true;
        _ = Task.Run(FetchAsync);
    }

    public void ForceRefresh()
    {
        if (_fetching) return;
        _fetching = true;
        _ = Task.Run(FetchAsync);
    }

    public PriceResult? TryByArt(string? artBasename)
    {
        if (string.IsNullOrWhiteSpace(artBasename)) return null;
        return _byArt.TryGetValue(artBasename.Trim(), out var p)
            ? new PriceResult(p.Name, p.Exalted, p.Quantity, p.Category) : null;
    }

    public PriceResult? TryByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _byName.TryGetValue(Normalize(name), out var p)
            ? new PriceResult(p.Name, p.Exalted, p.Quantity, p.Category) : null;
    }

    public string Format(double ex)
    {
        if (ExPerDivine > 1 && ex >= ExPerDivine) return $"{ex / ExPerDivine:0.##} div";
        return $"{ex:0.##} ex";
    }

    private async Task FetchAsync()
    {
        try
        {
            Status = "fetching…";
            var league = await ResolveLeagueAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(league)) { Status = "no league"; return; }
            var lg = Uri.EscapeDataString(league);

            var byArt = new Dictionary<string, PricedItem>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, PricedItem>(StringComparer.OrdinalIgnoreCase);
            double exPerDivine = 0;

            foreach (var type in ExchangeTypes)
                await FetchExchangeAsync(lg, type, byArt, byName, () => exPerDivine, r => exPerDivine = r).ConfigureAwait(false);
            foreach (var type in UniqueTypes)
                await FetchUniquesAsync(lg, type, byArt, byName, () => exPerDivine, r => exPerDivine = r).ConfigureAwait(false);

            if (byArt.Count == 0 && byName.Count == 0) { Status = "fetch returned no rows"; return; }

            _byArt = byArt;
            _byName = byName;
            _league = league;
            _lastFetchUtc = DateTime.UtcNow;
            Status = $"loaded {byName.Count} by name + {byArt.Count} by art for '{league}'";
            SaveCache();
        }
        catch (Exception ex) { Status = $"fetch failed: {ex.Message}"; }
        finally { _fetching = false; }
    }

    private async Task<string> ResolveLeagueAsync()
    {
        if (_leagueOverride != null) return _leagueOverride;
        var detected = _detectedLeague;
        List<ScoutLeague> leagues;
        try
        {
            var json = await Http.GetStringAsync("https://poe2scout.com/api/poe2/Leagues").ConfigureAwait(false);
            leagues = JsonSerializer.Deserialize<List<ScoutLeague>>(json, Json) ?? new();
        }
        catch
        {
            return detected ?? "";
        }

        if (detected != null)
        {
            var match = leagues.FirstOrDefault(l => string.Equals(l.Value, detected, StringComparison.OrdinalIgnoreCase));
            return match?.Value ?? detected;
        }

        var pick = leagues.FirstOrDefault(l => l.IsCurrent && !l.Value.StartsWith("HC", StringComparison.OrdinalIgnoreCase))
                   ?? leagues.FirstOrDefault(l => l.IsCurrent)
                   ?? leagues.FirstOrDefault();
        return pick?.Value ?? "";
    }

    private void ApplyRates(NinjaCore? core)
    {
        if (core?.Rates == null) return;
        if (core.Rates.TryGetValue("exalted", out var exPerDiv) && exPerDiv > 0)
        {
            ExPerDivine = exPerDiv;
            if (core.Rates.TryGetValue("chaos", out var chaosPerDiv) && chaosPerDiv > 0)
                ExPerChaos = exPerDiv / chaosPerDiv;
        }
    }

    private async Task FetchExchangeAsync(string leagueEscaped, string type,
        Dictionary<string, PricedItem> byArt, Dictionary<string, PricedItem> byName,
        Func<double> getRate, Action<double> setRate)
    {
        try
        {
            var url = $"{NinjaExchange}?league={leagueEscaped}&type={type}";
            var data = JsonSerializer.Deserialize<NinjaOverview>(await Http.GetStringAsync(url).ConfigureAwait(false), Json);
            if (data?.Lines == null) return;
            if (getRate() <= 0) { ApplyRates(data.Core); setRate(ExPerDivine); }
            var rate = getRate();
            if (rate <= 0) return;

            var meta = new Dictionary<string, NinjaItem>(StringComparer.Ordinal);
            if (data.Items != null)
                foreach (var it in data.Items)
                    if (!string.IsNullOrEmpty(it.Id)) meta[it.Id] = it;

            foreach (var ln in data.Lines)
            {
                if (ln.Id.ValueKind != JsonValueKind.String) continue;
                var id = ln.Id.GetString();
                if (string.IsNullOrEmpty(id) || !meta.TryGetValue(id, out var m)) continue;
                if (string.IsNullOrWhiteSpace(m.Name) || ln.PrimaryValue <= 0) continue;
                var ex = ln.PrimaryValue * rate;
                var qty = (int)Math.Clamp(ln.VolumePrimaryValue ?? 0, 0, int.MaxValue);
                var item = new PricedItem(m.Name.Trim(), ex, qty, type);
                Upsert(byName, Normalize(m.Name), item);
                var art = ArtBasenameFromIcon(m.Image);
                if (art != null) Upsert(byArt, art, item, preferVolume: true);
            }
        }
        catch { /* skip empty categories */ }
    }

    private async Task FetchUniquesAsync(string leagueEscaped, string type,
        Dictionary<string, PricedItem> byArt, Dictionary<string, PricedItem> byName,
        Func<double> getRate, Action<double> setRate)
    {
        try
        {
            var url = $"{NinjaStashItem}?league={leagueEscaped}&type={type}";
            var data = JsonSerializer.Deserialize<NinjaOverview>(await Http.GetStringAsync(url).ConfigureAwait(false), Json);
            if (data?.Lines == null) return;
            if (getRate() <= 0) { ApplyRates(data.Core); setRate(ExPerDivine); }
            var rate = getRate();
            if (rate <= 0) return;

            foreach (var ln in data.Lines)
            {
                if (string.IsNullOrWhiteSpace(ln.Name) || ln.PrimaryValue <= 0) continue;
                var ex = ln.PrimaryValue * rate;
                var item = new PricedItem(ln.Name.Trim(), ex, ln.ListingCount ?? 0, type);
                Upsert(byName, Normalize(ln.Name), item);
                var art = ArtBasenameFromIcon(ln.Icon);
                if (art != null) Upsert(byArt, art, item, preferVolume: true);
            }
        }
        catch { }
    }

    private static void Upsert(Dictionary<string, PricedItem> map, string key, PricedItem item, bool preferVolume = false)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!map.TryGetValue(key, out var cur)) { map[key] = item; return; }
        var replace = preferVolume
            ? item.Quantity > cur.Quantity || (item.Quantity == cur.Quantity && item.Exalted > cur.Exalted)
            : item.Exalted > cur.Exalted;
        if (replace) map[key] = item;
    }

    private static string? ArtBasenameFromIcon(string? iconUrl)
    {
        if (string.IsNullOrWhiteSpace(iconUrl)) return null;
        var noQuery = iconUrl.Split('?')[0];
        var seg = noQuery.Split('/').LastOrDefault();
        if (string.IsNullOrWhiteSpace(seg)) return null;
        var dot = seg.LastIndexOf('.');
        var name = dot > 0 ? seg[..dot] : seg;
        return name.Length >= 2 ? name : null;
    }

    private static string Normalize(string s) => s.Trim();

    private sealed class CacheDto
    {
        public string League { get; set; } = "";
        public DateTime FetchedUtc { get; set; }
        public double ExPerDivine { get; set; }
        public double ExPerChaos { get; set; }
        public Dictionary<string, PricedItem> ByArt { get; set; } = new();
        public Dictionary<string, PricedItem> ByName { get; set; } = new();
    }

    private void TryLoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) { Status = "no cache; will fetch"; return; }
            var dto = JsonSerializer.Deserialize<CacheDto>(File.ReadAllText(_cachePath), Json);
            if (dto == null) return;
            if (_leagueOverride != null && !string.Equals(dto.League, _leagueOverride, StringComparison.OrdinalIgnoreCase)) return;
            _byArt = new Dictionary<string, PricedItem>(dto.ByArt, StringComparer.OrdinalIgnoreCase);
            _byName = new Dictionary<string, PricedItem>(dto.ByName, StringComparer.OrdinalIgnoreCase);
            _league = dto.League;
            _lastFetchUtc = dto.FetchedUtc;
            if (dto.ExPerDivine > 0) ExPerDivine = dto.ExPerDivine;
            if (dto.ExPerChaos > 0) ExPerChaos = dto.ExPerChaos;
            Status = $"cache: {ItemCount} entries for '{_league}'";
        }
        catch (Exception ex) { Status = $"cache load failed: {ex.Message}"; }
    }

    private void SaveCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var dto = new CacheDto
            {
                League = _league, FetchedUtc = _lastFetchUtc, ExPerDivine = ExPerDivine, ExPerChaos = ExPerChaos,
                ByArt = new(_byArt), ByName = new(_byName),
            };
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(dto, Json));
        }
        catch { }
    }

    private sealed class ScoutLeague
    {
        public string Value { get; set; } = "";
        public bool IsCurrent { get; set; }
    }

    private sealed class NinjaOverview
    {
        public NinjaCore? Core { get; set; }
        public List<NinjaLine>? Lines { get; set; }
        public List<NinjaItem>? Items { get; set; }
    }

    private sealed class NinjaCore
    {
        public Dictionary<string, double>? Rates { get; set; }
    }

    private sealed class NinjaItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Image { get; set; }
    }

    private sealed class NinjaLine
    {
        public JsonElement Id { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public double PrimaryValue { get; set; }
        public double? VolumePrimaryValue { get; set; }
        public int? ListingCount { get; set; }
    }
}
