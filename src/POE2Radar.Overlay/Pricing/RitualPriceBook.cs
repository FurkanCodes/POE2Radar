// Ritual price book — derived from GameHelper RitualHelper PoeNinjaPriceFetcher (GPL-3.0).
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace POE2Radar.Overlay.Pricing;

public sealed class RitualPriceBook
{
    private const int CacheSchemaVersion = 2;

    private static readonly string[] ScoutCurrencyCategories =
    {
        "currency", "ritual", "runes", "idol", "essences", "fragments", "abyss", "breach",
        "delirium", "expedition", "incursion", "ultimatum", "vaal", "vaultkeys", "verisium",
        "uncutgems", "lineagesupportgems",
    };

    private static readonly string[] ScoutUniqueCategories =
    {
        "weapon", "armour", "accessory", "flask", "jewel", "map", "sanctum",
    };

    private static readonly string[] NinjaExchangeTypes =
    {
        "Ritual", "Currency", "Runes", "Idols", "Essences", "Fragments", "Abyss", "Breach",
        "Delirium", "Expedition", "Ultimatum", "UncutGems",
    };

    private static readonly string[] NinjaStashTypes =
    {
        "UniqueArmours", "UniqueAccessories", "UniqueCharms", "UniqueWeapons",
    };

    private static readonly HttpClient Http = CreateHttp();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly string _cachePath;
    private readonly object _gate = new();

    private Dictionary<string, double> _flatChaos = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<UniqueListing>> _uniqueListings = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _pathNames = new(StringComparer.OrdinalIgnoreCase);

    private volatile bool _fetching;
    private DateTime _lastFetchUtc = DateTime.MinValue;
    private string _league = "";
    private string? _leagueOverride;
    private volatile string? _detectedLeague;
    private int _priceSource = RitualPriceLookup.SourcePoe2Scout;

    public double ChaosPerDivine { get; private set; } = 12.0;
    public double ChaosPerExalted { get; private set; } = 0.1;
    public bool IsLoaded => _flatChaos.Count > 0 || _uniqueListings.Count > 0;
    public int ItemCount => _flatChaos.Count + _uniqueListings.Values.Sum(v => v.Count);
    public string League => _league;
    public string EffectiveLeague => string.IsNullOrWhiteSpace(_leagueOverride) ? (_detectedLeague ?? _league) : _leagueOverride;
    public string DetectedLeague => _detectedLeague ?? "";
    public bool IsFetching => _fetching;
    public string Status { get; private set; } = "not started";
    public DateTime LastFetchUtc => _lastFetchUtc;
    public int RefreshIntervalMinutes { get; set; } = 5;
    public int PriceSource { get => _priceSource; set => _priceSource = value is RitualPriceLookup.SourcePoeNinja or RitualPriceLookup.SourcePoe2Scout ? value : RitualPriceLookup.SourcePoe2Scout; }

    public RitualPriceBook(string cachePath, string? leagueOverride = null)
    {
        _cachePath = cachePath;
        _leagueOverride = string.IsNullOrWhiteSpace(leagueOverride) ? null : leagueOverride.Trim();
        TryLoadCache();
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("POE2Radar-RitualHelper");
        return c;
    }

    public void ReloadCacheFromDisk() => TryLoadCache();

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

    public string? TryPrettyName(string internalBasename)
    {
        if (RitualPriceLookup.TryResolveDisplayName(internalBasename, _pathNames, out var name))
            return name;
        return null;
    }

    public string? TryResolveArtName(string? artBasename)
    {
        foreach (var key in RitualPriceLookup.ArtKeyVariants(artBasename))
        {
            if (RitualPriceLookup.TryResolveDisplayName(key, _pathNames, out var mapped))
                return mapped;
            if (HasPriceDataForName(key))
                return key;
        }
        return null;
    }

    public bool HasPriceDataForName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var key = RitualPriceLookup.NormalizeKey(name);
        return _flatChaos.ContainsKey(key) || _uniqueListings.ContainsKey(key);
    }

    public double? GetPriceChaos(
        string itemName,
        IReadOnlyList<string>? mods,
        string? internalPathBasename,
        string? fullItemPath,
        string? scoutText = null)
    {
        foreach (var candidate in RitualPriceLookup.BuildNameCandidates(itemName, internalPathBasename, fullItemPath, scoutText, _pathNames))
        {
            if (!HasPriceDataForName(candidate)) continue;
            var chaos = LookupChaos(candidate, mods);
            if (chaos > 0) return chaos;
        }
        return null;
    }

    public double StabilizeSessionPrice(string key, double chaos, IDictionary<string, double> sessionStable)
    {
        if (string.IsNullOrWhiteSpace(key)) return chaos;
        if (!sessionStable.TryGetValue(key, out var prev) || chaos > prev)
        {
            sessionStable[key] = chaos;
            return chaos;
        }
        return prev;
    }

    private double LookupChaos(string itemName, IReadOnlyList<string>? mods)
    {
        var key = RitualPriceLookup.NormalizeKey(itemName);
        var chaosFromFlat = _flatChaos.TryGetValue(key, out var flat) ? flat : 0;
        var chaosFromUnique = 0.0;
        if (mods is { Count: > 0 } && _uniqueListings.TryGetValue(key, out var listings) && listings.Count > 0)
            chaosFromUnique = ResolveUniquePrice(listings, mods);
        else if (_uniqueListings.TryGetValue(key, out var all) && all.Count > 0)
            chaosFromUnique = MedianPrice(all);
        return Math.Max(chaosFromFlat, chaosFromUnique);
    }

    private static double ResolveUniquePrice(List<UniqueListing> listings, IReadOnlyList<string> mods)
    {
        var best = PickBestListing(listings, mods);
        return best?.PriceChaos ?? MedianPrice(listings);
    }

    private static UniqueListing? PickBestListing(List<UniqueListing> listings, IReadOnlyList<string> mods)
    {
        UniqueListing? best = null;
        var bestScore = 0;
        foreach (var listing in listings)
        {
            var score = RitualPriceLookup.ScoreModMatch(mods, listing.ExplicitMods);
            if (score > bestScore) { bestScore = score; best = listing; }
        }
        var threshold = mods.Count >= 4 ? 2 : 3;
        return best != null && bestScore >= threshold ? best : null;
    }

    private static double MedianPrice(List<UniqueListing> listings)
    {
        var prices = listings.Where(l => l.PriceChaos > 0).Select(l => l.PriceChaos).OrderBy(p => p).ToList();
        return prices.Count == 0 ? 0 : prices[prices.Count / 2];
    }

    private async Task FetchAsync()
    {
        try
        {
            var league = _leagueOverride ?? _detectedLeague ?? "Standard";
            var flat = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var uniques = new Dictionary<string, List<UniqueListing>>(StringComparer.OrdinalIgnoreCase);
            var pathNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var divChaos = ChaosPerDivine;
            var exChaos = ChaosPerExalted;

            if (_priceSource == RitualPriceLookup.SourcePoe2Scout)
            {
                (divChaos, exChaos) = await FetchFromScoutAsync(league, flat, uniques, pathNames, divChaos, exChaos).ConfigureAwait(false);
                var ninjaRates = await FetchNinjaStashAsync(league, flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
                exChaos = ninjaRates.ExChaos;
                divChaos = ninjaRates.DivChaos;
            }
            else
            {
                var rates = await FetchFromNinjaAsync(league, flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
                divChaos = rates.DivChaos;
                exChaos = rates.ExChaos;
            }

            lock (_gate)
            {
                _flatChaos = flat;
                _uniqueListings = uniques;
                _pathNames = pathNames;
                _league = league;
                ChaosPerDivine = divChaos > 0 ? divChaos : ChaosPerDivine;
                ChaosPerExalted = exChaos > 0 ? exChaos : ChaosPerExalted;
                _lastFetchUtc = DateTime.UtcNow;
                Status = $"loaded {ItemCount} for '{_league}'";
            }
            SaveCache();
        }
        catch (Exception ex)
        {
            Status = $"fetch failed: {ex.Message}";
        }
        finally
        {
            _fetching = false;
        }
    }

    private readonly record struct RatePair(double DivChaos, double ExChaos);

    private async Task<RatePair> FetchFromScoutAsync(
        string league, Dictionary<string, double> flat, Dictionary<string, List<UniqueListing>> uniques,
        Dictionary<string, string> pathNames, double divChaos, double exChaos)
    {
        var escaped = Uri.EscapeDataString(league);
        (divChaos, exChaos) = await UpdateScoutRatesAsync(escaped, divChaos, exChaos).ConfigureAwait(false);
        foreach (var cat in ScoutCurrencyCategories)
            await FetchScoutCurrencyCategoryAsync(escaped, cat, flat, pathNames).ConfigureAwait(false);
        foreach (var cat in ScoutUniqueCategories)
            await FetchScoutUniqueCategoryAsync(escaped, cat, uniques, pathNames).ConfigureAwait(false);
        return new RatePair(divChaos, exChaos);
    }

    private async Task<(double DivChaos, double ExChaos)> UpdateScoutRatesAsync(string leagueEscaped, double divChaos, double exChaos)
    {
        try
        {
            var json = await Http.GetStringAsync("https://poe2scout.com/api/poe2/Leagues").ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("value", out var leagues) || doc.RootElement.TryGetProperty("Value", out leagues))
            {
                foreach (var league in leagues.EnumerateArray())
                {
                    var name = league.TryGetProperty("Value", out var v) ? v.GetString() : null;
                    if (!string.Equals(name, _leagueOverride ?? _detectedLeague, StringComparison.OrdinalIgnoreCase)) continue;
                    if (league.TryGetProperty("ChaosDivinePrice", out var cd) && cd.TryGetDouble(out var chaosDiv) && chaosDiv > 0)
                        divChaos = chaosDiv;
                    if (league.TryGetProperty("DivinePrice", out var dp) && dp.TryGetDouble(out var divEx) && divEx > 0 && divChaos > 0)
                        exChaos = divChaos / divEx;
                    break;
                }
            }
        }
        catch { }

        try
        {
            var url = $"https://poe2scout.com/api/poe2/Leagues/{leagueEscaped}/Currencies/ByCategory?Category=currency&ReferenceCurrency=chaos&PerPage=250&Page=1";
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Items", out var items)) return (divChaos, exChaos);
            foreach (var item in items.EnumerateArray())
            {
                var text = item.TryGetProperty("Text", out var t) ? t.GetString() : null;
                var price = item.TryGetProperty("CurrentPrice", out var p) && p.TryGetDouble(out var pv) ? pv : 0;
                if (string.IsNullOrEmpty(text) || price <= 0) continue;
                if (text.Contains("Divine Orb", StringComparison.OrdinalIgnoreCase)) divChaos = price;
                if (text.Contains("Exalted Orb", StringComparison.OrdinalIgnoreCase)) exChaos = price;
            }
        }
        catch { }
        return (divChaos, exChaos);
    }

    private async Task FetchScoutCurrencyCategoryAsync(string leagueEscaped, string category, Dictionary<string, double> flat, Dictionary<string, string> pathNames)
    {
        for (var page = 1; page <= 20; page++)
        {
            try
            {
                var url = $"https://poe2scout.com/api/poe2/Leagues/{leagueEscaped}/Currencies/ByCategory?Category={category}&ReferenceCurrency=chaos&PerPage=250&Page={page}";
                var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var pages = doc.RootElement.TryGetProperty("Pages", out var pg) && pg.TryGetInt32(out var pn) ? pn : 1;
                if (!doc.RootElement.TryGetProperty("Items", out var items)) break;
                foreach (var item in items.EnumerateArray())
                {
                    var price = item.TryGetProperty("CurrentPrice", out var p) && p.TryGetDouble(out var pv) ? pv : 0;
                    if (price <= 0) continue;
                    AddFlat(flat, item.TryGetProperty("Text", out var t) ? t.GetString() : null, price);
                    AddFlat(flat, item.TryGetProperty("ApiId", out var id) ? id.GetString() : null, price);
                    if (item.TryGetProperty("ItemMetadata", out var meta))
                    {
                        AddFlat(flat, meta.TryGetProperty("name", out var n) ? n.GetString() : null, price);
                        AddFlat(flat, meta.TryGetProperty("base_type", out var bt) ? bt.GetString() : null, price);
                    }
                    IndexPath(pathNames, item.TryGetProperty("ApiId", out var api) ? api.GetString() : null,
                        item.TryGetProperty("Text", out var tx) ? tx.GetString() : null);
                    IndexPath(pathNames, IconBasename(item.TryGetProperty("IconUrl", out var ic) ? ic.GetString() : null),
                        item.TryGetProperty("Text", out var tx2) ? tx2.GetString() : null);
                }
                if (page >= pages) break;
            }
            catch { break; }
        }
    }

    private async Task FetchScoutUniqueCategoryAsync(string leagueEscaped, string category, Dictionary<string, List<UniqueListing>> uniques, Dictionary<string, string> pathNames)
    {
        for (var page = 1; page <= 20; page++)
        {
            try
            {
                var url = $"https://poe2scout.com/api/poe2/Leagues/{leagueEscaped}/Uniques/ByCategory?Category={category}&ReferenceCurrency=chaos&PerPage=250&Page={page}";
                var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var pages = doc.RootElement.TryGetProperty("Pages", out var pg) && pg.TryGetInt32(out var pn) ? pn : 1;
                if (!doc.RootElement.TryGetProperty("Items", out var items)) break;
                foreach (var item in items.EnumerateArray())
                {
                    var price = item.TryGetProperty("CurrentPrice", out var p) && p.TryGetDouble(out var pv) ? pv : 0;
                    if (price <= 0) continue;
                    var listing = new UniqueListing
                    {
                        Name = item.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                        Text = item.TryGetProperty("Text", out var tx) ? tx.GetString() ?? "" : "",
                        BaseType = item.TryGetProperty("Type", out var ty) ? ty.GetString() ?? "" : "",
                        PriceChaos = price,
                    };
                    if (item.TryGetProperty("ItemMetadata", out var meta))
                    {
                        if (string.IsNullOrEmpty(listing.BaseType) && meta.TryGetProperty("base_type", out var bt))
                            listing.BaseType = bt.GetString() ?? "";
                        listing.ExplicitMods.AddRange(ReadStringArray(meta, "implicit_mods"));
                        listing.ExplicitMods.AddRange(ReadStringArray(meta, "explicit_mods"));
                    }
                    AddUnique(uniques, listing);
                    IndexPath(pathNames, IconBasename(item.TryGetProperty("IconUrl", out var ic) ? ic.GetString() : null), listing.Name);
                    IndexPath(pathNames, listing.Name, listing.Name);
                }
                if (page >= pages) break;
            }
            catch { break; }
        }
    }

    private async Task<RatePair> FetchFromNinjaAsync(string league, Dictionary<string, double> flat, Dictionary<string, string> pathNames, double divChaos, double exChaos)
    {
        var leagueParam = Uri.EscapeDataString(league).Replace("%20", "+");
        foreach (var type in NinjaExchangeTypes)
        {
            var url = $"https://poe.ninja/poe2/api/economy/exchange/current/overview?league={leagueParam}&type={type}";
            var rates = await FetchNinjaExchangeApi(url, flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
            divChaos = rates.DivChaos;
            exChaos = rates.ExChaos;
        }
        return await FetchNinjaStashAsync(league, flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
    }

    private async Task<RatePair> FetchNinjaStashAsync(string league, Dictionary<string, double> flat, Dictionary<string, string> pathNames, double divChaos, double exChaos)
    {
        var leagueParam = Uri.EscapeDataString(league).Replace("%20", "+");
        foreach (var type in NinjaStashTypes)
        {
            var url = $"https://poe.ninja/poe2/api/economy/stash/current/item/overview?league={leagueParam}&type={type}";
            exChaos = await FetchNinjaStashApi(url, flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
        }
        return new RatePair(divChaos, exChaos);
    }

    private async Task<RatePair> FetchNinjaExchangeApi(string url, Dictionary<string, double> flat, Dictionary<string, string> pathNames, double divChaos, double exChaos)
    {
        try
        {
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var primary = doc.RootElement.TryGetProperty("core", out var core) && core.TryGetProperty("primary", out var prim)
                ? prim.GetString() ?? "divine" : "divine";
            var idToName = new Dictionary<string, string>();
            var idToIcon = new Dictionary<string, string>();
            if (doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (id == null) continue;
                    if (item.TryGetProperty("name", out var nm)) idToName[id] = nm.GetString() ?? "";
                    var icon = item.TryGetProperty("image", out var im) ? im.GetString() : item.TryGetProperty("icon", out var ic) ? ic.GetString() : null;
                    if (!string.IsNullOrEmpty(icon)) idToIcon[id] = icon;
                }
            }
            if (!doc.RootElement.TryGetProperty("lines", out var lines)) return new RatePair(divChaos, exChaos);
            foreach (var line in lines.EnumerateArray())
            {
                var id = line.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
                if (id == null || !idToName.TryGetValue(id, out var name)) continue;
                var pval = line.TryGetProperty("primaryValue", out var pv) && pv.TryGetDouble(out var pd) ? pd : 0;
                if (pval <= 0) continue;
                var chaos = PrimaryToChaos(pval, primary, divChaos, exChaos);
                AddFlat(flat, name, chaos);
                if (idToIcon.TryGetValue(id, out var iconUrl))
                    IndexPath(pathNames, IconBasename(iconUrl), name);
                if (name.Contains("Divine", StringComparison.OrdinalIgnoreCase)) divChaos = chaos;
                if (name.Contains("Exalted", StringComparison.OrdinalIgnoreCase)) exChaos = chaos;
            }
        }
        catch { }
        return new RatePair(divChaos, exChaos);
    }

    private async Task<double> FetchNinjaStashApi(string url, Dictionary<string, double> flat, Dictionary<string, string> pathNames, double divChaos, double exChaos)
    {
        try
        {
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var primary = doc.RootElement.TryGetProperty("core", out var core) && core.TryGetProperty("primary", out var prim)
                ? prim.GetString() ?? "exalted" : "exalted";
            if (!doc.RootElement.TryGetProperty("lines", out var lines)) return exChaos;
            foreach (var line in lines.EnumerateArray())
            {
                var name = line.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                var baseType = line.TryGetProperty("baseType", out var bt) ? bt.GetString() ?? "" : "";
                var pval = line.TryGetProperty("primaryValue", out var pv) && pv.TryGetDouble(out var pd) ? pd : 0;
                if (string.IsNullOrEmpty(name) || pval <= 0) continue;
                var chaos = PrimaryToChaos(pval, primary, divChaos, exChaos);
                var key = BuildStashKey(name, baseType);
                AddFlat(flat, key, chaos);
                var icon = line.TryGetProperty("icon", out var ic) ? ic.GetString() : line.TryGetProperty("image", out var im) ? im.GetString() : null;
                IndexPath(pathNames, IconBasename(icon), name);
            }
        }
        catch { }
        return exChaos;
    }

    private static double PrimaryToChaos(double value, string primary, double divChaos, double exChaos)
        => primary.Equals("divine", StringComparison.OrdinalIgnoreCase)
            ? value * (divChaos > 0 ? divChaos : 1.0)
            : value * (exChaos > 0 ? exChaos : 0.1);

    private static string BuildStashKey(string name, string baseType)
    {
        if (baseType.Contains("Runeforged", StringComparison.OrdinalIgnoreCase)) return $"{name} Runeforged";
        if (baseType.Contains("Runemastered", StringComparison.OrdinalIgnoreCase)) return $"{name} Runemastered";
        return name;
    }

    private static void AddFlat(Dictionary<string, double> flat, string? key, double price)
    {
        if (string.IsNullOrWhiteSpace(key) || price <= 0) return;
        var norm = RitualPriceLookup.NormalizeKey(key);
        if (!flat.TryGetValue(norm, out var cur) || cur < price) flat[norm] = price;
    }

    private static void AddUnique(Dictionary<string, List<UniqueListing>> uniques, UniqueListing listing)
    {
        if (string.IsNullOrWhiteSpace(listing.Name)) return;
        void add(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            var norm = RitualPriceLookup.NormalizeKey(key);
            if (!uniques.TryGetValue(norm, out var list)) { list = new(); uniques[norm] = list; }
            list.Add(listing);
        }
        add(listing.Name);
        add(listing.Text);
        if (!string.IsNullOrWhiteSpace(listing.BaseType))
            add($"{listing.Name} {listing.BaseType}");
    }

    private static void IndexPath(Dictionary<string, string> pathNames, string? basename, string? display)
    {
        if (string.IsNullOrWhiteSpace(basename) || string.IsNullOrWhiteSpace(display)) return;
        pathNames[RitualPriceLookup.NormalizeKey(basename)] = display.Trim();
    }

    private static string IconBasename(string? iconUrl)
    {
        if (string.IsNullOrWhiteSpace(iconUrl)) return "";
        var noQuery = iconUrl.Split('?')[0];
        var file = noQuery.Split('/').LastOrDefault() ?? "";
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    private static List<string> ReadStringArray(JsonElement parent, string prop)
    {
        var list = new List<string>();
        if (!parent.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var el in arr.EnumerateArray())
        {
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
        }
        return list;
    }

    private void TryLoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) { Status = "no cache; will fetch"; return; }
            var dto = JsonSerializer.Deserialize<CacheDto>(File.ReadAllText(_cachePath), Json);
            if (dto == null || dto.CacheVersion != CacheSchemaVersion) return;
            if (_leagueOverride != null && !string.Equals(dto.League, _leagueOverride, StringComparison.OrdinalIgnoreCase)) return;
            if (dto.PriceSource != _priceSource) return;
            _flatChaos = dto.FlatPricesChaos ?? new(StringComparer.OrdinalIgnoreCase);
            _uniqueListings = dto.UniqueListings ?? new(StringComparer.OrdinalIgnoreCase);
            _pathNames = dto.PathBasenameToItemName ?? new(StringComparer.OrdinalIgnoreCase);
            _league = dto.League;
            _lastFetchUtc = dto.LastFetchUtc;
            if (dto.ChaosPerDivine > 0) ChaosPerDivine = dto.ChaosPerDivine;
            if (dto.ChaosPerExalted > 0) ChaosPerExalted = dto.ChaosPerExalted;
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
            CacheDto dto;
            lock (_gate)
            {
                dto = new CacheDto
                {
                    CacheVersion = CacheSchemaVersion,
                    PriceSource = _priceSource,
                    League = _league,
                    LastFetchUtc = _lastFetchUtc,
                    ChaosPerDivine = ChaosPerDivine,
                    ChaosPerExalted = ChaosPerExalted,
                    FlatPricesChaos = new(_flatChaos),
                    UniqueListings = new(_uniqueListings),
                    PathBasenameToItemName = new(_pathNames),
                };
            }
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(dto, Json));
        }
        catch { }
    }

    private sealed class UniqueListing
    {
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public string BaseType { get; set; } = "";
        public double PriceChaos { get; set; }
        public List<string> ExplicitMods { get; set; } = new();
    }

    private sealed class CacheDto
    {
        public int CacheVersion { get; set; }
        public int PriceSource { get; set; }
        public string League { get; set; } = "";
        public DateTime LastFetchUtc { get; set; }
        public double ChaosPerDivine { get; set; }
        public double ChaosPerExalted { get; set; }
        public Dictionary<string, double>? FlatPricesChaos { get; set; }
        public Dictionary<string, List<UniqueListing>>? UniqueListings { get; set; }
        public Dictionary<string, string>? PathBasenameToItemName { get; set; }
    }
}
