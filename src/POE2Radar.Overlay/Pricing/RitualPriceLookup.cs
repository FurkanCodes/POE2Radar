// Price lookup helpers — derived from GameHelper RitualHelper PoeNinjaPriceFetcher (GPL-3.0).
using System.Text.RegularExpressions;

namespace POE2Radar.Overlay.Pricing;

public static class RitualPriceLookup
{
    public const int SourcePoeNinja = 0;
    public const int SourcePoe2Scout = 1;

    public const int DisplayDivine = 0;
    public const int DisplayExalted = 1;
    public const int DisplayChaos = 2;

    private static readonly HashSet<string> GenericLookupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Charm", "Ring", "Belt", "Wand", "Staff", "Bow", "Spear", "Gloves", "Boots", "Helmet",
        "Shield", "Quiver", "Amulet", "Focus", "Body Armour", "Quarterstaff", "Sceptre", "Mace",
        "Map", "Idol", "Omen", "Gem", "Flask", "Currency", "Rune",
    };

    private static readonly Dictionary<string, string> DefaultPathBasenames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["goldenuniquecharm"] = "Rite of Passage",
        ["silveruniquecharm"] = "The Fall of the Axe",
        ["stoneuniquecharm"] = "For Utopia",
        ["thawinguniquecharm"] = "Nascent Hope",
        ["dousinguniquecharm"] = "Beira's Anguish",
        ["topazuniquecharm"] = "Valako's Roar",
        ["staunchinguniquecharm"] = "Sanguis Heroum",
        ["groundinguniquecharm"] = "The Black Cat",
        ["rubyuniquecharm"] = "Ngamahu's Chosen",
        ["chaosuniquecharm"] = "Forsaken Bangle",
        ["antidoteuniquecharm"] = "Arakaali's Gift",
        ["sapphireuniquecharm"] = "Breath of the Mountains",
    };

    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        return Regex.Replace(key.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "");
    }

    public static bool IsGenericLookupName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        var trimmed = name.Trim();
        if (trimmed.Length < 4) return true;
        if (GenericLookupNames.Contains(trimmed)) return true;
        return trimmed.StartsWith("Item ", StringComparison.Ordinal);
    }

    public static IEnumerable<string> ArtKeyVariants(string? artBasename)
    {
        if (string.IsNullOrWhiteSpace(artBasename)) yield break;
        var b = artBasename.Trim();
        yield return b;
        if (b.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            yield return b[4..].TrimStart();
        else
            yield return "The " + b;
    }

    public static bool TryResolveDisplayName(
        string internalPathBasename,
        IReadOnlyDictionary<string, string> pathBasenameToItemName,
        out string displayName)
    {
        displayName = "";
        if (string.IsNullOrWhiteSpace(internalPathBasename)) return false;
        var norm = NormalizeKey(internalPathBasename);
        if (DefaultPathBasenames.TryGetValue(norm, out var def))
        {
            displayName = def;
            return true;
        }
        if (pathBasenameToItemName.TryGetValue(norm, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
        {
            displayName = resolved;
            return true;
        }
        return false;
    }

    public static List<string> BuildNameCandidates(
        string? itemName,
        string? internalPathBasename,
        string? fullItemPath,
        string? scoutText,
        IReadOnlyDictionary<string, string> pathBasenameToItemName)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var trimmed = value.Trim();
            if (IsGenericLookupName(trimmed)) return;
            if (seen.Add(trimmed)) candidates.Add(trimmed);
        }

        void addPathBasename(string? basename)
        {
            if (string.IsNullOrWhiteSpace(basename)) return;
            if (TryResolveDisplayName(basename, pathBasenameToItemName, out var mapped))
                add(mapped);
            add(basename);
        }

        add(scoutText);
        addPathBasename(internalPathBasename);
        if (!string.IsNullOrWhiteSpace(fullItemPath))
        {
            foreach (var segment in fullItemPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
                addPathBasename(segment);
        }
        add(itemName);
        return candidates;
    }

    public static int ScoreModMatch(IReadOnlyList<string> itemMods, IReadOnlyList<string> listingMods)
    {
        if (itemMods.Count == 0 || listingMods.Count == 0) return 0;
        var score = 0;
        foreach (var itemMod in itemMods)
        {
            var itemNorm = NormalizeMod(itemMod);
            if (itemNorm.Length < 4) continue;
            foreach (var listingMod in listingMods)
            {
                var listingNorm = NormalizeMod(listingMod);
                if (listingNorm == itemNorm) { score += 3; break; }
                if (listingNorm.Contains(itemNorm) || itemNorm.Contains(listingNorm)) { score += 2; break; }
                var itemNums = ExtractNumbers(itemMod);
                var listNums = ExtractNumbers(listingMod);
                if (itemNums.Count > 0 && itemNums.SequenceEqual(listNums)) { score += 1; break; }
            }
        }
        return score;
    }

    public static (double Value, string Currency) GetDisplayPrice(double chaosPrice, int displayCurrency, double chaosPerDivine, double chaosPerExalted)
    {
        if (displayCurrency == DisplayChaos)
            return (Math.Round(chaosPrice, 1), "chaos");
        if (displayCurrency == DisplayExalted)
        {
            var ex = chaosPerExalted > 0 ? chaosPrice / chaosPerExalted : chaosPrice;
            return (Math.Round(ex, 1), "ex");
        }
        var div = chaosPerDivine > 0 ? chaosPrice / chaosPerDivine : chaosPrice;
        return (Math.Round(div, 3), "divine");
    }

    public static string FormatDisplayValue(double value, string currency) => currency switch
    {
        "divine" => value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
        "chaos" => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string NormalizeMod(string mod)
    {
        if (string.IsNullOrWhiteSpace(mod)) return "";
        var s = mod.ToLowerInvariant();
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static List<int> ExtractNumbers(string text)
    {
        var nums = new List<int>();
        foreach (Match m in Regex.Matches(text ?? "", @"-?\d+"))
        {
            if (int.TryParse(m.Value, out var n)) nums.Add(n);
        }
        return nums;
    }
}
