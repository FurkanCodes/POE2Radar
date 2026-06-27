using System.Globalization;
using System.Numerics;

namespace POE2Radar.Core.Game;

public sealed partial class Poe2Live
{
    private static readonly string[] RitualSignatureTexts = ["Rituals Remaining", "tribute to the king"];
    private static readonly char[] PathSeparators = ['/', '\\'];

    private nint _ritualGridAddr;
    private nint _ritualGridRoot;
    private nint _ritualLastInGameState;
    private DateTime _ritualNextScanUtc = DateTime.MinValue;
    private Poe2UiAnchors.BranchKind _ritualProbeHint;
    private int _ritualScanRootIndex;

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
        _ritualScanRootIndex = 0;
        _ritualProbeHint = Poe2UiAnchors.BranchKind.None;
    }

    /// <summary>
    /// Reads the visible Ritual Favours reward grid. Window-open detection (visibility / lightweight grid
    /// validation) is separate from slot population so reopening the panel can succeed before tiles finish loading.
    /// Expensive UI-tree BFS is throttled; a previously found grid is reused while it still validates.
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
            && liveFast != _ritualGridAddr)
        {
            // [76][13] now points at a different element (reopened shop) — drop stale cache.
            InvalidateRitualUiCache();
        }

        if (_ritualGridAddr != 0 && IsValidRewardGrid(_ritualGridAddr))
        {
            return BuildRitualRead(
                _ritualGridAddr, _ritualGridRoot, gameUi, controllerGameUi,
                "cached", windowWidth, windowHeight);
        }

        _ritualGridAddr = 0;
        _ritualGridRoot = 0;
        _ritualNextScanUtc = DateTime.MinValue;

        if (!forceBfsFallback)
        {
            for (var i = 0; i < rootCount; i++)
            {
                var root = roots[i];
                if (!TryFastRitualGrid(root, out var grid)) continue;
                if (!IsValidRewardGrid(grid)) continue;

                RememberRitualGrid(grid, root, gameUi, controllerGameUi);
                return BuildRitualRead(grid, root, gameUi, controllerGameUi, "fast", windowWidth, windowHeight);
            }
        }

        if (TryScanRitualGridThrottled(roots, rootCount, gameUi, out var scanGrid, out var scanRoot))
        {
            RememberRitualGrid(scanGrid, scanRoot, gameUi, controllerGameUi);
            return BuildRitualRead(scanGrid, scanRoot, gameUi, controllerGameUi, "bfs", windowWidth, windowHeight);
        }

        if (!forceBfsFallback)
        {
            for (var i = 0; i < rootCount; i++)
            {
                var root = roots[i];
                if (!TryFastRitualGrid(root, out var grid)) continue;
                if (!IsVisibleUiElement(grid)) continue;
                if (!IsRitualGridInActiveShop(grid)) continue;
                if (!TryCountRewardGridItems(grid, out var tileCount, out var itemCount)) continue;

                // Closed-panel ghost: exactly one tiny stale tile. Real shop load-in has 0 items across many tiles.
                if (tileCount <= 1 && itemCount <= 1) continue;

                return BuildRitualRead(grid, root, gameUi, controllerGameUi, "fast-pending", windowWidth, windowHeight);
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
        var slots = ReadRitualSlots(grid, windowWidth, windowHeight);
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
        nint gameUi,
        out nint grid,
        out nint root)
    {
        grid = 0;
        root = 0;
        if (rootCount <= 0) return false;

        if (_ritualGridAddr != 0 && IsValidRewardGrid(_ritualGridAddr))
        {
            grid = _ritualGridAddr;
            root = _ritualGridRoot;
            return true;
        }

        _ritualGridAddr = 0;
        _ritualGridRoot = 0;

        var now = DateTime.UtcNow;
        if (now < _ritualNextScanUtc)
            return false;

        _ritualNextScanUtc = now.AddMilliseconds(750);

        if (gameUi != 0)
        {
            var gameUiGrid = FindRitualRewardGrid(gameUi);
            if (gameUiGrid != 0 && IsValidRewardGrid(gameUiGrid))
            {
                _ritualGridAddr = gameUiGrid;
                _ritualGridRoot = gameUi;
                grid = gameUiGrid;
                root = gameUi;
                return true;
            }
        }

        var scanIndex = Math.Clamp(_ritualScanRootIndex, 0, rootCount - 1);
        _ritualScanRootIndex = (scanIndex + 1) % rootCount;
        var scanRoot = roots[scanIndex];
        if (scanRoot == gameUi && rootCount > 1)
        {
            scanIndex = (scanIndex + 1) % rootCount;
            _ritualScanRootIndex = scanIndex;
            scanRoot = roots[scanIndex];
        }

        var scanGrid = FindRitualRewardGrid(scanRoot);
        if (scanGrid == 0 || !IsValidRewardGrid(scanGrid))
            return false;

        _ritualGridAddr = scanGrid;
        _ritualGridRoot = scanRoot;
        grid = scanGrid;
        root = scanRoot;
        return true;
    }

    /// <summary>
    /// Per-frame revalidation of a cached grid. Matches BFS <see cref="FindRewardGridChild"/> rules:
    /// visible, 1–16 tiles, at least two item entities, mostly populated, and the ritual shop
    /// signature text must be visible in a parent context (rejects the 1-tile ghost left when closed).
    /// </summary>
    private bool IsValidRewardGrid(nint grid)
    {
        if (!IsVisibleUiElement(grid)) return false;
        if (!TryCountRewardGridItems(grid, out var tileCount, out var itemCount)) return false;
        if (itemCount < 2 || itemCount * 2 < tileCount) return false;
        return IsRitualGridInActiveShop(grid);
    }

    /// <summary>True when a visible ancestor sibling still shows "Rituals Remaining" / tribute text.</summary>
    private bool IsRitualGridInActiveShop(nint grid)
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

        return false;
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
            if (Ptr(tile + Poe2.RitualUi.TileItemEntity) != 0)
                itemCount++;
        }

        return itemCount > 0;
    }

    private bool TryFastRitualGrid(nint root, out nint grid)
    {
        grid = 0;
        if (root == 0) return false;
        var children = ReadChildren(root, maxCount: 256);
        if (children.Length <= 76) return false;
        var child76 = children[76];
        var child76Children = ReadChildren(child76, maxCount: 128);
        if (child76Children.Length <= 13) return false;
        grid = child76Children[13];
        return grid != 0;
    }

    private RitualRewardSlot[] ReadRitualSlots(nint grid, float windowWidth, float windowHeight)
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
            if (item == 0) continue;
            if (!UiElementProjection.TryGetRect(tile, TryReadProjectionElement, windowWidth, windowHeight, elementCache, parentCache, out var rect))
                continue;

            slots.Add(ReadRitualSlot(tile, item, new UiRect(rect.X, rect.Y, rect.W, rect.H)));
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

    private nint FindRitualRewardGrid(nint root)
    {
        if (root == 0) return 0;

        var queue = new Queue<nint>();
        var visited = new HashSet<nint>();
        queue.Enqueue(root);
        var sigEl = (nint)0;

        while (queue.Count > 0 && visited.Count < 20000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (el != root && !IsVisibleUiElement(el)) continue;

            foreach (var child in ReadChildren(el, maxCount: 8192))
                queue.Enqueue(child);

            if (sigEl == 0 && MatchesRitualSignature(el))
                sigEl = el;
        }

        if (sigEl == 0) return 0;

        var cur = sigEl;
        for (var up = 0; up < 8; up++)
        {
            var grid = FindRewardGridChild(cur);
            if (grid != 0) return grid;
            cur = Ptr(cur + Poe2.UiElement.Parent);
            if (cur == 0) break;
        }

        return 0;
    }

    private bool MatchesRitualSignature(nint element)
    {
        var text = ReadStdWString(element + Poe2.UiElement.Text);
        if (text.Length < 6) return false;
        foreach (var sig in RitualSignatureTexts)
            if (text.Contains(sig, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private nint FindRewardGridChild(nint parent)
    {
        var children = ReadChildren(parent, maxCount: 256);
        var best = (nint)0;
        var bestItems = 0;

        foreach (var child in children)
        {
            if (!IsVisibleUiElement(child)) continue;
            var tiles = ReadChildren(child, maxCount: 32);
            if (tiles.Length is < 1 or > 16) continue;

            var items = 0;
            foreach (var tile in tiles)
                if (Ptr(tile + Poe2.RitualUi.TileItemEntity) != 0)
                    items++;

            if (items >= 2 && items > bestItems && items * 2 >= tiles.Length)
            {
                best = child;
                bestItems = items;
            }
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
