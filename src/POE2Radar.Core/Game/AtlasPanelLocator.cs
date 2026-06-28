namespace POE2Radar.Core.Game;

/// <summary>
/// Locates the atlas node-list container via a flags fingerprint walk from the
/// <see cref="Poe2.InGameState.KeyboardUiRootStructPtr"/> / <see cref="Poe2.InGameState.GamepadUiRootStructPtr"/>
/// manager — the same path as GameHelper's Atlas plugin (<c>GetAtlasPanelAddress</c>).
/// </summary>
public static class AtlasPanelLocator
{
    private const uint PanelFp = 0x00562EF5;
    private const uint GateFp = 0x00502EF1;
    private const uint NodeListFp = 0x00502EF3;
    private const uint MapNodeFp = 0x00542EF3;
    private const uint VisibleMask = 0x800u;

    private static readonly uint[] KbMouseChain = [PanelFp, GateFp, NodeListFp];
    private const int KbMouseGateStep = 1;
    private static readonly uint[] ControllerChain = [GateFp, MapNodeFp, GateFp, PanelFp, GateFp, NodeListFp];
    private const int ControllerGateStep = 4;

    public readonly record struct PanelDiag(
        bool NodeListResolved,
        bool NodeListOpen,
        nint NodeList,
        nint UiManager);

    public static PanelDiag GetDiag(MemoryReader reader, nint inGameState)
    {
        var resolved = TryResolveNodeList(reader, inGameState, out var list, out var mgr);
        var open = resolved && IsHierarchicallyVisible(reader, list);
        return new PanelDiag(resolved, open, list, mgr);
    }

    public static bool IsAtlasOpen(MemoryReader reader, nint inGameState)
    {
        if (!TryResolveNodeList(reader, inGameState, out var list, out _))
            return false;
        return IsHierarchicallyVisible(reader, list);
    }

    /// <summary>Try keyboard then gamepad UiRootStruct managers; each tries KBM then controller chains.</summary>
    public static bool TryResolveNodeList(MemoryReader reader, nint inGameState, out nint nodeList, out nint uiManager)
    {
        nodeList = 0;
        uiManager = 0;
        if (inGameState == 0) return false;

        var kb = SafePtr(reader, inGameState + Poe2.InGameState.KeyboardUiRootStructPtr);
        var pad = SafePtr(reader, inGameState + Poe2.InGameState.GamepadUiRootStructPtr);

        if (kb != 0 && TryResolveNodeListFromManager(reader, kb, out nodeList))
        {
            uiManager = kb;
            return true;
        }

        if (pad != 0 && pad != kb && TryResolveNodeListFromManager(reader, pad, out nodeList))
        {
            uiManager = pad;
            return true;
        }

        return false;
    }

    public static bool TryResolveNodeListFromManager(MemoryReader reader, nint uiManager, out nint nodeList)
    {
        nodeList = WalkFromManager(reader, uiManager);
        return nodeList != 0;
    }

    private static nint WalkFromManager(MemoryReader reader, nint uiManager)
    {
        var list = WalkFlagsChain(reader, uiManager, KbMouseChain, KbMouseGateStep, 0);
        return list != 0 ? list : WalkFlagsChain(reader, uiManager, ControllerChain, ControllerGateStep, 0);
    }

    private static nint WalkFlagsChain(MemoryReader reader, nint parentAddr, IReadOnlyList<uint> flagsChain, int gateStep, int step)
    {
        if (step == flagsChain.Count) return parentAddr;

        var first = SafePtr(reader, parentAddr + Poe2.UiElement.Children);
        if (first == 0 || !reader.TryReadStruct<nint>(parentAddr + Poe2.UiElement.ChildrenEnd, out var last))
            return 0;
        var count = ((long)last - (long)first) / 8;
        if (count is <= 0 or > 5000) return 0;

        var target = flagsChain[step] & ~VisibleMask;
        for (var pass = 0; pass < 2; pass++)
        {
            var wantVisible = pass == 0;
            for (long i = 0; i < count; i++)
            {
                var child = SafePtr(reader, first + (nint)(i * 8));
                if (child == 0 || SafePtr(reader, child + Poe2.UiElement.Self) != child) continue;
                if (!reader.TryReadStruct<uint>(child + Poe2.UiElement.Flags, out var flags)) continue;
                if ((flags & ~VisibleMask) != target) continue;
                var visible = (flags & VisibleMask) != 0;
                if (visible != wantVisible) continue;
                if (step == gateStep && !visible) continue;

                var deeper = WalkFlagsChain(reader, child, flagsChain, gateStep, step + 1);
                if (deeper != 0) return deeper;
            }
        }

        return 0;
    }

    private static bool IsHierarchicallyVisible(MemoryReader reader, nint el)
    {
        var cur = el;
        var guard = 0;
        while (cur != 0 && guard++ < 16)
        {
            if (!reader.TryReadStruct<uint>(cur + Poe2.UiElement.Flags, out var fl)) return false;
            if (((fl >> Poe2.UiElement.FlagVisibleBit) & 1) == 0) return false;
            var par = SafePtr(reader, cur + Poe2.UiElement.Parent);
            if (par == cur) break;
            cur = par;
        }
        return true;
    }

    private static nint SafePtr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
