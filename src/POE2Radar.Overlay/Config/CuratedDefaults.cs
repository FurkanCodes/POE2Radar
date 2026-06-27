using POE2Radar.Core.Game;
using POE2Radar.Overlay.Web;

namespace POE2Radar.Overlay.Config;

/// <summary>Curated important-only visibility and default path qualification: league mechanics,
/// bosses, and rare monsters — not generic chests, POIs, or trash.</summary>
public static class CuratedDefaults
{
    private static readonly HashSet<string> NavigableRuleNames = BuildNavigableRuleNames();

    private static readonly HashSet<string> HiddenNavRuleNames = BuildHiddenNavRuleNames();

    public static IReadOnlySet<string> DefaultNavigableRuleNames => NavigableRuleNames;

    public static IReadOnlySet<string> DefaultHiddenNavRuleNames => HiddenNavRuleNames;

    public static bool IsDefaultNavigable(string? ruleName)
        => !string.IsNullOrEmpty(ruleName) && NavigableRuleNames.Contains(ruleName);

    public static bool IsVisibleWhenImportantOnly(
        Poe2Live.EntityDot e,
        DisplayRule? rule,
        RadarStyles styles,
        IReadOnlyList<Poe2Live.EntityDot>? peers = null)
    {
        if (rule is { Hide: true }) return false;
        if (e.Category == Poe2Live.EntityCategory.Player) return true;

        var contentRule = rule;
        if (contentRule is null && EndgameMechanicCatalog.TryMatch(e, out _))
            return true;

        var tier = EntityImportanceHelper.Classify(e, styles, contentRule);
        return tier is EntityImportance.Mechanic or EntityImportance.UniqueBoss or EntityImportance.Rare;
    }

    /// <summary>One-time: apply curated Navigable flags without touching colors, Hide, or sprites.</summary>
    public static bool MigrateDisplayRules(List<DisplayRule> rules)
    {
        var changed = false;
        foreach (var r in rules)
        {
            if (NavigableRuleNames.Contains(r.Name))
            {
                if (!r.Navigable) { r.Navigable = true; changed = true; }
                continue;
            }

            if (HiddenNavRuleNames.Contains(r.Name))
            {
                if (r.Navigable) { r.Navigable = false; changed = true; }
            }
        }
        return changed;
    }

    private static HashSet<string> BuildNavigableRuleNames()
    {
        var set = new HashSet<string>(EndgameMechanicCatalog.RuleNames, StringComparer.OrdinalIgnoreCase)
        {
            "Boss",
            "Monster · Rare",
            "ServerIcon · Breach",
            "ServerIcon · Ritual",
            "ServerIcon · Abyss",
            "ServerIcon · Strongbox",
        };
        return set;
    }

    private static HashSet<string> BuildHiddenNavRuleNames()
    {
        var catalog = new HashSet<string>(EndgameMechanicCatalog.RuleNames, StringComparer.OrdinalIgnoreCase);
        var hidden = new HashSet<string>(ConservativeNavDefaults.LegacyBroadNavRuleNames, StringComparer.OrdinalIgnoreCase);
        hidden.ExceptWith(catalog);
        hidden.Remove("Boss");
        hidden.Remove("Monster · Rare");

        hidden.UnionWith(
        [
            "Player", "NPC",
            "Chest · Unique", "Chest · Rare",
            "ServerIcon · Waypoint", "ServerIcon · Entrance", "ServerIcon · Checkpoint",
            "ServerIcon · Portal", "ServerIcon · PartyMember", "ServerIcon · Chest", "ServerIcon · Other",
        ]);
        return hidden;
    }
}
