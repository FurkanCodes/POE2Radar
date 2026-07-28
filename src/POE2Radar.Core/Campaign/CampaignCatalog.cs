using System.Reflection;
using System.Text.Json;

namespace POE2Radar.Core.Campaign;

/// <summary>
/// Immutable, validated campaign guide. The embedded JSON is content; this class owns invariants and
/// stable lookup semantics so consumers never need to understand the import format.
/// </summary>
public sealed class CampaignCatalog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly CampaignObjective[] _objectives;
    private readonly Dictionary<string, CampaignObjective> _byId;
    private readonly Dictionary<string, CampaignZoneSection[]> _sectionsByChapter;
    private readonly Dictionary<string, CampaignZoneSection> _sectionByObjectiveId;

    public static CampaignCatalog Shared { get; } = LoadEmbedded();

    public int SchemaVersion { get; }
    public string GuideVersion { get; }
    public string SourceRepository { get; }
    public string SourceCommit { get; }
    public string SourceLicense { get; }
    public int SourceRowCount { get; }
    public IReadOnlyList<CampaignSourceChapter> SourceChapters { get; }
    public IReadOnlyList<CampaignObjective> Objectives => _objectives;

    private CampaignCatalog(CampaignCatalogDocument document)
    {
        SchemaVersion = document.SchemaVersion;
        GuideVersion = document.GuideVersion;
        SourceRepository = document.SourceRepository;
        SourceCommit = document.SourceCommit;
        SourceLicense = document.SourceLicense;
        SourceRowCount = document.SourceRowCount;
        SourceChapters = document.SourceChapters;
        _objectives = document.Objectives.OrderBy(x => x.Order).ToArray();
        _byId = _objectives.ToDictionary(x => x.Id, StringComparer.Ordinal);
        _sectionsByChapter = BuildSections(_objectives);
        _sectionByObjectiveId = _sectionsByChapter.Values
            .SelectMany(x => x)
            .SelectMany(section => section.Objectives.Select(objective => (objective.Id, Section: section)))
            .ToDictionary(x => x.Id, x => x.Section, StringComparer.Ordinal);
    }

    public CampaignObjective? Find(string id)
        => !string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id, out var objective) ? objective : null;

    public IReadOnlyList<CampaignObjective> ForChapter(string chapter)
        => _objectives.Where(x => string.Equals(x.Chapter, chapter, StringComparison.OrdinalIgnoreCase)).ToArray();

    public IReadOnlyList<CampaignZoneSection> SectionsForChapter(string chapter)
        => _sectionsByChapter.TryGetValue(chapter, out var sections) ? sections : [];

    public CampaignZoneSection? SectionContaining(string objectiveId)
        => _sectionByObjectiveId.TryGetValue(objectiveId, out var section) ? section : null;

    public bool IsCampaignArea(string areaCode)
    {
        if (string.IsNullOrWhiteSpace(areaCode))
            return false;
        if (_objectives.Any(x =>
                x.Target.AllowedAreaCodes.Contains(areaCode, StringComparer.OrdinalIgnoreCase)
                || string.Equals(x.Target.DestinationAreaCode, areaCode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.Completion.ExpectedAreaCode, areaCode, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Some checklist entries are intentionally non-spatial (dialogue/reward/loadout steps), so
        // they do not carry a target area gate. Still activate in every real campaign/interlude area
        // from the authoritative area table; exclude world maps and DNT/unused rows.
        var isCampaignCode =
            areaCode.StartsWith("G1_", StringComparison.OrdinalIgnoreCase)
            || areaCode.StartsWith("G2_", StringComparison.OrdinalIgnoreCase)
            || areaCode.StartsWith("G3_", StringComparison.OrdinalIgnoreCase)
            || areaCode.StartsWith("G4_", StringComparison.OrdinalIgnoreCase)
            || areaCode.StartsWith("P1_", StringComparison.OrdinalIgnoreCase)
            || areaCode.StartsWith("P2_", StringComparison.OrdinalIgnoreCase)
            || areaCode.StartsWith("P3_", StringComparison.OrdinalIgnoreCase);
        var area = Game.ZoneGuide.Shared.Area(areaCode);
        return isCampaignCode
               && area is { } resolvedArea
               && !resolvedArea.Name.Contains("DNT", StringComparison.OrdinalIgnoreCase)
               && !resolvedArea.Name.StartsWith("Act ", StringComparison.OrdinalIgnoreCase);
    }

    public static CampaignCatalog Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var document = JsonSerializer.Deserialize<CampaignCatalogDocument>(stream, Json)
                       ?? throw new InvalidDataException("Campaign catalog is empty.");
        Validate(document);
        return new CampaignCatalog(document);
    }

    private static CampaignCatalog LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames();
        var manifestResource = resources.SingleOrDefault(x =>
            x.EndsWith("Campaign.Data.manifest.json", StringComparison.Ordinal));
        if (manifestResource is null)
            throw new InvalidDataException("Embedded campaign manifest was not found.");
        using var manifestStream = assembly.GetManifestResourceStream(manifestResource)
                                   ?? throw new InvalidDataException("Embedded campaign manifest could not be opened.");
        var manifest = JsonSerializer.Deserialize<CampaignManifestDocument>(manifestStream, Json)
                       ?? throw new InvalidDataException("Campaign manifest is empty.");

        var objectiveResources = resources
            .Where(x => x.Contains(".Campaign.Data.Objectives.", StringComparison.Ordinal)
                        && x.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (objectiveResources.Length == 0)
            throw new InvalidDataException("No embedded campaign objective files were found.");
        var embeddedChapters = objectiveResources
            .Select(ObjectiveChapterFromResourceName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var declaredChapters = manifest.SourceChapters
            .Select(x => x.Id)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!embeddedChapters.SequenceEqual(declaredChapters, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Campaign objective files do not match the chapters declared by the manifest.");

        var objectives = new List<CampaignObjective>();
        foreach (var objectiveResource in objectiveResources)
        {
            using var objectiveStream = assembly.GetManifestResourceStream(objectiveResource)
                                        ?? throw new InvalidDataException(
                                            $"Embedded campaign objectives could not be opened: {objectiveResource}");
            var supplement = JsonSerializer.Deserialize<CampaignObjectiveDocument>(objectiveStream, Json)
                             ?? throw new InvalidDataException(
                                 $"Embedded campaign objectives are empty: {objectiveResource}");
            objectives.AddRange(supplement.Objectives);
        }

        var document = new CampaignCatalogDocument
        {
            SchemaVersion = manifest.SchemaVersion,
            GuideVersion = manifest.GuideVersion,
            SourceRepository = manifest.SourceRepository,
            SourceCommit = manifest.SourceCommit,
            SourceLicense = manifest.SourceLicense,
            SourceRowCount = manifest.SourceRowCount,
            SourceChapters = manifest.SourceChapters,
            Objectives = objectives.ToArray(),
        };
        Validate(document);
        return new CampaignCatalog(document);
    }

    private static string ObjectiveChapterFromResourceName(string resourceName)
    {
        const string marker = ".Campaign.Data.Objectives.";
        var start = resourceName.IndexOf(marker, StringComparison.Ordinal);
        start += marker.Length;
        return resourceName[start..^".json".Length];
    }

    private static Dictionary<string, CampaignZoneSection[]> BuildSections(
        IReadOnlyList<CampaignObjective> objectives)
    {
        var result = new Dictionary<string, CampaignZoneSection[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var chapter in objectives.GroupBy(x => x.Chapter, StringComparer.OrdinalIgnoreCase))
        {
            var sections = new List<CampaignZoneSection>();
            var current = new List<CampaignObjective>();
            foreach (var objective in chapter.OrderBy(x => x.Order))
            {
                if (current.Count > 0
                    && !string.Equals(
                        current[0].AreaName,
                        objective.AreaName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    sections.Add(ToSection(chapter.Key, current));
                    current = [];
                }
                current.Add(objective);
            }
            if (current.Count > 0)
                sections.Add(ToSection(chapter.Key, current));
            result[chapter.Key] = sections.ToArray();
        }
        return result;
    }

    private static CampaignZoneSection ToSection(
        string chapter,
        IReadOnlyCollection<CampaignObjective> objectives)
    {
        var array = objectives.ToArray();
        return new CampaignZoneSection(array[0].Id, chapter, array[0].AreaName, array);
    }

    private static void Validate(CampaignCatalogDocument document)
    {
        var errors = new List<string>();
        if (document.SchemaVersion <= 0) errors.Add("schemaVersion must be positive");
        if (document.SourceRowCount != document.SourceChapters.Sum(x => x.RowCount))
            errors.Add("sourceRowCount does not equal chapter row totals");
        if (string.IsNullOrWhiteSpace(document.SourceCommit))
            errors.Add("sourceCommit is required");

        var duplicateIds = document.Objectives
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Where(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1)
            .Select(x => string.IsNullOrWhiteSpace(x.Key) ? "<empty>" : x.Key);
        foreach (var id in duplicateIds) errors.Add($"duplicate objective id: {id}");

        var previousOrder = 0;
        foreach (var objective in document.Objectives.OrderBy(x => x.Order))
        {
            if (objective.Order <= previousOrder) errors.Add($"non-increasing order: {objective.Id}");
            previousOrder = objective.Order;
            if (string.IsNullOrWhiteSpace(objective.Text)) errors.Add($"missing text: {objective.Id}");
            if (objective.Source.Length == 0) errors.Add($"missing source reference: {objective.Id}");
            if (objective.Target.IsSpatial
                && objective.Target.AllowedAreaCodes.Length == 0
                && string.IsNullOrWhiteSpace(objective.Target.DestinationAreaCode))
                errors.Add($"spatial target has no area gate: {objective.Id}");

            foreach (var code in objective.Target.AllowedAreaCodes
                         .Append(objective.Target.DestinationAreaCode)
                         .Append(objective.Completion.ExpectedAreaCode)
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Cast<string>())
                if (Game.ZoneGuide.Shared.Area(code) is null)
                    errors.Add($"unknown area code '{code}': {objective.Id}");

            if (objective.Completion.Kind == CampaignCompletionKind.StableAreaEntry
                && string.IsNullOrWhiteSpace(objective.Completion.ExpectedAreaCode))
                errors.Add($"stable-area completion has no expected area: {objective.Id}");
            if (objective.Completion.Kind == CampaignCompletionKind.BossDefeated
                && objective.Target.Kind != CampaignTargetKind.Boss)
                errors.Add($"boss completion requires boss target: {objective.Id}");
            if (objective.Completion.Kind == CampaignCompletionKind.ObjectChanged
                && objective.Target.Kind is not (CampaignTargetKind.QuestObject or CampaignTargetKind.WaypointIcon))
                errors.Add($"object completion requires object/icon target: {objective.Id}");
        }

        foreach (var chapter in document.SourceChapters.Where(x => x.Implemented))
        {
            var covered = document.Objectives
                .SelectMany(x => x.Source)
                .Where(x => string.Equals(x.Chapter, chapter.Id, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Row)
                .ToHashSet();
            if (covered.Count != chapter.RowCount)
                errors.Add(
                    $"source coverage count mismatch: {chapter.Id} expected {chapter.RowCount}, got {covered.Count}");
        }

        var implementedRows = document.SourceChapters.Where(x => x.Implemented).Sum(x => x.RowCount);
        var coveredImplementedRows = document.Objectives
            .SelectMany(x => x.Source)
            .Select(x => (Chapter: x.Chapter.ToLowerInvariant(), x.Row))
            .Distinct()
            .Count(x => document.SourceChapters.Any(chapter =>
                chapter.Implemented
                && string.Equals(chapter.Id, x.Chapter, StringComparison.OrdinalIgnoreCase)));
        if (implementedRows != coveredImplementedRows)
            errors.Add(
                $"implemented source coverage mismatch: expected {implementedRows}, got {coveredImplementedRows}");

        foreach (var source in document.Objectives.SelectMany(x => x.Source))
        {
            var chapter = document.SourceChapters.FirstOrDefault(x =>
                string.Equals(x.Id, source.Chapter, StringComparison.OrdinalIgnoreCase));
            if (chapter is null || source.Row < 1)
                errors.Add($"invalid source reference: {source.Chapter} row {source.Row}");
        }

        if (errors.Count > 0)
            throw new InvalidDataException("Campaign catalog validation failed: " + string.Join("; ", errors));
    }
}
