using POE2Radar.Core.Campaign;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay.Campaign;

/// <summary>
/// Stateful campaign progression engine. It consumes the existing raw world snapshot, never performs
/// memory reads, and emits one compact immutable view for rendering/routing.
/// </summary>
public sealed class CampaignSession
{
    private readonly CampaignCatalog _catalog;
    private readonly CampaignProgressStore _store;
    private readonly CampaignTargetResolver _resolver;

    private string _profileHash = "";
    private string _observedObjectiveId = "";
    private string _stableArea = "";
    private int _stableAreaTicks;
    private readonly HashSet<uint> _observedAliveBosses = [];
    private readonly HashSet<uint> _observedIncompleteObjects = [];
    private CampaignView _view = CampaignView.Empty;

    public CampaignSession(
        CampaignCatalog catalog,
        CampaignProgressStore store,
        CampaignTargetResolver? resolver = null)
    {
        _catalog = catalog;
        _store = store;
        _resolver = resolver ?? new CampaignTargetResolver();
    }

    public CampaignView CurrentView => _view;

    public CampaignView Update(CampaignFrame frame, CampaignSettings settings)
    {
        if (!settings.Enabled)
            return _view = CampaignView.Empty;

        var profileHash = CampaignProgressStore.HashIdentity(frame.League, frame.CharacterName);
        if (profileHash.Length == 0)
            return _view = CampaignView.Empty;
        if (!string.Equals(_profileHash, profileHash, StringComparison.Ordinal))
        {
            _profileHash = profileHash;
            ResetObservations();
        }

        var profile = _store.Snapshot(profileHash);
        var requiredObjectives = _catalog.Objectives.Where(x => !x.Optional).ToArray();
        var requiredCompleted = requiredObjectives.Count(x => profile.Completed.Contains(x.Id));
        var fullCompleted = _catalog.Objectives.Count(x => profile.Completed.Contains(x.Id));
        var remaining = RemainingSequence(profile, frame.AreaCode, settings.GuideMode);
        var current = remaining.FirstOrDefault();
        if (current is null)
        {
            var completeCampaignArea = _catalog.IsCampaignArea(frame.AreaCode);
            var completeVisible = !profile.Dismissed
                                  && (!settings.AutoActivate
                                      || (completeCampaignArea && frame.CharacterLevel is > 0 and <= 80));
            var modeLabel = settings.GuideMode == CampaignGuideMode.Required ? "REQUIRED" : "CAMPAIGN";
            _view = new CampaignView(
                true, completeVisible, profileHash, modeLabel, null, [], [], profile.Completed,
                requiredCompleted, requiredObjectives.Length, fullCompleted, _catalog.Objectives.Count,
                0, 0, 0, 0, default, "COMPLETE",
                settings.GuideMode == CampaignGuideMode.Required
                    ? "All required objectives complete"
                    : "Campaign complete",
                profile.Dismissed);
            return _view;
        }

        if (!string.Equals(_observedObjectiveId, current.Id, StringComparison.Ordinal))
        {
            _observedObjectiveId = current.Id;
            ResetObservations(keepObjective: true);
        }

        var target = _resolver.Resolve(current, frame);
        var completionIsSafe = current.Completion.Kind == CampaignCompletionKind.StableAreaEntry
                               || current.Target.Validated;
        if (settings.SafeAutoCheck
            && completionIsSafe
            && ObserveDefinitiveCompletion(current, target, frame.AreaCode))
        {
            _store.SetComplete(profileHash, current.Id, true);
            return Update(frame, settings);
        }

        var chapterObjectives = _catalog.ForChapter(current.Chapter)
            .Where(x => IncludedByMode(x, settings.GuideMode))
            .ToArray();
        var chapterCompleted = chapterObjectives.Count(x => profile.Completed.Contains(x.Id));
        var areaObjectives = (_catalog.SectionContaining(current.Id)?.Objectives ?? [])
            .Where(x => IncludedByMode(x, settings.GuideMode))
            .ToArray();
        var areaCompleted = areaObjectives.Count(x => profile.Completed.Contains(x.Id));
        var zoneObjectives = areaObjectives
            .Select(x => new CampaignObjectiveState(
                x,
                profile.Completed.Contains(x.Id),
                string.Equals(x.Id, current.Id, StringComparison.Ordinal)))
            .ToArray();
        var next = remaining.Skip(1).Take(2).ToArray();
        var campaignArea = _catalog.IsCampaignArea(frame.AreaCode);
        var visible = !profile.Dismissed
                      && (!settings.AutoActivate || (campaignArea && frame.CharacterLevel is > 0 and <= 80));
        var completionBadge = current.Completion.Kind switch
        {
            CampaignCompletionKind.StableAreaEntry => "AUTO · AREA ENTRY",
            CampaignCompletionKind.BossDefeated when current.Target.Validated => "AUTO · BOSS DEATH",
            CampaignCompletionKind.ObjectChanged when current.Target.Validated => "AUTO · OBJECT STATE",
            _ => "MANUAL CHECK",
        };
        if (!current.Target.Validated
            && current.Target.IsSpatial
            && current.Completion.Kind != CampaignCompletionKind.StableAreaEntry)
            completionBadge = "MANUAL · UNVALIDATED";

        _view = new CampaignView(
            true, visible, profileHash, ChapterLabel(current), current, next,
            zoneObjectives, profile.Completed,
            requiredCompleted, requiredObjectives.Length,
            fullCompleted, _catalog.Objectives.Count,
            chapterCompleted, chapterObjectives.Length,
            areaCompleted, areaObjectives.Length,
            target, completionBadge, DescribeTarget(target), profile.Dismissed);
        return _view;
    }

    private CampaignObjective[] RemainingSequence(
        CampaignProfileSnapshot profile,
        string areaCode,
        CampaignGuideMode mode)
    {
        var remaining = _catalog.Objectives
            .Where(x => IncludedByMode(x, mode) && !profile.Completed.Contains(x.Id))
            .ToArray();
        if (remaining.Length == 0 || !string.Equals(remaining[0].Chapter, "interludes", StringComparison.OrdinalIgnoreCase))
            return remaining;

        var activeBranch = remaining
            .Where(x => !string.IsNullOrWhiteSpace(x.Branch))
            .Select(x => x.Branch!)
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(branch => _catalog.Objectives
                .Where(x => string.Equals(x.Branch, branch, StringComparison.Ordinal))
                .Any(x => x.Target.AllowedAreaCodes.Contains(areaCode, StringComparer.OrdinalIgnoreCase)
                          || string.Equals(x.Target.DestinationAreaCode, areaCode, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(x.Completion.ExpectedAreaCode, areaCode, StringComparison.OrdinalIgnoreCase)));
        if (activeBranch is null)
            return remaining;

        return remaining
            .OrderBy(x => string.Equals(x.Branch, activeBranch, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(x => x.Order)
            .ToArray();
    }

    private static bool IncludedByMode(CampaignObjective objective, CampaignGuideMode mode)
        => mode == CampaignGuideMode.FullClear || !objective.Optional;

    private static string ChapterLabel(CampaignObjective objective)
        => objective.Branch switch
        {
            "interlude-5.1" => "INTERLUDE 5.1",
            "interlude-5.2" => "INTERLUDE 5.2",
            "interlude-5.3" => "INTERLUDE 5.3",
            _ => objective.Chapter.ToLowerInvariant() switch
            {
                "act1" => "ACT I",
                "act2" => "ACT II",
                "act3" => "ACT III",
                "act4" => "ACT IV",
                "interludes" => "INTERLUDES",
                _ => objective.Chapter.ToUpperInvariant(),
            },
        };

    public void CompleteCurrent()
    {
        if (_profileHash.Length == 0 || _view.Current is null) return;
        _store.SetComplete(_profileHash, _view.Current.Id, true);
        ResetObservations();
    }

    public void SetObjectiveComplete(string objectiveId, bool complete)
    {
        SetObjectivesComplete([objectiveId], complete);
    }

    public void SetObjectivesComplete(IEnumerable<string> objectiveIds, bool complete)
    {
        if (_profileHash.Length == 0) return;
        var knownIds = objectiveIds
            .Where(x => _catalog.Find(x) is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _store.SetCompleted(_profileHash, knownIds, complete);
        ResetObservations();
    }

    public string ExportCurrentCharacter()
    {
        if (_profileHash.Length == 0) return "";
        var profile = _store.Snapshot(_profileHash);
        return CampaignProgressCodec.Encode(_catalog.GuideVersion, profile.Completed);
    }

    public bool ImportCurrentCharacter(string progressCode)
    {
        if (_profileHash.Length == 0) return false;
        string[] knownIds;
        if (CampaignProgressCodec.TryDecode(progressCode, out var imported))
        {
            knownIds = imported
                .Where(x => _catalog.Find(x) is not null)
                .ToArray();
        }
        else if (CampaignProgressCodec.TryDecodeWebsite(progressCode, out var sourceRows))
        {
            var completedRows = sourceRows
                .Select(x => (x.Chapter, x.Row))
                .ToHashSet();
            knownIds = _catalog.Objectives
                .Where(objective => objective.Source.Any(source =>
                    completedRows.Contains((source.Chapter, source.Row))))
                .Select(x => x.Id)
                .ToArray();
        }
        else
        {
            return false;
        }
        _store.ReplaceCompleted(_profileHash, knownIds);
        ResetObservations();
        return true;
    }

    public void Back()
    {
        if (_profileHash.Length == 0) return;
        var profile = _store.Snapshot(_profileHash);
        var previous = _catalog.Objectives.LastOrDefault(x => profile.Completed.Contains(x.Id));
        if (previous is null) return;
        _store.SetComplete(_profileHash, previous.Id, false);
        ResetObservations();
    }

    public void SetDismissed(bool dismissed)
    {
        if (_profileHash.Length == 0) return;
        _store.SetDismissed(_profileHash, dismissed);
    }

    public void ResetCurrentCharacter()
    {
        if (_profileHash.Length == 0) return;
        _store.Reset(_profileHash);
        ResetObservations();
    }

    private bool ObserveDefinitiveCompletion(
        CampaignObjective objective,
        CampaignTargetMatch target,
        string areaCode)
    {
        switch (objective.Completion.Kind)
        {
            case CampaignCompletionKind.StableAreaEntry:
            {
                var expected = objective.Completion.ExpectedAreaCode ?? "";
                if (!string.Equals(areaCode, expected, StringComparison.OrdinalIgnoreCase))
                {
                    _stableArea = "";
                    _stableAreaTicks = 0;
                    return false;
                }
                if (string.Equals(_stableArea, areaCode, StringComparison.OrdinalIgnoreCase))
                    _stableAreaTicks++;
                else
                {
                    _stableArea = areaCode;
                    _stableAreaTicks = 1;
                }
                return _stableAreaTicks >= Math.Max(2, objective.Completion.StableTicks);
            }
            case CampaignCompletionKind.BossDefeated:
            {
                if (target.EntityId is not { } id) return false;
                if (target.IsAliveBoss) _observedAliveBosses.Add(id);
                return target.IsDeadBoss && _observedAliveBosses.Contains(id);
            }
            case CampaignCompletionKind.ObjectChanged:
            {
                if (target.EntityId is not { } id) return false;
                if (!target.Opened && !target.IconComplete)
                {
                    _observedIncompleteObjects.Add(id);
                    return false;
                }
                return _observedIncompleteObjects.Contains(id);
            }
            default:
                return false;
        }
    }

    private void ResetObservations(bool keepObjective = false)
    {
        if (!keepObjective) _observedObjectiveId = "";
        _stableArea = "";
        _stableAreaTicks = 0;
        _observedAliveBosses.Clear();
        _observedIncompleteObjects.Clear();
    }

    private static string DescribeTarget(CampaignTargetMatch target)
        => target.Status switch
        {
            CampaignTargetStatus.Resolved => "Exact target resolved",
            CampaignTargetStatus.MultipleCandidates => $"{target.CandidateCount} exact targets · nearest selected",
            CampaignTargetStatus.WrongArea => "Destination is in another area",
            CampaignTargetStatus.Uncurated => "Text guidance · capture needed",
            CampaignTargetStatus.NotFound => "Exact target not observed yet",
            _ => "Text guidance",
        };
}
