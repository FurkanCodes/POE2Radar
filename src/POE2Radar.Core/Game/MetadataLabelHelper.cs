namespace POE2Radar.Core.Game;

/// <summary>Turns metadata path segments (e.g. <c>reforgingbench</c>, <c>sanctumlocker_hideout</c>) into
/// readable labels when the name table only echoes the raw token.</summary>
public static class MetadataLabelHelper
{
    /// <summary>Common substrings in PoE2 metadata tokens, longest first for suffix greedy split.</summary>
    private static readonly string[] CompoundParts = new[]
    {
        "healingwell", "craftingbench", "transmutationbench", "reforgingbench", "displaycase",
        "treasurepile", "chessboard", "mannequin", "teleportportal", "teleportowneronly",
        "hideoutstatue", "hideoutclaim", "hideouthealingwell", "oracleidentifier",
        "crafting", "transmutation", "reforging", "verisium", "sanctum", "locker", "hideout",
        "teleport", "portal", "statue", "bench", "healing", "well", "object", "claim", "treasure",
        "pile", "table", "atlas", "scourge", "delirium", "item", "display", "case", "telepad",
        "chess", "board", "mannequin", "male", "female", "oracle", "identifier", "justice",
        "transmutation", "owner", "only", "proxy", "base", "blue", "green", "orange", "purple",
        "red", "white", "yellow", "kulemak", "karui", "canopy", "challenger", "champion",
        "legend", "victor", "atiziri", "mektul", "navali", "monster", "hunter", "blood",
        "priest", "djinn", "order", "vaal", "time", "scientist",
    }.OrderByDescending(w => w.Length).ToArray();

    /// <summary>Whether <paramref name="resolved"/> is just the path token (not a real display name).</summary>
    public static bool IsTokenEquivalent(string metadataPath, string resolved)
    {
        if (string.IsNullOrWhiteSpace(resolved)) return true;
        var seg = LastSegment(metadataPath);
        return Normalize(seg) == Normalize(resolved);
    }

    /// <summary>Human-friendly label from a metadata path or its last segment.</summary>
    public static string HumanizePath(string metadataPath)
    {
        if (string.IsNullOrEmpty(metadataPath)) return "";
        var at = metadataPath.IndexOf('@');
        var path = at >= 0 ? metadataPath[..at] : metadataPath;
        return HumanizeSegment(LastSegment(path));
    }

    /// <summary>Human-friendly label from one path segment / type token.</summary>
    public static string HumanizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "";

        segment = segment.Trim();
        if (segment.Contains('_') || segment.Contains('-'))
            return string.Join(' ',
                segment.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(HumanizeSinglePart));

        return HumanizeSinglePart(segment);
    }

    private static string HumanizeSinglePart(string part)
    {
        if (string.IsNullOrEmpty(part)) return "";

        // PascalCase / digit boundaries (WaypointLongActivationRadius).
        var sb = new System.Text.StringBuilder(part.Length + 8);
        for (var i = 0; i < part.Length; i++)
        {
            var ch = part[i];
            if (i > 0)
            {
                var prev = part[i - 1];
                var boundary = (char.IsUpper(ch) && (char.IsLower(prev) || char.IsDigit(prev)))
                               || (char.IsDigit(ch) && char.IsLetter(prev) && !char.IsDigit(prev));
                if (boundary && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            }
            sb.Append(ch);
        }

        var spaced = sb.ToString().Trim();
        if (!spaced.Contains(' ') && part.All(c => char.IsLower(c) || char.IsDigit(c)))
            spaced = SplitLowercaseCompound(part);

        return TitleCaseWords(spaced);
    }

    private static string SplitLowercaseCompound(string s)
    {
        var lower = s.ToLowerInvariant();
        var parts = new List<string>();
        var rest = lower;
        while (rest.Length > 0)
        {
            string? matched = null;
            foreach (var w in CompoundParts)
            {
                if (rest.EndsWith(w, StringComparison.Ordinal))
                {
                    matched = w;
                    break;
                }
            }

            if (matched is null)
            {
                if (rest.Length > 0) parts.Insert(0, rest);
                break;
            }

            parts.Insert(0, matched);
            rest = rest[..^matched.Length];
        }

        return parts.Count == 0 ? s : string.Join(' ', parts);
    }

    private static string TitleCaseWords(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w
                : char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..].ToLowerInvariant() : "")));
    }

    private static string LastSegment(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static string Normalize(string s)
        => s.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();
}
