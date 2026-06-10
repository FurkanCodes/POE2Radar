using POE2Radar.Core.Game;

namespace POE2Radar.Research;

/// <summary>Mechanic matchers for <c>--mechanic-survey</c> only — keeps Research off Overlay/ImGui.</summary>
internal static class MechanicSurveyCatalog
{
    private sealed record Def(string Name, string[] Match, string[]? Categories, string[]? Exclude);

    private static readonly Def[] Defs = new[]
    {
        new Def("Expedition", ["Expedition2/Expedition2Encounter"], ["Other"], ["Expedition2EncounterCrack"]),
        new Def("Ritual", ["LeagueRitual", "RitualAltar", "Leagues/Ritual", "/Ritual/"], ["Object", "Other"], null),
        new Def("Breach", ["Leagues/Breach", "LeagueBreach", "BreachHand", "/Breach/"], null, null),
        new Def("Abyss",
            ["Leagues/Abyss", "/Abyss/", "AbyssJumpInteractable", "AbyssPit", "AbyssHole", "AbyssTrove", "AbyssalTrove",
                "AbyssGate", "AbyssFissure", "AbyssStart"],
            ["Object", "Other"], ["AbyssCrack", "EssenceOfTheAbyss"]),
        new Def("Delirium", ["Delirium", "Simulacrum", "LeagueDelirium", "Leagues/Delirium"], null, null),
        new Def("Strongbox", ["StrongBoxes"], ["Chest"], null),
        new Def("Essence", ["Essence"], null, ["EssenceOfTheAbyss"]),
        new Def("Shrine", ["Shrine"], null, ["/daemon/shrines/"]),
        new Def("Summoning Circle", ["SummoningCircle"], null, null),
        new Def("Wisp", ["AzmeriSpirit", "AzmeriWisp"], null, null),
        new Def("Rogue Exile", ["RogueExile"], ["Monster"], null),
    };

    public static IReadOnlyList<string> RuleNames { get; } = Defs.Select(d => d.Name).ToArray();

    public static bool TryMatch(Poe2Live.EntityDot e, out string? name)
    {
        foreach (var d in Defs)
        {
            if (Matches(e.Metadata, e.Category, d))
            {
                name = d.Name;
                return true;
            }
        }
        name = null;
        return false;
    }

    private static bool Matches(string metadata, Poe2Live.EntityCategory category, Def d)
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
}
