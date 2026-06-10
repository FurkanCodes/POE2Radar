namespace POE2Radar.Overlay.Config;

/// <summary>Return of the Ancients (0.5) atlas highlight presets — map names from live memory + world_areas.json.</summary>
public static class AtlasEndgameCatalog
{
    private readonly record struct MapRule(string Pattern, AtlasEndgameTier Tier, string Color, bool Track, bool Arrow);

    private readonly record struct TagRule(string Tag, AtlasEndgameTier Tier, string Color, bool Track, bool Arrow);

    private static readonly MapRule[] MapRules =
    [
        new("Origin Tower", AtlasEndgameTier.Pinnacle, "#e0533a", true, true),
        new("Divinity", AtlasEndgameTier.Pinnacle, "#e0533a", true, true),
        new("Burning Monolith", AtlasEndgameTier.Pinnacle, "#e0533a", true, true),
        new("Patriarch Halls", AtlasEndgameTier.KeyHalls, "#d946ef", true, true),
        new("Matriarch Halls", AtlasEndgameTier.KeyHalls, "#d946ef", true, true),
        new("Citadel", AtlasEndgameTier.Citadel, "#e0b341", true, true),
        new("Enigma Chamber", AtlasEndgameTier.Enigma, "#a06cff", true, false),
        new("Fortress", AtlasEndgameTier.Fortress, "#2fb6a8", true, false),
    ];

    private static readonly TagRule[] TagRules =
    [
        new("Powerful Map Boss", AtlasEndgameTier.BossContent, "#ff6b4a", true, false),
    ];

    public static AtlasEndgameTier Classify(string? mapName, IReadOnlyList<string>? tags)
    {
        if (!string.IsNullOrEmpty(mapName))
        {
            foreach (var r in MapRules)
                if (mapName.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase))
                    return r.Tier;
        }
        if (tags is { Count: > 0 })
        {
            foreach (var t in tags)
            {
                if (string.IsNullOrEmpty(t)) continue;
                foreach (var r in TagRules)
                    if (string.Equals(t, r.Tag, StringComparison.OrdinalIgnoreCase))
                        return r.Tier;
            }
        }
        return AtlasEndgameTier.None;
    }

    /// <summary>Match tier for a catalog key (map name or content tag from the live atlas list).</summary>
    public static bool TryGetRuleForKey(string key, out AtlasEndgameTier tier, out string color, out bool track, out bool arrow)
    {
        foreach (var r in MapRules)
            if (key.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase))
            {
                tier = r.Tier; color = r.Color; track = r.Track; arrow = r.Arrow;
                return true;
            }
        foreach (var r in TagRules)
            if (string.Equals(key, r.Tag, StringComparison.OrdinalIgnoreCase))
            {
                tier = r.Tier; color = r.Color; track = r.Track; arrow = r.Arrow;
                return true;
            }
        tier = AtlasEndgameTier.None; color = ""; track = false; arrow = false;
        return false;
    }

    /// <summary>Apply endgame highlight rules from the live tag catalog + static rules (Settings button).</summary>
    public static void ApplyEndgameDefaults(RadarSettings settings, IReadOnlyList<AtlasTagCatalogEntry>? catalog)
    {
        if (catalog is null) return;
        foreach (var e in catalog)
        {
            if (!TryGetRuleForKey(e.Key, out _, out var color, out var track, out var arrow)) continue;
            if (track && !ListContains(settings.AtlasHighlightTags, e.Key))
                settings.AtlasHighlightTags.Add(e.Key);
            if (arrow && !ListContains(settings.AtlasArrowTags, e.Key))
                settings.AtlasArrowTags.Add(e.Key);
            settings.AtlasHighlightColors[e.Key] = color;
        }
    }

    private static bool ListContains(List<string> list, string key)
    {
        foreach (var t in list)
            if (string.Equals(t, key, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
