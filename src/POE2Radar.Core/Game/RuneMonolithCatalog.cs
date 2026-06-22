using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace POE2Radar.Core.Game;

/// <summary>
/// Offline runeshape-monolith recipe catalog decoded from Expedition2 dat tables.
/// Pure logic — pairs with <see cref="Poe2Live.ReadMonolith"/> for live state.
/// </summary>
public sealed class RuneMonolithCatalog
{
    private readonly List<Recipe> _recipes;
    private readonly Dictionary<int, string> _runeNames;
    private readonly Dictionary<long, int> _partialMinLevel;

    private RuneMonolithCatalog(List<Recipe> recipes, Dictionary<int, string> runeNames, Dictionary<long, int> partial)
    {
        _recipes = recipes;
        _runeNames = runeNames;
        _partialMinLevel = partial;
    }

    public bool IsLoaded => _recipes.Count > 0;

    public string RuneName(int idx) => _runeNames.TryGetValue(idx, out var n) ? n : $"#{idx}";

    public readonly record struct Offer(string Name, int Count, int Size, string Runes, string Description);

    public List<Offer> Offers(int anchorIdx, int anchorPos, int holeCount, bool isUnique, int areaLevel)
    {
        var result = new List<Offer>();
        if (holeCount <= 0) return result;

        foreach (var rec in _recipes)
        {
            if (rec.size > holeCount) continue;
            if (areaLevel > 0 && rec.maxLevel > 0 && (areaLevel < rec.minLevel || areaLevel > rec.maxLevel)) continue;

            if (isUnique || anchorIdx < 0)
            {
                result.Add(ToOffer(rec));
                continue;
            }

            if (rec.runeIdx is null || rec.runeIdx.Count <= anchorPos) continue;
            if (rec.runeIdx[anchorPos] != anchorIdx) continue;
            if (rec.size == holeCount || IsPartialAllowed(anchorIdx, anchorPos, rec.size, areaLevel))
                result.Add(ToOffer(rec));
        }
        return result;
    }

    private Offer ToOffer(Recipe rec) => new(
        rec.reward?.name ?? string.Empty,
        Math.Max(1, rec.rewardCount),
        rec.size,
        rec.runes is null ? string.Empty : string.Join(" · ", rec.runes),
        rec.description ?? string.Empty);

    private bool IsPartialAllowed(int idx, int pos, int size, int areaLevel)
    {
        if (!_partialMinLevel.TryGetValue(PartialKey(idx, pos + 1, size), out var minL)) return false;
        return areaLevel <= 0 || areaLevel >= minL;
    }

    private static long PartialKey(int rune, int pos1Based, int size)
        => ((long)rune << 16) | ((long)pos1Based << 8) | (uint)size;

    private static RuneMonolithCatalog? _instance;
    public static RuneMonolithCatalog Instance => _instance ??= LoadEmbedded();

    private static RuneMonolithCatalog LoadEmbedded()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.Contains("expedition2_recipes", StringComparison.Ordinal));
            if (name is null) return Empty();
            using var s = asm.GetManifestResourceStream(name);
            if (s is null) return Empty();
            var file = JsonSerializer.Deserialize<CatalogFile>(s, JsonOpts);
            if (file?.recipes is null) return Empty();

            var runeNames = new Dictionary<int, string>();
            if (file.runes is not null)
                foreach (var kv in file.runes)
                    if (int.TryParse(kv.Key, out var k)) runeNames[k] = kv.Value;

            var partial = new Dictionary<long, int>();
            if (file.runeWeights is not null)
                foreach (var w in file.runeWeights)
                {
                    var key = PartialKey(w.rune, w.pos, w.size);
                    if (!partial.TryGetValue(key, out var cur) || w.minLevel < cur) partial[key] = w.minLevel;
                }

            return new RuneMonolithCatalog(file.recipes, runeNames, partial);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"RuneMonolithCatalog load failed: {ex.Message}");
            return Empty();
        }
    }

    private static RuneMonolithCatalog Empty()
        => new(new List<Recipe>(), new Dictionary<int, string>(), new Dictionary<long, int>());

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class CatalogFile
    {
        public Dictionary<string, string>? runes { get; set; }
        public List<Recipe>? recipes { get; set; }
        public List<RuneWeight>? runeWeights { get; set; }
    }

    private sealed class RuneWeight
    {
        public int rune { get; set; }
        public int pos { get; set; }
        public int size { get; set; }
        public int minLevel { get; set; }
    }

    private sealed class Recipe
    {
        public int size { get; set; }
        public List<int>? runeIdx { get; set; }
        public List<string>? runes { get; set; }
        public Reward? reward { get; set; }
        public int rewardCount { get; set; }
        public string? description { get; set; }
        public int minLevel { get; set; }
        public int maxLevel { get; set; }
    }

    private sealed class Reward
    {
        public string name { get; set; } = string.Empty;
    }
}
