using System.Text.Json.Serialization;

namespace POE2Radar.Core.Campaign;

/// <summary>The kind of in-game subject an objective may navigate toward.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CampaignTargetKind>))]
public enum CampaignTargetKind
{
    NonSpatial,
    AreaTransition,
    Npc,
    Boss,
    QuestObject,
    WaypointIcon,
    TerrainLandmark,
    ItemLocation,
}

/// <summary>
/// Completion signals the overlay is allowed to trust. Everything not represented here remains
/// an explicit player checkmark.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CampaignCompletionKind>))]
public enum CampaignCompletionKind
{
    Manual,
    StableAreaEntry,
    BossDefeated,
    ObjectChanged,
}

/// <summary>Provenance for one imported checklist row.</summary>
public sealed record CampaignSourceReference
{
    public int Row { get; init; }
    public string Chapter { get; init; } = "";
}

/// <summary>
/// Curated, exact-only ways to locate a campaign subject. Globs support only '*' and are matched
/// against complete metadata or tile paths; runtime display-name/fuzzy matching is intentionally absent.
/// </summary>
public sealed record CampaignTargetSpec
{
    public CampaignTargetKind Kind { get; init; } = CampaignTargetKind.NonSpatial;
    public string Label { get; init; } = "";
    public string[] AllowedAreaCodes { get; init; } = [];
    public string[] MetadataGlobs { get; init; } = [];
    public string[] ServerIconNames { get; init; } = [];
    public string[] LandmarkPathGlobs { get; init; } = [];
    public string? DestinationAreaCode { get; init; }
    public bool Validated { get; init; }
    public string? Guidance { get; init; }

    [JsonIgnore]
    public bool IsSpatial => Kind != CampaignTargetKind.NonSpatial;
}

/// <summary>Definitive completion rule for an atomic objective.</summary>
public sealed record CampaignCompletionRule
{
    public CampaignCompletionKind Kind { get; init; } = CampaignCompletionKind.Manual;
    public string? ExpectedAreaCode { get; init; }
    public int StableTicks { get; init; } = 2;

    [JsonIgnore]
    public bool CanAutoComplete => Kind != CampaignCompletionKind.Manual;
}

/// <summary>One stable, atomic campaign objective.</summary>
public sealed record CampaignObjective
{
    public string Id { get; init; } = "";
    public string Chapter { get; init; } = "";
    /// <summary>
    /// Optional independently ordered branch inside a chapter. Interludes use this to follow the
    /// branch the player actually enters while retaining the preferred 5.3 -> 5.1 -> 5.2 order.
    /// </summary>
    public string? Branch { get; init; }
    public int Order { get; init; }
    public string AreaName { get; init; } = "";
    public string Text { get; init; } = "";
    public string? Note { get; init; }
    public bool Optional { get; init; }
    public string[] Rewards { get; init; } = [];
    public CampaignSourceReference[] Source { get; init; } = [];
    public CampaignTargetSpec Target { get; init; } = new();
    public CampaignCompletionRule Completion { get; init; } = new();
}

/// <summary>Expected source-row count for a guide chapter.</summary>
public sealed record CampaignSourceChapter
{
    public string Id { get; init; } = "";
    public int RowCount { get; init; }
    public bool Implemented { get; init; }
}

/// <summary>A contiguous visit to one campaign area in guide order.</summary>
public sealed record CampaignZoneSection(
    string Id,
    string Chapter,
    string AreaName,
    CampaignObjective[] Objectives);

internal record CampaignManifestDocument
{
    public int SchemaVersion { get; init; }
    public string GuideVersion { get; init; } = "";
    public string SourceRepository { get; init; } = "";
    public string SourceCommit { get; init; } = "";
    public string SourceLicense { get; init; } = "";
    public int SourceRowCount { get; init; }
    public CampaignSourceChapter[] SourceChapters { get; init; } = [];
}

internal sealed record CampaignCatalogDocument : CampaignManifestDocument
{
    public CampaignObjective[] Objectives { get; init; } = [];
}

internal sealed record CampaignObjectiveDocument
{
    public CampaignObjective[] Objectives { get; init; } = [];
}
