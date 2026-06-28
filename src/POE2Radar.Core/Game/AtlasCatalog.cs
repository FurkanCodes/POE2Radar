using System.Reflection;
using System.Text.Json;

namespace POE2Radar.Core.Game;

/// <summary>
/// Atlas display/catalog metadata. Map codes come from embedded <c>world_areas.json</c>; content/biome
/// chip colors and translations are loaded from GH-compatible embedded JSON under <c>Resources/</c>.
/// </summary>
public sealed class AtlasCatalog
{
    public readonly record struct MapInfo(string Code, string Name, string Type, string[] Tags, string Group);
    public readonly record struct BiomeInfo(byte Id, string Name, string Color, float Alpha, bool Show);
    public readonly record struct ContentInfo(
        string Key, string Label, string Abbrev,
        byte BgR, byte BgG, byte BgB, float BgA,
        byte FgR, byte FgG, byte FgB, float FgA,
        string Description, bool IsFlag, bool Show);
    public readonly record struct MapGroupInfo(string Name, string Color, string FontColor, string[] Maps);
    public readonly record struct RouteTargetInfo(string Name, string Match, string Color, int MaxHops, bool Enabled);
    public readonly record struct MapContentMeta(string Name, string? IconBasename, string Description);

    private readonly Dictionary<string, MapInfo> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ContentInfo> _contentByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ContentInfo> _contentByLabel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MapContentMeta> _mapContent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, (string Name, string Desc)>> _mapContentTranslations = new(StringComparer.OrdinalIgnoreCase);

    public static AtlasCatalog Shared { get; } = Load();

    public IReadOnlyList<BiomeInfo> Biomes { get; private set; } = Array.Empty<BiomeInfo>();
    public IReadOnlyList<MapGroupInfo> DefaultMapGroups { get; private init; } = Array.Empty<MapGroupInfo>();
    public IReadOnlyList<RouteTargetInfo> DefaultRouteTargets { get; private init; } = Array.Empty<RouteTargetInfo>();
    public IReadOnlyCollection<MapInfo> Maps => _maps.Values;
    public IReadOnlyCollection<ContentInfo> Content => _contentByKey.Values;

    public MapInfo? Map(string code)
        => !string.IsNullOrWhiteSpace(code) && _maps.TryGetValue(code, out var m) ? m : null;

    public string MapName(string code)
        => Map(code)?.Name ?? PrettifyMapCode(code);

    public BiomeInfo Biome(byte id)
    {
        foreach (var b in Biomes)
            if (b.Id == id) return b;
        return new BiomeInfo(id, "Unknown", "#8a93a0", 0.9f, true);
    }

    public ContentInfo? ContentInfoFor(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (_contentByKey.TryGetValue(key, out var exact)) return exact;
        var norm = NormalizeName(key);
        if (_contentByLabel.TryGetValue(norm, out var byLabel)) return byLabel;
        foreach (var c in _contentByKey.Values)
        {
            if (key.Contains(c.Key, StringComparison.OrdinalIgnoreCase)
                || key.Contains(c.Label, StringComparison.OrdinalIgnoreCase)
                || (c.Abbrev.Length > 1 && key.Contains(c.Abbrev, StringComparison.OrdinalIgnoreCase)))
                return c;
        }
        return null;
    }

    public void CategorizeContentTags(
        IEnumerable<string> rawNames,
        out List<ContentInfo> flags,
        out List<ContentInfo> contents)
    {
        flags = [];
        contents = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rawNames)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var info = ContentInfoFor(raw);
            if (info is null || !info.Value.Show) continue;
            var ci = info.Value;
            if (!seen.Add(ci.Key)) continue;
            if (ci.IsFlag) flags.Add(ci);
            else contents.Add(ci);
        }
    }

    public string LocalizedMapName(string code, string language)
        => MapName(code);

    public string LocalizedContentName(string contentName, string language)
    {
        if (string.IsNullOrWhiteSpace(contentName)) return contentName;
        var lang = NormalizeLanguage(language);
        if (_mapContentTranslations.TryGetValue(contentName, out var langs)
            && langs.TryGetValue(lang, out var tr)
            && !string.IsNullOrWhiteSpace(tr.Name))
            return tr.Name;
        if (_mapContent.TryGetValue(contentName, out var meta) && !string.IsNullOrWhiteSpace(meta.Name))
            return meta.Name;
        return contentName;
    }

    public string? LocalizedContentDescription(string contentName, string language)
    {
        if (string.IsNullOrWhiteSpace(contentName)) return null;
        var lang = NormalizeLanguage(language);
        if (_mapContentTranslations.TryGetValue(contentName, out var langs)
            && langs.TryGetValue(lang, out var tr)
            && !string.IsNullOrWhiteSpace(tr.Desc))
            return tr.Desc;
        if (_mapContent.TryGetValue(contentName, out var meta) && !string.IsNullOrWhiteSpace(meta.Description))
            return meta.Description;
        return null;
    }

    public string? ContentIconBasename(string contentName)
        => _mapContent.TryGetValue(contentName, out var meta) ? meta.IconBasename : null;

    private static AtlasCatalog Load()
    {
        var cat = new AtlasCatalog
        {
            Biomes = LoadBiomesFromResource(),
            DefaultMapGroups = BuildMapGroups(),
            DefaultRouteTargets = BuildRouteTargets(),
        };

        cat.LoadContentJson();
        cat.LoadMapContentJson();

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.Contains("world_areas"));
            if (res != null)
            {
                using var s = asm.GetManifestResourceStream(res);
                if (s != null)
                {
                    var doc = JsonDocument.Parse(s);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (!prop.Name.StartsWith("Map", StringComparison.Ordinal)) continue;
                        var v = prop.Value;
                        var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var info = BuildMapInfo(prop.Name, string.IsNullOrWhiteSpace(name) ? PrettifyMapCode(prop.Name) : name);
                        cat._maps[prop.Name] = info;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AtlasCatalog world_areas load failed: {ex.Message}");
        }

        return cat;
    }

    private void LoadContentJson()
    {
        try
        {
            using var s = OpenResource("atlas_content.json");
            if (s is null) return;
            var doc = JsonDocument.Parse(s);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var v = prop.Value;
                var label = v.TryGetProperty("Label", out var l) ? l.GetString() ?? prop.Name : prop.Name;
                var abbrev = v.TryGetProperty("Abbrev", out var a) ? a.GetString() ?? label[..Math.Min(1, label.Length)] : label[..Math.Min(1, label.Length)];
                var isFlag = v.TryGetProperty("IsFlag", out var f) && f.GetBoolean();
                var show = !v.TryGetProperty("Show", out var sh) || sh.GetBoolean();
                ParseColor(v, "BackgroundColor", out var bgR, out var bgG, out var bgB, out var bgA);
                ParseColor(v, "FontColor", out var fgR, out var fgG, out var fgB, out var fgA);
                var info = new ContentInfo(prop.Name, label, abbrev, bgR, bgG, bgB, bgA, fgR, fgG, fgB, fgA, label, isFlag, show);
                _contentByKey[prop.Name] = info;
                _contentByLabel[NormalizeName(label)] = info;
                if (!string.IsNullOrEmpty(abbrev))
                    _contentByLabel[NormalizeName(abbrev)] = info;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AtlasCatalog content load failed: {ex.Message}");
        }
    }

    private void LoadMapContentJson()
    {
        try
        {
            using var s = OpenResource("atlas_mapcontent.json");
            if (s is null) return;
            var doc = JsonDocument.Parse(s);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var v = prop.Value;
                var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                var icon = v.TryGetProperty("icon", out var ic) ? ic.GetString() : null;
                var desc = v.TryGetProperty("desc", out var d) ? d.GetString() ?? "" : "";
                _mapContent[name] = new MapContentMeta(name, icon, desc);

                if (!v.TryGetProperty("translates", out var tr) || tr.ValueKind != JsonValueKind.Object)
                    continue;
                var langMap = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
                foreach (var langProp in tr.EnumerateObject())
                {
                    var lv = langProp.Value;
                    var tName = lv.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "";
                    var tDesc = lv.TryGetProperty("desc", out var td) ? td.GetString() ?? "" : "";
                    langMap[langProp.Name] = (tName, tDesc);
                }
                _mapContentTranslations[name] = langMap;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AtlasCatalog mapcontent load failed: {ex.Message}");
        }
    }

    private static BiomeInfo[] LoadBiomesFromResource()
    {
        try
        {
            using var s = OpenStaticResource("atlas_biome.json");
            if (s is null) return BuildBiomesFallback();
            var doc = JsonDocument.Parse(s);
            var list = new List<BiomeInfo>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!byte.TryParse(prop.Name, out var id)) continue;
                var v = prop.Value;
                var label = v.TryGetProperty("Label", out var l) ? l.GetString() ?? "Unknown" : "Unknown";
                var show = !v.TryGetProperty("Show", out var sh) || sh.GetBoolean();
                ParseColor(v, "BorderColor", out var r, out var g, out var b, out var a);
                list.Add(new BiomeInfo(id, label, RgbToHex(r, g, b), a, show));
            }
            return list.Count > 0 ? list.ToArray() : BuildBiomesFallback();
        }
        catch
        {
            return BuildBiomesFallback();
        }
    }

    private static BiomeInfo[] BuildBiomesFallback() =>
    [
        new(0, "Water", "#5FA5F8", 0.9f, true),
        new(1, "Mountain", "#FDF4E3", 0.9f, true),
        new(2, "Grass", "#33DE00", 0.9f, true),
        new(3, "Forest", "#196B23", 0.9f, true),
        new(4, "Swamp", "#494847", 0.9f, true),
        new(5, "Desert", "#FBC561", 0.9f, true),
        new(6, "Ezomyte City", "#F66161", 0.9f, true),
        new(7, "Faridun City", "#F66161", 0.9f, true),
        new(8, "Vaal City", "#F66161", 0.9f, true),
        new(9, "Breach City", "#991ACC", 0.9f, true),
        new(10, "Ocean", "#044782", 0.9f, true),
        new(11, "Island", "#479687", 0.9f, true),
        new(12, "Oriath City", "#F66161", 0.9f, true),
    ];

    private Stream? OpenResource(string fileName) => OpenStaticResource(fileName);

    private static Stream? OpenStaticResource(string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        return res != null ? asm.GetManifestResourceStream(res) : null;
    }

    private static void ParseColor(JsonElement parent, string propName, out byte r, out byte g, out byte b, out float a)
    {
        r = g = b = 0;
        a = 1f;
        if (!parent.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        var i = 0;
        float rf = 0, gf = 0, bf = 0, af = 1f;
        foreach (var el in arr.EnumerateArray())
        {
            var v = (float)el.GetDouble();
            switch (i++)
            {
                case 0: rf = v; break;
                case 1: gf = v; break;
                case 2: bf = v; break;
                case 3: af = v; break;
            }
        }
        r = (byte)Math.Clamp(rf * 255f, 0, 255);
        g = (byte)Math.Clamp(gf * 255f, 0, 255);
        b = (byte)Math.Clamp(bf * 255f, 0, 255);
        a = Math.Clamp(af, 0f, 1f);
    }

    private static string RgbToHex(byte r, byte g, byte b)
        => $"#{r:X2}{g:X2}{b:X2}";

    private static string NormalizeName(string s)
        => s.Trim().ToLowerInvariant();

    private static string NormalizeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "english";
        return language.Trim().ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "chinese" => "chinese",
            "zh-tw" or "traditional chinese" => "traditional chinese",
            _ => language.Trim().ToLowerInvariant(),
        };
    }

    private static MapInfo BuildMapInfo(string code, string name)
    {
        var tags = new List<string>();
        var group = "map";
        var type = code.Contains("Unique", StringComparison.OrdinalIgnoreCase) ? "unique" : "normal";

        if (name.Contains("Citadel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Patriarch Halls", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Matriarch Halls", StringComparison.OrdinalIgnoreCase)
            || code.Contains("UberBoss", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("arbiter");
            group = "arbiter";
        }
        if (name.Contains("Jade Citadel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Derelict Mansion", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cavern City", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Vaal Vault", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("lineage");
            group = "lineage";
        }
        if (name.Contains("Citadel", StringComparison.OrdinalIgnoreCase)) group = "citadel";
        else if (name.Contains("Halls", StringComparison.OrdinalIgnoreCase)) group = "halls";
        else if (name.Contains("Enigma", StringComparison.OrdinalIgnoreCase)) group = "enigma";
        else if (name.Contains("Fortress", StringComparison.OrdinalIgnoreCase)) group = "fortress";
        else if (type == "unique") group = "unique";

        return new MapInfo(code, name, type, tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), group);
    }

    private static MapGroupInfo[] BuildMapGroups() =>
    [
        new("Expedition unique", "#000000", "#0000FF", ["Moor of Fallen Skies"]),
        new("Expedition bosses", "#000000", "#0000FF", ["Sprawling Jungle", "Secluded Temple", "Obscure Island", "Mournful Cliffside"]),
        new("Unique maps low tier", "#000000", "#F97405", ["The Fractured Lake", "The Ezomyte Megaliths", "Merchant's Campsite", "Jado's Campsite",
            "Moment of Zen", "The Voyage", "The Silent Cave", "Vaults of Kamasa", "The Viridian Wildwood"]),
        new("Unique maps top tier", "#CECF59", "#E9E9E9", ["Castaway", "Untainted Paradise"]),
        new("Citadels", "#626265", "#FFF400", ["The Copper Citadel", "The Iron Citadel", "The Stone Citadel"]),
        new("Halls", "#626265", "#FFF400", ["The Matriarch Halls", "The Patriarch Halls"]),
        new("Anomaly maps", "#0E3614", "#FFFFFF", ["The Jade Isles", "Sealed Vault", "Sacred Reservoir", "Derelict Mansion"]),
    ];

    private static RouteTargetInfo[] BuildRouteTargets() =>
    [
        new("Stone Citadel", "map:Stone Citadel", "#FFF164", 25, true),
        new("Iron Citadel", "map:Iron Citadel", "#FFF164", 25, true),
        new("Copper Citadel", "map:Copper Citadel", "#FFF164", 25, true),
        new("Patriarch Halls", "map:Patriarch Halls", "#FFF164", 25, true),
        new("Matriarch Halls", "map:Matriarch Halls", "#FFF164", 25, true),
        new("Jade Citadel", "map:Jade Citadel", "#048E3B", 25, true),
        new("Derelict Mansion", "map:Derelict Mansion", "#048E3B", 25, true),
        new("Cavern City", "map:Cavern City", "#048E3B", 25, true),
        new("Vaal Vault", "map:Vaal Vault", "#048E3B", 25, true),
        new("Untainted Paradise", "map:Untainted Paradise", "#ff9e42", 25, false),
        new("Castaway", "map:Castaway", "#ff9e42", 25, false),
        new("Moor of Fallen Skies", "map:Moor of Fallen Skies", "#e0533a", 25, false),
        new("All unique maps", "type:unique", "#FF00FF", 0, false),
        new("Lineage maps", "tag:lineage", "#00E000", 0, false),
        new("Arbiter maps", "tag:arbiter", "#FF0000", 0, false),
    ];

    public static string PrettifyMapCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var s = code.StartsWith("Map", StringComparison.Ordinal) ? code[3..] : code;
        if (s.StartsWith("UberBoss_", StringComparison.Ordinal)) s = s["UberBoss_".Length..];
        if (s.StartsWith("Unique", StringComparison.Ordinal)) s = s["Unique".Length..];
        return System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1 $2").Replace('_', ' ').Trim();
    }
}
