using POE2Radar.Core.Campaign;
using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Campaign;

public enum CampaignTargetStatus
{
    NonSpatial,
    WrongArea,
    Uncurated,
    NotFound,
    Resolved,
    MultipleCandidates,
}

public readonly record struct CampaignTargetMatch(
    CampaignTargetStatus Status,
    string Label,
    NumVec2? Grid,
    uint? EntityId,
    int CandidateCount,
    bool Opened,
    bool IconComplete,
    int HpCur,
    int HpMax,
    string Diagnostic)
{
    public bool HasPosition => Grid.HasValue;
    public bool IsAliveBoss => EntityId.HasValue && HpMax > 0 && HpCur > 0;
    public bool IsDeadBoss => EntityId.HasValue && HpMax > 0 && HpCur <= 0;
}

public readonly record struct CampaignObjectiveState(
    CampaignObjective Objective,
    bool Completed,
    bool Current);

public sealed record CampaignView(
    bool Available,
    bool Visible,
    string ProfileHash,
    string ChapterLabel,
    CampaignObjective? Current,
    CampaignObjective[] Next,
    CampaignObjectiveState[] ZoneObjectives,
    IReadOnlySet<string> CompletedObjectiveIds,
    int RequiredCompleted,
    int RequiredTotal,
    int FullCompleted,
    int FullTotal,
    int ChapterCompleted,
    int ChapterTotal,
    int AreaCompleted,
    int AreaTotal,
    CampaignTargetMatch Target,
    string CompletionBadge,
    string TargetStatus,
    bool Dismissed)
{
    public static readonly CampaignView Empty = new(
        false, false, "", "", null, [], [], new HashSet<string>(StringComparer.Ordinal),
        0, 0, 0, 0, 0, 0, 0, 0,
        new CampaignTargetMatch(CampaignTargetStatus.NonSpatial, "", null, null, 0, false, false, 0, 0, ""),
        "", "", false);
}

public readonly record struct CampaignFrame(
    string AreaCode,
    int CharacterLevel,
    NumVec2 PlayerGrid,
    string League,
    string CharacterName,
    IReadOnlyList<Poe2Live.EntityDot> Entities,
    IReadOnlyList<Poe2Live.Landmark> Landmarks,
    IReadOnlyList<Poe2Live.ServerMinimapIcon> ServerIcons);

public sealed record CampaignPathView(
    string ObjectiveId,
    (int x, int y)[] Points,
    (int x, int y)[] FullPoints,
    (int x, int y)? ResolvedGoal,
    RoutePlanStatus Status,
    string FailureReason)
{
    public static readonly CampaignPathView Empty = new(
        "", [], [], null, RoutePlanStatus.Unplanned, "");
}
