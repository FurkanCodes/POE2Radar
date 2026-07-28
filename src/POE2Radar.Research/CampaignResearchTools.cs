using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using POE2Radar.Core;
using POE2Radar.Core.Campaign;
using POE2Radar.Core.Game;

namespace POE2Radar.Research;

internal static partial class CampaignResearchTools
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static int Validate()
    {
        try
        {
            var catalog = CampaignCatalog.Shared;
            Console.WriteLine($"Campaign schema: {catalog.SchemaVersion}");
            Console.WriteLine($"Guide: {catalog.GuideVersion}");
            Console.WriteLine($"Source: {catalog.SourceRepository} @ {catalog.SourceCommit}");
            Console.WriteLine($"Source rows declared: {catalog.SourceRowCount}");
            foreach (var chapter in catalog.SourceChapters)
            {
                var covered = catalog.Objectives
                    .SelectMany(x => x.Source)
                    .Where(x => string.Equals(x.Chapter, chapter.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Row)
                    .Distinct()
                    .Count();
                var atomic = catalog.ForChapter(chapter.Id).Count;
                Console.WriteLine(
                    $"  {chapter.Id,-12} rows {covered,3}/{chapter.RowCount,3} · atomic {atomic,3} · "
                    + (chapter.Implemented ? "IMPLEMENTED" : "PENDING"));
            }

            var curated = catalog.Objectives.Count(x => x.Target.IsSpatial && x.Target.Validated);
            var spatial = catalog.Objectives.Count(x => x.Target.IsSpatial);
            Console.WriteLine($"Spatial matchers: {curated}/{spatial} marked validated.");
            Console.WriteLine("PASS · catalog invariants and implemented-chapter coverage are valid.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL · {ex.Message}");
            return 1;
        }
    }

    public static int Import(string htmlPath, string? savePath)
    {
        if (!File.Exists(htmlPath))
        {
            Console.Error.WriteLine($"Campaign source HTML not found: {htmlPath}");
            return 1;
        }

        var chapter = ChapterFromFile(htmlPath);
        var html = File.ReadAllText(htmlPath);
        var rows = new List<ImportedRow>();
        var zone = "";
        var canAttachNote = false;
        foreach (Match match in GuideElementRegex().Matches(html))
        {
            if (match.Groups["zone"].Success)
            {
                zone = Clean(match.Groups["zone"].Value);
                zone = ZoneSuffixRegex().Replace(zone, "").Trim();
                canAttachNote = false;
                continue;
            }

            if (match.Groups["note"].Success)
            {
                if (canAttachNote)
                {
                    var note = Clean(match.Groups["note"].Value);
                    if (note.Length > 0)
                        rows[^1] = rows[^1] with { Notes = [.. rows[^1].Notes, note] };
                }
                continue;
            }

            var body = match.Groups["body"].Value;
            var rewards = RewardRegex().Matches(body)
                .Select(x => Clean(x.Groups["reward"].Value))
                .Where(x => x.Length > 0)
                .ToArray();
            rows.Add(new ImportedRow(
                chapter,
                int.Parse(match.Groups["row"].Value, System.Globalization.CultureInfo.InvariantCulture),
                zone,
                body.Contains("class=\"skip\"", StringComparison.Ordinal),
                Clean(body),
                rewards,
                [],
                Path.GetFileName(htmlPath)));
            canAttachNote = true;
        }

        var output = JsonSerializer.Serialize(rows, Json);
        if (string.IsNullOrWhiteSpace(savePath))
            Console.WriteLine(output);
        else
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(savePath));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(savePath, output);
            Console.WriteLine($"Wrote {rows.Count} imported {chapter} rows to {Path.GetFullPath(savePath)}");
        }
        return rows.Count == 0 ? 1 : 0;
    }

    public static int Survey(
        ProcessHandle process,
        MemoryReader reader,
        string? objectiveId,
        string? savePath)
    {
        var slot = LootResearchProbes.ResolveGameStateSlot(process, reader);
        if (slot == 0)
        {
            Console.Error.WriteLine("Could not resolve the GameState slot.");
            return 1;
        }

        var live = new Poe2Live(reader, slot);
        if (!live.TryResolve(out _, out var area, out var player))
        {
            Console.Error.WriteLine("Not in a stable game area.");
            return 1;
        }

        var catalog = CampaignCatalog.Shared;
        var areaCode = live.AreaCode(area);
        var objective = !string.IsNullOrWhiteSpace(objectiveId)
            ? catalog.Find(objectiveId)
            : catalog.Objectives.FirstOrDefault(x =>
                x.Target.AllowedAreaCodes.Contains(areaCode, StringComparer.OrdinalIgnoreCase));
        if (objective is null)
        {
            Console.Error.WriteLine($"No objective selected for area {areaCode}. Pass --objective <stable-id>.");
            return 1;
        }

        var playerGrid = live.PlayerGrid(player) ?? System.Numerics.Vector2.Zero;
        var (entities, _, _) = live.Entities(area);
        var icons = live.ServerMinimapIcons(area);
        var landmarks = live.Landmarks(area);
        var entityMatches = entities.Where(x =>
                objective.Target.MetadataGlobs.Any(glob =>
                    GlobMatches(glob, x.Metadata)
                    || (!string.IsNullOrWhiteSpace(x.ItemMetadata) && GlobMatches(glob, x.ItemMetadata))))
            .ToArray();
        var iconMatches = icons.Where(x =>
                objective.Target.ServerIconNames.Contains(x.Name, StringComparer.Ordinal))
            .ToArray();
        var landmarkMatches = landmarks.Where(x =>
                objective.Target.LandmarkPathGlobs.Any(glob => GlobMatches(glob, x.Path)))
            .ToArray();

        Console.WriteLine($"Objective: {objective.Id}");
        Console.WriteLine($"Area: {areaCode} ({ZoneGuide.Shared.FriendlyName(areaCode)})");
        Console.WriteLine($"Target: {objective.Target.Kind} · {objective.Target.Label}");
        Console.WriteLine(
            $"Exact matches: entities={entityMatches.Length}, icons={iconMatches.Length}, landmarks={landmarkMatches.Length}");
        foreach (var entity in entityMatches)
            Console.WriteLine(
                $"  ENTITY id={entity.Id} grid=({entity.Grid.X:F0},{entity.Grid.Y:F0}) "
                + $"hp={entity.HpCur}/{entity.HpMax} opened={entity.Opened} complete={entity.IconComplete} {entity.Metadata}");
        foreach (var icon in iconMatches)
            Console.WriteLine($"  ICON {icon.Name} grid=({icon.Grid.X:F0},{icon.Grid.Y:F0})");
        foreach (var landmark in landmarkMatches)
            Console.WriteLine($"  TILE grid=({landmark.Center.X:F0},{landmark.Center.Y:F0}) {landmark.Path}");

        if (!string.IsNullOrWhiteSpace(savePath))
        {
            var capture = new
            {
                schemaVersion = 1,
                capturedUtc = DateTime.UtcNow,
                catalogCommit = catalog.SourceCommit,
                objective = new
                {
                    objective.Id,
                    objective.Text,
                    objective.Target,
                },
                areaCode,
                playerGrid = new { x = playerGrid.X, y = playerGrid.Y },
                exactMatches = new
                {
                    entities = entityMatches.Select(EntityCapture).ToArray(),
                    icons = iconMatches.Select(x => new { x.Name, x.Id, x.Grid.X, x.Grid.Y }).ToArray(),
                    landmarks = landmarkMatches.Select(x => new { x.Path, x.Center.X, x.Center.Y }).ToArray(),
                },
                nearbyEntities = entities
                    .OrderBy(x => System.Numerics.Vector2.DistanceSquared(x.Grid, playerGrid))
                    .Take(250)
                    .Select(EntityCapture)
                    .ToArray(),
                serverIcons = icons.Select(x => new { x.Name, x.Id, x.Grid.X, x.Grid.Y }).ToArray(),
                landmarks = landmarks.Select(x => new { x.Path, x.Center.X, x.Center.Y }).ToArray(),
            };
            var directory = Path.GetDirectoryName(Path.GetFullPath(savePath));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(savePath, JsonSerializer.Serialize(capture, Json));
            Console.WriteLine($"Capture written: {Path.GetFullPath(savePath)}");
        }

        return entityMatches.Length + iconMatches.Length + landmarkMatches.Length > 0 ? 0 : 2;
    }

    private static object EntityCapture(Poe2Live.EntityDot entity)
        => new
        {
            entity.Id,
            entity.Metadata,
            category = entity.Category.ToString(),
            entity.Grid.X,
            entity.Grid.Y,
            entity.HpCur,
            entity.HpMax,
            entity.Opened,
            entity.IconComplete,
            entity.IsSleeping,
            entity.ItemMetadata,
            entity.ItemName,
        };

    private static bool GlobMatches(string glob, string value)
    {
        if (string.IsNullOrWhiteSpace(glob) || string.IsNullOrWhiteSpace(value)) return false;
        if (!glob.Contains('*', StringComparison.Ordinal))
            return string.Equals(glob, value, StringComparison.Ordinal);
        var pattern = "\\A" + Regex.Escape(glob).Replace("\\*", ".*", StringComparison.Ordinal) + "\\z";
        return Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant);
    }

    private static string ChapterFromFile(string path)
    {
        var file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (file.Contains("interlude", StringComparison.Ordinal)) return "interludes";
        var match = Regex.Match(file, "act(?<act>[1-4])", RegexOptions.CultureInvariant);
        return match.Success ? $"act{match.Groups["act"].Value}" : "unknown";
    }

    private static string Clean(string html)
        => WebUtility.HtmlDecode(TagRegex().Replace(html, " "))
            .Replace('\u00A0', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Aggregate("", (current, part) => current.Length == 0 ? part : current + " " + part);

    private sealed record ImportedRow(
        string Chapter,
        int Row,
        string Zone,
        bool Optional,
        string Text,
        string[] Rewards,
        string[] Notes,
        string SourceFile);

    [GeneratedRegex(
        "<div\\s+class=\"zone-header\"[^>]*>(?<zone>[\\s\\S]*?)</div>|"
        + "<div\\s+class=\"step\"\\s+data-step=\"(?<row>\\d+)\"[^>]*>[\\s\\S]*?"
        + "<div\\s+class=\"step-content\">(?<body>[\\s\\S]*?)</div></div>|"
        + "<div\\s+class=\"note[^\"]*\"[^>]*>(?<note>[\\s\\S]*?)</div>",
        RegexOptions.CultureInvariant)]
    private static partial Regex GuideElementRegex();

    [GeneratedRegex("class=\"reward-tag[^\"]*\">(?<reward>[\\s\\S]*?)</span>", RegexOptions.CultureInvariant)]
    private static partial Regex RewardRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\s+(?:(?:TOWN|WAYPOINT)\\b.*|Lvl\\b.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ZoneSuffixRegex();
}
