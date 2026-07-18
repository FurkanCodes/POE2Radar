namespace POE2Radar.Core.Game;

/// <summary>
/// Minimal atlas search: comma = OR groups; within a group, significant words must all match
/// (so "Moor of the skies" matches "Moor of Fallen Skies").
/// </summary>
public static class AtlasSearch
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "of", "the", "to", "in", "on", "for", "map",
    };

    /// <summary>Parsed once per atlas tick — avoids re-scanning the catalog per node.</summary>
    public sealed class Query
    {
        public IReadOnlyList<string> OrTerms { get; }
        public bool IsEmpty => OrTerms.Count == 0;
        private readonly HashSet<string> _catalogCodes;
        private readonly string _language;
        private readonly AtlasCatalog _catalog;

        internal Query(IReadOnlyList<string> orTerms, HashSet<string> catalogCodes, string language, AtlasCatalog catalog)
        {
            OrTerms = orTerms;
            _catalogCodes = catalogCodes;
            _language = language;
            _catalog = catalog;
        }

        public bool Matches(
            string? mapName,
            string? mapCode,
            IEnumerable<string>? tags = null,
            IEnumerable<string>? badges = null)
        {
            if (IsEmpty) return true;

            if (!string.IsNullOrWhiteSpace(mapCode) && _catalogCodes.Contains(mapCode))
                return true;

            foreach (var term in OrTerms)
            {
                if (TermMatches(mapName, mapCode, tags, badges, term, _language, _catalog))
                    return true;
            }
            return false;
        }
    }

    public static Query Parse(string? query, string? language = null, AtlasCatalog? catalog = null)
    {
        catalog ??= AtlasCatalog.Shared;
        language = string.IsNullOrWhiteSpace(language) ? "english" : language;
        var orTerms = SplitOrTerms(query);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in orTerms)
            CollectCatalogCodes(term, language, catalog, codes);
        return new Query(orTerms, codes, language, catalog);
    }

    /// <summary>Split a query into OR terms (comma-separated).</summary>
    public static List<string> SplitOrTerms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return query.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Significant tokens for matching (stop-words dropped).</summary>
    public static List<string> SignificantTokens(string? phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return [];
        var parts = phrase.Split([' ', '\t', '-', '_', '/', '\\', '.', ',', ';', ':', '\'', '"'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tokens = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            if (p.Length < 2) continue;
            if (StopWords.Contains(p)) continue;
            tokens.Add(p);
        }
        return tokens;
    }

    private static void CollectCatalogCodes(string term, string language, AtlasCatalog catalog, HashSet<string> codes)
    {
        var tokens = SignificantTokens(term);
        foreach (var map in catalog.Maps)
        {
            var localized = catalog.LocalizedMapName(map.Code, language);
            if (tokens.Count == 0)
            {
                if (Contains(map.Name, term) || Contains(map.Code, term) || Contains(localized, term))
                    codes.Add(map.Code);
                continue;
            }

            if (Contains(map.Name, term) || Contains(map.Code, term) || Contains(localized, term))
            {
                codes.Add(map.Code);
                continue;
            }

            var ok = true;
            foreach (var token in tokens)
            {
                if (Contains(map.Name, token) || Contains(map.Code, token) || Contains(localized, token))
                    continue;
                ok = false;
                break;
            }
            if (ok) codes.Add(map.Code);
        }
    }

    private static bool TermMatches(
        string? mapName,
        string? mapCode,
        IEnumerable<string>? tags,
        IEnumerable<string>? badges,
        string term,
        string language,
        AtlasCatalog catalog)
    {
        var tokens = SignificantTokens(term);
        if (tokens.Count == 0)
            return FieldContains(mapName, mapCode, tags, badges, language, catalog, term);

        if (FieldContains(mapName, mapCode, tags, badges, language, catalog, term))
            return true;

        foreach (var token in tokens)
        {
            if (!FieldContains(mapName, mapCode, tags, badges, language, catalog, token))
                return false;
        }
        return true;
    }

    private static bool FieldContains(
        string? mapName,
        string? mapCode,
        IEnumerable<string>? tags,
        IEnumerable<string>? badges,
        string language,
        AtlasCatalog catalog,
        string needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) return false;
        if (Contains(mapName, needle)) return true;
        if (Contains(mapCode, needle)) return true;
        if (tags is not null)
        {
            foreach (var t in tags)
                if (Contains(t, needle)) return true;
        }
        if (badges is not null)
        {
            foreach (var b in badges)
                if (Contains(b, needle)) return true;
        }

        if (!string.IsNullOrWhiteSpace(mapCode))
        {
            if (Contains(catalog.MapName(mapCode), needle)) return true;
            if (Contains(catalog.LocalizedMapName(mapCode, language), needle)) return true;
        }

        return false;
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack)
           && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
