using System.Globalization;

namespace POE2Radar.Core.Game;

/// <summary>
/// Decodes the complete special-island manifest for each Uncharted Waters chunk from its
/// already-materialized atlas nodes and attaches the community farming tier for each destination.
/// Unlike the game's three-line sampler, this includes hidden fourth-or-later outcomes.
/// </summary>
public static class AtlasIslandRumours
{
    public const string MoorPriorityColor = "#FF2FD0";

    public sealed record Preparation(
        string Investment,
        string Tablets,
        string Waystone);

    public sealed record Definition(
        string Destination,
        string Rumour,
        string Kind,
        string Summary,
        string Tier,
        string TierColor,
        string Color,
        string Icon,
        int SortOrder,
        Preparation Preparation,
        bool IsMoorPriority = false);

    public sealed record Row(Definition Definition, int Count)
    {
        public string TitleLine { get; } = Count > 1
            ? $"{Definition.Rumour} ×{Count.ToString(CultureInfo.InvariantCulture)}"
            : Definition.Rumour;

        public string DetailLine { get; } = $"{Definition.Destination} — {Definition.Summary}";
        public string TierLine { get; } = $"Tier {Definition.Tier} · {Definition.Kind}";
        public string PreparationLine { get; } = $"Prep: {Definition.Preparation.Investment}";
    }

    public sealed record Manifest(
        int ChunkX,
        int ChunkY,
        int TotalIslands,
        IReadOnlyList<Row> Rows)
    {
        public string BadgeText { get; } = TotalIslands.ToString(CultureInfo.InvariantCulture);
        public string TitleLine { get; } =
            $"ISLAND RUMOURS — {TotalIslands.ToString(CultureInfo.InvariantCulture)} special islands";
        public bool HasMoorOfFallenSkies { get; } =
            Rows.Any(row => row.Definition.IsMoorPriority);
    }

    // Destination/rumour identities are backed by the live atlas area IDs and embedded map catalog.
    // Farming tiers follow the community ratings at poe2-exp.gnejs.app/rumours (checked 2026-07-18);
    // they are advisory because league balance and the player economy can change their value.
    private static readonly Definition[] Catalog =
    [
        new("Castaway", "All that glitters...", "Unique map",
            "Gold conversion and buried treasure.", "A", "#6EEB87", "#FFB347", "", 0,
            Prep("GOLD / BALANCED",
                "Irradiated: Gold Found, rare monsters, then monster rarity. Do not spend premium tablets solely on the fixed treasure.",
                "T15, 6-8 safe mods. Favor monster rarity/item rarity only for monster drops.")),
        new("Untainted Paradise", "Almost paradise.", "Unique map",
            "Greatly increased experience; monsters drop no items.", "C", "#AAB2BF", "#FFB347", "", 0,
            Prep("XP / SAFE",
                "Use Experience Gain if tablet effects apply; skip item rarity because monsters drop no items.",
                "Use a safe T15+ Waystone. Do not add lethal effectiveness when the reward is experience.")),
        new("The Fractured Lake", "Reflective waters...", "Unique map",
            "Mirrored rares, ring bases, and the Fragmented Mirror.", "A", "#6EEB87", "#FFB347", "", 0,
            Prep("SAFE / BALANCED",
                "Rare monsters and monster rarity are useful; do not premium-juice solely for the fixed ring-base reward.",
                "T15, 6-8 safe mods. Prioritize a clean clear over maximum monster effectiveness.")),
        new("Moment of Zen", "A good fellow...", "Trader",
            "A travelling merchant offers unique wares.", "C", "#AAB2BF", "#6EE7B7", "", 1,
            Prep("UTILITY / CHEAP",
                "No premium tablet is needed for the merchant. Use a cheap Irradiated Tablet only for surrounding maps.",
                "Use the cheapest safe T15 Waystone; item rarity and monster effectiveness do not improve the shop.")),
        new("The Jade Isles", "Crazed Chieftain...", "Boss island",
            "Three Manoki fights; chance for Rakiata's Flow.", "S+", "#FFD166", "#FF6B6B", "AtlasIconContentMapBoss", 2,
            Prep("S+ / BOSS SAFE",
                "Map-boss item rarity/quantity is the useful tablet angle; avoid extra effectiveness unless your bossing is comfortable.",
                "Use the highest tier you can kill reliably with safe mods. Survival beats an 8-mod brick here.")),
        new("Sprawling Jungle", "End of the circle...", "Boss island",
            "Medved, the Fallen Seer.", "B", "#63B3ED", "#FF6B6B", "AtlasIconContentMapBoss", 2,
            Prep("BOSS SAFE",
                "Map-boss item rarity/quantity if available; otherwise save premium Irradiated Tablets.",
                "T15 with safe mods. Avoid stacking monster effectiveness for a boss-specific reward.")),
        new("Mournful Cliffside", "The last to fall...", "Boss island",
            "Vorana, Last to Fall.", "B", "#63B3ED", "#FF6B6B", "AtlasIconContentMapBoss", 2,
            Prep("BOSS SAFE",
                "Map-boss item rarity/quantity if available; otherwise save premium Irradiated Tablets.",
                "T15 with safe mods. Avoid stacking monster effectiveness for a boss-specific reward.")),
        new("Secluded Temple", "Stardrinker...", "Boss island",
            "Uhtred, the Stardrinker.", "A", "#6EEB87", "#FF6B6B", "AtlasIconContentMapBoss", 2,
            Prep("HIGH / BOSS SAFE",
                "Map-boss item rarity/quantity if available; add monster rarity only when the fight is already safe.",
                "T15+ with safe high-return mods. Do not sacrifice boss reliability for effectiveness.")),
        new("Obscure Island", "Origin of the fall...", "Boss island",
            "Olroth; Expedition pinnacle progression.", "A", "#6EEB87", "#FF6B6B", "AtlasIconContentMapBoss", 2,
            Prep("HIGH / BOSS SAFE",
                "Map-boss item rarity/quantity if available; save monster-effectiveness juice for normal Grand Expeditions.",
                "Use the highest safe tier and mods you can reliably boss. Progression is worth more than greed.")),
        new("Barren Atoll", "Somethin' fishy...", "Grand Expedition",
            "Clam chests with Pearlescent Amulet bases.", "B+", "#4DD9FF", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("TILE LOOT / BALANCED",
                "Rare monsters and monster rarity improve normal drops; do not premium-juice solely for the fixed clam chests.",
                "T15 with 6-8 safe mods. Monster effectiveness does not improve the special chest contents.")),
        new("Stagnant Basin", "Nothin' to drink...", "Grand Expedition",
            "Oil-focused Grand Expedition.", "A", "#6EEB87", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("HIGH / BALANCED",
                "3x Irradiated: +2 random map modifiers, rare monsters, then monster effectiveness/rarity.",
                "T15, 6-8 safe mods. Do not overpay for item rarity solely for the special oil reward.")),
        new("Exhumed Ruins", "Unknown ruins...", "Grand Expedition",
            "Precursor Leylines; utility-focused rewards.", "B", "#63B3ED", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("UTILITY / BUDGET",
                "Budget Irradiated: rare monsters, monster rarity, or effectiveness. Save +2-mod tablets for higher tiers.",
                "T15 with 6-8 comfortable mods; prioritize completion over maximum juice.")),
        new("Sloughed Gully", "It's dry at least...", "Grand Expedition",
            "Monster-effectiveness modifiers.", "D", "#FF6B6B", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("LOW / BUDGET",
                "Use cheap monster rarity or item rarity rolls; do not stack more effectiveness unless overgeared.",
                "T15 with easy mods. Do not spend an 8-mod premium Waystone on this D-tier island.")),
        new("Moor of Fallen Skies", "Fallen stars...", "Unique map",
            "Runestone encounter and guaranteed Aldur's Saga.", "S+", "#FFD166", "#FFB347", "", -1,
            Prep("MAX JUICE",
                "3x Irradiated: +2 random map modifiers > monster effectiveness/rare monsters > monster rarity > item rarity.",
                "Reserve a T15+ 8-mod Waystone; take the highest safe monster effectiveness and monster rarity. T16 is optional."),
            IsMoorPriority: true),
        new("Craggy Peninsula", "Endless cliffs...", "Grand Expedition",
            "Item rarity and Rogue Exiles.", "A", "#6EEB87", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("HIGH JUICE",
                "3x Irradiated: additional Rogue Exile/rare monsters, monster rarity, then +2 random map modifiers.",
                "T15, 8 mods if safe. Favor effectiveness + monster rarity, but avoid mods dangerous for Rogue Exiles.")),
        new("Grazed Prairie", "Warm but risky...", "Grand Expedition",
            "Experience, Beyond, and hoards.", "B", "#63B3ED", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("XP / BALANCED",
                "Experience Gain + rare monsters; add +2 modifiers/effectiveness only if farming the monster hoards.",
                "T15 with safe dense mods. Keep deaths low when using this as an experience island.")),
        new("Bleached Shoals", "Bleak and awful...", "Grand Expedition",
            "Strongbox-focused Grand Expedition.", "D", "#FF6B6B", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("TILE LOOT / BUDGET",
                "Additional Strongboxes if desired; premium monster-effectiveness and rarity rolls do not scale strongbox loot.",
                "Use a cheap, safe T15 Waystone. Do not spend your best 8-mod Waystone here.")),
        new("Lush Isle", "Wild roaming free...", "Grand Expedition",
            "Azmeri Spirits.", "D", "#FF6B6B", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("LOW / BUDGET",
                "Additional Azmeri Spirits if specifically farming them; otherwise use cheap general Irradiated rolls.",
                "T15 with easy mods. Save high-effectiveness and 8-mod Waystones for better islands.")),
        new("Frigid Bluffs", "Cold as ice...", "Grand Expedition",
            "Old Expedition rewards.", "A+", "#9BF6A4", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("VERY HIGH / DANGEROUS",
                "3x Irradiated: +2 random map modifiers, rare monsters, monster rarity; add effectiveness only if your build is strong.",
                "T15, 8 mods only when safe. Old Expedition remnants can multiply danger, so inspect immunities first.")),
        new("Scorched Cay", "Sulphite!", "Grand Expedition",
            "Increased item rarity.", "A", "#6EEB87", "#63B3ED", "AtlasIconContentExpedition", 3,
            Prep("HIGH JUICE",
                "3x Irradiated: +2 random map modifiers + monster effectiveness/rare monsters; monster rarity complements its item rarity.",
                "T15, 8 mods if safe. Prioritize monster effectiveness and monster rarity before more item rarity.")),
    ];

    private static Preparation Prep(string investment, string tablets, string waystone)
        => new(investment, tablets, waystone);

    private static readonly Dictionary<string, Definition> ByDestination =
        Catalog.ToDictionary(definition => definition.Destination, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<Definition> Definitions => Catalog;

    public static bool TryGetDefinition(string? destination, out Definition definition)
    {
        if (!string.IsNullOrWhiteSpace(destination)
            && ByDestination.TryGetValue(destination, out var found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    /// <summary>
    /// Build immutable manifests in one pass over the existing node snapshot. This performs no memory
    /// reads and is intended to run once after Atlas map-name resolution completes.
    /// </summary>
    public static Manifest[] Build(IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes)
    {
        var chunks = new Dictionary<(int X, int Y), Dictionary<Definition, int>>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (string.IsNullOrEmpty(node.MapName)
                || !ByDestination.TryGetValue(node.MapName, out var definition))
                continue;

            var chunk = (node.GridX >> 4, node.GridY >> 4);
            if (!chunks.TryGetValue(chunk, out var counts))
            {
                counts = new Dictionary<Definition, int>();
                chunks[chunk] = counts;
            }
            counts.TryGetValue(definition, out var count);
            counts[definition] = count + 1;
        }

        if (chunks.Count == 0)
            return Array.Empty<Manifest>();

        var manifests = new Manifest[chunks.Count];
        var manifestIndex = 0;
        foreach (var (chunk, counts) in chunks)
        {
            var rows = counts
                .Select(entry => new Row(entry.Key, entry.Value))
                .OrderBy(row => row.Definition.SortOrder)
                .ThenBy(row => row.Definition.Rumour, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var total = 0;
            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                total += rows[rowIndex].Count;
            manifests[manifestIndex++] = new Manifest(chunk.X, chunk.Y, total, rows);
        }

        Array.Sort(manifests, static (left, right) =>
        {
            var byCount = right.TotalIslands.CompareTo(left.TotalIslands);
            if (byCount != 0) return byCount;
            var byX = left.ChunkX.CompareTo(right.ChunkX);
            return byX != 0 ? byX : left.ChunkY.CompareTo(right.ChunkY);
        });
        return manifests;
    }
}
