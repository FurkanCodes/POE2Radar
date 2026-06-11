using POE2Radar.Core.Game;
using POE2Radar.Overlay.Web;

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
    /// <param name="contentRule">First matching enabled display rule (from <see cref="DisplayRules.ResolveContent"/>).</param>
    public static EntityImportance Classify(Poe2Live.EntityDot e, RadarStyles styles, DisplayRule? contentRule = null)
    {
        if (contentRule is { Name: { Length: > 0 } n } && EntityDisplayHelper.IsMechanicRuleName(n))
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
}
