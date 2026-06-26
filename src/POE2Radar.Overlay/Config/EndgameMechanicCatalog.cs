using POE2Radar.Core.Game;
using POE2Radar.Overlay.Web;

namespace POE2Radar.Overlay.Config;

/// <summary>Canonical PoE2 endgame / map-mechanic matchers and display defaults (single source of truth).</summary>
public sealed record EndgameMechanicDef(
    string Name,
    string[] Match,
    string[]? Categories,
    string[]? Exclude,
    string Shape,
    string Color,
    float Opacity,
    float Size,
    SpriteIconRef Sprite,
    bool Navigable = true);

public static class EndgameMechanicCatalog
{
    private static readonly EndgameMechanicDef[] Defs = BuildDefs();

    public static IReadOnlyList<EndgameMechanicDef> All => Defs;

    public static IReadOnlyList<string> RuleNames { get; } =
        Defs.Select(d => d.Name).ToArray();

    public static bool TryMatch(Poe2Live.EntityDot e, out EndgameMechanicDef? def)
    {
        foreach (var d in Defs)
        {
            if (Matches(e.Metadata, e.Category, d))
            {
                def = d;
                return true;
            }
        }
        def = null;
        return false;
    }

    public static bool TryMatchMetadata(string metadata, Poe2Live.EntityCategory category, out EndgameMechanicDef? def)
    {
        foreach (var d in Defs)
        {
            if (Matches(metadata, category, d))
            {
                def = d;
                return true;
            }
        }
        def = null;
        return false;
    }

    public static bool Matches(Poe2Live.EntityDot e, EndgameMechanicDef d)
        => Matches(e.Metadata, e.Category, d);

    /// <summary>True when metadata matches any catalog mechanic except Essence.</summary>
    public static bool MatchesNonEssenceMechanic(Poe2Live.EntityDot e)
    {
        foreach (var def in Defs)
        {
            if (string.Equals(def.Name, "Essence", StringComparison.OrdinalIgnoreCase)) continue;
            if (Matches(e.Metadata, e.Category, def)) return true;
        }
        return false;
    }

    public static bool Matches(string metadata, Poe2Live.EntityCategory category, EndgameMechanicDef d)
    {
        if (string.IsNullOrEmpty(metadata)) return false;

        if (d.Exclude is { Length: > 0 })
            foreach (var ex in d.Exclude)
                if (!string.IsNullOrEmpty(ex) && metadata.Contains(ex, StringComparison.OrdinalIgnoreCase))
                    return false;

        if (d.Categories is { Length: > 0 })
        {
            var cat = category.ToString();
            var ok = false;
            foreach (var c in d.Categories)
                if (string.Equals(c, cat, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
            if (!ok) return false;
        }

        foreach (var term in d.Match)
        {
            if (string.IsNullOrEmpty(term)) continue;
            if (metadata.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static DisplayRule ToDisplayRule(EndgameMechanicDef d) => new()
    {
        Name = d.Name,
        Label = d.Name,
        Enabled = true,
        Navigable = d.Navigable,
        Categories = d.Categories is { Length: > 0 } ? new List<string>(d.Categories) : new(),
        Match = new List<string>(d.Match),
        Shape = d.Shape,
        Color = d.Color,
        Opacity = d.Opacity,
        Size = d.Size,
        Sprite = d.Sprite.Clone(),
    };

    public static MechanicStyle ToMechanicStyle(EndgameMechanicDef d) => new()
    {
        Name = d.Name,
        Enabled = true,
        Match = new List<string>(d.Match),
        Categories = d.Categories is { Length: > 0 } ? new List<string>(d.Categories) : new(),
        Shape = d.Shape,
        Color = d.Color,
        Opacity = d.Opacity,
        Size = d.Size,
        Sprite = d.Sprite.Clone(),
    };

    public static void AppendDisplayRules(List<DisplayRule> rules)
    {
        foreach (var d in Defs)
            rules.Add(ToDisplayRule(d));
    }

    public static List<MechanicStyle> DefaultMechanicStyles()
        => Defs.Select(ToMechanicStyle).ToList();

    /// <summary>Merge duplicate rules, insert missing catalog rows, and move mechanic rules before Map marker.</summary>
    public static bool MigrateDisplayRules(List<DisplayRule> rules)
    {
        var changed = false;
        var catalogNames = new HashSet<string>(Defs.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var name in catalogNames)
        {
            var dupes = rules.Where(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (dupes.Count <= 1) continue;

            var primary = dupes[0];
            for (var i = 1; i < dupes.Count; i++)
            {
                var dup = dupes[i];
                foreach (var m in dup.Match)
                    if (!primary.Match.Contains(m, StringComparer.OrdinalIgnoreCase))
                        primary.Match.Add(m);
                rules.Remove(dup);
                changed = true;
            }
        }

        var insertBefore = FindMechanicInsertIndex(rules);

        foreach (var def in Defs)
        {
            var existing = rules.FirstOrDefault(r => string.Equals(r.Name, def.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                rules.Insert(insertBefore, ToDisplayRule(def));
                insertBefore++;
                changed = true;
                continue;
            }

            var canonicalMatch = new List<string>(def.Match);
            if (!existing.Match.SequenceEqual(canonicalMatch, StringComparer.OrdinalIgnoreCase))
            {
                existing.Match = canonicalMatch;
                changed = true;
            }

            var catList = def.Categories is { Length: > 0 } ? new List<string>(def.Categories) : new List<string>();
            if (!existing.Categories.SequenceEqual(catList, StringComparer.OrdinalIgnoreCase))
            {
                existing.Categories = catList;
                changed = true;
            }

            if (existing.Label is not { Length: > 0 })
            {
                existing.Label = def.Name;
                changed = true;
            }

            existing.Sprite ??= def.Sprite.Clone();
            if (existing.Size < def.Size) { existing.Size = def.Size; changed = true; }

            if (existing.Navigable != def.Navigable)
            {
                existing.Navigable = def.Navigable;
                changed = true;
            }
        }

        var mechanicBlock = rules.Where(r => catalogNames.Contains(r.Name)).ToList();
        if (mechanicBlock.Count == 0) return changed;

        var targetIdx = FindMechanicInsertIndex(rules);

        foreach (var r in mechanicBlock)
        {
            var curIdx = rules.IndexOf(r);
            if (curIdx < 0) continue;
            if (curIdx < targetIdx) continue;
            rules.RemoveAt(curIdx);
            if (curIdx < targetIdx) targetIdx--;
            rules.Insert(targetIdx, r);
            targetIdx++;
            changed = true;
        }

        return changed;
    }

    /// <summary>Insert mechanics before category defaults (Boss / rare monsters) and Map marker.</summary>
    private static int FindMechanicInsertIndex(List<DisplayRule> rules)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var n = rules[i].Name;
            if (string.Equals(n, "Boss", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "Monster · Rare", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "Monster · Magic", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "Monster · Normal", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return FindMapMarkerIndex(rules);
    }

    private static int FindMapMarkerIndex(List<DisplayRule> rules)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (string.Equals(r.Name, "Map marker", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Name, "Point of Interest", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return rules.FindIndex(r =>
            string.Equals(r.Poi, "Yes", StringComparison.OrdinalIgnoreCase)
            && r.Categories.Any(c => string.Equals(c, "Object", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(c, "Other", StringComparison.OrdinalIgnoreCase)));
    }

    private static EndgameMechanicDef[] BuildDefs() => new[]
    {
        new EndgameMechanicDef(
            "Expedition",
            ["Expedition2/Expedition2Encounter"],
            ["Other"],
            ["Expedition2EncounterCrack"],
            "Plus", "#26E6D9", 1f, 12f, SpriteCatalog.Expedition(),
            Navigable: true),
        new EndgameMechanicDef(
            "Ritual",
            ["LeagueRitual", "RitualAltar", "Leagues/Ritual", "/Ritual/"],
            ["Object", "Other"],
            null,
            "Star", "#FF3355", 1f, 12f, SpriteCatalog.Ritual(),
            Navigable: true),
        new EndgameMechanicDef(
            "Breach",
            ["Leagues/Breach", "LeagueBreach", "BreachHand", "/Breach/"],
            null,
            null,
            "Diamond", "#A64DFF", 1f, 12f, SpriteCatalog.Breach(),
            Navigable: true),
        new EndgameMechanicDef(
            "Abyss · Pit",
            ["AbyssPit", "AbyssHole", "AbyssJumpInteractable"],
            ["Object", "Other"],
            null,
            "Diamond", "#33CCCC", 1f, 12f, SpriteCatalog.Abyss(),
            Navigable: true),
        new EndgameMechanicDef(
            "Abyss",
            [
                "Leagues/Abyss", "/Abyss/", "AbyssTrove", "AbyssalTrove", "AbyssGate",
                "AbyssFissure", "AbyssStart",
            ],
            ["Object", "Other"],
            ["AbyssCrack", "EssenceOfTheAbyss", "AbyssPit", "AbyssHole", "AbyssJumpInteractable"],
            "Diamond", "#33CCCC", 1f, 12f, SpriteCatalog.Abyss(),
            Navigable: false),
        new EndgameMechanicDef(
            "Delirium",
            ["Delirium", "Simulacrum", "LeagueDelirium", "Leagues/Delirium"],
            null,
            null,
            "Triangle", "#9B59B6", 1f, 12f, SpriteCatalog.Delirium(),
            Navigable: false),
        new EndgameMechanicDef(
            "Ultimatum",
            ["LeagueUltimatum", "/Ultimatum/", "UltimatumAltar"],
            ["Object", "Other"],
            ["ultimatumboss"],
            "Hexagon", "#FF5533", 1f, 12f, SpriteCatalog.Ritual(),
            Navigable: false),
        new EndgameMechanicDef(
            "Corruption",
            ["precursorcorruption", "VaalCorruption", "CorruptionAltar", "TrailOfCorruption"],
            ["Object", "Other"],
            null,
            "Triangle", "#66CC33", 1f, 12f, SpriteCatalog.Delirium(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Unique",
            [
                "uniquevaalstrongbox", "uniqueezomytegrave", "uniqueetchedbeetle",
                "ventorstrongbox", "beaststrongbox", "uniqueventorsmysterybox",
            ],
            ["Chest"],
            null,
            "Star", "#FFD700", 1f, 12f, SpriteCatalog.ChestUnique(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Landmark",
            ["landmarkstrongbox"],
            ["Chest"],
            null,
            "Triangle", "#FF7043", 1f, 12f, SpriteCatalog.Landmark(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Cartographer",
            ["cartographer"],
            ["Chest"],
            null,
            "Exclamation", "#5C9EFF", 1f, 12f, SpriteCatalog.MapMarker(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Arcane",
            ["arcanist"],
            ["Chest"],
            null,
            "Diamond", "#A64DFF", 1f, 11f, SpriteCatalog.ChestRare(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Armourer",
            ["armory"],
            ["Chest"],
            null,
            "Shield", "#8899AA", 1f, 11f, SpriteCatalog.Strongbox(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Jeweller",
            ["gemcutter", "gemstrongbox"],
            ["Chest"],
            null,
            "Gem", "#FF66CC", 1f, 11f, SpriteCatalog.ChestRare(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Divination",
            ["strongboxdivination", "strongboxdivinationshaper"],
            ["Chest"],
            null,
            "Exclamation", "#CC9966", 1f, 11f, SpriteCatalog.MapMarker(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Expedition",
            ["strongboxexpedition"],
            ["Chest"],
            null,
            "Square", "#1FA89A", 1f, 11f, SpriteCatalog.Expedition(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Researcher",
            ["strongboxes/strongbox"],
            ["Chest"],
            [
                "landmarkstrongbox", "cartographer", "unique", "beaststrongbox",
                "ventorstrongbox", "abyssstrongbox", "arcanist", "armory",
                "gemcutter", "gemstrongbox", "strongboxdivination", "strongboxexpedition",
            ],
            "Gem", "#7DFF7D", 1f, 11f, SpriteCatalog.ChestRare(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox · Abyss",
            ["abyssstrongbox"],
            ["Chest"],
            null,
            "Diamond", "#33CCCC", 1f, 11f, SpriteCatalog.Strongbox(),
            Navigable: false),
        new EndgameMechanicDef(
            "Strongbox",
            ["StrongBoxes"],
            ["Chest"],
            null,
            "Square", "#FFB300", 1f, 10f, SpriteCatalog.Strongbox(),
            Navigable: false),
        new EndgameMechanicDef(
            "Essence",
            [
                "fireessencemod", "coldessencemod", "lifeessencemod", "chaosessencemod",
                "physicalessencemod", "manaessencemod", "speedessencemod",
                "casteressencemod", "attackessencemod", "attributeessencemod",
                "greaterchaosessencemod", "greatercoldessencemod", "greaterlifeessencemod",
                "greaterphysicalessencemod",
                "essencehorror", "essencehysteria", "essenceinsanity",
                "essencestartcalm",
                "/essencemoddaemons/essencemod",
            ],
            null,
            [
                "EssenceOfTheAbyss",
                "essenceinsanityproxtrigger",
                "coldessencedeliveryobject",
                "fireessencemodsolarorb",
                "chaosessencevine",
                "essencebonewall",
                "reaperboss",
                "essencedelirium",
                "hysteriaorbdaemon",
                "breachessencedaemon",
            ],
            "Triangle", "#33E0FF", 1f, 12f, SpriteCatalog.Essence(),
            Navigable: false),
        new EndgameMechanicDef(
            "Shrine",
            ["Shrine"],
            null,
            ["/daemon/shrines/"],
            "Star", "#7DFF7D", 1f, 10f, SpriteCatalog.Shrine(),
            Navigable: false),
        new EndgameMechanicDef(
            "Summoning Circle",
            ["SummoningCircle"],
            null,
            null,
            "Diamond", "#E0B341", 1f, 10f, SpriteCatalog.SummoningCircle(),
            Navigable: true),
        new EndgameMechanicDef(
            "Wisp",
            ["AzmeriSpirit", "AzmeriWisp"],
            null,
            null,
            "Star", "#A06CFF", 1f, 10f, SpriteCatalog.Wisp(),
            Navigable: false),
        new EndgameMechanicDef(
            "Rogue Exile",
            ["RogueExile"],
            ["Monster"],
            null,
            "Diamond", "#FF8866", 1f, 10f, SpriteCatalog.RogueExile(),
            Navigable: false),
    };
}
