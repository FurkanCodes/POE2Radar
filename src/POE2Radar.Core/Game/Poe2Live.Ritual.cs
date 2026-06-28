using System.Globalization;
using System.Numerics;

namespace POE2Radar.Core.Game;

public sealed partial class Poe2Live
{
    private static readonly string[] RitualSignatureTexts = ["Rituals Remaining", "Ritual Remaining", "tribute to the king"];
    private static readonly char[] PathSeparators = ['/', '\\'];

    private nint _ritualGridAddr;
    private nint _ritualGridRoot;
    private nint _ritualLastInGameState;
    private DateTime _ritualNextScanUtc = DateTime.MinValue;
    private Poe2UiAnchors.BranchKind _ritualProbeHint;
    private int _ritualGridMissStreak;

    private const int RitualGridMissStreakMax = 6;

    public readonly record struct UiRect(float X, float Y, float W, float H);

    public readonly record struct RitualRewardSlot(
        nint TileElement,
        nint ItemEntity,
        UiRect Rect,
        string FullItemPath,
        string InternalName,
        string BaseItemName,
        Rarity Rarity,
        string ArtBasename,
        string[] ModLines);

    public readonly record struct RitualRewardsRead(
        bool IsOpen,
        nint GridAddress,
        nint RootAddress,
        Poe2UiAnchors.BranchKind Branch,
        string Source,
        string Note,
        RitualRewardSlot[] Slots)
    {
        public static readonly RitualRewardsRead Closed = new(false, 0, 0, Poe2UiAnchors.BranchKind.None, "", "", []);
    }

    /// <summary>Clears cached ritual grid pointers and scan throttle. Call on ritual window close or zone change.</summary>
    public void InvalidateRitualUiCache()
    {
        _ritualGridAddr = 0;
        _ritualGridRoot = 0;
        _ritualNextScanUtc = DateTime.MinValue;
        _ritualProbeHint = Poe2UiAnchors.BranchKind.None;
        _ritualGridMissStreak = 0;
    }

    /// <summary>Reads ritual reward tiles with screen rects for in-game overlay labels.</summary>
    public RitualRewardSlot[] ReadRitualOverlaySlots(nint grid, float windowWidth, float windowHeight)
        => grid == 0 ? [] : ReadRitualSlots(grid, windowWidth, windowHeight, requireScreenRect: true);

    /// <summary>
    /// Reads the visible Ritual Favours reward grid. Shop is considered open only when at least one
    /// tribute item slot is populated (metadata read does not require HUD screen projection).
    /// </summary>
    public RitualRewardsRead ReadRitualRewards(
        nint inGameState,
        float windowWidth,
        float windowHeight,
        bool forceBfsFallback = false)
    {
        if (inGameState == 0 || windowWidth <= 0 || windowHeight <= 0)
            return RitualRewardsRead.Closed;

        if (_ritualLastInGameState != inGameState)
        {
            InvalidateRitualUiCache();
            _ritualLastInGameState = inGameState;
        }

        DiscoverGameUiAnchors(inGameState, out var gameUi, out var controllerGameUi);

        var uiRoot = GetUiRoot(inGameState);
        var fixedRoot = Ptr(inGameState + Poe2.InGameState.UiRoot);
        Span<nint> roots = stackalloc nint[6];
        var rootCount = UiBranchCandidates.Fill(
            roots, gameUi, controllerGameUi, uiRoot, fixedRoot, _ritualProbeHint, _ritualGridRoot);

        if (_ritualGridAddr != 0 && _ritualGridRoot != 0
            && TryFastRitualGrid(_ritualGridRoot, out var liveFast)
            && liveFast != 0
            && liveFast != _ritualGridAddr
            && IsValidRewardGrid(liveFast))
        {
            var relocated = BuildRitualRead(
                liveFast, _ritualGridRoot, gameUi, controllerGameUi,
                "fast-reloc", windowWidth, windowHeight);
            if (relocated.IsOpen)
            {
                RememberRitualGrid(liveFast, _ritualGridRoot, gameUi, controllerGameUi);
                _ritualGridMissStreak = 0;
                return relocated;
            }
        }

        if (_ritualGridAddr != 0)
        {
            var cached = BuildRitualRead(
                _ritualGridAddr, _ritualGridRoot, gameUi, controllerGameUi,
                "cached", windowWidth, windowHeight);
            if (cached.IsOpen)
            {
                _ritualGridMissStreak = 0;
                return cached;
            }

            if (++_ritualGridMissStreak < RitualGridMissStreakMax)
                return RitualRewardsRead.Closed;

            _ritualGridMissStreak = 0;
            _ritualGridAddr = 0;
            _ritualGridRoot = 0;
        }

        _ritualNextScanUtc = DateTime.MinValue;

        if (!forceBfsFallback)
        {
            for (var i = 0; i < rootCount; i++)
            {
                var root = roots[i];
                if (!TryFastRitualGrid(root, out var grid)) continue;
                if (!IsValidRewardGrid(grid, BranchHasRitualSignature(root))) continue;

                RememberRitualGrid(grid, root, gameUi, controllerGameUi);
                var read = BuildRitualRead(grid, root, gameUi, controllerGameUi, "fast", windowWidth, windowHeight);
                if (read.IsOpen) return read;
            }
        }

        if (TryScanRitualGridThrottled(roots, rootCount, windowWidth, windowHeight, out var scanGrid, out var scanRoot))
        {
            RememberRitualGrid(scanGrid, scanRoot, gameUi, controllerGameUi);
            var read = BuildRitualRead(scanGrid, scanRoot, gameUi, controllerGameUi, "bfs", windowWidth, windowHeight);
            if (read.IsOpen) return read;
        }

        if (!forceBfsFallback)
        {
            for (var i = 0; i < rootCount; i++)
            {
                var root = roots[i];
                if (!TryFastRitualGrid(root, out var grid)) continue;
                if (!IsVisibleUiElement(grid)) continue;
                if (!IsRitualGridInActiveShop(grid) && !BranchHasRitualSignature(root)) continue;
                if (!TryCountRewardGridItems(grid, out var tileCount, out var itemCount)) continue;

                if (tileCount <= 1 && itemCount <= 1) continue;
                if (itemCount < 2 || itemCount * 2 < tileCount) continue;

                var pending = BuildRitualRead(grid, root, gameUi, controllerGameUi, "fast-pending", windowWidth, windowHeight);
                if (pending.IsOpen) return pending;
            }
        }

        return RitualRewardsRead.Closed;
    }

    /// <summary>Lightweight ritual UI snapshot for Research probes and overlay diagnostics.</summary>
    public RitualUiProbeSnapshot ProbeRitualUi(nint inGameState, float windowWidth, float windowHeight)
    {
        if (inGameState == 0 || windowWidth <= 0 || windowHeight <= 0)
            return default;

        DiscoverGameUiAnchors(inGameState, out var gameUi, out var controllerGameUi);
        var uiRoot = GetUiRoot(inGameState);
        var fixedRoot = Ptr(inGameState + Poe2.InGameState.UiRoot);
        Span<nint> roots = stackalloc nint[6];
        var rootCount = UiBranchCandidates.Fill(
            roots, gameUi, controllerGameUi, uiRoot, fixedRoot, _ritualProbeHint, _ritualGridRoot);

        var branches = new RitualUiBranchProbe[rootCount];
        for (var i = 0; i < rootCount; i++)
        {
            var root = roots[i];
            var branch = UiBranchCandidates.BranchForRoot(root, gameUi, controllerGameUi);
            var fastGrid = (nint)0;
            var fastVisible = false;
            var fastValid = false;
            var tileCount = 0;
            var itemCount = 0;
            if (TryFastRitualGrid(root, out fastGrid))
            {
                fastVisible = IsVisibleUiElement(fastGrid);
                fastValid = IsValidRewardGrid(fastGrid);
                var tiles = ReadChildren(fastGrid, maxCount: 32);
                tileCount = tiles.Length;
                foreach (var tile in tiles)
                    if (Ptr(tile + Poe2.RitualUi.TileItemEntity) != 0)
                        itemCount++;
            }

            branches[i] = new RitualUiBranchProbe(
                branch.ToString(),
                root,
                fastGrid,
                fastVisible,
                fastValid,
                tileCount,
                itemCount);
        }

        var read = ReadRitualRewards(inGameState, windowWidth, windowHeight, forceBfsFallback: false);
        return new RitualUiProbeSnapshot(
            _ritualGridAddr,
            _ritualGridRoot,
            _ritualProbeHint.ToString(),
            _ritualNextScanUtc,
            branches,
            read);
    }

    public readonly record struct RitualUiBranchProbe(
        string Branch,
        nint Root,
        nint FastGrid,
        bool FastVisible,
        bool FastValid,
        int TileCount,
        int ItemCount);

    public readonly record struct RitualUiProbeSnapshot(
        nint CachedGrid,
        nint CachedRoot,
        string ProbeHint,
        DateTime NextBfsUtc,
        RitualUiBranchProbe[] Branches,
        RitualRewardsRead Read);

    public readonly record struct RitualDeepGridCandidate(
        nint Grid,
        int TileCount,
        int ItemCountAt4F8,
        int BestItemCount,
        int BestItemOffset,
        UiRect Rect,
        bool Visible,
        bool PassesShopContext,
        bool PassesStrictValidation);

    public readonly record struct RitualDeepTextHit(
        string Text,
        nint Element,
        bool MatchesSignature);

    public readonly record struct RitualDeepBranchReport(
        string Branch,
        nint Root,
        int RootChildCount,
        int Fast76ChildCount,
        nint FastGrid,
        RitualDeepTextHit[] TextHits,
        RitualDeepGridCandidate[] GridCandidates);

    /// <summary>Research-only deep scan: signature text, fast-chain shape, and reward-grid heuristics per UI branch.</summary>
    public RitualDeepBranchReport[] DeepProbeRitualUi(nint inGameState, float windowWidth, float windowHeight)
    {
        if (inGameState == 0 || windowWidth <= 0 || windowHeight <= 0)
            return [];

        DiscoverGameUiAnchors(inGameState, out var gameUi, out var controllerGameUi);
        var uiRoot = GetUiRoot(inGameState);
        var fixedRoot = Ptr(inGameState + Poe2.InGameState.UiRoot);
        Span<nint> roots = stackalloc nint[6];
        var rootCount = UiBranchCandidates.Fill(
            roots, gameUi, controllerGameUi, uiRoot, fixedRoot, Poe2UiAnchors.BranchKind.None, 0);

        var reports = new RitualDeepBranchReport[rootCount];
        for (var i = 0; i < rootCount; i++)
            reports[i] = DeepProbeRitualBranch(roots[i], gameUi, controllerGameUi, windowWidth, windowHeight);

        return reports;
    }

    private RitualDeepBranchReport DeepProbeRitualBranch(
        nint root,
        nint gameUi,
        nint controllerGameUi,
        float windowWidth,
        float windowHeight)
    {
        var branch = UiBranchCandidates.BranchForRoot(root, gameUi, controllerGameUi).ToString();
        if (root == 0)
            return new RitualDeepBranchReport(branch, 0, 0, 0, 0, [], []);

        var rootChildren = ReadChildren(root, maxCount: 256);
        var fast76Count = 0;
        nint fastGrid = 0;
        if (rootChildren.Length > 76)
        {
            var child76Children = ReadChildren(rootChildren[76], maxCount: 128);
            fast76Count = child76Children.Length;
            if (child76Children.Length > 13)
                fastGrid = child76Children[13];
        }

        var textHits = new List<RitualDeepTextHit>(16);
        var gridCandidates = new List<RitualDeepGridCandidate>(8);
        var queue = new Queue<nint>();
        var visited = new HashSet<nint>();
        queue.Enqueue(root);

        while (queue.Count > 0 && visited.Count < 25000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (el != root && !IsVisibleUiElement(el)) continue;

            foreach (var child in ReadChildren(el, maxCount: 8192))
                queue.Enqueue(child);

            var text = ReadStdWString(el + Poe2.UiElement.Text);
            if (text.Length >= 4)
            {
                var ritualish = text.Contains("Ritual", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("tribute", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Favour", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Favor", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Remaining", StringComparison.OrdinalIgnoreCase);
                if (ritualish)
                {
                    textHits.Add(new RitualDeepTextHit(
                        text.Length > 96 ? text[..96] + "…" : text,
                        el,
                        MatchesRitualSignature(el)));
                }
            }

            var tiles = ReadChildren(el, maxCount: 32);
            if (tiles.Length is < 1 or > 16) continue;

            var items4F8 = 0;
            var bestOffset = Poe2.RitualUi.TileItemEntity;
            var bestItems = 0;
            foreach (var tile in tiles)
            {
                if (tile == 0) continue;
                if (Ptr(tile + Poe2.RitualUi.TileItemEntity) != 0)
                    items4F8++;

                for (var off = 0x4D0; off <= 0x520; off += 8)
                {
                    var probe = Ptr(tile + off);
                    if (probe == 0 || !LooksLikeEntity(probe)) continue;
                    var count = 0;
                    foreach (var t2 in tiles)
                        if (Ptr(t2 + off) != 0 && LooksLikeEntity(Ptr(t2 + off)))
                            count++;
                    if (count > bestItems)
                    {
                        bestItems = count;
                        bestOffset = off;
                    }
                }
            }

            if (items4F8 == 0 && bestItems == 0) continue;

            var rect = TryProjectUiRect(el, windowWidth, windowHeight, out var projectedRect)
                ? projectedRect
                : default;

            gridCandidates.Add(new RitualDeepGridCandidate(
                el,
                tiles.Length,
                items4F8,
                bestItems,
                bestOffset,
                rect,
                IsVisibleUiElement(el),
                IsRitualGridInActiveShop(el, root),
                IsValidRewardGrid(el, BranchHasRitualSignature(root))));
        }

        gridCandidates.Sort((a, b) => b.BestItemCount.CompareTo(a.BestItemCount));
        if (gridCandidates.Count > 6)
            gridCandidates.RemoveRange(6, gridCandidates.Count - 6);

        return new RitualDeepBranchReport(
            branch,
            root,
            rootChildren.Length,
            fast76Count,
            fastGrid,
            textHits.Count == 0 ? [] : textHits.ToArray(),
            gridCandidates.Count == 0 ? [] : gridCandidates.ToArray());

    }

    private bool LooksLikeEntity(nint address)
    {
        if (address == 0) return false;
        var details = Ptr(address + Poe2.Entity.EntityDetailsPtr);
        if (details == 0) return false;
        var name = ReadStdWString(details + Poe2.EntityDetails.Name);
        return name.Length >= 2;
    }

    private RitualRewardsRead BuildRitualRead(
        nint grid,
        nint root,
        nint gameUi,
        nint controllerGameUi,
        string source,
        float windowWidth,
        float windowHeight)
    {
        var branch = UiBranchCandidates.BranchForRoot(root, gameUi, controllerGameUi);
        var slots = ReadRitualSlots(grid, windowWidth, windowHeight, requireScreenRect: false);
        if (slots.Length == 0)
            return RitualRewardsRead.Closed;

        return new RitualRewardsRead(true, grid, root, branch, source, "", slots);
    }

    private void RememberRitualGrid(nint grid, nint root, nint gameUi, nint controllerGameUi)
    {
        _ritualGridAddr = grid;
        _ritualGridRoot = root;
        var branch = UiBranchCandidates.BranchForRoot(root, gameUi, controllerGameUi);
        if (branch != Poe2UiAnchors.BranchKind.None)
            _ritualProbeHint = branch;
    }

    /// <summary>
    /// BFS fallback used when the fixed index chain is broken. Throttle gates only new tree walks;
    /// a cached grid address is returned while <see cref="IsValidRewardGrid"/> still passes.
    /// </summary>
    private bool TryScanRitualGridThrottled(
        ReadOnlySpan<nint> roots,
        int rootCount,
        float windowWidth,
        float windowHeight,
        out nint grid,
        out nint root)
    {
        grid = 0;
        root = 0;
        if (rootCount <= 0) return false;

        if (_ritualGridAddr != 0)
        {
            grid = _ritualGridAddr;
            root = _ritualGridRoot;
            return true;
        }

        var now = DateTime.UtcNow;
        if (now < _ritualNextScanUtc)
            return false;

        _ritualNextScanUtc = now.AddMilliseconds(250);

        for (var i = 0; i < rootCount; i++)
        {
            var scanRoot = roots[i];
            if (scanRoot == 0) continue;

            var branchHasRitualSignature = BranchHasRitualSignature(scanRoot);
            var scanGrid = FindRitualRewardGrid(scanRoot, windowWidth, windowHeight);
            if (scanGrid == 0 || !IsValidRewardGrid(scanGrid, branchHasRitualSignature))
                continue;

            _ritualGridAddr = scanGrid;
            _ritualGridRoot = scanRoot;
            grid = scanGrid;
            root = scanRoot;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Per-frame revalidation of a cached grid. Matches BFS <see cref="FindRewardGridChild"/> rules:
    /// visible, 1–16 tiles, at least two item entities, mostly populated, and the ritual shop
    /// signature text must be visible in a parent context (rejects the 1-tile ghost left when closed).
    /// </summary>
    private bool IsValidRewardGrid(nint grid, bool branchHasRitualSignature = false)
    {
        if (!IsVisibleUiElement(grid)) return false;
        if (!TryCountRewardGridItems(grid, out var tileCount, out var itemCount)) return false;
        if (itemCount < 2 || itemCount * 2 < tileCount) return false;
        return IsRitualGridInActiveShop(grid) || branchHasRitualSignature;
    }

    /// <summary>True when a visible ancestor sibling still shows "Rituals Remaining" / tribute text.</summary>
    private bool IsRitualGridInActiveShop(nint grid, nint branchRoot = 0)
    {
        var cur = grid;
        for (var up = 0; up < 10; up++)
        {
            var parent = Ptr(cur + Poe2.UiElement.Parent);
            if (parent == 0) break;

            foreach (var child in ReadChildren(parent, maxCount: 64))
            {
                if (!IsVisibleUiElement(child)) continue;
                if (MatchesRitualSignature(child))
                    return true;
            }

            cur = parent;
        }

        return branchRoot != 0 && BranchHasRitualSignature(branchRoot);
    }

    private bool TryCountRewardGridItems(nint grid, out int tileCount, out int itemCount)
    {
        tileCount = 0;
        itemCount = 0;
        if (!IsVisibleUiElement(grid)) return false;

        var tiles = ReadChildren(grid, maxCount: 32);
        if (tiles.Length is < 1 or > 16) return false;

        tileCount = tiles.Length;
        foreach (var tile in tiles)
        {
            var item = Ptr(tile + Poe2.RitualUi.TileItemEntity);
            if (item != 0 && LooksLikeEntity(item))
                itemCount++;
        }

        return itemCount > 0;
    }

    private bool TryFastRitualGrid(nint root, out nint grid)
    {
        grid = 0;
        if (root == 0) return false;
        var children = ReadChildren(root, maxCount: 256);
        bool? branchHasRitualSignature = null;
        bool IsValidInBranch(nint candidate)
        {
            if (IsValidRewardGrid(candidate)) return true;
            branchHasRitualSignature ??= BranchHasRitualSignature(root);
            return IsValidRewardGrid(candidate, branchHasRitualSignature.Value);
        }

        if (children.Length > 76)
        {
            var child76Children = ReadChildren(children[76], maxCount: 128);
            if (child76Children.Length > 13)
            {
                grid = child76Children[13];
                if (grid != 0) return true;
            }
        }

        // Controller / compact GameUi layouts use fewer top-level children than the GH2 [76][13] chain.
        foreach (var child in children)
        {
            if (IsValidInBranch(child))
            {
                grid = child;
                return true;
            }

            foreach (var grandchild in ReadChildren(child, maxCount: 64))
            {
                if (!IsValidInBranch(grandchild)) continue;
                grid = grandchild;
                return true;
            }
        }

        return false;
    }

    private RitualRewardSlot[] ReadRitualSlots(
        nint grid,
        float windowWidth,
        float windowHeight,
        bool requireScreenRect = true)
    {
        if (!IsVisibleUiElement(grid)) return [];

        var tiles = ReadChildren(grid, maxCount: 32);
        if (tiles.Length is < 1 or > 16) return [];

        var elementCache = new Dictionary<nint, UiElementProjection.Element>(64);
        var parentCache = new Dictionary<nint, UiElementProjection.Point>(32);
        var slots = new List<RitualRewardSlot>(tiles.Length);
        foreach (var tile in tiles)
        {
            if (tile == 0 || !IsVisibleUiElement(tile)) continue;
            var item = Ptr(tile + Poe2.RitualUi.TileItemEntity);
            if (item == 0 || !LooksLikeEntity(item)) continue;

            UiRect rect = default;
            if (requireScreenRect)
            {
                if (!UiElementProjection.TryGetRect(tile, TryReadProjectionElement, windowWidth, windowHeight, elementCache, parentCache, out var projected))
                    continue;
                rect = new UiRect(projected.X, projected.Y, projected.W, projected.H);
            }

            slots.Add(ReadRitualSlot(tile, item, rect));
        }

        return slots.Count == 0 ? [] : slots.ToArray();

        bool TryReadProjectionElement(nint address, out UiElementProjection.Element element)
            => UiElementProjection.TryRead(_reader, address, out element);
    }

    private RitualRewardSlot ReadRitualSlot(nint tile, nint item, UiRect rect)
    {
        var fullPath = ReadMetadata(item);
        var internalName = PathBasename(fullPath);
        var rarity = Rarity.Normal;
        var baseName = "";
        var art = "";

        var modsComp = ResolveComponent(item, "Mods");
        if (modsComp != 0 && _reader.TryReadStruct<int>(modsComp + Poe2.ModsComponent.Rarity, out var r) && r is >= 0 and <= 3)
            rarity = (Rarity)r;

        var baseComp = ResolveComponent(item, "Base");
        if (baseComp != 0)
        {
            var nameRow = Ptr(baseComp + Poe2.BaseComponent.NameRow);
            var namePtr = nameRow == 0 ? 0 : Ptr(nameRow + Poe2.BaseComponent.RowDisplayName);
            if (namePtr != 0)
                baseName = _reader.ReadStringUtf16(namePtr, 96).Trim();
        }

        var renderItem = ResolveComponent(item, "RenderItem");
        if (renderItem != 0)
        {
            var pathPtr = Ptr(renderItem + Poe2.RenderItemComponent.ResourcePath);
            if (pathPtr != 0)
                art = PathBasename(_reader.ReadStringUtf16(pathPtr, 160));
        }

        var mods = ReadItemModLines(item, modsComp);
        return new RitualRewardSlot(tile, item, rect, fullPath, internalName, baseName, rarity, art, mods);
    }

    private string[] ReadItemModLines(nint item, nint modsComp)
    {
        var lines = new List<string>(8);
        if (modsComp != 0)
        {
            AddModLines(lines, modsComp + Poe2.ModsComponent.Mods + Poe2.ModVectors.Implicit);
            AddModLines(lines, modsComp + Poe2.ModsComponent.Mods + Poe2.ModVectors.Explicit);
            AddModLines(lines, modsComp + Poe2.ModsComponent.Mods + Poe2.ModVectors.Enchant);
        }

        var omp = ResolveComponent(item, "ObjectMagicProperties");
        if (omp != 0)
        {
            AddModLines(lines, omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Implicit);
            AddModLines(lines, omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Explicit);
            AddModLines(lines, omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Enchant);
        }

        return lines.Count == 0 ? [] : lines.ToArray();
    }

    private void AddModLines(List<string> lines, nint vectorAddress)
    {
        if (!_reader.TryReadStruct<StdVector>(vectorAddress, out var vec)) return;
        var count = ((long)vec.Last - (long)vec.First) / Poe2.ModVectors.EntryStride;
        if (vec.First == 0 || count is <= 0 or > 64) return;

        ModArrayStruct[] mods;
        try { mods = _reader.ReadArray<ModArrayStruct>(vec.First, (int)count); }
        catch { return; }

        foreach (var mod in mods)
        {
            if (mod.ModsPtr == 0) continue;
            var template = ReadModTemplate(mod.ModsPtr);
            if (string.IsNullOrWhiteSpace(template)) continue;
            var (v0, v1) = ReadModValues(mod.Values, mod.Value0);
            var formatted = FormatModLine(template, v0, v1);
            if (!string.IsNullOrWhiteSpace(formatted))
                lines.Add(formatted);
        }
    }

    private string ReadModTemplate(nint modsDatRow)
    {
        var ptr = Ptr(modsDatRow);
        return ptr == 0 ? "" : _reader.ReadStringUtf16(ptr, 256);
    }

    private (float value0, float value1) ReadModValues(StdVector values, int value0)
    {
        var count = ((long)values.Last - (long)values.First) / 4;
        if (values.First == 0 || count <= 0) return (float.NaN, float.NaN);
        if (count == 1) return (value0, float.NaN);
        Span<int> vals = stackalloc int[2];
        try
        {
            var read = _reader.ReadArray<int>(values.First, 2);
            vals[0] = read[0];
            vals[1] = read[1];
            return (vals[0], vals[1]);
        }
        catch
        {
            return (float.NaN, float.NaN);
        }
    }

    private static string FormatModLine(string template, float value0, float value1)
    {
        var line = template;
        if (!float.IsNaN(value0))
        {
            line = line.Replace("{0}", FormatNumber(value0), StringComparison.Ordinal);
            if (!float.IsNaN(value1))
                line = line.Replace("{1}", FormatNumber(value1), StringComparison.Ordinal);
        }
        return line.Trim();
    }

    private static string FormatNumber(float value)
    {
        if (Math.Abs(value - MathF.Round(value)) < 0.001f)
            return ((int)MathF.Round(value)).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private nint FindRitualRewardGrid(nint root, float windowWidth, float windowHeight, int minPopulatedItems = 2)
    {
        if (root == 0) return 0;

        minPopulatedItems = Math.Max(1, minPopulatedItems);
        var branchHasRitualSignature = BranchHasRitualSignature(root);
        var queue = new Queue<nint>();
        var visited = new HashSet<nint>();
        queue.Enqueue(root);

        var best = (nint)0;
        var bestScore = 0;

        while (queue.Count > 0 && visited.Count < 30000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (el != root && !IsVisibleUiElement(el)) continue;

            foreach (var child in ReadChildren(el, maxCount: 8192))
                queue.Enqueue(child);

            if (!TryCountRewardGridItems(el, out var tileCount, out var itemCount)) continue;
            if (itemCount < minPopulatedItems) continue;
            if (minPopulatedItems >= 2 && itemCount * 2 < tileCount) continue;
            if (!IsRitualGridInActiveShop(el) && !branchHasRitualSignature) continue;

            var score = itemCount * 100 + tileCount;
            if (TryProjectUiRect(el, windowWidth, windowHeight, out var rect))
            {
                score += (int)Math.Clamp(rect.H * 3f, 0f, 3000f);
                score += (int)Math.Clamp((windowWidth - rect.X) * 0.35f, 0f, 1200f);
            }
            if (score <= bestScore) continue;
            bestScore = score;
            best = el;
        }

        return best;
    }

    private bool TryProjectUiRect(nint element, float windowWidth, float windowHeight, out UiRect rect)
    {
        rect = default;
        var elementCache = new Dictionary<nint, UiElementProjection.Element>(64);
        var parentCache = new Dictionary<nint, UiElementProjection.Point>(32);
        if (!UiElementProjection.TryGetRect(element, TryReadProjectionElement, windowWidth, windowHeight, elementCache, parentCache, out var projected))
            return false;

        rect = new UiRect(projected.X, projected.Y, projected.W, projected.H);
        return true;

        bool TryReadProjectionElement(nint address, out UiElementProjection.Element uiElement)
            => UiElementProjection.TryRead(_reader, address, out uiElement);
    }

    private bool MatchesRitualSignature(nint element)
    {
        var text = ReadStdWString(element + Poe2.UiElement.Text);
        if (text.Length < 6) return false;
        foreach (var sig in RitualSignatureTexts)
            if (text.Contains(sig, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private bool BranchHasRitualSignature(nint root)
    {
        if (root == 0) return false;

        var queue = new Queue<nint>();
        var visited = new HashSet<nint>();
        queue.Enqueue(root);

        while (queue.Count > 0 && visited.Count < 25000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (el != root && !IsVisibleUiElement(el)) continue;
            if (MatchesRitualSignature(el)) return true;

            foreach (var child in ReadChildren(el, maxCount: 8192))
                queue.Enqueue(child);
        }

        return false;
    }

    private nint FindRewardGridChild(nint parent, int minPopulatedItems = 2)
    {
        var children = ReadChildren(parent, maxCount: 256);
        var best = (nint)0;
        var bestItems = 0;
        minPopulatedItems = Math.Max(1, minPopulatedItems);

        foreach (var child in children)
        {
            if (!IsVisibleUiElement(child)) continue;
            var tiles = ReadChildren(child, maxCount: 32);
            if (tiles.Length is < 1 or > 16) continue;

            var items = 0;
            foreach (var tile in tiles)
                if (Ptr(tile + Poe2.RitualUi.TileItemEntity) != 0)
                    items++;

            if (items < minPopulatedItems || items <= bestItems)
                continue;
            if (minPopulatedItems >= 2 && items * 2 < tiles.Length)
                continue;

            best = child;
            bestItems = items;
        }

        return best;
    }

    private bool IsVisibleUiElement(nint element)
        => element != 0
           && Ptr(element + Poe2.UiElement.Self) == element
           && _reader.TryReadStruct<uint>(element + Poe2.UiElement.Flags, out var flags)
           && (flags & (1u << Poe2.UiElement.FlagVisibleBit)) != 0;

    private nint[] ReadChildren(nint element, int maxCount)
    {
        var first = Ptr(element + Poe2.UiElement.Children);
        if (first == 0 || !_reader.TryReadStruct<nint>(element + Poe2.UiElement.ChildrenEnd, out var last))
            return [];

        var count = ((long)last - (long)first) / 8;
        if (count <= 0 || count > maxCount) return [];

        var result = new nint[count];
        for (long i = 0; i < count; i++)
            result[i] = Ptr(first + (nint)(i * 8));
        return result;
    }

    private static string PathBasename(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var file = path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }
}
