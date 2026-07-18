using System.Globalization;

namespace POE2Radar.Core.Game;

/// <summary>
/// Decodes the complete special-island manifest for each Uncharted Waters chunk from its
/// already-materialized atlas nodes and attaches the community farming tier for each destination.
/// Unlike the game's three-line sampler, this includes hidden fourth-or-later outcomes.
/// </summary>
public static class AtlasIslandRumours
{
    public sealed record Definition(
        string Destination,
        string Rumour,
        string Kind,
        string Summary,
        string Tier,
        string TierColor,
        string Color,
        string Icon,
        int SortOrder);

    public sealed record Row(Definition Definition, int Count)
    {
        public string TitleLine { get; } = Count > 1
            ? $"{Definition.Rumour} ×{Count.ToString(CultureInfo.InvariantCulture)}"
            : Definition.Rumour;

        public string DetailLine { get; } = $"{Definition.Destination} — {Definition.Summary}";
        public string TierLine { get; } = $"Tier {Definition.Tier} · {Definition.Kind}";
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
    }

    // Destination/rumour identities are backed by the live atlas area IDs and embedded map catalog.
    // Farming tiers follow the community ratings at poe2-exp.gnejs.app/rumours (checked 2026-07-18);
    // they are advisory because league balance and the player economy can change their value.
    private static readonly Definition[] Catalog =
    [
        new("Castaway", "All that glitters...", "Unique map",
            "Gold conversion and buried treasure.", "A", "#6EEB87", "#FFB347", "", 0),
        new("Untainted Paradise", "Almost paradise.", "Unique map",
            "Greatly increased experience; monsters drop no items.", "C", "#AAB2BF", "#FFB347", "", 0),
        new("The Fractured Lake", "Reflective waters...", "Unique map",
            "Mirrored rares, ring bases, and the Fragmented Mirror.", "A", "#6EEB87", "#FFB347", "", 0),
        new("Moment of Zen", "A good fellow...", "Trader",
            "A travelling merchant offers unique wares.", "C", "#AAB2BF", "#6EE7B7", "", 1),
        new("The Jade Isles", "Crazed Chieftain...", "Boss island",
            "Three Manoki fights; chance for Rakiata's Flow.", "S+", "#FFD166", "#FF6B6B", "AtlasIconContentMapBoss", 2),
        new("Sprawling Jungle", "End of the circle...", "Boss island",
            "Medved, the Fallen Seer.", "B", "#63B3ED", "#FF6B6B", "AtlasIconContentMapBoss", 2),
        new("Mournful Cliffside", "The last to fall...", "Boss island",
            "Vorana, Last to Fall.", "B", "#63B3ED", "#FF6B6B", "AtlasIconContentMapBoss", 2),
        new("Secluded Temple", "Stardrinker...", "Boss island",
            "Uhtred, the Stardrinker.", "A", "#6EEB87", "#FF6B6B", "AtlasIconContentMapBoss", 2),
        new("Obscure Island", "Origin of the fall...", "Boss island",
            "Olroth; Expedition pinnacle progression.", "A", "#6EEB87", "#FF6B6B", "AtlasIconContentMapBoss", 2),
        new("Barren Atoll", "Somethin' fishy...", "Grand Expedition",
            "Gold-focused Grand Expedition.", "B+", "#4DD9FF", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Stagnant Basin", "Nothin' to drink...", "Grand Expedition",
            "Oil-focused Grand Expedition.", "A", "#6EEB87", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Exhumed Ruins", "Unknown ruins...", "Grand Expedition",
            "Precursor Leylines; utility-focused rewards.", "B", "#63B3ED", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Sloughed Gully", "It's dry at least...", "Grand Expedition",
            "Monster-effectiveness modifiers.", "D", "#FF6B6B", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Moor of Fallen Skies", "Fallen stars...", "Grand Expedition",
            "Runestone encounter and Aldur's Saga sustain.", "S+", "#FFD166", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Craggy Peninsula", "Endless cliffs...", "Grand Expedition",
            "Item rarity and Rogue Exiles.", "A", "#6EEB87", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Grazed Prairie", "Warm but risky...", "Grand Expedition",
            "Azmeri Spirits.", "D", "#FF6B6B", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Bleached Shoals", "Bleak and awful...", "Grand Expedition",
            "Strongbox-focused Grand Expedition.", "D", "#FF6B6B", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Lush Isle", "Wild roaming free...", "Grand Expedition",
            "Experience, Beyond, and hoards.", "B", "#63B3ED", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Frigid Bluffs", "Cold as ice...", "Grand Expedition",
            "Old Expedition rewards.", "A+", "#9BF6A4", "#63B3ED", "AtlasIconContentExpedition", 3),
        new("Scorched Cay", "Sulphite!", "Grand Expedition",
            "Increased item rarity.", "A", "#6EEB87", "#63B3ED", "AtlasIconContentExpedition", 3),
    ];

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
