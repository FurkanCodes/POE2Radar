namespace POE2Radar.Core.Game;

/// <summary>Atlas2-style built-in path categories (MordWraith GameHelper Atlas2 MapGroups).</summary>
public static class Atlas2Defaults
{
    public readonly record struct CategorySeed(
        string Name,
        string BuiltInKey,
        string Color,
        string BackgroundColor,
        bool DrawPath,
        int MaxHops,
        IReadOnlyList<(string Name, bool Enabled)> Targets,
        string? ContentRule = null);

    /// <summary>Default categories mirroring Atlas2Settings constructor.</summary>
    public static IReadOnlyList<CategorySeed> Categories { get; } =
    [
        new("Search", "search", "#FFFFFF", "#000000D9", true, 100,
            [("Current search query", true)]),
        new("Lineage Maps", "lineage", "#000000", "#00B521", false, 100,
        [
            ("Derelict Mansion", true), ("Sacred Reservoir", true), ("Sealed Vault", true), ("The Jade Isles", true),
        ]),
        new("Corrupted Nexus", "corrupted_nexus", "#730000", "#000000D9", false, 100,
            [("Corrupted Nexus content", true)], ContentRule: "corrupted_nexus"),
        new("Grand Mirror", "grand_mirror", "#00AAFF", "#000000D9", false, 100,
            [("Grand Mirror content", true)], ContentRule: "grand_mirror"),
        new("Atlas Progression", "atlas_progression", "#8C4512", "#000000D9", false, 100,
        [
            ("Precursor Tower", true), ("Ancient Gateway", true), ("The Burning Monolith", true),
            ("Western Gateway", true), ("Eastern Gateway", true), ("Western Enigma Chamber", true),
            ("Eastern Enigma Chamber", true), ("The Origin Tower", true),
        ]),
        new("Quests", "quests", "#00FFFF", "#000000D9", false, 100,
            [("The Withered Willow", true)]),
        new("Ritual", "ritual", "#4000F4", "#FFFFFFD9", false, 100,
            [("Caer Tarth", true), ("Crux of Nothingness", true)]),
        new("Breach", "breach", "#FF33BD", "#000000D9", false, 100,
            [("Hive Colony", true), ("Hive Fortress", true)]),
        new("Expedition", "expedition", "#5BC1ED", "#000000D9", false, 100,
        [
            ("Barren Atoll", false), ("Bleached Shoals", false), ("Craggy Peninsula", false),
            ("Exhumed Ruins", false), ("Frigid Bluffs", false), ("Grazed Prairie", false),
            ("Lush Isle", false), ("Moor of Fallen Skies", true), ("Mournful Cliffside", true),
            ("Obscure Island", true), ("Scorched Cay", false), ("Secluded Temple", true),
            ("Sloughed Gully", false), ("Sprawling Jungle", false), ("Stagnant Basin", false),
            ("The Chained Beast", true), ("The Fallen Star", true), ("Tomb of the Fallen Knight", true),
            ("Ruins of Kingsmarch", true),
        ]),
        new("Abyss", "abyss", "#26FF00", "#000000D9", false, 100,
            [("The Well of Souls", true)]),
        new("Temple", "temple", "#DEA700", "#000000D9", false, 100,
            [("Vaal Ruins", true)]),
        new("Citadels", "arbiter", "#FF0000", "#FFFFFFD9", false, 100,
        [
            ("The Copper Citadel", true), ("The Iron Citadel", true), ("The Stone Citadel", true),
            ("The Matriarch Halls", true), ("The Patriarch Halls", true),
        ]),
        new("Towers", "towers", "#000000D9", "#DB00E0", false, 100,
        [
            ("Bluff", true), ("Lost Towers", true), ("Mesa", true), ("Sinking Spire", true), ("Alpine Ridge", true),
        ]),
        new("Good", "good", "#FFFF00", "#000000D9", false, 100,
        [
            ("Burial Bog", true), ("Creek", true), ("Rustbowl", true), ("Sandspit", true), ("Savannah", true),
            ("Steaming Springs", true), ("Steppe", true), ("Wetlands", true), ("Willow", true),
        ]),
        new("Unique Maps", "unique", "#000000D9", "#FF8F00", false, 100,
        [
            ("Ancient Gateway", true), ("Castaway", true), ("Eastern Gateway", true), ("Freight", true),
            ("Jado's Campsite", true), ("Merchant's Campsite", true), ("Moment of Zen", true),
            ("Moor of Fallen Skies", true), ("Precursor Tower", true), ("Site of the Chosen", true),
            ("The Ezomyte Megaliths", true), ("The Fractured Lake", true), ("The Silent Cave", true),
            ("The Viridian Wildwood", true), ("The Voyage", true), ("Untainted Paradise", true),
            ("Vaults of Kamasa", true), ("Western Gateway", true),
        ]),
        new("Special", "special", "#FFFFFF", "#000000D9", false, 100,
            [("Ice Cave", true)]),
    ];

    public static bool IsCorruptedNexus(in Poe2Atlas.AtlasNodeLive node)
    {
        var hasCorruption = ContentHas(node, "Corruption") || ContentHas(node, "CoalescedCorruption");
        var hasBoss = ContentHas(node, "Powerful Map Boss") || ContentHas(node, "PowerfulMapBoss")
            || ContentHas(node, "Deadly Map Boss") || ContentHas(node, "DeadlyMapBoss");
        if (!hasCorruption || !hasBoss) return false;
        var map = !string.IsNullOrEmpty(node.MapCode) ? AtlasCatalog.Shared.Map(node.MapCode) : null;
        if (map?.Tags.Any(t => t.Equals("arbiter", StringComparison.OrdinalIgnoreCase)) == true)
            return false;
        return true;
    }

    public static bool IsGrandMirror(in Poe2Atlas.AtlasNodeLive node)
        => ContentHas(node, "Grand Mirror") || ContentHas(node, "GigaMirror");

    private static bool ContentHas(in Poe2Atlas.AtlasNodeLive node, string label)
    {
        if (node.Tags is { Count: > 0 })
            foreach (var t in node.Tags)
                if (t.Contains(label, StringComparison.OrdinalIgnoreCase)) return true;
        if (node.Badges is { Count: > 0 })
            foreach (var b in node.Badges)
                if (b.Contains(label, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
