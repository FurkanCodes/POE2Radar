using POE2Radar.Core.Game;

namespace POE2Radar.Overlay.Config;

/// <summary>Gameplay-importance tier for an entity — drives the curated "important only" filter and the
/// grouped Entities-tab UI. Ordered from highest to lowest priority for display grouping.</summary>
public enum EntityImportance
{
    Mechanic,
    UniqueBoss,
    Rare,
    PoiTransition,
    Chest,
    Npc,
    NormalMonster,
    Other,
}

/// <summary>Classifies live entities into importance tiers using the same mechanic matchers as
/// <see cref="RadarStyles.Mechanics"/> plus category/rarity/POI signals.</summary>
public static class EntityImportanceHelper
{
    public static EntityImportance Classify(Poe2Live.EntityDot e, RadarStyles styles)
    {
        if (EndgameMechanicCatalog.TryMatch(e, out _))
            return EntityImportance.Mechanic;

        if (styles.Mechanics is { Count: > 0 })
            foreach (var m in styles.Mechanics)
                if (m.Enabled && MatchesMechanic(e, m))
                    return EntityImportance.Mechanic;

        if (e.Category == Poe2Live.EntityCategory.Monster)
        {
            if (e.Rarity == Poe2Live.Rarity.Unique) return EntityImportance.UniqueBoss;
            if (e.Rarity == Poe2Live.Rarity.Rare) return EntityImportance.Rare;
            if (e.Rarity is Poe2Live.Rarity.Normal or Poe2Live.Rarity.Magic)
                return EntityImportance.NormalMonster;
        }

        if (e.Poi || e.Category == Poe2Live.EntityCategory.Transition)
            return EntityImportance.PoiTransition;

        if (e.Category == Poe2Live.EntityCategory.Chest) return EntityImportance.Chest;
        if (e.Category == Poe2Live.EntityCategory.Npc) return EntityImportance.Npc;

        return EntityImportance.Other;
    }

    public static bool IsTrash(EntityImportance tier)
        => tier is EntityImportance.NormalMonster or EntityImportance.Other;

    public static bool IsImportant(EntityImportance tier) => !IsTrash(tier);

    public static bool IsNavDefault(EntityImportance tier)
        => tier is EntityImportance.Mechanic or EntityImportance.UniqueBoss
            or EntityImportance.Rare or EntityImportance.PoiTransition;

    public static string TierLabel(EntityImportance tier) => tier switch
    {
        EntityImportance.Mechanic => "Endgame mechanics",
        EntityImportance.UniqueBoss => "Bosses & uniques",
        EntityImportance.Rare => "Rare monsters",
        EntityImportance.PoiTransition => "POIs & transitions",
        EntityImportance.Chest => "Chests",
        EntityImportance.Npc => "NPCs",
        EntityImportance.NormalMonster => "Normal / magic monsters",
        EntityImportance.Other => "Other",
        _ => tier.ToString(),
    };

    /// <summary>Default display-rule names backing each tier's group-level Show/Nav toggles.</summary>
    public static IReadOnlyList<string> MechanicRuleNames => EndgameMechanicCatalog.RuleNames;

    public static string[] RuleNamesForTier(EntityImportance tier) => tier switch
    {
        EntityImportance.Mechanic => MechanicRuleNames.ToArray(),
        EntityImportance.UniqueBoss => ["Boss", "Monster · Unique"],
        EntityImportance.Rare => ["Monster · Rare"],
        EntityImportance.PoiTransition => ["Waypoint", "Checkpoint", "Portal", "Town portal", "Stash", "Quest marker", "Quest object", "Bridge", "Map marker", "Point of Interest", "Transition"],
        EntityImportance.Chest => ["Chest · Unique", "Chest · Rare"],
        EntityImportance.Npc => ["NPC"],
        EntityImportance.NormalMonster => ["Monster · Normal", "Monster · Magic"],
        _ => Array.Empty<string>(),
    };

    public static EntityImportance[] DisplayOrder { get; } =
    [
        EntityImportance.Mechanic,
        EntityImportance.UniqueBoss,
        EntityImportance.Rare,
        EntityImportance.PoiTransition,
        EntityImportance.Chest,
        EntityImportance.Npc,
        EntityImportance.NormalMonster,
        EntityImportance.Other,
    ];

    private static bool MatchesMechanic(Poe2Live.EntityDot e, MechanicStyle m)
    {
        if (m.Match is not { Count: > 0 }) return false;

        if (m.Categories is { Count: > 0 })
        {
            var cat = e.Category.ToString();
            var ok = false;
            foreach (var c in m.Categories)
                if (string.Equals(c, cat, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
            if (!ok) return false;
        }

        foreach (var term in m.Match)
        {
            if (string.IsNullOrEmpty(term)) continue;
            if (e.Metadata.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
