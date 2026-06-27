namespace POE2Radar.Core.Game;

/// <summary>Locate GameUi / GameUiController branches for panel readers (Ritual, loot tags, maps).</summary>
public static class Poe2UiAnchors
{
    public enum BranchKind { None, KeyboardMouse, Controller }

    public static bool TryDiscover(MemoryReader reader, nint inGameState, out nint gameUi, out nint controllerGameUi)
    {
        gameUi = controllerGameUi = 0;
        var uiRootStruct = Ptr(reader, inGameState + Poe2.InGameState.UiRootStructPtr);
        if (uiRootStruct != 0)
        {
            gameUi = Ptr(reader, uiRootStruct + Poe2.UiRootStruct.GameUiPtr);
            controllerGameUi = Ptr(reader, uiRootStruct + Poe2.UiRootStruct.GameUiControllerPtr);
            if (HasMapParentChain(reader, gameUi) || HasMapParentChain(reader, controllerGameUi))
                return gameUi != 0 || controllerGameUi != 0;
        }

        for (var o = 0; o < 0x2000; o += 8)
        {
            var s = Ptr(reader, inGameState + o);
            if (s == 0) continue;
            var g = Ptr(reader, s + Poe2.UiRootStruct.GameUiPtr);
            var c = Ptr(reader, s + Poe2.UiRootStruct.GameUiControllerPtr);
            if (gameUi == 0 && HasMapParentChain(reader, g)) gameUi = g;
            if (controllerGameUi == 0 && HasMapParentChain(reader, c)) controllerGameUi = c;
            if (gameUi != 0 && controllerGameUi != 0) return true;
        }

        for (var o = 0; o < 0x3000; o += 8)
        {
            var p = Ptr(reader, inGameState + o);
            if (p == 0) continue;
            if (gameUi == 0 && HasMapParentChain(reader, p)) gameUi = p;
            if (controllerGameUi == 0 && HasMapParentChain(reader, p)) controllerGameUi = p;
            if (gameUi != 0 && controllerGameUi != 0) return true;
        }

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
