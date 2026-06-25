namespace POE2Radar.Core.Game;

/// <summary>
/// Locates the persistent Atlas panel UiElement under UiRoot when
/// <see cref="Poe2.AtlasPanel.UiRootChildIndex"/> drifts after a patch. Scores direct children by
/// child-count signature (~18) and visible-bit toggling (open vs closed).
/// </summary>
public static class AtlasPanelResolver
{
    private static nint _cachedForUiRoot;
    private static int _cachedIndex = Poe2.AtlasPanel.UiRootChildIndex;
    private static nint _cachedPanel;
    private static readonly Dictionary<int, bool> _lastVisible = new();
    private static readonly Dictionary<int, int> _toggleScore = new();

    /// <summary>Diagnostic snapshot for overlay logging and Research probes.</summary>
    public readonly record struct PanelDiag(
        bool Resolved,
        int Index,
        int HardcodedIndex,
        bool HardcodedOpen,
        bool ResolvedOpen,
        int ToggleScore,
        int ChildCount);

    public static void Invalidate(nint uiRoot = 0)
    {
        if (uiRoot == 0 || _cachedForUiRoot == uiRoot)
        {
            DropCachedPanel();
        }
        _lastVisible.Clear();
        _toggleScore.Clear();
    }

    public static void NotifyPanelOpened(nint uiRoot)
    {
        if (uiRoot != 0 && _cachedForUiRoot == uiRoot)
            DropCachedPanel();
    }

    /// <summary>Call each atlas tick to accumulate visible-bit toggles per UiRoot child index.</summary>
    public static void RecordSample(MemoryReader reader, nint uiRoot)
    {
        if (uiRoot == 0) return;
        var opened = false;
        foreach (var (index, el, _) in EnumerateCandidates(reader, uiRoot))
        {
            var vis = ReadVisible(reader, el);
            if (!_lastVisible.TryGetValue(index, out var prev))
            {
                _lastVisible[index] = vis;
                continue;
            }
            if (prev == vis) continue;
            _toggleScore[index] = _toggleScore.GetValueOrDefault(index) + 1;
            _lastVisible[index] = vis;
            opened |= vis;
        }
        if (opened && _cachedForUiRoot == uiRoot)
            DropCachedPanel();
    }

    public static bool TryResolvePanel(MemoryReader reader, nint uiRoot, out nint panel, out int index)
    {
        panel = 0;
        index = -1;
        if (uiRoot == 0) return false;

        if (_cachedForUiRoot == uiRoot
            && _cachedPanel != 0
            && TryChildAt(reader, uiRoot, _cachedIndex, out var livePanel)
            && livePanel == _cachedPanel
            && IsValidPanel(reader, _cachedPanel))
        {
            panel = _cachedPanel;
            index = _cachedIndex;
            return true;
        }
        if (_cachedForUiRoot == uiRoot)
            DropCachedPanel();

        var bestIndex = DiscoverBestIndex(reader, uiRoot);
        if (bestIndex < 0)
            bestIndex = Poe2.AtlasPanel.UiRootChildIndex;

        if (!TryChildAt(reader, uiRoot, bestIndex, out panel) || !IsValidPanel(reader, panel))
            return false;

        _cachedForUiRoot = uiRoot;
        _cachedIndex = bestIndex;
        _cachedPanel = panel;
        index = bestIndex;
        return true;
    }

    public static bool IsPanelOpen(MemoryReader reader, nint uiRoot)
    {
        if (!TryResolvePanel(reader, uiRoot, out var panel, out _)) return false;
        return IsPanelHierarchicallyOpen(reader, panel);
    }

    public static bool IsPanelHierarchicallyOpen(MemoryReader reader, nint panel)
        => ReadHierarchicallyVisible(reader, panel);

    public static PanelDiag GetDiag(MemoryReader reader, nint uiRoot)
    {
        var hardcodedOpen = IsPanelOpenAtIndex(reader, uiRoot, Poe2.AtlasPanel.UiRootChildIndex, out var hardcodedChildCount);
        var resolvedOpen = IsPanelOpen(reader, uiRoot);
        var resolved = TryResolvePanel(reader, uiRoot, out _, out var index);
        var toggle = index >= 0 ? _toggleScore.GetValueOrDefault(index) : 0;
        var childCount = index >= 0 && TryChildAt(reader, uiRoot, index, out var p)
            ? CountDirectChildren(reader, p)
            : hardcodedChildCount;
        return new PanelDiag(resolved, index, Poe2.AtlasPanel.UiRootChildIndex, hardcodedOpen, resolvedOpen, toggle, childCount);
    }

    /// <summary>Score a UiRoot child as the atlas panel candidate (unit-testable).</summary>
    public static int ScoreCandidate(int childIndex, int directChildCount, int toggleCount, bool visibleNow = false)
    {
        var score = toggleCount * 1000;
        if (visibleNow) score += 500;
        if (directChildCount is >= 5 and <= 30)
            score += 200 - Math.Abs(directChildCount - Poe2.AtlasPanel.ExpectedChildCount) * 15;
        // Endgame shell @ ~12 (24 children) toggles too but is hidden when the map is open — deprioritize.
        if (directChildCount is >= 20 and <= 28) score -= 400;
        if (childIndex == Poe2.AtlasPanel.UiRootChildIndex) score += 8;
        return score;
    }

    public static int PickBestIndex(IReadOnlyList<(int Index, int ChildCount, int ToggleCount, bool VisibleNow)> candidates)
    {
        if (candidates.Count == 0) return -1;
        var best = candidates[0];
        var bestScore = ScoreCandidate(best.Index, best.ChildCount, best.ToggleCount, best.VisibleNow);
        for (var i = 1; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var s = ScoreCandidate(c.Index, c.ChildCount, c.ToggleCount, c.VisibleNow);
            if (s > bestScore) { best = c; bestScore = s; }
        }
        return bestScore > 0 ? best.Index : -1;
    }

    private static int DiscoverBestIndex(MemoryReader reader, nint uiRoot)
    {
        var list = new List<(int Index, int ChildCount, int ToggleCount, bool VisibleNow)>();
        foreach (var (index, el, childCount) in EnumerateCandidates(reader, uiRoot))
        {
            if (childCount is < 5 or > 30) continue;
            list.Add((index, childCount, _toggleScore.GetValueOrDefault(index), ReadVisible(reader, el)));
        }
        var best = PickBestIndex(list);
        if (best >= 0) return best;

        // Atlas open: map panel is visible with ~8 direct children; Endgame shell (~24 @ index 12) is hidden.
        foreach (var (index, el, childCount) in EnumerateCandidates(reader, uiRoot))
        {
            if (childCount is < 5 or > 15) continue;
            if (!ReadVisible(reader, el)) continue;
            return index;
        }
        return -1;
    }

    private static IEnumerable<(int Index, nint Element, int ChildCount)> EnumerateCandidates(MemoryReader reader, nint uiRoot)
    {
        var first = SafePtr(reader, uiRoot + Poe2.UiElement.Children);
        if (first == 0) yield break;
        if (!reader.TryReadStruct<nint>(uiRoot + Poe2.UiElement.ChildrenEnd, out var last)) yield break;
        var count = ((long)last - (long)first) / 8;
        if (count is <= 0 or > 512) yield break;
        for (long i = 0; i < count; i++)
        {
            var el = SafePtr(reader, first + (nint)(i * 8));
            if (!IsValidPanel(reader, el)) continue;
            yield return ((int)i, el, CountDirectChildren(reader, el));
        }
    }

    private static bool IsPanelOpenAtIndex(MemoryReader reader, nint uiRoot, int index, out int childCount)
    {
        childCount = 0;
        if (!TryChildAt(reader, uiRoot, index, out var panel) || !IsValidPanel(reader, panel)) return false;
        childCount = CountDirectChildren(reader, panel);
        return IsPanelHierarchicallyOpen(reader, panel);
    }

    private static bool TryChildAt(MemoryReader reader, nint uiRoot, int index, out nint child)
    {
        child = 0;
        var first = SafePtr(reader, uiRoot + Poe2.UiElement.Children);
        if (first == 0) return false;
        child = SafePtr(reader, first + (nint)(index * 8));
        return child != 0;
    }

    private static bool IsValidPanel(MemoryReader reader, nint el)
        => el != 0 && SafePtr(reader, el + Poe2.UiElement.Self) == el;

    private static int CountDirectChildren(MemoryReader reader, nint el)
    {
        var first = SafePtr(reader, el + Poe2.UiElement.Children);
        if (first == 0) return 0;
        if (!reader.TryReadStruct<nint>(el + Poe2.UiElement.ChildrenEnd, out var last)) return 0;
        var n = ((long)last - (long)first) / 8;
        return n is > 0 and <= 8192 ? (int)n : 0;
    }

    private static bool ReadVisible(MemoryReader reader, nint el)
    {
        if (!reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var fl)) return false;
        return ((fl >> Poe2.UiElement.FlagVisibleBit) & 1) != 0;
    }

    private static bool ReadHierarchicallyVisible(MemoryReader reader, nint el)
    {
        var cur = el;
        var guard = 0;
        while (cur != 0 && guard++ < 16)
        {
            if (!ReadVisible(reader, cur)) return false;
            var parent = SafePtr(reader, cur + Poe2.UiElement.Parent);
            if (parent == cur) break;
            cur = parent;
        }
        return true;
    }

    private static void DropCachedPanel()
    {
        _cachedForUiRoot = 0;
        _cachedPanel = 0;
        _cachedIndex = Poe2.AtlasPanel.UiRootChildIndex;
    }

    private static nint SafePtr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
