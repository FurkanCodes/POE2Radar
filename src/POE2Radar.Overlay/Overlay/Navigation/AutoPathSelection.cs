namespace POE2Radar.Overlay.Navigation;

/// <summary>
/// GH-static auto-path pool: zone-stable qualifying targets, separate from capped manual picks.
/// </summary>
public static class AutoPathSelection
{
    public const int MaxManualTargets = 8;
    public const int MaxAutoTargets = 24;

    public static List<string> FilterDesiredAuto(
        IEnumerable<string> candidates,
        IReadOnlySet<string> reachedKeys,
        IReadOnlySet<string> dismissedIds,
        int maxAuto = MaxAutoTargets)
    {
        var result = new List<string>(Math.Min(maxAuto, 32));
        foreach (var id in candidates)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (reachedKeys.Contains(id) || dismissedIds.Contains(id)) continue;
            result.Add(id);
            if (result.Count >= maxAuto) break;
        }
        return result;
    }

    public static void ApplyAutoDiff(
        List<string> selectedIds,
        HashSet<string> autoSelectedIds,
        IReadOnlyList<string> desiredAuto)
    {
        var desiredSet = new HashSet<string>(desiredAuto, StringComparer.Ordinal);
        foreach (var id in autoSelectedIds.ToList())
        {
            if (desiredSet.Contains(id)) continue;
            autoSelectedIds.Remove(id);
            selectedIds.Remove(id);
        }

        foreach (var id in desiredAuto)
        {
            if (!autoSelectedIds.Add(id)) continue;
            if (!selectedIds.Contains(id))
                selectedIds.Add(id);
        }
    }

    public static int CountManual(IReadOnlyList<string> selectedIds, IReadOnlySet<string> autoSelectedIds)
    {
        var n = 0;
        foreach (var id in selectedIds)
        {
            if (!autoSelectedIds.Contains(id)) n++;
        }
        return n;
    }

    public static bool CanAddManual(IReadOnlyList<string> selectedIds, IReadOnlySet<string> autoSelectedIds, string id)
        => autoSelectedIds.Contains(id) || CountManual(selectedIds, autoSelectedIds) < MaxManualTargets;
}
