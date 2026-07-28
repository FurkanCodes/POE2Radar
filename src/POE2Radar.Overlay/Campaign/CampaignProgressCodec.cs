using System.Text;
using System.Text.Json;
using POE2Radar.Core.Campaign;

namespace POE2Radar.Overlay.Campaign;

/// <summary>
/// Portable, identity-free campaign progress codes. Character and league names are never included.
/// Unknown objective IDs are filtered by <see cref="CampaignSession"/> during import.
/// </summary>
public static class CampaignProgressCodec
{
    private const int CurrentVersion = 1;
    private const int MaximumCodeLength = 128 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Encode(string guideVersion, IEnumerable<string> completedObjectiveIds)
    {
        var document = new TransferDocument
        {
            Version = CurrentVersion,
            GuideVersion = guideVersion,
            Completed = completedObjectiveIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string code, out IReadOnlyList<string> completedObjectiveIds)
    {
        completedObjectiveIds = [];
        if (string.IsNullOrWhiteSpace(code) || code.Length > MaximumCodeLength)
            return false;
        try
        {
            var normalized = code.Trim().Replace('-', '+').Replace('_', '/');
            normalized += (normalized.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => "",
            };
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var document = JsonSerializer.Deserialize<TransferDocument>(json, Json);
            if (document is null || document.Version != CurrentVersion || document.Completed is null)
                return false;
            completedObjectiveIds = document.Completed
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }

    public static bool TryDecodeWebsite(
        string code,
        out IReadOnlyList<CampaignSourceReference> completedSourceRows)
    {
        completedSourceRows = [];
        if (string.IsNullOrWhiteSpace(code) || code.Length > 10_000)
            return false;
        var prefix = code.StartsWith("PoE2v05_", StringComparison.Ordinal)
            ? "PoE2v05_"
            : code.StartsWith("PoE2_", StringComparison.Ordinal) ? "PoE2_" : "";
        if (prefix.Length == 0)
            return false;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(code[prefix.Length..]));
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var rows = new List<CampaignSourceReference>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var chapter = WebsiteChapter(property.Name);
                if (chapter is null || property.Value.ValueKind != JsonValueKind.Array)
                    continue;
                var chapterRows = property.Value.EnumerateArray().ToArray();
                if (chapterRows.Length > 200)
                    continue;
                foreach (var row in chapterRows)
                {
                    if (row.ValueKind != JsonValueKind.String
                        || !int.TryParse(
                            row.GetString(),
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var number)
                        || number <= 0)
                        continue;
                    rows.Add(new CampaignSourceReference { Chapter = chapter, Row = number });
                }
            }
            completedSourceRows = rows
                .DistinctBy(x => (x.Chapter, x.Row))
                .ToArray();
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }

    private static string? WebsiteChapter(string storageKey)
        => storageKey switch
        {
            "poe2-act1-v05" => "act1",
            "poe2-act2-v05" => "act2",
            "poe2-act3-v05" => "act3",
            "poe2-act4-v05" => "act4",
            "poe2-interludes-v05" or "poe2-interludes-v06" => "interludes",
            _ => null,
        };

    private sealed class TransferDocument
    {
        public int Version { get; init; }
        public string GuideVersion { get; init; } = "";
        public string[] Completed { get; init; } = [];
    }
}
