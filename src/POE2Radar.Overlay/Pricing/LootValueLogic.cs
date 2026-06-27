namespace POE2Radar.Overlay.Pricing;

/// <summary>Pure helpers for ground-loot filtering (unit-testable).</summary>
public static class LootValueLogic
{
    public static string CategoryGroup(string category)
    {
        if (category.StartsWith("Unique", StringComparison.OrdinalIgnoreCase)) return "Uniques";
        return category switch
        {
            "PrecursorTablets" => "Tablets",
            "Currency" => "Currency",
            "Runes" => "Runes",
            "SoulCores" => "SoulCores",
            "Essences" => "Essences",
            "Fragments" => "Fragments",
            "UncutGems" => "UncutGems",
            "Delirium" => "Delirium",
            "Breach" => "Breach",
            "Ritual" => "Ritual",
            "Abyss" => "Abyss",
            "Expedition" => "Expedition",
            "Verisium" => "Verisium",
            "Idols" => "Idols",
            "LineageSupportGems" => "Gems",
            _ => "Other",
        };
    }

    public static double GroundFloor(string group, double uniqueMin, double currencyMin, double otherMin) => group switch
    {
        "Uniques" => uniqueMin,
        "Currency" => currencyMin,
        _ => otherMin,
    };

    public static string StripCount(string raw)
    {
        var name = raw?.Trim() ?? "";
        var i = 0;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        return i > 0 && i < name.Length && (name[i] == 'x' || name[i] == 'X')
            ? name[(i + 1)..].TrimStart() : name;
    }

    /// <summary>Generate poe.ninja lookup keys for a display name (tiers, level suffixes, count prefixes).</summary>
    public static IEnumerable<string> NameLookupCandidates(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;
        var n = name.Trim();
        yield return n;
        var stripped = StripCount(n);
        if (!string.Equals(stripped, n, StringComparison.Ordinal)) yield return stripped;

        var levelIdx = n.IndexOf("(Level", StringComparison.OrdinalIgnoreCase);
        if (levelIdx > 0)
        {
            var baseName = n[..levelIdx].TrimEnd();
            if (baseName.Length >= 2) yield return baseName;
        }

        // PoE2 gem tiers often omit "Spirit"/"Skill" in stash listings.
        if (n.Contains("Uncut", StringComparison.OrdinalIgnoreCase) && n.Contains("Gem", StringComparison.OrdinalIgnoreCase))
        {
            var noLevel = levelIdx > 0 ? n[..levelIdx].TrimEnd() : n;
            yield return noLevel;
            if (noLevel.Contains(' '))
                yield return noLevel.Split(' ')[0] + " Gem";
        }
    }

    public static PriceResult? TryResolvePrice(PoeNinjaPriceBook book, string name, int count = 1)
    {
        foreach (var key in NameLookupCandidates(name))
        {
            if (book.TryByName(key) is { } pr)
                return pr with { Exalted = pr.Exalted * Math.Max(1, count) };
        }
        return null;
    }

    public static uint ValueTierColor(double ex)
        => ex >= 5.0 ? 0xFF66E066u : ex < 0.5 ? 0xFFE06666u : 0xFFE6C84Du;
}
