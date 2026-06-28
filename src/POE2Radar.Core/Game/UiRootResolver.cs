namespace POE2Radar.Core.Game;

/// <summary>Resolve the outer UiElement tree via <see cref="Poe2.UiRootStruct.UiRootPtr"/> inside the
/// keyboard/gamepad manager structs at <see cref="Poe2.InGameState.KeyboardUiRootStructPtr"/> /
/// <see cref="Poe2.InGameState.GamepadUiRootStructPtr"/>. Falls back to legacy direct reads + scan.</summary>
internal static class UiRootResolver
{
    private static nint _cachedForIgs;
    private static nint _cachedRoot;
    private static int _cachedOffset = Poe2.InGameState.KeyboardUiRootStructPtr;

    public static int CachedOffset => _cachedOffset;

    public static nint Resolve(MemoryReader reader, nint inGameState)
    {
        if (inGameState == 0) return 0;
        if (_cachedForIgs == inGameState && _cachedRoot != 0)
            return _cachedRoot;

        var kbStruct = SafePtr(reader, inGameState + Poe2.InGameState.KeyboardUiRootStructPtr);
        if (kbStruct != 0)
        {
            var fromKb = SafePtr(reader, kbStruct + Poe2.UiRootStruct.UiRootPtr);
            if (IsUiRoot(reader, fromKb))
            {
                Cache(inGameState, fromKb, Poe2.InGameState.KeyboardUiRootStructPtr);
                return fromKb;
            }
        }

        var padStruct = SafePtr(reader, inGameState + Poe2.InGameState.GamepadUiRootStructPtr);
        if (padStruct != 0)
        {
            var fromPad = SafePtr(reader, padStruct + Poe2.UiRootStruct.UiRootPtr);
            if (IsUiRoot(reader, fromPad))
            {
                Cache(inGameState, fromPad, Poe2.InGameState.GamepadUiRootStructPtr);
                return fromPad;
            }
        }

        // Legacy: +0x2F0 pointed directly at a self-ref UiElement (pre-0.5.x).
        var atFixed = SafePtr(reader, inGameState + Poe2.InGameState.UiRoot);
        if (IsUiRoot(reader, atFixed))
        {
            Cache(inGameState, atFixed, Poe2.InGameState.UiRoot);
            return atFixed;
        }

        for (var o = 0; o < 0x1000; o += 8)
        {
            var p = SafePtr(reader, inGameState + o);
            if (!IsUiRoot(reader, p)) continue;
            Cache(inGameState, p, o);
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

    private static void Cache(nint inGameState, nint root, int offset)
    {
        _cachedForIgs = inGameState;
        _cachedRoot = root;
        _cachedOffset = offset;
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
