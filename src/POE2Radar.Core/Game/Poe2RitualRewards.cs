// Ritual Favours window reader — derived from GameHelper RitualHelper (GPL-3.0).
namespace POE2Radar.Core.Game;

/// <summary>Read-only Ritual tribute-shop reward tiles. Fast index chain first; throttled BFS fallback.</summary>
public sealed class Poe2RitualRewards
{
    public enum PathKind { Closed, FastChain, CachedFallback, BfsFallback }

    public readonly record struct RitualWindowState(
        bool PanelOpen,
        bool SignatureDetected,
        int InBoundsTiles,
        long ItemSignature,
        PathKind PathKind,
        bool FastPathHit);

    public sealed class RitualPerfCounters
    {
        public long FastChainHits;
        public long CachedGridHits;
        public long BfsHits;
        public long FullReads;
        public long CacheHits;
        public double LastProbeMs;
    }

    private readonly MemoryReader _reader;
    private readonly Poe2Live _live;
    private int _cachedTileItemOffset;

    private readonly List<RitualRewardTile> _cachedRewards = new();
    private nint _cachedBranchIgs;
    private Poe2UiAnchors.BranchKind _cachedBranch = Poe2UiAnchors.BranchKind.None;
    private nint _branchRoot;
    private nint _cachedGameUi;
    private nint _cachedControllerUi;
    private nint _probeRoot;
    private bool _preferController;
    private int _cachedFastChainA = -1;
    private int _cachedFastChainB = -1;
    private nint _cachedFastChainRoot;
    private nint _cachedGrid;
    private nint _bfsGrid;
    private long _rewardSignature;
    private DateTime _nextBfsUtc = DateTime.MinValue;
    private DateTime _nextColdUtc = DateTime.MinValue;

    public Poe2RitualRewards(MemoryReader reader, Poe2Live live)
    {
        _reader = reader;
        _live = live;
    }

    public Poe2UiAnchors.BranchKind LastBranchKind => _cachedBranch;
    public nint LastBranchRoot => _branchRoot;
    public int LastFastChainChildA { get; private set; } = Poe2.Ritual.FastChainChildA;
    public int LastFastChainChildB { get; private set; } = Poe2.Ritual.FastChainChildB;

    public bool PanelOpen { get; private set; }
    public PathKind LastPathKind { get; private set; } = PathKind.Closed;
    public bool LastIdleProbeFastPathHit { get; private set; }
    public long LastItemSignature { get; private set; }
    public int InBoundsTiles { get; private set; }
    public RitualPerfCounters Perf { get; } = new();

    public readonly record struct RitualRewardTile(Poe2Live.RitualReward Reward);

    /// <summary>Cheap window pass: panel open + tile signature only — no full item identity reads.</summary>
    public RitualWindowState ReadWindowState(
        nint inGameState,
        float winW,
        float winH,
        bool allowFullLocate,
        Poe2UiAnchors.BranchKind probeHint,
        bool forceBfsFallback = false)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        RitualWindowState Finish(RitualWindowState state)
        {
            Perf.LastProbeMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
                / System.Diagnostics.Stopwatch.Frequency;
            return state;
        }

        LastIdleProbeFastPathHit = false;
        if (inGameState == 0 || winW <= 0 || winH <= 0)
        {
            PanelOpen = false;
            LastPathKind = PathKind.Closed;
            InBoundsTiles = 0;
            return Finish(new RitualWindowState(false, false, 0, 0, PathKind.Closed, false));
        }

        var now = DateTime.UtcNow;
        SyncBranchCache(inGameState);
        _preferController = UiBranchCandidates.PreferControllerOrder(probeHint);

        if (!forceBfsFallback && !PanelOpen && now < _nextColdUtc)
            return Finish(new RitualWindowState(false, false, 0, 0, PathKind.Closed, false));

        if (!forceBfsFallback && PanelOpen && _cachedGrid != 0 && IsValidRewardGrid(_cachedGrid))
        {
            _probeRoot = _branchRoot;
            if (UiCountsVisible(_cachedGrid))
            {
                var sig = ComputeGridSignature(_cachedGrid);
                var count = CountSignatureTiles(_cachedGrid);
                if (count > 0)
                {
                    LastItemSignature = sig;
                    InBoundsTiles = count;
                    LastPathKind = PathKind.CachedFallback;
                    LastIdleProbeFastPathHit = true;
                    Perf.CachedGridHits++;
                    return Finish(new RitualWindowState(true, true, count, sig, PathKind.CachedFallback, true));
                }
            }
        }

        if (!forceBfsFallback && (TryFastChain(inGameState, probeHint, allowBrute: false, out var fastGrid, out var branch)
            || TryFastChain(inGameState, AlternateProbeHint(probeHint), allowBrute: false, out fastGrid, out branch)))
        {
            _probeRoot = _branchRoot;
            if (!UiCountsVisible(fastGrid))
            {
                ClearOpenSession();
                _nextColdUtc = now.AddMilliseconds(Poe2.Ritual.ColdClosedThrottleMs);
                return Finish(new RitualWindowState(false, false, 0, 0, PathKind.FastChain, false));
            }

            _cachedGrid = fastGrid;
            _cachedBranch = branch;
            LastPathKind = PathKind.FastChain;
            LastIdleProbeFastPathHit = true;
            Perf.FastChainHits++;
            var tileSig = ComputeGridSignature(fastGrid);
            var tiles = CountSignatureTiles(fastGrid);
            if (tiles > 0)
            {
                PanelOpen = true;
                LastItemSignature = tileSig;
                InBoundsTiles = tiles;
                _nextColdUtc = DateTime.MinValue;
                return Finish(new RitualWindowState(true, true, tiles, tileSig, PathKind.FastChain, true));
            }
        }

        if (!forceBfsFallback && _cachedGrid != 0 && IsValidRewardGrid(_cachedGrid))
        {
            _probeRoot = _branchRoot;
            if (!UiCountsVisible(_cachedGrid))
            {
                ClearOpenSession();
                _nextColdUtc = now.AddMilliseconds(Poe2.Ritual.ColdClosedThrottleMs);
                return Finish(new RitualWindowState(false, false, 0, 0, PathKind.CachedFallback, false));
            }

            var sig = ComputeGridSignature(_cachedGrid);
            var count = CountSignatureTiles(_cachedGrid);
            if (count > 0)
            {
                PanelOpen = true;
                LastItemSignature = sig;
                InBoundsTiles = count;
                LastPathKind = PathKind.CachedFallback;
                Perf.CachedGridHits++;
                return Finish(new RitualWindowState(true, true, count, sig, PathKind.CachedFallback, true));
            }
        }

        if ((allowFullLocate || forceBfsFallback)
            && TryLocatePanelGrid(inGameState, probeHint, forceBfsFallback, now, out var scanGrid, out var branchRoot))
        {
            _cachedGrid = scanGrid;
            _branchRoot = branchRoot;
            _cachedBranch = BranchForRoot(branchRoot);
            LastPathKind = PathKind.BfsFallback;
            Perf.BfsHits++;
            var tileSig = ComputeGridSignature(scanGrid);
            var tiles = CountSignatureTiles(scanGrid);
            if (tiles > 0)
            {
                PanelOpen = true;
                LastItemSignature = tileSig;
                InBoundsTiles = tiles;
                _nextColdUtc = DateTime.MinValue;
                return Finish(new RitualWindowState(true, true, tiles, tileSig, PathKind.BfsFallback, false));
            }
        }

        PanelOpen = false;
        LastPathKind = PathKind.Closed;
        InBoundsTiles = 0;
        _bfsGrid = 0;
        _nextColdUtc = now.AddMilliseconds(Poe2.Ritual.ColdClosedThrottleMs);
        return Finish(new RitualWindowState(false, false, 0, 0, PathKind.Closed, false));
    }

    /// <summary>Full item reads on cached grid — skipped when tile signature unchanged unless forced.</summary>
    public IReadOnlyList<RitualRewardTile> ReadRewardsFromCachedWindow(
        float winW,
        float winH,
        bool forceRefresh,
        Func<string, string?>? prettyNameLookup = null)
    {
        if (_cachedGrid == 0 || !PanelOpen) return Array.Empty<RitualRewardTile>();

        var tileSig = ComputeGridSignature(_cachedGrid);
        LastItemSignature = tileSig;
        InBoundsTiles = CountSignatureTiles(_cachedGrid);
        if (!forceRefresh && tileSig != 0 && tileSig == _rewardSignature && _cachedRewards.Count > 0)
        {
            Perf.CacheHits++;
            return _cachedRewards;
        }

        Perf.FullReads++;
        _cachedRewards.Clear();
        _cachedRewards.AddRange(ReadTilesFromGrid(_cachedGrid, winW, winH, prettyNameLookup));
        _rewardSignature = tileSig;
        return _cachedRewards;
    }

    /// <summary>Legacy one-shot read for research probes.</summary>
    public IReadOnlyList<RitualRewardTile> ReadRewards(
        nint inGameState,
        float winW,
        float winH,
        bool forceBfsFallback = false,
        Func<string, string?>? prettyNameLookup = null,
        Poe2UiAnchors.BranchKind probeHint = Poe2UiAnchors.BranchKind.None)
    {
        var state = ReadWindowState(inGameState, winW, winH, allowFullLocate: true, probeHint, forceBfsFallback);
        if (!state.PanelOpen) return Array.Empty<RitualRewardTile>();
        return ReadRewardsFromCachedWindow(winW, winH, forceRefresh: true, prettyNameLookup);
    }

    public void ResetSession()
    {
        ClearOpenSession();
        _cachedBranchIgs = 0;
        _cachedBranch = Poe2UiAnchors.BranchKind.None;
        _branchRoot = 0;
        _bfsGrid = 0;
        _nextBfsUtc = DateTime.MinValue;
        _nextColdUtc = DateTime.MinValue;
        _cachedTileItemOffset = 0;
        _cachedFastChainA = -1;
        _cachedFastChainB = -1;
        _cachedFastChainRoot = 0;
        Poe2UiAnchors.InvalidateDiscoverCache();
    }

    private void ClearOpenSession()
    {
        PanelOpen = false;
        _cachedGrid = 0;
        _bfsGrid = 0;
        _rewardSignature = 0;
        LastItemSignature = 0;
        InBoundsTiles = 0;
        _cachedRewards.Clear();
        LastPathKind = PathKind.Closed;
        LastIdleProbeFastPathHit = false;
    }

    private void SyncBranchCache(nint inGameState)
    {
        if (_cachedBranchIgs == inGameState) return;
        _cachedBranch = Poe2UiAnchors.BranchKind.None;
        _branchRoot = 0;
        _cachedGrid = 0;
        _bfsGrid = 0;
        _cachedBranchIgs = inGameState;
        _rewardSignature = 0;
        _cachedRewards.Clear();
        _cachedTileItemOffset = 0;
        _cachedFastChainA = -1;
        _cachedFastChainB = -1;
        _cachedFastChainRoot = 0;
    }

    private void DiscoverAnchors(nint inGameState)
    {
        Poe2UiAnchors.TryDiscoverCached(_reader, inGameState, allowScan: true, out _cachedGameUi, out _cachedControllerUi);
        Poe2UiAnchors.SanitizeBranches(_reader, ref _cachedGameUi, ref _cachedControllerUi);
    }

    private void RememberFastChainIndices(nint root, int indexA, int indexB)
    {
        _cachedFastChainRoot = root;
        _cachedFastChainA = indexA;
        _cachedFastChainB = indexB;
        LastFastChainChildA = indexA;
        LastFastChainChildB = indexB;
    }

    private bool IsPlausibleBranchRoot(nint root)
        => Poe2UiAnchors.IsPlausibleBranch(_reader, root);

    private Poe2UiAnchors.BranchKind BranchForRoot(nint root)
        => UiBranchCandidates.BranchForRoot(root, _cachedGameUi, _cachedControllerUi);

    private static Poe2UiAnchors.BranchKind AlternateProbeHint(Poe2UiAnchors.BranchKind hint)
        => hint == Poe2UiAnchors.BranchKind.Controller
            ? Poe2UiAnchors.BranchKind.KeyboardMouse
            : Poe2UiAnchors.BranchKind.Controller;

    private bool UiCountsVisible(nint el)
        => _probeRoot == _cachedControllerUi && _cachedControllerUi != 0
           || _preferController
           || Visible(el);

    private bool TryFastChain(nint inGameState, Poe2UiAnchors.BranchKind probeHint, bool allowBrute, out nint grid, out Poe2UiAnchors.BranchKind branch)
    {
        grid = 0;
        branch = _cachedBranch;
        DiscoverAnchors(inGameState);

        nint bestRoot = 0;
        nint bestGrid = 0;
        var bestTiles = 0;

        foreach (var root in _live.GetUiBranches(inGameState, probeHint, _branchRoot))
        {
            if (root == 0 || !IsPlausibleBranchRoot(root)) continue;
            _probeRoot = root;
            if (!TryFastChainOnRoot(root, allowBrute, requireRitualSignature: false, out var candidateGrid)) continue;
            var tiles = CountSignatureTiles(candidateGrid);
            if (tiles <= bestTiles) continue;
            bestTiles = tiles;
            bestGrid = candidateGrid;
            bestRoot = root;
        }

        if (bestTiles <= 0)
        {
            _probeRoot = 0;
            return false;
        }

        _probeRoot = bestRoot;
        grid = bestGrid;
        branch = BranchForRoot(bestRoot);
        _cachedBranch = branch;
        _branchRoot = bestRoot;
        return true;
    }

    private bool TryFastChainOnRoot(
        nint root,
        bool allowBrute,
        bool requireRitualSignature,
        out nint grid)
    {
        grid = 0;
        if (!IsPlausibleBranchRoot(root)) return false;

        if (root == _cachedFastChainRoot && _cachedFastChainA >= 0 && _cachedFastChainB >= 0
            && TryFastChainAtIndices(root, _cachedFastChainA, _cachedFastChainB, requireRitualSignature, out grid))
            return true;

        if (TryFastChainAtIndices(root, Poe2.Ritual.FastChainChildA, Poe2.Ritual.FastChainChildB, requireRitualSignature, out grid))
        {
            RememberFastChainIndices(root, Poe2.Ritual.FastChainChildA, Poe2.Ritual.FastChainChildB);
            return true;
        }

        if (!allowBrute) return false;
        if (!TryBruteFastChainOnRoot(root, out grid, out var indexA, out var indexB)) return false;
        RememberFastChainIndices(root, indexA, indexB);
        return true;
    }

    private bool TryFastChainAtIndices(
        nint root,
        int indexA,
        int indexB,
        bool requireRitualSignature,
        out nint grid)
    {
        grid = 0;
        var a = Child(root, indexA);
        if (a == 0) return false;
        var window = Child(a, indexB);
        if (window == 0 || !UiCountsVisible(window)) return false;
        if (requireRitualSignature && !HasSignatureInSubtree(window, maxDepth: 3, maxNodes: 64)) return false;
        grid = IsValidRewardGrid(window) ? window : FindRewardGridChild(window);
        if (grid == 0) grid = window;
        if (grid == 0 || !IsValidRewardGrid(grid)) return false;
        return CountSignatureTiles(grid) > 0;
    }

    private bool TryBruteFastChainOnRoot(nint root, out nint grid, out int indexA, out int indexB)
    {
        grid = 0;
        indexA = indexB = -1;
        if (!Children(root, out _, out var rootN) || rootN <= 0 || rootN > 256) return false;

        nint bestGrid = 0;
        var bestTiles = 0;
        var bestA = -1;
        var bestB = -1;

        for (var a = 0; a < rootN; a++)
        {
            var ritualRoot = Child(root, a);
            if (ritualRoot == 0 || !Children(ritualRoot, out _, out var bN) || bN <= 0 || bN > 64) continue;
            for (var b = 0; b < bN; b++)
            {
                var window = Child(ritualRoot, b);
                if (window == 0 || !UiCountsVisible(window)) continue;
                if (!HasSignatureInSubtree(window, maxDepth: 3, maxNodes: 64)) continue;
                var candidate = IsValidRewardGrid(window) ? window : FindRewardGridChild(window);
                if (candidate == 0) candidate = window;
                if (candidate == 0 || !IsValidRewardGrid(candidate)) continue;
                var tiles = CountSignatureTiles(candidate);
                if (tiles <= bestTiles) continue;
                bestTiles = tiles;
                bestGrid = candidate;
                bestA = a;
                bestB = b;
            }
        }

        if (bestTiles <= 0) return false;
        grid = bestGrid;
        indexA = bestA;
        indexB = bestB;
        return true;
    }

    private bool TryLocatePanelGrid(
        nint inGameState,
        Poe2UiAnchors.BranchKind probeHint,
        bool force,
        DateTime now,
        out nint grid,
        out nint branchRoot)
    {
        grid = 0;
        branchRoot = 0;

        if (!force && PanelOpen && _bfsGrid != 0 && IsValidRewardGrid(_bfsGrid))
        {
            grid = _bfsGrid;
            branchRoot = _branchRoot;
            return branchRoot != 0;
        }

        _bfsGrid = 0;
        if (!force && now < _nextBfsUtc) return false;
        _nextBfsUtc = now.AddMilliseconds(Poe2.Ritual.BfsThrottleMs);

        DiscoverAnchors(inGameState);
        foreach (var root in _live.GetUiBranches(inGameState, probeHint, _branchRoot))
        {
            if (root == 0 || !IsPlausibleBranchRoot(root)) continue;
            _probeRoot = root;

            if (TryFastChainOnRoot(root, allowBrute: true, requireRitualSignature: true, out grid)
                && CountSignatureTiles(grid) > 0)
            {
                branchRoot = root;
                _bfsGrid = grid;
                return true;
            }

            if (TryFastPathIndexHint(root) && FindRitualRewardGrid(root, out grid) && CountSignatureTiles(grid) > 0)
            {
                branchRoot = root;
                _bfsGrid = grid;
                return true;
            }

            if (TryShallowSignatureScan(root) && FindRitualRewardGrid(root, out grid) && CountSignatureTiles(grid) > 0)
            {
                branchRoot = root;
                _bfsGrid = grid;
                return true;
            }

            if (FindRitualRewardGrid(root, out grid) && CountSignatureTiles(grid) > 0)
            {
                branchRoot = root;
                _bfsGrid = grid;
                return true;
            }
        }

        return false;
    }

    private bool TryFastPathIndexHint(nint branch)
    {
        if (branch == 0) return false;
        var ritualRoot = Child(branch, Poe2.Ritual.FastChainChildA);
        if (ritualRoot == 0) return false;
        var window = Child(ritualRoot, Poe2.Ritual.FastChainChildB);
        if (window == 0 || !UiCountsVisible(window)) return false;
        return HasSignatureInSubtree(window, maxDepth: 3, maxNodes: 32);
    }

    private bool TryShallowSignatureScan(nint branch)
    {
        if (branch == 0) return false;
        var queue = new Queue<nint>();
        queue.Enqueue(branch);
        var visited = new HashSet<nint>();
        var scanLimit = _probeRoot == _cachedControllerUi && _cachedControllerUi != 0 ? 6000
            : _preferController ? 2400 : 800;

        while (queue.Count > 0 && visited.Count < scanLimit)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (MatchesRitualSignature(el)) return true;
            if (!Children(el, out var first, out var n)) continue;
            for (long k = 0; k < n; k++) queue.Enqueue(Ptr(first + (nint)(k * 8)));
        }

        return false;
    }

    private bool HasSignatureInSubtree(nint root, int maxDepth, int maxNodes)
    {
        if (root == 0) return false;
        var queue = new Queue<(nint El, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = 0;
        while (queue.Count > 0 && visited < maxNodes)
        {
            var (el, depth) = queue.Dequeue();
            if (el == 0) continue;
            visited++;
            if (MatchesRitualSignature(el)) return true;
            if (depth >= maxDepth || !Children(el, out var first, out var n)) continue;
            for (long k = 0; k < n; k++)
                queue.Enqueue((Ptr(first + (nint)(k * 8)), depth + 1));
        }
        return false;
    }

    private int CountSignatureTiles(nint grid)
    {
        if (!Children(grid, out var first, out var n)) return 0;
        var count = 0;
        for (long i = 0; i < n; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            if (tile == 0 || !UiCountsVisible(tile)) continue;
            if (TryReadTileItem(tile) != 0) count++;
        }
        return count;
    }

    private long ComputeGridSignature(nint grid)
    {
        long sig = 0;
        if (!Children(grid, out var first, out var n)) return 0;
        for (long i = 0; i < n; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            if (tile == 0 || !UiCountsVisible(tile)) continue;
            var itemPtr = TryReadTileItemFast(tile);
            if (itemPtr == 0) continue;
            sig = unchecked((sig * 31) ^ (long)itemPtr);
        }
        return sig;
    }

    private IReadOnlyList<RitualRewardTile> ReadTilesFromGrid(
        nint grid, float winW, float winH, Func<string, string?>? prettyNameLookup)
    {
        _ = prettyNameLookup;
        var result = new List<RitualRewardTile>();
        if (!Children(grid, out var first, out var n)) return result;

        for (long i = 0; i < n && result.Count < Poe2.Ritual.MaxRewardTiles; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            if (tile == 0 || !UiCountsVisible(tile)) continue;
            var item = TryReadTileItem(tile);
            if (item == 0) continue;
            if (!_live.TryReadRitualRewardTile(tile, item, winW, winH, out var reward)) continue;
            result.Add(new RitualRewardTile(reward));
        }

        return result;
    }

    private nint TryReadTileItem(nint tile)
    {
        if (tile == 0) return 0;

        if (_cachedTileItemOffset != 0)
        {
            var cached = Ptr(tile + _cachedTileItemOffset);
            if (_live.IsPlausibleItemEntity(cached)) return cached;
        }

        foreach (var off in Poe2.Ritual.TileItemOffsetCandidates)
        {
            var item = Ptr(tile + off);
            if (!_live.IsPlausibleItemEntity(item)) continue;
            _cachedTileItemOffset = off;
            return item;
        }
        return 0;
    }

    private nint TryReadTileItemFast(nint tile)
    {
        if (tile == 0) return 0;
        if (_cachedTileItemOffset != 0)
        {
            var cached = Ptr(tile + _cachedTileItemOffset);
            if (_live.IsPlausibleItemEntity(cached)) return cached;
        }
        return TryReadTileItem(tile);
    }

    private bool IsValidRewardGrid(nint gridAddr)
    {
        if (gridAddr == 0 || !UiCountsVisible(gridAddr)) return false;
        if (!Children(gridAddr, out var first, out var n)) return false;
        if (n is < 1 or > Poe2.Ritual.GridDetectMaxChildren) return false;

        for (long i = 0; i < n; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            if (tile != 0 && TryReadTileItemFast(tile) != 0) return true;
        }
        return false;
    }

    private bool FindRitualRewardGrid(nint gameUiRoot, out nint grid)
    {
        grid = 0;
        if (gameUiRoot == 0) return false;
        _probeRoot = gameUiRoot;
        var queue = new Queue<nint>();
        var visited = new HashSet<nint>();
        queue.Enqueue(gameUiRoot);
        nint sigEl = 0;

        while (queue.Count > 0 && visited.Count < Poe2.Ritual.BfsMaxNodes)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (el != gameUiRoot && !UiCountsVisible(el)) continue;

            if (Children(el, out var f, out var nn))
            {
                for (long k = 0; k < nn; k++)
                    queue.Enqueue(Ptr(f + (nint)(k * 8)));
            }

            if (sigEl == 0 && MatchesRitualSignature(el))
                sigEl = el;

        }

        if (sigEl == 0) return false;
        var cur = sigEl;
        for (var up = 0; up < 8; up++)
        {
            var candidate = FindRewardGridChild(cur);
            if (candidate != 0)
            {
                grid = candidate;
                return true;
            }
            var parent = Ptr(cur + Poe2.UiElement.Parent);
            if (parent == 0) break;
            cur = parent;
        }
        return false;
    }

    private nint FindRewardGridChild(nint parent)
    {
        if (!Children(parent, out var first, out var n)) return 0;
        nint best = 0;
        var bestScore = 0;
        for (long i = 0; i < n; i++)
        {
            var c = Ptr(first + (nint)(i * 8));
            if (c == 0) continue;
            var score = ScoreRewardGrid(c);
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return bestScore >= 2 ? best : 0;
    }

    private int ScoreRewardGrid(nint candidate)
    {
        if (!Children(candidate, out var first, out var n)) return 0;
        if (n is < 1 or > Poe2.Ritual.GridDetectMaxChildren) return 0;
        var score = 0;
        for (long i = 0; i < n; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            if (tile != 0 && TryReadTileItemFast(tile) != 0) score++;
        }
        return score;
    }

    private bool MatchesRitualSignature(nint element)
    {
        var text = ReadElementText(element);
        if (text.Length < 6) return false;
        foreach (var sig in Poe2.Ritual.SignatureTexts)
        {
            if (text.Contains(sig, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private string ReadElementText(nint el) => ReadStdWString(el + Poe2.UiElement.Text);

    private bool Visible(nint el)
        => _reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var f)
           && (f & (1u << Poe2.UiElement.FlagVisibleBit)) != 0;

    private bool Children(nint el, out nint first, out long n)
    {
        first = Ptr(el + Poe2.UiElement.Children); n = 0;
        if (first == 0) return false;
        if (!_reader.TryReadStruct<nint>(el + Poe2.UiElement.ChildrenEnd, out var last)) return false;
        n = ((long)last - (long)first) / 8;
        return n is > 0 and <= 4000;
    }

    private nint Child(nint el, int index)
        => Children(el, out var first, out var n) && index >= 0 && index < n
            ? Ptr(first + (nint)(index * 8)) : 0;

    private string ReadStdWString(nint addr)
    {
        if (!_reader.TryReadStruct<long>(addr + 0x10, out var len) || len <= 0 || len > 1024) return "";
        if (len < 8) return _reader.ReadStringUtf16(addr, (int)len);
        var ptr = Ptr(addr);
        return ptr == 0 ? "" : _reader.ReadStringUtf16(ptr, (int)len);
    }

    private nint Ptr(nint addr)
    {
        if (!_reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
