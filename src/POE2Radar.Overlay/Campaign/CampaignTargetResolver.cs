using System.Text.RegularExpressions;
using POE2Radar.Core.Campaign;
using POE2Radar.Core.Game;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Campaign;

/// <summary>
/// Resolves only curated exact metadata/path globs and exact server-icon names. It deliberately has
/// no display-name, Contains, edit-distance, or other fuzzy fallback.
/// </summary>
public sealed class CampaignTargetResolver
{
    private readonly record struct Candidate(
        NumVec2 Grid,
        uint? EntityId,
        bool Opened,
        bool IconComplete,
        int HpCur,
        int HpMax,
        string Identity);

    public CampaignTargetMatch Resolve(CampaignObjective objective, CampaignFrame frame)
    {
        var spec = objective.Target;
        if (!spec.IsSpatial)
            return Result(CampaignTargetStatus.NonSpatial, spec, "Text guidance only.");

        if (spec.AllowedAreaCodes.Length > 0
            && !spec.AllowedAreaCodes.Contains(frame.AreaCode, StringComparer.OrdinalIgnoreCase))
            return Result(
                CampaignTargetStatus.WrongArea,
                spec,
                $"Expected {string.Join(" or ", spec.AllowedAreaCodes)}; current area is {frame.AreaCode}.");

        if (!spec.Validated)
            return Result(
                CampaignTargetStatus.Uncurated,
                spec,
                spec.Guidance ?? "Matcher awaits live survey validation; no marker will be invented.");

        var candidates = new List<Candidate>();
        if (spec.MetadataGlobs.Length > 0)
        {
            foreach (var entity in frame.Entities)
            {
                var metadata = entity.Metadata ?? "";
                var itemMetadata = entity.ItemMetadata ?? "";
                if (!spec.MetadataGlobs.Any(glob =>
                        GlobMatches(glob, metadata) || (itemMetadata.Length > 0 && GlobMatches(glob, itemMetadata))))
                    continue;
                candidates.Add(new Candidate(
                    entity.Grid, entity.Id, entity.Opened, entity.IconComplete,
                    entity.HpCur, entity.HpMax, metadata));
            }
        }

        if (spec.ServerIconNames.Length > 0)
        {
            foreach (var icon in frame.ServerIcons)
                if (spec.ServerIconNames.Contains(icon.Name, StringComparer.Ordinal))
                    candidates.Add(new Candidate(icon.Grid, null, false, false, 0, 0, $"server-icon:{icon.Name}"));
        }

        if (spec.LandmarkPathGlobs.Length > 0)
        {
            foreach (var landmark in frame.Landmarks)
                if (spec.LandmarkPathGlobs.Any(glob => GlobMatches(glob, landmark.Path)))
                    candidates.Add(new Candidate(landmark.Center, null, false, false, 0, 0, landmark.Path));
        }

        if (spec.MetadataGlobs.Length == 0
            && spec.ServerIconNames.Length == 0
            && spec.LandmarkPathGlobs.Length == 0)
            return Result(CampaignTargetStatus.Uncurated, spec, spec.Guidance ?? "No validated spatial matcher is curated yet.");

        if (candidates.Count == 0)
            return Result(CampaignTargetStatus.NotFound, spec, "Exact curated target is not present in the raw world snapshot.");

        var selected = candidates
            .OrderBy(x => NumVec2.DistanceSquared(x.Grid, frame.PlayerGrid))
            .ThenBy(x => x.Identity, StringComparer.Ordinal)
            .First();
        var status = candidates.Count == 1 ? CampaignTargetStatus.Resolved : CampaignTargetStatus.MultipleCandidates;
        var diagnostic = candidates.Count == 1
            ? selected.Identity
            : $"{candidates.Count} exact candidates; routing to nearest: {selected.Identity}";
        return new CampaignTargetMatch(
            status, spec.Label, selected.Grid, selected.EntityId, candidates.Count,
            selected.Opened, selected.IconComplete, selected.HpCur, selected.HpMax, diagnostic);
    }

    public static bool GlobMatches(string glob, string value)
    {
        if (string.IsNullOrWhiteSpace(glob) || string.IsNullOrWhiteSpace(value)) return false;
        if (!glob.Contains('*', StringComparison.Ordinal))
            return string.Equals(glob, value, StringComparison.Ordinal);
        var pattern = "\\A" + Regex.Escape(glob).Replace("\\*", ".*", StringComparison.Ordinal) + "\\z";
        return Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant);
    }

    private static CampaignTargetMatch Result(
        CampaignTargetStatus status,
        CampaignTargetSpec spec,
        string diagnostic)
        => new(status, spec.Label, null, null, 0, false, false, 0, 0, diagnostic);
}
