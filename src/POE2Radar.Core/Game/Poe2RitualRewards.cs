// Ritual Favours window reader — derived from GameHelper RitualHelper (GPL-3.0).
namespace POE2Radar.Core.Game;

/// <summary>Read-only Ritual tribute-shop reward tiles. Fast index chain first; throttled BFS fallback.</summary>
public sealed class Poe2RitualRewards
{
    public enum PathKind { Closed, FastChain, CachedFallback, BfsFallback }

    private readonly MemoryReader _reader;

    private nint _cachedBranchIgs;
    private Poe2UiAnchors.BranchKind _cachedBranch = Poe2UiAnchors.BranchKind.None;
    private nint _cachedGrid;
    private nint _bfsGrid;
    private DateTime _nextBfsUtc = DateTime.MinValue;
    private DateTime _nextColdUtc = DateTime.MinValue;

    public Poe2RitualRewards(MemoryReader reader) => _reader = reader;

    public bool PanelOpen { get; private set; }
    public PathKind LastPathKind { get; private set; } = PathKind.Closed;

    public readonly record struct RitualRewardTile(
        float X, float Y, float W, float H,
        Poe2RitualItemReader.RitualItemIdentity Item);

    public IReadOnlyList<RitualRewardTile> ReadRewards(
        nint inGameState,
        float winW,
        float winH,
        bool forceBfsFallback = false,
        Func<string, string?>? prettyNameLookup = null)
    {
        PanelOpen = false;
        LastPathKind = PathKind.Closed;

        if (inGameState == 0 || winW <= 0 || winH <= 0)
            return Array.Empty<RitualRewardTile>();

        if (_cachedBranchIgs != inGameState)
        {
            _cachedBranch = Poe2UiAnchors.BranchKind.None;
            _cachedGrid = 0;
            _bfsGrid = 0;
            _cachedBranchIgs = inGameState;
        }

        var now = DateTime.UtcNow;
        if (!forceBfsFallback && _cachedGrid != 0 && IsValidRewardGrid(_cachedGrid))
        {
            if (!Visible(_cachedGrid))
            {
                _cachedGrid = 0;
                return Array.Empty<RitualRewardTile>();
            }
            LastPathKind = PathKind.CachedFallback;
            return ReadTilesFromGrid(_cachedGrid, winW, winH, prettyNameLookup);
        }

        if (!forceBfsFallback && now < _nextColdUtc && _cachedGrid == 0 && _bfsGrid == 0)
            return Array.Empty<RitualRewardTile>();

        nint grid = 0;
        if (!forceBfsFallback && TryFastChain(inGameState, out var fastGrid, out var branch) && Visible(fastGrid))
        {
            grid = fastGrid;
            _cachedBranch = branch;
            LastPathKind = PathKind.FastChain;
        }
        else if (TryBfsGrid(inGameState, forceBfsFallback, now, out var scanGrid))
        {
            grid = scanGrid;
            LastPathKind = PathKind.BfsFallback;
        }

        if (grid == 0)
        {
            _nextColdUtc = now.AddMilliseconds(Poe2.Ritual.ColdClosedThrottleMs);
            return Array.Empty<RitualRewardTile>();
        }

        _cachedGrid = grid;
        if (Visible(grid) && IsValidRewardGrid(grid))
            PanelOpen = true;

        var tiles = ReadTilesFromGrid(grid, winW, winH, prettyNameLookup);
        if (tiles.Count == 0)
        {
            if (!PanelOpen)
            {
                _cachedGrid = 0;
                _nextColdUtc = now.AddMilliseconds(Poe2.Ritual.ColdClosedThrottleMs);
            }
            return Array.Empty<RitualRewardTile>();
        }

        return tiles;
    }

    private bool TryFastChain(nint inGameState, out nint grid, out Poe2UiAnchors.BranchKind branch)
    {
        grid = 0;
        branch = _cachedBranch;

        if (!Poe2UiAnchors.TryDiscover(_reader, inGameState, out var gameUi, out var controllerUi))
            return false;

        if (branch == Poe2UiAnchors.BranchKind.Controller && controllerUi != 0)
        {
            if (TryFastChainOnRoot(controllerUi, out grid)) return grid != 0;
        }
        else if (branch == Poe2UiAnchors.BranchKind.KeyboardMouse && gameUi != 0)
        {
            if (TryFastChainOnRoot(gameUi, out grid)) return grid != 0;
        }

        if (gameUi != 0 && TryFastChainOnRoot(gameUi, out grid))
        {
            branch = Poe2UiAnchors.BranchKind.KeyboardMouse;
            _cachedBranch = branch;
            return true;
        }

        if (controllerUi != 0 && TryFastChainOnRoot(controllerUi, out grid))
        {
            branch = Poe2UiAnchors.BranchKind.Controller;
            _cachedBranch = branch;
            return true;
        }

        return false;
    }

    private bool TryFastChainOnRoot(nint root, out nint grid)
    {
        grid = 0;
        var a = Child(root, Poe2.Ritual.FastChainChildA);
        if (a == 0) return false;
        var window = Child(a, Poe2.Ritual.FastChainChildB);
        if (window == 0 || !Visible(window)) return false;

        // GameHelper: child[13] is the ritual window; its children are reward tiles.
        if (IsValidRewardGrid(window))
        {
            grid = window;
            PanelOpen = true;
            return true;
        }

        var nested = FindRewardGridChild(window);
        if (nested != 0)
        {
            grid = nested;
            PanelOpen = true;
            return true;
        }

        if (Children(window, out _, out var n) && n is >= 1 and <= Poe2.Ritual.MaxRewardTiles)
        {
            grid = window;
            PanelOpen = true;
            return true;
        }

        return false;
    }

    private bool TryBfsGrid(nint inGameState, bool force, DateTime now, out nint grid)
    {
        grid = 0;
        if (_bfsGrid != 0 && IsValidRewardGrid(_bfsGrid))
        {
            grid = _bfsGrid;
            return true;
        }

        _bfsGrid = 0;
        if (!force && now < _nextBfsUtc) return false;
        _nextBfsUtc = now.AddMilliseconds(Poe2.Ritual.BfsThrottleMs);

        if (!Poe2UiAnchors.TryDiscover(_reader, inGameState, out var gameUi, out var controllerUi))
            return false;

        var roots = new List<nint>(2);
        if (_cachedBranch == Poe2UiAnchors.BranchKind.Controller && controllerUi != 0) roots.Add(controllerUi);
        else if (_cachedBranch == Poe2UiAnchors.BranchKind.KeyboardMouse && gameUi != 0) roots.Add(gameUi);
        else
        {
            if (gameUi != 0) roots.Add(gameUi);
            if (controllerUi != 0 && controllerUi != gameUi) roots.Add(controllerUi);
        }

        foreach (var root in roots)
        {
            var found = FindRitualRewardGrid(root);
            if (found == 0) continue;
            _bfsGrid = found;
            grid = found;
            return true;
        }

        return false;
    }

    private IReadOnlyList<RitualRewardTile> ReadTilesFromGrid(
        nint grid, float winW, float winH, Func<string, string?>? prettyNameLookup)
    {
        var result = new List<RitualRewardTile>();
        if (!Children(grid, out var first, out var n)) return result;

        for (long i = 0; i < n && result.Count < Poe2.Ritual.MaxRewardTiles; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            if (tile == 0 || !Visible(tile)) continue;
            var itemPtr = Ptr(tile + Poe2.Ritual.TileItemEntityPtr);
            if (itemPtr == 0) continue;
            if (!Poe2RitualItemReader.TryRead(_reader, itemPtr, prettyNameLookup, out var item)) continue;
            if (!TryTileRect(tile, winW, winH, out var x, out var y, out var w, out var h)) continue;
            result.Add(new RitualRewardTile(x, y, w, h, item));
        }

        if (result.Count > 0) PanelOpen = true;
        return result;
    }

    private bool IsValidRewardGrid(nint gridAddr)
    {
        if (gridAddr == 0 || !Visible(gridAddr)) return false;
        if (!Children(gridAddr, out var first, out var n)) return false;
        if (n is < 1 or > Poe2.Ritual.MaxRewardTiles) return false;

        for (long i = 0; i < n; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            if (tile != 0 && Ptr(tile + Poe2.Ritual.TileItemEntityPtr) != 0) return true;
        }
        return false;
    }

    private nint FindRitualRewardGrid(nint gameUiRoot)
    {
        if (gameUiRoot == 0) return 0;
        var queue = new Queue<nint>();
        var visited = new HashSet<nint>();
        queue.Enqueue(gameUiRoot);
        nint sigEl = 0;

        while (queue.Count > 0 && visited.Count < Poe2.Ritual.BfsMaxNodes)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            if (el != gameUiRoot && !Visible(el)) continue;

            if (Children(el, out var f, out var nn))
            {
                for (long k = 0; k < nn; k++)
                    queue.Enqueue(Ptr(f + (nint)(k * 8)));
            }

            if (sigEl == 0 && MatchesRitualSignature(el))
                sigEl = el;
        }

        if (sigEl == 0) return 0;
        var cur = sigEl;
        for (var up = 0; up < 8; up++)
        {
            var grid = FindRewardGridChild(cur);
            if (grid != 0) return grid;
            var parent = Ptr(cur + Poe2.UiElement.Parent);
            if (parent == 0) break;
            cur = parent;
        }
        return 0;
    }

    private nint FindRewardGridChild(nint parent)
    {
        if (!Children(parent, out var first, out var n)) return 0;
        nint best = 0;
        var bestItems = 0;
        for (long i = 0; i < n; i++)
        {
            var c = Ptr(first + (nint)(i * 8));
            if (c == 0) continue;
            var (items, tiles) = CountRewardTiles(c);
            if (items >= 2 && items > bestItems && items * 2 >= tiles)
            {
                bestItems = items;
                best = c;
            }
        }
        return best;
    }

    private (int Items, int Tiles) CountRewardTiles(nint candidate)
    {
        if (!Children(candidate, out var first, out var n)) return (0, 0);
        if (n is < 1 or > Poe2.Ritual.MaxRewardTiles) return (0, 0);
        var items = 0;
        for (long i = 0; i < n; i++)
        {
            var tile = Ptr(first + (nint)(i * 8));
            if (tile != 0 && Ptr(tile + Poe2.Ritual.TileItemEntityPtr) != 0) items++;
        }
        return (items, (int)n);
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

    private bool TryTileRect(nint el, float winW, float winH, out float x, out float y, out float w, out float h)
        => UiElementScreenRect.TryGet(_reader, el, winW, winH, out x, out y, out w, out h);

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
