namespace POE2Radar.Core.Game;

/// <summary>Auto-locate the live UiRoot when the hardcoded <see cref="Poe2.InGameState.UiRoot"/>
/// offset drifts (reads 0). Same self-ref + children validation as <c>--find-map</c> in Research.</summary>
internal static class UiRootResolver
{
    private static nint _cachedForIgs;
    private static nint _cachedRoot;
    private static int _cachedOffset = Poe2.InGameState.UiRoot;

    public static int CachedOffset => _cachedOffset;

    public static nint Resolve(MemoryReader reader, nint inGameState)
    {
        if (inGameState == 0) return 0;
        if (_cachedForIgs == inGameState && _cachedRoot != 0)
            return _cachedRoot;

        var atFixed = SafePtr(reader, inGameState + Poe2.InGameState.UiRoot);
        if (IsUiRoot(reader, atFixed))
        {
            _cachedForIgs = inGameState;
            _cachedRoot = atFixed;
            _cachedOffset = Poe2.InGameState.UiRoot;
            return atFixed;
        }

        for (var o = 0; o < 0x1000; o += 8)
        {
            var p = SafePtr(reader, inGameState + o);
            if (!IsUiRoot(reader, p)) continue;
            _cachedForIgs = inGameState;
            _cachedRoot = p;
            _cachedOffset = o;
            return p;
        }

        _cachedForIgs = inGameState;
        _cachedRoot = 0;
        return 0;
    }

    public static void Invalidate(nint inGameState)
    {
        if (_cachedForIgs == inGameState)
        {
            _cachedForIgs = 0;
            _cachedRoot = 0;
        }
    }

    private static bool IsUiRoot(MemoryReader reader, nint el)
    {
        if (el == 0) return false;
        if (SafePtr(reader, el + Poe2.UiElement.Self) != el) return false;
        var first = SafePtr(reader, el + Poe2.UiElement.Children);
        if (first == 0) return false;
        if (!reader.TryReadStruct<nint>(el + Poe2.UiElement.ChildrenEnd, out var last)) return false;
        var n = ((long)last - (long)first) / 8;
        if (n is < 1 or > 8192) return false;
        var c0 = SafePtr(reader, first);
        return c0 != 0 && SafePtr(reader, c0 + Poe2.UiElement.Self) == c0;
    }

    private static nint SafePtr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
