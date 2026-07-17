using System.Text.Json;

namespace POE2Radar.Overlay.Pricing;

public sealed class RunecraftRecipeCatalog
{
    private sealed class RecipeFile
    {
        public Dictionary<string, string>? runes { get; set; }
        public List<RecipeRow>? recipes { get; set; }
        public List<RuneWeightRow>? runeWeights { get; set; }
    }

    public sealed class RecipeRow
    {
        public int row { get; set; }
        public string id { get; set; } = "";
        public int size { get; set; }
        public int category { get; set; }
        public string description { get; set; } = "";
        public List<int>? runeIdx { get; set; }
        public List<string>? runes { get; set; }
        public RewardRow? reward { get; set; }
        public int rewardCount { get; set; }
        public int minLevel { get; set; }
        public int maxLevel { get; set; } = 100;
    }

    public sealed class RewardRow
    {
        public int idx { get; set; }
        public string id { get; set; } = "";
        public string name { get; set; } = "";
    }

    private sealed class RuneWeightRow
    {
        public int rune { get; set; }
        public int pos { get; set; }
        public int size { get; set; }
        public int minLevel { get; set; }
    }

    public sealed class Candidate
    {
        public string Reward = "";
        public int Count;
        public int Size;
        public double UnitEx;
        public bool Priced;
        public string Runes = "";
        public string RewardId = "";
        public string MetaId = "";
    }

    public sealed class MonolithView
    {
        public nint DeviceAddress;
        public double Distance;
        public System.Numerics.Vector2 Grid;
        public float TerrainHeight;
        public double Best;
        public int HoleCount;
        public int AnchorIdx = -1;
        public int AnchorPos;
        public string AnchorName = "?";
        public bool IsUnique;
        public bool IsRerolled;
        public bool PanelOpen;
        public string SelectedRecipeId = "";
        public List<Candidate> Candidates = new();
    }

    private List<RecipeRow> _recipes = new();
    private Dictionary<int, string> _runeNames = new();
    private Dictionary<long, int> _partialMinLevel = new();
    private Dictionary<string, string> _metaIdToEnglish = new(StringComparer.Ordinal);
    private bool _loadTried;

    public bool IsLoaded => _recipes.Count > 0;
    public IReadOnlyDictionary<string, string> MetaIdToEnglish => _metaIdToEnglish;

    public bool TryLoad(string path)
    {
        if (_recipes.Count > 0) return true;
        if (_loadTried) return false;
        _loadTried = true;
        if (!File.Exists(path)) return false;

        try
        {
            var file = JsonSerializer.Deserialize<RecipeFile>(File.ReadAllText(path));
            if (file?.recipes == null || file.recipes.Count == 0) return false;
            _recipes = file.recipes;
            _runeNames = new Dictionary<int, string>();
            if (file.runes != null)
            {
                foreach (var kv in file.runes)
                {
                    if (int.TryParse(kv.Key, out var k))
                        _runeNames[k] = kv.Value;
                }
            }

            _partialMinLevel = new Dictionary<long, int>();
            if (file.runeWeights != null)
            {
                foreach (var w in file.runeWeights)
                {
                    long key = PartialKey(w.rune, w.pos, w.size);
                    if (!_partialMinLevel.TryGetValue(key, out var cur) || w.minLevel < cur)
                        _partialMinLevel[key] = w.minLevel;
                }
            }

            BuildMetaIdToEnglish();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void BuildMetaIdToEnglish()
    {
        if (_metaIdToEnglish.Count > 0) return;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rec in _recipes)
        {
            var id = rec.reward?.id;
            var nm = rec.reward?.name;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(nm)) continue;
            var key = RunecraftPriceMath.LastMetaSegment(id);
            if (key.Length > 0) map[key] = nm;
        }

        if (map.Count > 0) _metaIdToEnglish = map;
    }

    public string? EnglishNameForMetaId(string metaId)
    {
        if (string.IsNullOrEmpty(metaId)) return null;
        return _metaIdToEnglish.TryGetValue(metaId, out var eng) ? eng : null;
    }

    public void BuildCandidates(
        MonolithView view,
        int areaLevel,
        Func<RecipeRow, double> unitPrice)
    {
        view.Candidates.Clear();
        if (view.IsRerolled && !string.IsNullOrEmpty(view.SelectedRecipeId))
        {
            var sel = _recipes.Find(r => string.Equals(r.id, view.SelectedRecipeId, StringComparison.Ordinal));
            if (sel != null) AddCandidate(view, sel, unitPrice);
        }
        else if (view.IsUnique)
        {
            BuildUnique(view, areaLevel, unitPrice);
        }
        else if (view.AnchorIdx >= 0 && view.AnchorPos >= 0 && view.HoleCount > 0)
        {
            BuildAnchored(view, areaLevel, unitPrice);
        }

        view.Candidates.Sort((a, b) => (b.UnitEx * b.Count).CompareTo(a.UnitEx * a.Count));
        view.Best = 0;
        foreach (var c in view.Candidates)
            if (c.Priced) view.Best = Math.Max(view.Best, c.UnitEx * c.Count);
    }

    private void BuildAnchored(MonolithView view, int areaLevel, Func<RecipeRow, double> unitPrice)
    {
        foreach (var rec in _recipes)
        {
            if (rec.runeIdx == null || rec.runeIdx.Count <= view.AnchorPos) continue;
            if (rec.size > view.HoleCount) continue;
            if (rec.runeIdx[view.AnchorPos] != view.AnchorIdx) continue;
            if (areaLevel > 0 && rec.maxLevel > 0 &&
                (areaLevel < rec.minLevel || areaLevel > rec.maxLevel)) continue;
            if (rec.size != view.HoleCount &&
                !IsPartialAllowed(view.AnchorIdx, view.AnchorPos, rec.size, areaLevel)) continue;
            AddCandidate(view, rec, unitPrice);
        }
    }

    private void BuildUnique(MonolithView view, int areaLevel, Func<RecipeRow, double> unitPrice)
    {
        foreach (var rec in _recipes)
        {
            if (rec.size > view.HoleCount) continue;
            if (areaLevel > 0 && rec.maxLevel > 0 &&
                (areaLevel < rec.minLevel || areaLevel > rec.maxLevel)) continue;
            AddCandidate(view, rec, unitPrice);
        }
    }

    private void AddCandidate(MonolithView view, RecipeRow rec, Func<RecipeRow, double> unitPrice)
    {
        var unit = unitPrice(rec);
        var metaId = RunecraftPriceMath.LastMetaSegment(rec.reward?.id ?? "");
        var reward = rec.reward?.name;
        if (string.IsNullOrWhiteSpace(reward))
            reward = string.IsNullOrWhiteSpace(rec.description) ? $"(unique) {rec.id}" : rec.description;
        view.Candidates.Add(new Candidate
        {
            Reward = reward,
            Count = Math.Max(1, rec.rewardCount),
            Size = rec.size,
            UnitEx = unit,
            Priced = unit > 0,
            Runes = rec.runes != null ? string.Join(" · ", rec.runes) : "",
            RewardId = rec.reward?.id ?? "",
            MetaId = metaId,
        });
    }

    private bool IsPartialAllowed(int anchorRune, int anchorPos0, int size, int areaLevel)
    {
        if (!_partialMinLevel.TryGetValue(PartialKey(anchorRune, anchorPos0 + 1, size), out var minLvl))
            return false;
        return areaLevel <= 0 || areaLevel >= minLvl;
    }

    private static long PartialKey(int rune, int pos1Based, int size)
        => ((long)rune << 32) | ((long)pos1Based << 16) | (uint)size;

    public string RuneName(int index)
        => _runeNames.TryGetValue(index, out var nm) ? nm : $"#{index}";
}
