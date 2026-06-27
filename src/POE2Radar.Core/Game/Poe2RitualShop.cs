namespace POE2Radar.Core.Game;

/// <summary>
/// Ritual Favours / tribute-shop reader. Single entry point: <see cref="ReadPanelState"/>.
/// Signature finds the branch; in-bounds reward tiles decide panel open (same as --ritual-probe).
/// </summary>
public sealed class Poe2RitualShop
{
    private readonly MemoryReader _reader;
    private readonly Poe2Live _live;

    private nint _cachedUiRoot;
    private readonly List<nint> _cachedGrids = new();
    private int _cachedTileOffset = Poe2.Ritual.TileSlotItem;
    private float _boundsMinX = float.MaxValue;
    private float _boundsMinY = float.MaxValue;
    private float _boundsMaxX = float.MinValue;
    private float _boundsMaxY = float.MinValue;
    private int _cachedInBoundsTileCount;
    private int _openMissStreak;
    private DateTime _nextLocateUtc = DateTime.MinValue;
    private readonly List<Poe2Live.RitualReward> _cachedRewards = new();
    private long _rewardSignature;
    /// <summary>Last branch that hosted an open tribute shop — probed first on idle to save CPU.</summary>
    private nint _branchHint;
    private bool _preferControllerBranch;

    public void ConfigureUiPreference(bool preferControllerBranch)
    {
        if (_preferControllerBranch == preferControllerBranch) return;
        _preferControllerBranch = preferControllerBranch;
        _branchHint = 0;
        if (PanelOpen) ClearSession();
    }

    private static readonly string[] SignatureTexts =
    [
        "Rituals Remaining", "tribute to the king",
        "Spend Tribute", "Tribute", "Favours", "Favors", "Reverence",
    ];

    private const int BranchScanMaxNodes = 6000;
    private const int IdleScanMaxNodes = 800;
    private const int OpenMissCloseThreshold = 2;
    /// <summary>After idle signature hit but ritualSlotsInBounds=0, skip full locate (probe closed case).</summary>
    private const int IdleLocateCooldownSeconds = 2;
    /// <summary>Max width of the tribute reward column (excludes inventory ~920+).</summary>
    private const float RewardColumnMaxWidth = 480f;
    /// <summary>Grids with average tile center left of this screen fraction are tribute rewards (KBM).</summary>
    private const float RewardColumnMaxScreenX = 0.52f;
    /// <summary>Controller / centered layouts — wider column before inventory.</summary>
    private const float RewardColumnMaxScreenXWide = 0.82f;

    public Poe2RitualShop(MemoryReader reader, Poe2Live live)
    {
        _reader = reader;
        _live = live;
    }

    public bool PanelOpen { get; private set; }
    public nint LastUiBranch { get; private set; }

    /// <summary>How the last idle probe detected the panel (probe diagnostics).</summary>
    public IdleProbeKind LastIdleProbeKind { get; private set; }

    /// <summary>True when the last idle probe used the fixed child-index fast path.</summary>
    public bool LastIdleProbeFastPathHit { get; private set; }

    public enum IdleProbeKind { None, FastPath, ShallowScan }

    /// <summary>Complete ritual panel read ΓÇö same contract as Research --ritual-probe reader step.</summary>
    public readonly record struct RitualPanelRead(
        bool SignatureDetected,
        bool PanelOpen,
        int InBoundsTiles,
        nint Branch,
        IReadOnlyList<Poe2Live.RitualReward> Rewards);

    /// <summary>Overlay only: after a failed full locate while closed, suppress 6k BFS until cooldown elapses.</summary>
    public bool IsLocateDue => PanelOpen || DateTime.UtcNow >= _nextLocateUtc;

    /// <summary>True when the visible ritual window fast-path fired ΓÇö shop is open or opening; bypass locate cooldown.</summary>
    public bool ShouldAllowFullLocate => PanelOpen || IsLocateDue || LastIdleProbeFastPathHit;

    /// <summary>
    /// Probe flow: signature hint ΓåÆ tile gate ΓåÆ reward read.
    /// Set <paramref name="allowFullLocate"/> false while closed to skip expensive BFS (overlay cooldown path).
    /// </summary>
    public RitualPanelRead ReadPanelState(nint inGameState, float winW, float winH, bool allowFullLocate,
        bool preferControllerBranch = false)
    {
        ConfigureUiPreference(preferControllerBranch);

        var signatureDetected = PanelOpen || TryIdleProbe(inGameState, winW, winH);
        if (!signatureDetected && preferControllerBranch)
            signatureDetected = TryIdleGridProbe(inGameState, winW, winH);
        if (!signatureDetected)
        {
            ClearSession();
            return new RitualPanelRead(false, false, 0, 0, []);
        }

        var branch = PanelOpen && _cachedUiRoot != 0 ? _cachedUiRoot : LastUiBranch;
        if (branch == 0)
        {
            ClearSession();
            return new RitualPanelRead(true, false, 0, 0, []);
        }

        var tileCount = EnsureGridsAndCountTiles(branch, winW, winH, allowFullLocate, out var tileSig);
        if (tileCount <= 0 && allowFullLocate)
        {
            _branchHint = 0;
            _cachedUiRoot = 0;
            _cachedGrids.Clear();
            foreach (var alt in _live.GetUiBranches(inGameState, preferControllerBranch))
            {
                if (alt == 0) continue;
                var altCount = EnsureGridsAndCountTiles(alt, winW, winH, true, out var altSig);
                if (altCount <= tileCount) continue;
                branch = alt;
                tileCount = altCount;
                tileSig = altSig;
                LastUiBranch = alt;
            }
        }

        if (tileCount <= 0)
        {
            if (PanelOpen)
            {
                if (++_openMissStreak >= OpenMissCloseThreshold)
                    ClearSession();
            }
            else if (allowFullLocate)
                NoteLocateCooldown();
            return new RitualPanelRead(true, false, 0, branch, []);
        }

        _openMissStreak = 0;

        var rewards = ReadCachedGridRewards(winW, winH, tileSig);
        if (rewards.Count == 0)
        {
            if (PanelOpen)
                ClearSession();
            else if (allowFullLocate)
                NoteLocateCooldown();
            return new RitualPanelRead(true, false, tileCount, branch, []);
        }

        PanelOpen = true;
        _nextLocateUtc = DateTime.MinValue;
        LastUiBranch = branch;
        _branchHint = branch;
        return new RitualPanelRead(true, true, tileCount, branch, rewards);
    }

    /// <summary>Cheap recount from cached grids, or full locate when allowed and cache is empty.</summary>
    private int EnsureGridsAndCountTiles(nint branch, float winW, float winH, bool allowFullLocate, out long tileSignature)
    {
        tileSignature = 0;
        if (_cachedUiRoot == branch && _cachedGrids.Count > 0)
        {
            _cachedInBoundsTileCount = CountVisibleInBoundsTiles(_cachedGrids, winW, winH,
                _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY, out tileSignature);
            return _cachedInBoundsTileCount;
        }

        if (!allowFullLocate)
            return 0;

        if (!TryLocateRitualBranch(branch, winW, winH, out var grids, out var sigHits,
                out var bMinX, out var bMinY, out var bMaxX, out var bMaxY))
            return 0;

        if (grids.Count == 0 && sigHits == 0)
            return 0;

        _cachedUiRoot = branch;
        _cachedGrids.Clear();
        _cachedGrids.AddRange(grids);
        _boundsMinX = bMinX;
        _boundsMinY = bMinY;
        _boundsMaxX = bMaxX;
        _boundsMaxY = bMaxY;
        _cachedInBoundsTileCount = CountVisibleInBoundsTiles(grids, winW, winH, bMinX, bMinY, bMaxX, bMaxY, out tileSignature);
        _branchHint = branch;
        return _cachedInBoundsTileCount;
    }

    /// <summary>Cheap signature-only probe (~800 nodes). Does not locate grids or read items.</summary>
    public bool TryIdleProbe(nint inGameState, float winW, float winH)
    {
        _ = winW;
        _ = winH;
        if (PanelOpen)
        {
            if (_cachedUiRoot != 0) LastUiBranch = _cachedUiRoot;
            return true;
        }

        LastIdleProbeKind = IdleProbeKind.None;
        LastIdleProbeFastPathHit = false;
        LastUiBranch = 0;

        var branches = _live.GetUiBranches(inGameState, _preferControllerBranch);
        if (branches.Length == 0)
        {
            ClearSession();
            return false;
        }

        if (_branchHint != 0 && TryCheapSignatureOnBranch(_branchHint, out var hintKind, out var hintFast))
        {
            LastUiBranch = _branchHint;
            LastIdleProbeKind = hintKind;
            LastIdleProbeFastPathHit = hintFast;
            return true;
        }

        foreach (var branch in branches)
        {
            if (branch == _branchHint) continue;
            if (!TryCheapSignatureOnBranch(branch, out var kind, out var fastHit)) continue;
            LastUiBranch = branch;
            LastIdleProbeKind = kind;
            LastIdleProbeFastPathHit = fastHit;
            return true;
        }

        ClearSession();
        return false;
    }

    /// <summary>Fast-path index or shallow BFS for tribute header text ΓÇö no grid locate.</summary>
    private bool TryCheapSignatureOnBranch(nint branch, out IdleProbeKind kind, out bool fastHit)
    {
        kind = IdleProbeKind.None;
        fastHit = false;
        if (branch == 0) return false;

        if (TryFastPathIndexHint(branch))
        {
            kind = IdleProbeKind.FastPath;
            fastHit = true;
            return true;
        }

        if (TryShallowSignatureScan(branch))
        {
            kind = IdleProbeKind.ShallowScan;
            return true;
        }

        if (TryDeepSignatureScan(branch))
        {
            kind = IdleProbeKind.ShallowScan;
            return true;
        }

        return false;
    }

    private bool TryDeepSignatureScan(nint branch)
    {
        if (branch == 0) return false;
        var queue = new Queue<nint>();
        queue.Enqueue(branch);
        var visited = new HashSet<nint>();
        var maxNodes = _preferControllerBranch ? BranchScanMaxNodes / 2 : IdleScanMaxNodes * 2;

        while (queue.Count > 0 && visited.Count < maxNodes)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (MatchesSignature(el)) return true;
            if (Children(el, out var first, out var n))
                for (long k = 0; k < n; k++) queue.Enqueue(Ptr(first + (nint)(k * 8)));
        }
        return false;
    }

    /// <summary>Full reward read while panel is open. Prefer <see cref="ReadPanelState"/> for overlay/probe.</summary>
    public List<Poe2Live.RitualReward> ReadOpenRewards(nint inGameState, float winW, float winH)
    {
        var read = ReadPanelState(inGameState, winW, winH, allowFullLocate: true);
        return read.Rewards is List<Poe2Live.RitualReward> list
            ? list
            : read.Rewards.ToList();
    }

    private void NoteLocateCooldown()
    {
        _nextLocateUtc = DateTime.UtcNow.AddSeconds(IdleLocateCooldownSeconds);
        _cachedUiRoot = 0;
        _cachedGrids.Clear();
        _cachedInBoundsTileCount = 0;
        ClearRewardCache();
    }

    private void ClearRewardCache()
    {
        _cachedRewards.Clear();
        _rewardSignature = 0;
    }

    /// <summary>Backward-compatible entry: signature probe then open read.</summary>
    public List<Poe2Live.RitualReward> ReadRewards(nint inGameState, float winW, float winH)
        => ReadOpenRewards(inGameState, winW, winH);

    /// <summary>Probe helper: item slots on cached tribute grids inside panel bounds.</summary>
    public int CountRitualGridTilesInBounds(nint uiRoot, float winW, float winH)
    {
        if (!TryLocateRitualBranch(uiRoot, winW, winH, out var grids, out _, out var minX, out var minY, out var maxX, out var maxY))
            return 0;
        return CountGridTilesInBounds(grids, winW, winH, minX, minY, maxX, maxY);
    }

    private int CountGridTilesInBounds(IReadOnlyList<nint> grids, float winW, float winH,
        float minX, float minY, float maxX, float maxY, bool requireVisible = false)
    {
        var total = 0;
        foreach (var g in grids)
            total += CountTilesInBounds(g, winW, winH, minX, minY, maxX, maxY, requireVisible);
        return total;
    }

    private void ClearSession()
    {
        PanelOpen = false;
        LastUiBranch = 0;
        _nextLocateUtc = DateTime.MinValue;
        _cachedUiRoot = 0;
        _cachedGrids.Clear();
        _cachedInBoundsTileCount = 0;
        _openMissStreak = 0;
        ClearRewardCache();
        _boundsMinX = float.MaxValue;
        _boundsMinY = float.MaxValue;
        _boundsMaxX = float.MinValue;
        _boundsMaxY = float.MinValue;
    }

    private bool UiCountsVisible(nint el) => _preferControllerBranch || IsVisible(el);

    private bool TryFastPathIndexHint(nint branch)
    {
        if (branch == 0) return false;
        if (!TryChildAtIndex(branch, Poe2.Ritual.UiRootChildIndex, out var ritualRoot)) return false;
        if (!TryChildAtIndex(ritualRoot, Poe2.Ritual.WindowChildIndex, out var windowEl)) return false;
        if (!UiCountsVisible(windowEl)) return false;
        return HasSignatureInSubtree(windowEl, maxDepth: 3, maxNodes: 32);
    }

    private bool TryShallowSignatureScan(nint branch)
    {
        if (branch == 0) return false;
        var queue = new Queue<nint>();
        queue.Enqueue(branch);
        var visited = new HashSet<nint>();

        var scanLimit = _preferControllerBranch ? IdleScanMaxNodes * 3 : IdleScanMaxNodes;
        while (queue.Count > 0 && visited.Count < scanLimit)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;

            if (MatchesSignature(el))
                return true;

            if (Children(el, out var first, out var n))
                for (long k = 0; k < n; k++) queue.Enqueue(Ptr(first + (nint)(k * 8)));
        }

        return false;
    }

    /// <summary>Controller UI may omit tribute header text — detect open shop by reward item grids.</summary>
    private bool TryIdleGridProbe(nint inGameState, float winW, float winH)
    {
        foreach (var branch in _live.GetUiBranches(inGameState, preferController: true))
        {
            if (branch == 0) continue;
            var tiles = EnsureGridsAndCountTiles(branch, winW, winH, allowFullLocate: true, out _);
            if (tiles <= 0) continue;
            LastUiBranch = branch;
            LastIdleProbeKind = IdleProbeKind.ShallowScan;
            LastIdleProbeFastPathHit = false;
            return true;
        }
        return false;
    }

    /// <summary>Read only item slots on cached tribute grids — excludes ground-loot UI on the same branch.</summary>
    private List<Poe2Live.RitualReward> ReadCachedGridRewards(float winW, float winH, long tileSig)
    {
        if (_cachedGrids.Count == 0) return [];

        if (tileSig != 0 && tileSig == _rewardSignature && _cachedRewards.Count > 0)
            return _cachedRewards;

        var result = ReadBranchRewardsOpen(LastUiBranch, winW, winH);

        _rewardSignature = tileSig;
        _cachedRewards.Clear();
        _cachedRewards.AddRange(result);
        return _cachedRewards;
    }

    /// <summary>Single pass: visible in-bounds tile count + item-pointer signature for reward cache.</summary>
    private int CountVisibleInBoundsTiles(IReadOnlyList<nint> grids, float winW, float winH,
        float minX, float minY, float maxX, float maxY, out long signature)
    {
        signature = 0;
        var total = 0;
        foreach (var grid in grids)
        {
            if (grid == 0 || !UiCountsVisible(grid)) continue;
            if (!Children(grid, out var first, out var n)) continue;
            for (long i = 0; i < n; i++)
            {
                var tile = Ptr(first + (nint)(i * 8));
                if (!UiCountsVisible(tile)) continue;
                var item = TryReadTileItem(tile);
                if (item == 0) continue;
                if (!_live.TryReadRitualRewardTile(tile, item, winW, winH, out var reward)) continue;
                if (!InPanelBounds(reward, minX, minY, maxX, maxY)) continue;
                signature = unchecked((signature * 31) ^ (long)item);
                total++;
            }
        }
        return total;
    }

    private List<Poe2Live.RitualReward> ReadBranchRewardsFull(nint uiBranch, float winW, float winH)
    {
        if (_cachedGrids.Count == 0)
            return [];

        var seenItems = new HashSet<nint>();
        var result = new List<Poe2Live.RitualReward>();
        foreach (var grid in _cachedGrids)
            CollectTilesFromGrid(grid, result, seenItems, winW, winH, _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY);

        CollectEntityRewardsInBounds(uiBranch, seenItems, result, winW, winH,
            _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY);
        MergeLabelsIntoRewards(uiBranch, result, winW, winH,
            _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY);
        result.RemoveAll(static r => IsRitualChrome(r.Name));
        result.RemoveAll(r => !IsInRewardColumn(r, _boundsMinX, _boundsMaxX));
        return result;
    }

    private List<Poe2Live.RitualReward> ReadBranchEntityScan(nint uiBranch, float winW, float winH)
    {
        var seenItems = new HashSet<nint>();
        var result = new List<Poe2Live.RitualReward>();
        CollectEntityRewardsInBounds(uiBranch, seenItems, result, winW, winH,
            _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY);
        MergeLabelsIntoRewards(uiBranch, result, winW, winH,
            _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY);
        result.RemoveAll(static r => IsRitualChrome(r.Name));
        result.RemoveAll(r => !IsInRewardColumn(r, _boundsMinX, _boundsMaxX));
        return result;
    }

    private bool HasSignatureInSubtree(nint root, int maxDepth, int maxNodes)
    {
        var queue = new Queue<(nint El, int Depth)>();
        queue.Enqueue((root, 0));
        var visited = new HashSet<nint>();

        while (queue.Count > 0 && visited.Count < maxNodes)
        {
            var (el, depth) = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;

            if (MatchesSignature(el) && UiCountsVisible(el))
                return true;

            if (depth >= maxDepth) continue;
            if (Children(el, out var first, out var n))
                for (long k = 0; k < n && k < 16; k++)
                    queue.Enqueue((Ptr(first + (nint)(k * 8)), depth + 1));
        }

        return false;
    }

    private List<Poe2Live.RitualReward> ReadBranchRewardsOpen(nint uiBranch, float winW, float winH)
    {
        var seenItems = new HashSet<nint>();
        var result = new List<Poe2Live.RitualReward>();

        foreach (var grid in _cachedGrids)
            CollectTilesFromGrid(grid, result, seenItems, winW, winH, _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY);

        var needHeavy = result.Count < Math.Max(1, _cachedInBoundsTileCount - 2);
        var needLabels = false;
        foreach (var r in result)
        {
            if (string.IsNullOrEmpty(r.Name)
                || string.Equals(r.Name, "Hidden Item", StringComparison.OrdinalIgnoreCase))
            {
                needLabels = true;
                break;
            }
        }

        if (needHeavy)
        {
            CollectEntityRewardsInBounds(uiBranch, seenItems, result, winW, winH,
                _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY);
            needLabels = true;
        }

        if (needLabels)
        {
            MergeLabelsIntoRewards(uiBranch, result, winW, winH,
                _boundsMinX, _boundsMinY, _boundsMaxX, _boundsMaxY);
        }

        result.RemoveAll(static r => IsRitualChrome(r.Name));
        return result;
    }

    /// <summary>True for tribute-panel UI copy that is not a shop reward name.</summary>
    public static bool IsRitualChrome(string? line) => IsChromeLabel(line ?? "");

    private void MergeLabelsIntoRewards(nint scanRoot, List<Poe2Live.RitualReward> rewards, float winW, float winH,
        float minX, float minY, float maxX, float maxY)
    {
        var labels = FilterToPanelBounds(CollectItemLabelRewards(scanRoot, winW, winH), minX, minY, maxX, maxY);
        var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var label in labels)
        {
            if (label.Name is not { Length: > 0 } name || !usedLabels.Add(name)) continue;
            var lx = label.X + label.W * 0.5f;
            var ly = label.Y + label.H * 0.5f;
            var matched = false;

            for (var i = 0; i < rewards.Count; i++)
            {
                var r = rewards[i];
                var rx = r.X + r.W * 0.5f;
                var ry = r.Y + r.H * 0.5f;
                if (MathF.Abs(lx - rx) > 72f || MathF.Abs(ly - ry) > 72f) continue;

                if (string.IsNullOrEmpty(r.Name)
                    || string.Equals(r.Name, "Hidden Item", StringComparison.OrdinalIgnoreCase)
                    || !LooksLikeItemLabel(r.Name))
                {
                    rewards[i] = r with { Name = name };
                }
                matched = true;
                break;
            }

            if (!matched && !IsRitualChrome(name))
                rewards.Add(label);
        }
    }

    private bool TryLocateRitualBranch(nint uiRoot, float winW, float winH, out List<nint> ritualGrids, out int sigHits,
        out float minX, out float minY, out float maxX, out float maxY)
    {
        ritualGrids = new List<nint>();
        sigHits = 0;
        minX = minY = float.MaxValue;
        maxX = maxY = float.MinValue;
        if (uiRoot == 0) return false;

        var gridCandidates = new List<nint>();
        var queue = new Queue<nint>();
        queue.Enqueue(uiRoot);
        var visited = new HashSet<nint>();

        while (queue.Count > 0 && visited.Count < BranchScanMaxNodes)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;

            if (MatchesSignature(el))
            {
                sigHits++;
                if (_live.TryUiElementRect(el, winW, winH, out var x, out var y, out var w, out var h, requireVisible: false))
                {
                    minX = MathF.Min(minX, x);
                    minY = MathF.Min(minY, y);
                    maxX = MathF.Max(maxX, x + w);
                    maxY = MathF.Max(maxY, y + h);
                }
            }

            if (Children(el, out var first, out var n))
            {
                if (n is > 0 and <= 32)
                {
                    var tileItems = CountRewardTiles(el);
                    if (tileItems is >= 1 and <= 20)
                        gridCandidates.Add(el);
                }
                for (long k = 0; k < n; k++) queue.Enqueue(Ptr(first + (nint)(k * 8)));
            }
        }

        if (sigHits == 0)
        {
            var itemTiles = 0;
            foreach (var g in gridCandidates)
                itemTiles += CountRewardTiles(g);
            if (itemTiles < 2) return false;
        }

        var columnMaxX = winW * (_preferControllerBranch ? RewardColumnMaxScreenXWide : RewardColumnMaxScreenX);
        var leftGrids = SelectLeftColumnGrids(gridCandidates, winW, winH, columnMaxX);
        if (ExpandBoundsFromGridTiles(leftGrids, winW, winH, columnMaxX, ref minX, ref minY, ref maxX, ref maxY))
        {
            minX -= 32f;
            minY -= 28f;
            maxX = MathF.Min(maxX + 64f, minX + RewardColumnMaxWidth);
            maxY += 48f;
        }
        else if (maxX > minX && maxY > minY)
        {
            const float padLeft = 400f;
            const float padTop = 28f;
            const float padBottom = 640f;
            var anchorMinX = minX;
            var anchorMaxY = maxY;
            minX = anchorMinX - padLeft;
            minY = minY - padTop;
            maxX = minX + RewardColumnMaxWidth;
            maxY = anchorMaxY + padBottom;
        }
        else
            return true;

        var rewardMaxCenterX = maxX;
        var scored = new List<(nint Grid, int InBounds, float AvgX)>();
        foreach (var candidate in leftGrids.Count > 0 ? leftGrids : gridCandidates)
        {
            var avgX = AvgTileCenterX(candidate, winW, winH);
            if (avgX > rewardMaxCenterX) continue;
            var inBounds = CountTilesInBounds(candidate, winW, winH, minX, minY, maxX, maxY);
            if (inBounds >= 1)
                scored.Add((candidate, inBounds, avgX));
        }
        scored.Sort((a, b) =>
        {
            var cmp = b.InBounds.CompareTo(a.InBounds);
            return cmp != 0 ? cmp : a.AvgX.CompareTo(b.AvgX);
        });
        foreach (var (g, _, _) in scored) ritualGrids.Add(g);
        return true;
    }

    private List<nint> SelectLeftColumnGrids(List<nint> gridCandidates, float winW, float winH, float columnMaxX)
    {
        var left = new List<(nint Grid, float AvgX)>();
        foreach (var g in gridCandidates)
        {
            var avgX = AvgTileCenterX(g, winW, winH);
            if (avgX >= float.MaxValue) continue;
            if (avgX <= columnMaxX)
                left.Add((g, avgX));
        }
        left.Sort((a, b) => a.AvgX.CompareTo(b.AvgX));
        if (left.Count > 0)
            return left.ConvertAll(x => x.Grid);

        var fallback = new List<(nint Grid, float AvgX)>();
        foreach (var g in gridCandidates)
        {
            var avgX = AvgTileCenterX(g, winW, winH);
            if (avgX < float.MaxValue) fallback.Add((g, avgX));
        }
        fallback.Sort((a, b) => a.AvgX.CompareTo(b.AvgX));
        return fallback.Take(2).Select(x => x.Grid).ToList();
    }

    private bool ExpandBoundsFromGridTiles(IReadOnlyList<nint> grids, float winW, float winH, float columnMaxX,
        ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        var haveTile = false;
        foreach (var grid in grids)
        {
            if (!Children(grid, out var first, out var n)) continue;
            for (long i = 0; i < n; i++)
            {
                var tile = Ptr(first + (nint)(i * 8));
                var item = TryReadTileItem(tile);
                if (item == 0) continue;
                if (!_live.TryReadRitualRewardTile(tile, item, winW, winH, out var r)) continue;
                var cx = r.X + r.W * 0.5f;
                if (cx > columnMaxX) continue;
                minX = MathF.Min(minX, r.X);
                minY = MathF.Min(minY, r.Y);
                maxX = MathF.Max(maxX, r.X + r.W);
                maxY = MathF.Max(maxY, r.Y + r.H);
                haveTile = true;
            }
        }
        return haveTile;
    }

    private float AvgTileCenterX(nint gridAddr, float winW, float winH)
    {
        if (gridAddr == 0 || !Children(gridAddr, out var first, out var n) || n == 0) return float.MaxValue;
        var sum = 0f;
        var count = 0;
        for (long i = 0; i < n; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            var item = TryReadTileItem(tile);
            if (item == 0) continue;
            if (!_live.TryReadRitualRewardTile(tile, item, winW, winH, out var reward)) continue;
            sum += reward.X + reward.W * 0.5f;
            count++;
        }
        return count > 0 ? sum / count : float.MaxValue;
    }

    private int CountTilesInBounds(nint gridAddr, float winW, float winH,
        float minX, float minY, float maxX, float maxY, bool requireVisible = false)
    {
        if (gridAddr == 0 || !Children(gridAddr, out var first, out var n)) return 0;
        if (requireVisible && !IsVisible(gridAddr)) return 0;
        var count = 0;
        for (long i = 0; i < n; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            if (requireVisible && !IsVisible(tile)) continue;
            var item = TryReadTileItem(tile);
            if (item == 0) continue;
            if (!_live.TryReadRitualRewardTile(tile, item, winW, winH, out var reward)) continue;
            if (InPanelBounds(reward, minX, minY, maxX, maxY)) count++;
        }
        return count;
    }

    private void CollectEntityRewardsInBounds(nint scanRoot, HashSet<nint> seenItems, List<Poe2Live.RitualReward> result,
        float winW, float winH, float minX, float minY, float maxX, float maxY)
    {
        const uint visBit = 1u << Poe2.UiElement.FlagVisibleBit;
        var queue = new Queue<nint>();
        queue.Enqueue(scanRoot);
        var visited = new HashSet<nint>();

        while (queue.Count > 0 && visited.Count < BranchScanMaxNodes)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;

            if (Children(el, out var first, out var n))
                for (long k = 0; k < n; k++) queue.Enqueue(Ptr(first + (nint)(k * 8)));

            if (!_reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags) || (flags & visBit) == 0)
                continue;

            var item = TryReadTileItem(el);
            if (item == 0 || !seenItems.Add(item)) continue;
            if (!_live.TryReadRitualRewardTile(el, item, winW, winH, out var reward)) continue;
            if (!InPanelBounds(reward, minX, minY, maxX, maxY)) continue;
            EnrichRewardNameFromTile(el, ref reward);
            if (IsRitualChrome(reward.Name)) continue;
            result.Add(reward);
        }
    }

    private static bool IsInRewardColumn(Poe2Live.RitualReward r, float minX, float maxX)
    {
        var cx = r.X + r.W * 0.5f;
        return cx >= minX && cx <= maxX;
    }

    private static bool InPanelBounds(Poe2Live.RitualReward r, float minX, float minY, float maxX, float maxY)
    {
        if (maxX <= minX || maxY <= minY) return true;
        var cx = r.X + r.W * 0.5f;
        var cy = r.Y + r.H * 0.5f;
        return cx >= minX && cx <= maxX && cy >= minY && cy <= maxY;
    }

    private static List<Poe2Live.RitualReward> FilterToPanelBounds(List<Poe2Live.RitualReward> rewards,
        float minX, float minY, float maxX, float maxY)
    {
        if (rewards.Count == 0 || maxX <= minX || maxY <= minY) return rewards;
        var filtered = new List<Poe2Live.RitualReward>(rewards.Count);
        foreach (var r in rewards)
        {
            if (InPanelBounds(r, minX, minY, maxX, maxY))
                filtered.Add(r);
        }
        return filtered;
    }

    private void EnrichRewardNameFromTile(nint tile, ref Poe2Live.RitualReward reward)
    {
        if (reward.Name is { Length: >= 3 } || reward.Art is { Length: >= 2 }) return;
        if (!TryReadItemLabelOnElement(tile, out var label)) return;
        reward = reward with { Name = label };
    }

    private bool TryReadItemLabelOnElement(nint root, out string label)
    {
        label = "";
        const uint visBit = 1u << Poe2.UiElement.FlagVisibleBit;
        var queue = new Queue<nint>();
        queue.Enqueue(root);
        var visited = new HashSet<nint>();
        while (queue.Count > 0 && visited.Count < 48)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (Children(el, out var f, out var n))
                for (long i = 0; i < n && i < 8; i++) queue.Enqueue(Ptr(f + (nint)(i * 8)));

            if (!_reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags) || (flags & visBit) == 0)
                continue;

            var text = ReadStdWString(el + Poe2.UiElement.Text);
            if (text.Length < 2) continue;
            var nl = text.IndexOf('\n');
            var line = (nl >= 0 ? text[..nl] : text).Trim();
            if (!LooksLikeItemLabel(line)) continue;
            label = StripCountPrefix(line);
            return true;
        }
        return false;
    }

    private List<Poe2Live.RitualReward> CollectItemLabelRewards(nint scanRoot, float winW, float winH)
    {
        var result = new List<Poe2Live.RitualReward>();
        const uint visBit = 1u << Poe2.UiElement.FlagVisibleBit;
        var queue = new Queue<nint>();
        queue.Enqueue(scanRoot);
        var visited = new HashSet<nint>();
        var seenText = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0 && visited.Count < BranchScanMaxNodes)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;

            if (Children(el, out var first, out var n))
                for (long k = 0; k < n; k++) queue.Enqueue(Ptr(first + (nint)(k * 8)));

            if (!_reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags) || (flags & visBit) == 0)
                continue;

            var text = ReadStdWString(el + Poe2.UiElement.Text);
            if (text.Length < 2) continue;
            var nl = text.IndexOf('\n');
            var line = (nl >= 0 ? text[..nl] : text).Trim();
            if (!LooksLikeItemLabel(line) || !seenText.Add(line)) continue;
            if (!_live.TryUiElementRect(el, winW, winH, out var x, out var y, out var w, out var h, requireVisible: false))
                continue;

            result.Add(new Poe2Live.RitualReward(
                Poe2Live.Rarity.NonMonster, null, StripCountPrefix(line), true, x, y, w, h));
        }
        return result;
    }

    internal static bool LooksLikeItemLabel(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var t = line.Trim();
        if (t.Length is < 3 or > 96) return false;
        if (t.IndexOfAny(['{', '}', '<', '>']) >= 0) return false;
        if (IsChromeLabel(t)) return false;

        var i = 0;
        while (i < t.Length && char.IsDigit(t[i])) i++;
        if (i > 0 && i < t.Length && (t[i] == 'x' || t[i] == 'X'))
        {
            var name = t[(i + 1)..].TrimStart();
            return name.Length >= 3 && !IsChromeLabel(name);
        }

        return t.Count(char.IsLetter) >= 4;
    }

    private static bool IsChromeLabel(string line)
    {
        var t = line.Trim();
        if (t.Length <= 1) return true;
        if (t.All(static c => char.IsDigit(c) || c is '.' or ',' or '%' or ' ' or '-')) return true;
        if (t.StartsWith('-') && t.EndsWith('-')) return true;

        var lower = t.ToLowerInvariant();
        if (lower is "favours" or "favors" or "reverence" or "tribute" or "spectate" or "i want"
            or "cycle player" or "click to select") return true;
        if (lower.StartsWith("click ")) return true;
        if (lower.Contains("recommended")) return true;
        if (lower.Contains("click to")) return true;
        if (lower.Contains("cycle player")) return true;
        if (lower.EndsWith(" player") && !lower.Contains(" of ")) return true;
        if (lower.Contains("tribute")) return true;
        if (lower.Contains("remaining")) return true;
        if (lower.Contains("defer mode")) return true;
        if (lower.Contains("offer tribute")) return true;
        if (lower.Contains("spend tribute")) return true;
        if (lower.Contains("ritual error")) return true;
        if (lower is "defer" or "reroll" or "skip" or "close" or "exit" or "cancel" or "want") return true;

        if (lower.StartsWith("ritual"))
        {
            if (lower.Contains("tablet") || lower.Contains("splinter") || lower.Contains("dagger"))
                return false;
            if (!lower.Contains("orb")) return true;
        }

        return false;
    }

    private static string StripCountPrefix(string raw)
    {
        var t = raw.Trim();
        var i = 0;
        while (i < t.Length && char.IsDigit(t[i])) i++;
        return i > 0 && i < t.Length && (t[i] == 'x' || t[i] == 'X')
            ? t[(i + 1)..].TrimStart() : t;
    }

    private int CountRewardTiles(nint gridAddr)
    {
        if (gridAddr == 0 || !Children(gridAddr, out var first, out var n) || n is < 1 or > 32)
            return 0;
        var items = 0;
        for (long i = 0; i < n; i++)
        {
            if (TryReadTileItem(Ptr(first + (nint)(i * 8))) != 0) items++;
        }
        return items;
    }

    private nint TryReadTileItem(nint tile)
    {
        if (tile == 0) return 0;

        if (_cachedTileOffset != 0)
        {
            var cached = Ptr(tile + _cachedTileOffset);
            if (_live.IsPlausibleItemEntity(cached)) return cached;
        }

        foreach (var off in Poe2.Ritual.TileItemOffsetCandidates)
        {
            var item = Ptr(tile + off);
            if (!_live.IsPlausibleItemEntity(item)) continue;
            _cachedTileOffset = off;
            return item;
        }
        return 0;
    }

    private void CollectTilesFromGrid(nint grid, List<Poe2Live.RitualReward> result, HashSet<nint> seenItems,
        float winW, float winH, float minX, float minY, float maxX, float maxY)
    {
        if (!Children(grid, out var gf, out var gn)) return;
        if (!UiCountsVisible(grid)) return;
        for (long i = 0; i < gn; i++)
        {
            var tile = Ptr(gf + (nint)(i * 8));
            if (!UiCountsVisible(tile)) continue;
            var item = TryReadTileItem(tile);
            if (item == 0 || !seenItems.Add(item)) continue;
            if (!_live.TryReadRitualRewardTile(tile, item, winW, winH, out var reward)) continue;
            if (!InPanelBounds(reward, minX, minY, maxX, maxY)) continue;
            EnrichRewardNameFromTile(tile, ref reward);
            if (IsRitualChrome(reward.Name)) continue;
            result.Add(reward);
        }
    }

    private bool MatchesSignature(nint el)
    {
        var t = ReadStdWString(el + Poe2.UiElement.Text);
        if (t.Length < 4) return false;
        foreach (var sig in SignatureTexts)
        {
            if (t.Contains(sig, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private bool IsVisible(nint el)
    {
        const uint visBit = 1u << Poe2.UiElement.FlagVisibleBit;
        return _reader.TryReadStruct<uint>(el + Poe2.UiElement.Flags, out var flags) && (flags & visBit) != 0;
    }

    private bool TryChildAtIndex(nint parent, int index, out nint child)
    {
        child = 0;
        if (!Children(parent, out var first, out var n)) return false;
        if (index < 0 || index >= n) return false;
        child = Ptr(first + (nint)(index * 8));
        return child != 0;
    }

    private bool Children(nint el, out nint first, out long n)
    {
        first = Ptr(el + Poe2.UiElement.Children);
        n = 0;
        if (first == 0) return false;
        if (!_reader.TryReadStruct<nint>(el + Poe2.UiElement.ChildrenEnd, out var last)) return false;
        n = ((long)last - (long)first) / 8;
        return n is > 0 and <= 4000;
    }

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
