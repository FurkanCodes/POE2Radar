// Runecraft price/format helpers — logic traced from GameHelper RunecraftHelper (MIT/community plugin).
using System.Globalization;

namespace POE2Radar.Overlay.Pricing;

public enum RunecraftColorMode
{
    Off = 0,
    Relative = 1,
    Absolute = 2,
}

public static class RunecraftPriceMath
{
    public static void ParseNameAndCount(string raw, out int count, out string name)
    {
        count = 1;
        name = raw?.Trim() ?? string.Empty;
        if (name.Length == 0) return;

        int i = 0;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        if (i > 0 && i < name.Length && (name[i] == 'x' || name[i] == 'X'))
        {
            if (int.TryParse(name.AsSpan(0, i), out var c) && c > 0)
            {
                count = c;
                name = name[(i + 1)..].TrimStart();
                return;
            }
        }

        if (name[^1] == ')')
        {
            int open = name.LastIndexOf('(');
            if (open > 0)
            {
                var inner = name.Substring(open + 1, name.Length - open - 2).Trim();
                if (int.TryParse(inner, out var c) && c > 0)
                {
                    count = c;
                    name = name[..open].TrimEnd();
                }
            }
        }
    }

    public static string LastMetaSegment(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    public static string ArtIdFromDdsPath(string path)
    {
        var seg = LastMetaSegment(path);
        int dot = seg.LastIndexOf('.');
        return dot > 0 ? seg[..dot] : seg;
    }

    public static int LevelFromMetaId(string metaId)
    {
        if (string.IsNullOrEmpty(metaId)) return -1;
        int i = metaId.Length;
        while (i > 0 && char.IsDigit(metaId[i - 1])) i--;
        if (i == metaId.Length) return -1;
        const string marker = "Level";
        if (i < marker.Length || !metaId.AsSpan(i - marker.Length, marker.Length).SequenceEqual(marker))
            return -1;
        return int.TryParse(metaId.AsSpan(i), out var n) ? n : -1;
    }

    public static bool IsUncutGem(string metaId) =>
        !string.IsNullOrEmpty(metaId) &&
        (metaId.StartsWith("SkillGemUncut", StringComparison.Ordinal)
         || metaId.StartsWith("SupportGemUncut", StringComparison.Ordinal)
         || metaId.StartsWith("ReservationGemUncut", StringComparison.Ordinal));

    public static int UncutGemLevel(string metaId)
    {
        if (string.IsNullOrEmpty(metaId)) return -1;
        int i = metaId.Length;
        while (i > 0 && char.IsDigit(metaId[i - 1])) i--;
        if (i == metaId.Length) return -1;
        return int.TryParse(metaId.AsSpan(i), out var n) ? n : -1;
    }

    public static string FormatExalted(double value)
    {
        if (value >= 100) return $"{value:F0} ex";
        int decimals = value >= 1 ? 1 : value >= 0.1 ? 2 : 3;
        double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        string num = rounded.ToString("0.###", CultureInfo.InvariantCulture);
        if (!num.Contains('.')) num += ".0";
        return $"{num} ex";
    }

    public static double MedianOf(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var arr = values.ToArray();
        Array.Sort(arr);
        int n = arr.Length;
        return n % 2 == 1 ? arr[n / 2] : (arr[n / 2 - 1] + arr[n / 2]) * 0.5;
    }

    public static uint PickColor(double totalEx, double median, RunecraftColorMode mode)
    {
        const uint white = 0xFFFFFFFFu;
        const uint green = 0xFF55FF55u;
        const uint yellow = 0xFF55FFFFu;
        const uint red = 0xFF4040FFu;

        return mode switch
        {
            RunecraftColorMode.Absolute when totalEx >= 5.0 => green,
            RunecraftColorMode.Absolute when totalEx < 0.5 => red,
            RunecraftColorMode.Absolute => yellow,
            RunecraftColorMode.Relative when median <= 0 => white,
            RunecraftColorMode.Relative when totalEx / median >= 1.3 => green,
            RunecraftColorMode.Relative when totalEx / median <= 0.7 => red,
            RunecraftColorMode.Relative => yellow,
            _ => white,
        };
    }

    public static bool TryGetUnitPriceExalted(
        string metaId,
        string ddsArtId,
        string localizedName,
        string? englishName,
        out double exalted)
    {
        exalted = 0;

        if (IsUncutGem(metaId))
        {
            int gemLevel = UncutGemLevel(metaId);
            if (gemLevel >= 0 && !string.IsNullOrEmpty(ddsArtId) &&
                PoeNinjaPriceFetcher.TryGetExaltedByArtId(ddsArtId + gemLevel.ToString(), out exalted) && exalted > 0)
                return true;
            return false;
        }

        if (!string.IsNullOrEmpty(metaId) &&
            PoeNinjaPriceFetcher.TryGetExaltedByArtId(metaId, out exalted) && exalted > 0)
            return true;

        int level = LevelFromMetaId(metaId);
        if (level >= 0)
        {
            if (!string.IsNullOrEmpty(ddsArtId) &&
                PoeNinjaPriceFetcher.TryGetExaltedByArtId(ddsArtId + level.ToString(), out exalted) && exalted > 0)
                return true;
        }
        else if (!string.IsNullOrEmpty(ddsArtId) &&
                 PoeNinjaPriceFetcher.TryGetExaltedByArtId(ddsArtId, out exalted) && exalted > 0)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(englishName) &&
            PoeNinjaPriceFetcher.TryGetExaltedByName(englishName, out exalted) && exalted > 0)
            return true;

        if (!string.IsNullOrEmpty(localizedName) &&
            PoeNinjaPriceFetcher.TryGetExaltedByName(localizedName, out exalted) && exalted > 0)
            return true;

        exalted = 0;
        return false;
    }
}
