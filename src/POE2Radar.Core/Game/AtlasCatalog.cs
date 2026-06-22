using System.Reflection;
using System.Text.Json;

namespace POE2Radar.Core.Game;

/// <summary>
/// Atlas display/catalog metadata adapted to POE2Radar's data model. This intentionally keeps the
/// catalog native to this repository: map names come from the embedded <c>world_areas.json</c>, while
/// atlas-specific groups, biomes, content badges, and route targets are curated here.
/// </summary>
public sealed class AtlasCatalog
{
    public readonly record struct MapInfo(string Code, string Name, string Type, string[] Tags, string Group);
    public readonly record struct BiomeInfo(byte Id, string Name, string Color, bool Show);
    public readonly record struct ContentInfo(string Key, string Label, string ShortLabel, string Color, string Description, bool Flag);
    public readonly record struct MapGroupInfo(string Name, string Color, string FontColor, string[] Maps);
    public readonly record struct RouteTargetInfo(string Name, string Match, string Color, int MaxHops, bool Enabled);

    private readonly Dictionary<string, MapInfo> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ContentInfo> _content = new(StringComparer.OrdinalIgnoreCase);

    public static AtlasCatalog Shared { get; } = Load();

    public IReadOnlyList<BiomeInfo> Biomes { get; private init; } = Array.Empty<BiomeInfo>();
    public IReadOnlyList<MapGroupInfo> DefaultMapGroups { get; private init; } = Array.Empty<MapGroupInfo>();
    public IReadOnlyList<RouteTargetInfo> DefaultRouteTargets { get; private init; } = Array.Empty<RouteTargetInfo>();
    public IReadOnlyCollection<MapInfo> Maps => _maps.Values;
    public IReadOnlyCollection<ContentInfo> Content => _content.Values;

    public MapInfo? Map(string code)
        => !string.IsNullOrWhiteSpace(code) && _maps.TryGetValue(code, out var m) ? m : null;

    public string MapName(string code)
        => Map(code)?.Name ?? PrettifyMapCode(code);

    public ContentInfo? ContentInfoFor(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (_content.TryGetValue(key, out var exact)) return exact;
        foreach (var c in _content.Values)
            if (key.Contains(c.Key, StringComparison.OrdinalIgnoreCase) || key.Contains(c.Label, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }

    public string LocalizedMapName(string code, string language)
    {
        // world_areas.json currently stores English names only. Keep the language argument in the public
        // shape so adding translated catalogs later does not change callers.
        return MapName(code);
    }

    private static AtlasCatalog Load()
    {
        var cat = new AtlasCatalog
        {
            Biomes = BuildBiomes(),
            DefaultMapGroups = BuildMapGroups(),
            DefaultRouteTargets = BuildRouteTargets(),
        };

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
            Console.Error.WriteLine($"AtlasCatalog load failed: {ex.Message}");
        }

        foreach (var c in BuildContent())
            cat._content[c.Key] = c;
        return cat;
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

    private static BiomeInfo[] BuildBiomes() =>
    [
        new(0, "Unknown", "#8a93a0", true),
        new(1, "Grass", "#66d17a", true),
        new(2, "Sand", "#d7b46a", true),
        new(3, "Swamp", "#55a37b", true),
        new(4, "Forest", "#3fb56a", true),
        new(5, "Snow", "#bde7ff", true),
        new(6, "Stone", "#a7a7a7", true),
        new(7, "Volcanic", "#e05a3a", true),
        new(8, "Coast", "#58c7d8", true),
        new(9, "Cave", "#9c7a55", true),
        new(10, "Vaal", "#c0395a", true),
        new(11, "Water", "#3ca0ff", true),
        new(12, "Desert", "#d98a2b", true),
    ];

    private static ContentInfo[] BuildContent() =>
    [
        new("Powerful Map Boss", "Powerful Map Boss", "Boss", "#e0533a", "Map contains a powerful boss reward modifier.", true),
        new("Breach", "Breach", "Br", "#7c5cff", "Map contains Breach content.", true),
        new("Delirium", "Delirium", "De", "#b86cff", "Map contains Delirium content.", true),
        new("Ritual", "Ritual", "Ri", "#d946ef", "Map contains Ritual altars.", true),
        new("Expedition", "Expedition", "Ex", "#d98a2b", "Map contains Expedition content.", true),
        new("Abyss", "Abyss", "Ab", "#55a37b", "Map contains Abyss content.", true),
        new("Irradiated", "Irradiated", "+1", "#f6c343", "Map has an irradiated area-level bonus.", true),
        new("Corruption", "Corruption", "Cor", "#c0395a", "Map contains corruption content.", true),
        new("Grand Mirror", "Grand Mirror", "Mir", "#3ca0ff", "Map contains a Grand Mirror.", true),
        new("Vaal Beacons", "Vaal Beacons", "Vaal", "#c0395a", "Map contains Vaal Beacons.", true),
        new("Indomitable Essence", "Indomitable Essence", "Ess", "#58c7d8", "Map contains Essence content.", false),
        new("Strongbox", "Strongbox", "Box", "#2fb6a8", "Map contains strongboxes.", false),
        new("Shrine", "Shrine", "Shr", "#e0b341", "Map contains shrines.", false),
        new("Hideout", "Hideout", "HO", "#8a93a0", "Map can contain a hideout.", true),
    ];

    private static MapGroupInfo[] BuildMapGroups() =>
    [
        new("Expedition unique", "#8b2f2f", "#ffffff", ["Moor of Fallen Skies"]),
        new("Expedition bosses", "#a15d2a", "#ffffff", ["Sprawling Jungle", "Secluded Temple", "Obscure Island", "Mournful Cliffside"]),
        new("Unique maps low tier", "#5943a8", "#ffffff", ["Fractured Lake", "Ezomyte Megaliths", "Moment of Zen", "Silent Cave", "Vaults of Kamasa", "Viridian Wildwood"]),
        new("Unique maps top tier", "#d98a2b", "#ffffff", ["Castaway", "Untainted Paradise"]),
        new("Citadels", "#e0b341", "#1b1400", ["Copper Citadel", "Iron Citadel", "Stone Citadel", "Jade Citadel"]),
        new("Halls", "#d946ef", "#ffffff", ["Matriarch Halls", "Patriarch Halls"]),
        new("Anomaly maps", "#2fb6a8", "#031615", ["Jade Isles", "Sealed Vault", "Sacred Reservoir", "Derelict Mansion"]),
    ];

    private static RouteTargetInfo[] BuildRouteTargets() =>
    [
        new("Stone Citadel", "map:Stone Citadel", "#e0b341", 25, true),
        new("Iron Citadel", "map:Iron Citadel", "#e0b341", 25, true),
        new("Copper Citadel", "map:Copper Citadel", "#e0b341", 25, true),
        new("Patriarch Halls", "map:Patriarch Halls", "#e0b341", 25, true),
        new("Matriarch Halls", "map:Matriarch Halls", "#e0b341", 25, true),
        new("Jade Citadel", "map:Jade Citadel", "#6ee787", 25, true),
        new("Derelict Mansion", "map:Derelict Mansion", "#6ee787", 25, true),
        new("Cavern City", "map:Cavern City", "#6ee787", 25, true),
        new("Vaal Vault", "map:Vaal Vault", "#6ee787", 25, true),
        new("Untainted Paradise", "map:Untainted Paradise", "#ff9e42", 25, false),
        new("Castaway", "map:Castaway", "#ff9e42", 25, false),
        new("Moor of Fallen Skies", "map:Moor of Fallen Skies", "#e0533a", 25, false),
        new("All unique maps", "type:unique", "#d946ef", 0, false),
        new("Lineage maps", "tag:lineage", "#6ee787", 0, false),
        new("Arbiter maps", "tag:arbiter", "#e0b341", 0, false),
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
