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
        var resolved = TryResolveNodeListAndPanel(reader, inGameState, out var list, out var mgr, out var panel);
        var open = resolved
            && IsLocallyVisible(reader, panel)
            && IsLocallyVisible(reader, list);
        return new PanelDiag(resolved, open, list, mgr);
    }

    public static bool IsAtlasOpen(MemoryReader reader, nint inGameState)
    {
        if (!TryResolveNodeListAndPanel(reader, inGameState, out var list, out _, out var panel))
            return false;
        // The persistent panel's own visible bit is the authoritative open/close signal.
        // The node-list and its runtime Parent chain can remain visible after the panel closes.
        return IsLocallyVisible(reader, panel)
            && IsLocallyVisible(reader, list);
    }

    /// <summary>
    /// Probe every UiRootStruct manager candidate (keyboard, gamepad, and nested GameUi / GameUiController
    /// branches) and pick the best visible node-list canvas. When a controller is plugged in the game keeps
    /// parallel HUD trees — child count alone is not enough; we also score rolled-content signals (+0x310).
    /// </summary>
    public static bool TryResolveNodeList(MemoryReader reader, nint inGameState, out nint nodeList, out nint uiManager, nint preferManager = 0)
        => TryResolveNodeListAndPanel(reader, inGameState, out nodeList, out uiManager, out _, preferManager);

    private static bool TryResolveNodeListAndPanel(
        MemoryReader reader,
        nint inGameState,
        out nint nodeList,
        out nint uiManager,
        out nint atlasPanel,
        nint preferManager = 0)
    {
        nodeList = 0;
        uiManager = 0;
        atlasPanel = 0;
        if (inGameState == 0) return false;

        Span<nint> managers = stackalloc nint[8];
        var managerCount = FillManagerCandidates(reader, inGameState, managers, preferManager);

        var bestScore = -1;
        for (var i = 0; i < managerCount; i++)
        {
            var mgr = managers[i];
            if (!TryResolveNodeListFromManager(reader, mgr, out var list, out var panel)) continue;
            var score = ScoreNodeList(reader, list);
            if (score < bestScore) continue;
            if (score == bestScore && mgr != preferManager) continue;
            bestScore = score;
            nodeList = list;
            uiManager = mgr;
            atlasPanel = panel;
        }

        return nodeList != 0;
    }

    private static int FillManagerCandidates(MemoryReader reader, nint inGameState, Span<nint> dest, nint preferManager)
    {
        var count = 0;
        if (preferManager != 0)
            AddUnique(dest, ref count, preferManager);

        var kb = SafePtr(reader, inGameState + Poe2.InGameState.KeyboardUiRootStructPtr);
        var pad = SafePtr(reader, inGameState + Poe2.InGameState.GamepadUiRootStructPtr);
        AddUnique(dest, ref count, kb);
        if (pad != kb)
            AddUnique(dest, ref count, pad);

        foreach (var root in new[] { kb, pad })
        {
            if (root == 0) continue;
            AddUnique(dest, ref count, SafePtr(reader, root + Poe2.UiRootStruct.GameUiPtr));
            AddUnique(dest, ref count, SafePtr(reader, root + Poe2.UiRootStruct.GameUiControllerPtr));
        }

        return count;
    }

    private static void AddUnique(Span<nint> dest, ref int count, nint value)
    {
        if (value == 0 || count >= dest.Length) return;
        for (var i = 0; i < count; i++)
            if (dest[i] == value) return;
        dest[count++] = value;
    }

    /// <summary>Prefer visible canvases with more children and more nodes carrying live rolled content.</summary>
    internal static int ScoreNodeList(MemoryReader reader, nint list)
    {
        if (list == 0 || !IsHierarchicallyVisible(reader, list)) return 0;
        var score = 1000;
        var first = SafePtr(reader, list + Poe2.UiElement.Children);
        if (first == 0 || !reader.TryReadStruct<nint>(list + Poe2.UiElement.ChildrenEnd, out var last))
            return score;

        var count = ((long)last - (long)first) / 8;
        if (count is <= 0 or > 20000) return score;
        score += (int)Math.Min(count, 1500);

        var sampled = 0;
        var contentHits = 0;
        for (long i = 0; i < count && sampled < 96; i++)
        {
            var child = SafePtr(reader, first + (nint)(i * 8));
            if (child == 0) continue;
            sampled++;
            if (SafePtr(reader, child + Poe2.AtlasNode.Content) != 0)
                contentHits++;
        }
        score += contentHits * 40;
        return score;
    }

    public static bool TryResolveNodeListFromManager(MemoryReader reader, nint uiManager, out nint nodeList)
    {
        nodeList = WalkFromManager(reader, uiManager, out _);
        return nodeList != 0;
    }

    private static bool TryResolveNodeListFromManager(
        MemoryReader reader,
        nint uiManager,
        out nint nodeList,
        out nint atlasPanel)
    {
        nodeList = WalkFromManager(reader, uiManager, out atlasPanel);
        return nodeList != 0;
    }

    private static nint WalkFromManager(MemoryReader reader, nint uiManager, out nint atlasPanel)
    {
        var list = WalkFlagsChain(reader, uiManager, KbMouseChain, KbMouseGateStep, 0, out atlasPanel);
        if (list != 0) return list;
        return WalkFlagsChain(reader, uiManager, ControllerChain, ControllerGateStep, 0, out atlasPanel);
    }

    private static nint WalkFlagsChain(
        MemoryReader reader,
        nint parentAddr,
        IReadOnlyList<uint> flagsChain,
        int gateStep,
        int step,
        out nint atlasPanel)
    {
        atlasPanel = 0;
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

                var deeper = WalkFlagsChain(reader, child, flagsChain, gateStep, step + 1, out var deeperPanel);
                if (deeper == 0) continue;
                atlasPanel = target == (PanelFp & ~VisibleMask) ? child : deeperPanel;
                return deeper;
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

    private static bool IsLocallyVisible(MemoryReader reader, nint element)
        => element != 0
           && reader.TryReadStruct<uint>(element + Poe2.UiElement.Flags, out var flags)
           && (flags & VisibleMask) != 0;

    private static nint SafePtr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
