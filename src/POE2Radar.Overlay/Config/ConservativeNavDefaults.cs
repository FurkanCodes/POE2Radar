namespace POE2Radar.Overlay.Config;

/// <summary>
/// GameHelper-aligned default auto-path qualification: only league mechanics and curated terrain
/// should path by default; POIs, strongboxes, quests, and trash stay visible but not auto-followed.
/// </summary>
public static class ConservativeNavDefaults
{
    /// <summary>Display rules that default to <c>Navigable=true</c> on fresh installs (GH ShowPath parity).</summary>
    public static readonly HashSet<string> DefaultNavigableRuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Breach",
        "Ritual",
        "Abyss · Pit",
        "Expedition",
        "Summoning Circle",
        "ServerIcon · Breach",
        "ServerIcon · Ritual",
        "ServerIcon · Abyss",
    };

    /// <summary>Rules that old migrations flipped on; reset to non-navigable unless user opted in via zone overrides.</summary>
    public static readonly HashSet<string> LegacyBroadNavRuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Boss",
        "Monster · Rare",
        "Monster · Magic",
        "Monster · Normal",
        "Transition",
        "Point of Interest",
        "Map marker",
        "Quest object",
        "Quest marker",
        "Waypoint",
        "Checkpoint",
        "ServerIcon · Waypoint",
        "ServerIcon · Entrance",
        "ServerIcon · Checkpoint",
        "ServerIcon · Portal",
        "ServerIcon · Strongbox",
        "ServerIcon · Chest",
        "Essence",
        "Shrine",
        "Delirium",
        "Ultimatum",
        "Corruption",
        "Wisp",
        "Rogue Exile",
        "Abyss",
        "Strongbox",
        "Strongbox · Unique",
        "Strongbox · Landmark",
        "Strongbox · Cartographer",
        "Strongbox · Arcane",
        "Strongbox · Armourer",
        "Strongbox · Jeweller",
        "Strongbox · Divination",
        "Strongbox · Expedition",
        "Strongbox · Researcher",
        "Strongbox · Abyss",
    };

    public static bool IsDefaultNavigable(string ruleName)
        => DefaultNavigableRuleNames.Contains(ruleName);

    /// <summary>One-time: tighten Navigable on rules still at legacy broad defaults.</summary>
    public static bool MigrateDisplayRules(List<Web.DisplayRule> rules)
    {
        var changed = false;
        foreach (var r in rules)
        {
            if (DefaultNavigableRuleNames.Contains(r.Name))
            {
                if (!r.Navigable) { r.Navigable = true; changed = true; }
                continue;
            }

            if (LegacyBroadNavRuleNames.Contains(r.Name) || r.Name.StartsWith("Strongbox", StringComparison.OrdinalIgnoreCase))
            {
                if (r.Navigable) { r.Navigable = false; changed = true; }
            }
        }
        return changed;
    }
}
