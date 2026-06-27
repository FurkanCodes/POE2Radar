namespace POE2Radar.Core.Game;

/// <summary>Locate GameUi / GameUiController branches for panel readers (Ritual, loot tags, maps).</summary>
public static class Poe2UiAnchors
{
    public enum BranchKind { None, KeyboardMouse, Controller }

    private static nint _discoverCacheIgs;
    private static nint _discoverCacheGameUi;
    private static nint _discoverCacheControllerUi;

    public static void InvalidateDiscoverCache() => _discoverCacheIgs = 0;

    /// <summary>Cached GameUi discovery — fast path avoids InGameState scans unless <paramref name="allowScan"/>.</summary>
    public static bool TryDiscoverCached(MemoryReader reader, nint inGameState, bool allowScan, out nint gameUi, out nint controllerGameUi)
    {
        gameUi = controllerGameUi = 0;
        if (_discoverCacheIgs == inGameState && (_discoverCacheGameUi != 0 || _discoverCacheControllerUi != 0))
        {
            gameUi = _discoverCacheGameUi;
            controllerGameUi = _discoverCacheControllerUi;
            SanitizeBranches(reader, ref gameUi, ref controllerGameUi);
            return gameUi != 0 || controllerGameUi != 0;
        }

        var uiRootStruct = Ptr(reader, inGameState + Poe2.InGameState.UiRootStructPtr);
        if (uiRootStruct != 0)
        {
            gameUi = Ptr(reader, uiRootStruct + Poe2.UiRootStruct.GameUiPtr);
            controllerGameUi = Ptr(reader, uiRootStruct + Poe2.UiRootStruct.GameUiControllerPtr);
            if (HasMapParentChain(reader, gameUi) || HasMapParentChain(reader, controllerGameUi))
            {
                SanitizeBranches(reader, ref gameUi, ref controllerGameUi);
                CacheDiscover(inGameState, gameUi, controllerGameUi);
                return gameUi != 0 || controllerGameUi != 0;
            }
            gameUi = controllerGameUi = 0;
        }

        if (!allowScan) return false;
        if (!TryDiscover(reader, inGameState, out gameUi, out controllerGameUi)) return false;
        CacheDiscover(inGameState, gameUi, controllerGameUi);
        return gameUi != 0 || controllerGameUi != 0;
    }

    private static void CacheDiscover(nint inGameState, nint gameUi, nint controllerGameUi)
    {
        _discoverCacheIgs = inGameState;
        _discoverCacheGameUi = gameUi;
        _discoverCacheControllerUi = controllerGameUi;
    }

    /// <summary>Reject garbage UiElement roots (invalid std::vector child range).</summary>
    public static bool IsPlausibleBranch(MemoryReader reader, nint root)
    {
        if (root == 0) return false;
        var first = Ptr(reader, root + Poe2.UiElement.Children);
        if (first == 0) return false;
        if (!reader.TryReadStruct<nint>(root + Poe2.UiElement.ChildrenEnd, out var last)) return false;
        var n = ((long)last - (long)first) / 8;
        return n is >= 0 and <= 4096;
    }

    public static void SanitizeBranches(MemoryReader reader, ref nint gameUi, ref nint controllerGameUi)
    {
        if (!IsPlausibleBranch(reader, gameUi)) gameUi = 0;
        if (!IsPlausibleBranch(reader, controllerGameUi)) controllerGameUi = 0;
    }

    public static bool TryDiscover(MemoryReader reader, nint inGameState, out nint gameUi, out nint controllerGameUi)
    {
        gameUi = controllerGameUi = 0;
        var uiRootStruct = Ptr(reader, inGameState + Poe2.InGameState.UiRootStructPtr);
        if (uiRootStruct != 0)
        {
            gameUi = Ptr(reader, uiRootStruct + Poe2.UiRootStruct.GameUiPtr);
            controllerGameUi = Ptr(reader, uiRootStruct + Poe2.UiRootStruct.GameUiControllerPtr);
            if (HasMapParentChain(reader, gameUi) || HasMapParentChain(reader, controllerGameUi))
            {
                SanitizeBranches(reader, ref gameUi, ref controllerGameUi);
                return gameUi != 0 || controllerGameUi != 0;
            }
        }

        for (var o = 0; o < 0x2000; o += 8)
        {
            var s = Ptr(reader, inGameState + o);
            if (s == 0) continue;
            var g = Ptr(reader, s + Poe2.UiRootStruct.GameUiPtr);
            var c = Ptr(reader, s + Poe2.UiRootStruct.GameUiControllerPtr);
            if (gameUi == 0 && HasMapParentChain(reader, g) && IsPlausibleBranch(reader, g)) gameUi = g;
            if (controllerGameUi == 0 && HasMapParentChain(reader, c) && IsPlausibleBranch(reader, c)) controllerGameUi = c;
            if (gameUi != 0 && controllerGameUi != 0)
            {
                SanitizeBranches(reader, ref gameUi, ref controllerGameUi);
                return gameUi != 0 || controllerGameUi != 0;
            }
        }

        for (var o = 0; o < 0x3000; o += 8)
        {
            var p = Ptr(reader, inGameState + o);
            if (p == 0) continue;
            if (gameUi == 0 && HasMapParentChain(reader, p) && IsPlausibleBranch(reader, p)) gameUi = p;
            if (controllerGameUi == 0 && HasMapParentChain(reader, p) && IsPlausibleBranch(reader, p)) controllerGameUi = p;
            if (gameUi != 0 && controllerGameUi != 0)
            {
                SanitizeBranches(reader, ref gameUi, ref controllerGameUi);
                return gameUi != 0 || controllerGameUi != 0;
            }
        }

        SanitizeBranches(reader, ref gameUi, ref controllerGameUi);
        return gameUi != 0 || controllerGameUi != 0;
    }

    private static bool HasMapParentChain(MemoryReader reader, nint importantUi)
    {
        if (importantUi == 0) return false;
        var mapParent = Ptr(reader, importantUi + Poe2.ImportantUi.MapParentPtr);
        if (mapParent == 0) return false;
        var large = Ptr(reader, mapParent + Poe2.MapParent.LargeMapPtr);
        var mini = Ptr(reader, mapParent + Poe2.MapParent.MiniMapPtr);
        return large != 0 && mini != 0 && large != mini;
    }

    private static nint Ptr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
