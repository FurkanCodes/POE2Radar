using System.Text;

namespace POE2Radar.Core.Game;

public sealed partial class Poe2Live
{
    private static readonly uint[] RunecraftPanelFingerprints =
    {
        0x00462EF1,
        0x00502EF3,
        0x00502EF7,
        0x00542EF1,
        0x00502EF1,
    };

    private const int RunecraftGateStep = 0;
    private const int RunecraftViewportStep = 2;
    private const uint RunecraftVisibleMask = 1u << Poe2.UiElement.FlagVisibleBit;

    private nint _runecraftPanelRoot;
    private nint _runecraftBranchRoot;
    private nint _runecraftViewport;
    private nint _runecraftLastInGameState;
    private DateTime _runecraftNextScanUtc = DateTime.MinValue;
    private Poe2UiAnchors.BranchKind _runecraftProbeHint;
    private Dictionary<string, (string MetaId, string DdsArt)> _runecraftNameToKeys = new(StringComparer.Ordinal);
    private DateTime _runecraftNameDictNextTryUtc = DateTime.MinValue;

    public readonly record struct RunecraftRecipeRow(
        nint RowAddress,
        string RawLabel,
        string Name,
        int Count,
        string MetaId,
        string DdsArtId,
        UiRect Rect,
        float ContentRightX);

    public readonly record struct RunecraftRowChildProbe(int Index, UiRect Rect, bool Visible);

    public readonly record struct RunecraftRowProbe(
        string RawLabel,
        UiRect RowRect,
        float ContentRightX,
        float RightmostChildRight,
        int ChildCount,
        RunecraftRowChildProbe[] Children);

    public readonly record struct RunecraftUiBranchProbe(
        string Branch,
        nint Root,
        bool PanelFound,
        nint FpPanelAddress,
        nint BfsPanelAddress,
        nint PanelAddress,
        nint ViewportAddress,
        string DiscoverSource,
        bool GateVisible,
        int RawRowCount);

    public readonly record struct RunecraftUiProbeSnapshot(
        nint CachedPanel,
        nint CachedBranchRoot,
        nint CachedViewport,
        string ProbeHint,
        DateTime NextScanUtc,
        RunecraftUiBranchProbe[] Branches,
        RunecraftPanelRead Read,
        UiRect ViewportRect,
        UiRect PanelRect,
        Vector2 Scroll,
        RunecraftRowProbe[] SampleRows);

    public readonly record struct RunecraftPanelRead(
        bool IsOpen,
        nint PanelRoot,
        nint ViewportAddress,
        Poe2UiAnchors.BranchKind Branch,
        Vector2 ViewportScroll,
        UiRect ViewportRect,
        RunecraftRecipeRow[] Rows,
        string LockedMetaId,
        string LockedName)
    {
        public static readonly RunecraftPanelRead Closed = new(
            false, 0, 0, Poe2UiAnchors.BranchKind.None, default, default, [], "", "");
    }

    public readonly record struct RunecraftStateValue(string Name, long Value);

    public readonly record struct RunecraftMonolithStation(
        nint DeviceAddress,
        nint StationAddress,
        int HoleCount,
        int AnchorIndex,
        int AnchorPos,
        bool PanelOpen,
        bool IsRerolled,
        bool IsUnique,
        bool Collected,
        string SelectedRecipeId,
        int SocketsState,
        string StatesDump);

    public void InvalidateRunecraftUiCache()
    {
        _runecraftPanelRoot = 0;
        _runecraftBranchRoot = 0;
        _runecraftViewport = 0;
        _runecraftNextScanUtc = DateTime.MinValue;
        _runecraftProbeHint = Poe2UiAnchors.BranchKind.None;
        _runecraftNameToKeys = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        _runecraftNameDictNextTryUtc = DateTime.MinValue;
    }

    public RunecraftPanelRead ReadRuneshapePanel(nint inGameState, float windowWidth, float windowHeight)
    {
        if (inGameState == 0 || windowWidth <= 0 || windowHeight <= 0)
            return RunecraftPanelRead.Closed;

        if (_runecraftLastInGameState != inGameState)
        {
            InvalidateRunecraftUiCache();
            _runecraftLastInGameState = inGameState;
        }

        DiscoverGameUiAnchors(inGameState, out var gameUi, out var controllerGameUi);
        var uiRoot = GetUiRoot(inGameState);
        var fixedRoot = Ptr(inGameState + Poe2.InGameState.UiRoot);
        Span<nint> roots = stackalloc nint[6];
        var rootCount = UiBranchCandidates.Fill(
            roots, gameUi, controllerGameUi, uiRoot, fixedRoot, _runecraftProbeHint, _runecraftBranchRoot);

        var now = DateTime.UtcNow;
        nint panel = 0;
        nint viewport = 0;
        Poe2UiAnchors.BranchKind branch = Poe2UiAnchors.BranchKind.None;

        if (_runecraftPanelRoot != 0)
        {
            if (IsRunecraftPanelGateVisible(_runecraftPanelRoot) && IsValidRecipesContainer(_runecraftPanelRoot))
            {
                panel = _runecraftPanelRoot;
                viewport = _runecraftViewport;
                branch = _runecraftProbeHint;
            }
            else
            {
                _runecraftPanelRoot = 0;
                _runecraftBranchRoot = 0;
                _runecraftViewport = 0;
                return RunecraftPanelRead.Closed;
            }
        }
        else if (now >= _runecraftNextScanUtc)
        {
            _runecraftNextScanUtc = now.AddMilliseconds(250);
            for (var i = 0; i < rootCount; i++)
            {
                var root = roots[i];
                if (root == 0) continue;
                if (!TryResolveRunecraftPanelFromRoot(root, out var found, out var foundViewport, out _))
                    continue;
                panel = found;
                viewport = foundViewport;
                branch = UiBranchCandidates.BranchForRoot(root, gameUi, controllerGameUi);
                _runecraftPanelRoot = panel;
                _runecraftBranchRoot = root;
                _runecraftViewport = viewport;
                _runecraftProbeHint = branch;
                break;
            }
        }

        if (panel == 0 || !IsRunecraftPanelGateVisible(panel))
            return RunecraftPanelRead.Closed;

        BuildRunecraftNameDictIfNeeded(panel);
        var scroll = ReadRunecraftScrollOffset(viewport);
        var rows = ReadRunecraftRows(panel, viewport, scroll, windowWidth, windowHeight);

        UiRect viewportRect = default;
        if (viewport != 0)
            TryResolveRunecraftRowRect(viewport, viewport, scroll, windowWidth, windowHeight, out viewportRect);

        UiRect panelRect = default;
        TryResolveRunecraftRowRect(panel, viewport, scroll, windowWidth, windowHeight, out panelRect);

        return new RunecraftPanelRead(
            true,
            panel,
            viewport,
            branch,
            scroll,
            viewportRect,
            rows,
            "",
            "");
    }

    /// <summary>Lightweight runecraft UI snapshot for Research probes and overlay diagnostics.</summary>
    public RunecraftUiProbeSnapshot ProbeRunecraftUi(nint inGameState, float windowWidth, float windowHeight)
    {
        if (inGameState == 0 || windowWidth <= 0 || windowHeight <= 0)
            return default;

        DiscoverGameUiAnchors(inGameState, out var gameUi, out var controllerGameUi);
        var uiRoot = GetUiRoot(inGameState);
        var fixedRoot = Ptr(inGameState + Poe2.InGameState.UiRoot);
        Span<nint> roots = stackalloc nint[6];
        var rootCount = UiBranchCandidates.Fill(
            roots, gameUi, controllerGameUi, uiRoot, fixedRoot, _runecraftProbeHint, _runecraftBranchRoot);

        var branches = new RunecraftUiBranchProbe[rootCount];
        for (var i = 0; i < rootCount; i++)
        {
            var root = roots[i];
            var branch = UiBranchCandidates.BranchForRoot(root, gameUi, controllerGameUi);
            nint fpViewport = 0;
            var fpPanel = root == 0 ? 0 : WalkRunecraftFp(root, RunecraftPanelFingerprints, RunecraftGateStep, 0, ref fpViewport);
            var bfsPanel = root == 0 ? 0 : FindRunecraftRecipesContainerBfs(root);
            nint viewport = 0;
            string source = "";
            nint panel = 0;
            if (fpPanel != 0 && IsRunecraftPanelGateVisible(fpPanel))
            {
                panel = fpPanel;
                viewport = fpViewport;
                source = "fp";
            }
            else if (bfsPanel != 0 && IsRunecraftPanelGateVisible(bfsPanel))
            {
                panel = bfsPanel;
                viewport = FindRunecraftViewportAncestor(bfsPanel);
                source = "bfs";
            }

            var gateVisible = panel != 0 && IsRunecraftPanelGateVisible(panel);
            var rawRows = 0;
            if (panel != 0 && _reader.TryReadStruct<StdVector>(panel + Poe2.UiElement.Children, out var children))
                rawRows = (int)(((long)children.Last - (long)children.First) / 8);

            branches[i] = new RunecraftUiBranchProbe(
                branch.ToString(),
                root,
                panel != 0,
                fpPanel,
                bfsPanel,
                panel,
                viewport,
                source,
                gateVisible,
                rawRows);
        }

        var read = ReadRuneshapePanel(inGameState, windowWidth, windowHeight);
        var scroll = read.ViewportScroll;
        var sampleRows = BuildRunecraftRowProbes(read, windowWidth, windowHeight, maxRows: 3);

        UiRect panelRect = default;
        if (read.PanelRoot != 0)
            TryResolveRunecraftRowRect(read.PanelRoot, read.ViewportAddress, scroll, windowWidth, windowHeight, out panelRect);

        return new RunecraftUiProbeSnapshot(
            _runecraftPanelRoot,
            _runecraftBranchRoot,
            _runecraftViewport,
            _runecraftProbeHint.ToString(),
            _runecraftNextScanUtc,
            branches,
            read,
            read.ViewportRect,
            panelRect,
            scroll,
            sampleRows);
    }

    public bool TryReadRunecraftMonolithStation(nint deviceEntity, out RunecraftMonolithStation station)
    {
        station = default;
        if (deviceEntity == 0) return false;

        var sm = ResolveComponent(deviceEntity, "StateMachine");
        if (sm == 0) return false;

        var states = ReadStateMachineStates(sm, out var dump);
        bool collected = false;
        bool isRerolled = false;
        int sockets = -1;
        foreach (var s in states)
        {
            if (string.Equals(s.Name, "activated", StringComparison.OrdinalIgnoreCase))
                collected = s.Value >= 7;
            else if (string.Equals(s.Name, "is_rerolled", StringComparison.OrdinalIgnoreCase))
                isRerolled = s.Value != 0;
            else if (string.Equals(s.Name, "sockets", StringComparison.OrdinalIgnoreCase))
                sockets = (int)s.Value;
        }

        if (collected) return false;

        if (!TryResolveRuneStation(sm, deviceEntity, out var stationAddr, out _))
            return false;

        _reader.TryReadStruct<int>(stationAddr + Poe2.RuneStation.HoleCount, out var holeCount);
        if (holeCount <= 0 || holeCount > 16) holeCount = Math.Max(sockets, 0);

        var anchorRow = Ptr(stationAddr + Poe2.RuneStation.AnchorRef);
        bool isUnique = anchorRow == 0 && holeCount > 0;
        int anchorIdx = -1;
        int anchorPos = 0;
        if (!isUnique)
            TryReadRunecraftAnchor(stationAddr, out anchorIdx, out anchorPos);

        var panelOpen = IsExeModulePtr(Ptr(stationAddr + Poe2.RuneStation.PanelOpenListener));
        var selectedId = ReadSelectedRecipeId(stationAddr);

        station = new RunecraftMonolithStation(
            deviceEntity,
            stationAddr,
            holeCount,
            anchorIdx,
            anchorPos,
            panelOpen,
            isRerolled,
            isUnique,
            false,
            selectedId,
            sockets,
            dump);
        return true;
    }

    public RunecraftStateValue[] ReadStateMachineStates(nint stateMachine, out string dump)
    {
        dump = "";
        if (stateMachine == 0) return [];

        if (!_reader.TryReadStruct<StdVector>(stateMachine + Poe2.StateMachine.StatesValues, out var valuesVec))
            return [];

        var valueCount = (int)(((long)valuesVec.Last - (long)valuesVec.First) / 8);
        if (valueCount <= 0 || valueCount > 256) return [];

        var values = new long[valueCount];
        var valueBytes = new byte[valueCount * 8];
        if (_reader.TryReadBytes(valuesVec.First, valueBytes) < valueBytes.Length)
            return [];
        Buffer.BlockCopy(valueBytes, 0, values, 0, valueBytes.Length);

        var namesPtr = Ptr(stateMachine + Poe2.StateMachine.StatesPtr);
        var tablePtr = namesPtr == 0 ? 0 : Ptr(namesPtr + 0x10);
        if (tablePtr == 0) return [];

        var list = new List<RunecraftStateValue>(valueCount);
        var sb = new StringBuilder();
        for (var i = 0; i < valueCount; i++)
        {
            var name = ReadStateMachineName(tablePtr + i * Poe2.StateMachine.StateStructSize);
            list.Add(new RunecraftStateValue(name, values[i]));
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(name).Append('=').Append(values[i]);
        }

        dump = sb.ToString();
        return list.Count == 0 ? [] : list.ToArray();
    }

    private string ReadStateMachineName(nint addr)
    {
        var strPtr = Ptr(addr);
        if (strPtr == 0) return "";
        return _reader.ReadStringUtf8(strPtr, 64).Trim();
    }

    private nint WalkRunecraftFp(nint parent, uint[] fps, int gateStep, int step, ref nint viewportOut)
    {
        if (step == fps.Length)
            return IsValidRecipesContainer(parent) ? parent : 0;

        if (!_reader.TryReadStruct<StdVector>(parent + Poe2.UiElement.Children, out var children))
            return 0;

        var count = ((long)children.Last - (long)children.First) / 8;
        if (count <= 0 || count > 4000) return 0;

        var target = fps[step] & ~RunecraftVisibleMask;
        for (var pass = 0; pass < 2; pass++)
        {
            var wantVisible = pass == 0;
            for (long i = 0; i < count; i++)
            {
                var child = Ptr(children.First + (nint)(i * 8));
                if (child == 0) continue;
                if (!_reader.TryReadStruct<uint>(child + Poe2.UiElement.Flags, out var flags)) continue;
                if ((flags & ~RunecraftVisibleMask) != target) continue;
                var visible = (flags & RunecraftVisibleMask) != 0;
                if (visible != wantVisible) continue;
                if (step == gateStep && !visible) continue;

                var deeper = WalkRunecraftFp(child, fps, gateStep, step + 1, ref viewportOut);
                if (deeper != 0)
                {
                    if (step == RunecraftViewportStep)
                        viewportOut = child;
                    return deeper;
                }
            }
        }

        return 0;
    }

    private bool TryResolveRunecraftPanelFromRoot(nint root, out nint panel, out nint viewport, out string source)
    {
        panel = 0;
        viewport = 0;
        source = "";
        if (root == 0) return false;

        nint fpViewport = 0;
        var fpPanel = WalkRunecraftFp(root, RunecraftPanelFingerprints, RunecraftGateStep, 0, ref fpViewport);
        if (fpPanel != 0 && IsRunecraftPanelGateVisible(fpPanel))
        {
            panel = fpPanel;
            viewport = fpViewport;
            source = "fp";
            return true;
        }

        var bfsPanel = FindRunecraftRecipesContainerBfs(root);
        if (bfsPanel != 0 && IsRunecraftPanelGateVisible(bfsPanel))
        {
            panel = bfsPanel;
            viewport = FindRunecraftViewportAncestor(bfsPanel);
            source = "bfs";
            return true;
        }

        return false;
    }

    private nint FindRunecraftRecipesContainerBfs(nint root)
    {
        if (root == 0) return 0;

        var queue = new Queue<nint>();
        var visited = new HashSet<nint>();
        queue.Enqueue(root);
        nint best = 0;
        var bestRows = 0;

        while (queue.Count > 0 && visited.Count < 30_000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;

            foreach (var child in ReadChildren(el, maxCount: 96))
                queue.Enqueue(child);

            if (el == root) continue;
            if (!IsValidRecipesContainer(el)) continue;
            if (!IsRunecraftPanelGateVisible(el)) continue;

            var rows = CountVisibleRecipeRows(el);
            if (rows > bestRows)
            {
                best = el;
                bestRows = rows;
            }
        }

        return bestRows > 0 ? best : 0;
    }

    private nint FindRunecraftViewportAncestor(nint recipesContainer)
    {
        var target = RunecraftPanelFingerprints[RunecraftViewportStep] & ~RunecraftVisibleMask;
        var cur = Ptr(recipesContainer + Poe2.UiElement.Parent);
        for (var depth = 0; depth < 20 && cur != 0; depth++)
        {
            if (!_reader.TryReadStruct<uint>(cur + Poe2.UiElement.Flags, out var flags))
                break;
            if ((flags & ~RunecraftVisibleMask) == target)
                return cur;
            cur = Ptr(cur + Poe2.UiElement.Parent);
        }

        return 0;
    }

    private int CountVisibleRecipeRows(nint recipesContainer)
    {
        if (!_reader.TryReadStruct<StdVector>(recipesContainer + Poe2.UiElement.Children, out var children))
            return 0;
        var count = ((long)children.Last - (long)children.First) / 8;
        if (count <= 0 || count > 4000) return 0;

        var visible = 0;
        for (long i = 0; i < count; i++)
        {
            var row = Ptr(children.First + (nint)(i * 8));
            if (row != 0 && IsVisibleUiElement(row))
                visible++;
        }

        return visible;
    }

    private bool IsRunecraftPanelGateVisible(nint recipesContainer)
    {
        var gateFingerprint = RunecraftPanelFingerprints[RunecraftGateStep] & ~RunecraftVisibleMask;
        var cur = recipesContainer;
        for (var depth = 0; depth < 16 && cur != 0; depth++)
        {
            if (!_reader.TryReadStruct<uint>(cur + Poe2.UiElement.Flags, out var flags))
                return false;
            if ((flags & ~RunecraftVisibleMask) == gateFingerprint)
                return (flags & RunecraftVisibleMask) != 0;
            cur = Ptr(cur + Poe2.UiElement.Parent);
        }

        return false;
    }

    private bool IsValidRecipesContainer(nint addr)
    {
        if (!_reader.TryReadStruct<StdVector>(addr + Poe2.UiElement.Children, out var children))
            return false;
        var count = ((long)children.Last - (long)children.First) / 8;
        if (count <= 0 || count > 4000) return false;

        for (long i = 0; i < count; i++)
        {
            var row = Ptr(children.First + (nint)(i * 8));
            if (row == 0 || !IsVisibleUiElement(row)) continue;
            var label = GetRunecraftChild(row, 0);
            if (label == 0) continue;
            var raw = ReadStdWString(label + Poe2.UiElement.Text);
            if (!string.IsNullOrEmpty(raw)) return true;
        }

        return false;
    }

    private nint GetRunecraftChild(nint addr, int index)
    {
        if (!_reader.TryReadStruct<StdVector>(addr + Poe2.UiElement.Children, out var children))
            return 0;
        var count = ((long)children.Last - (long)children.First) / 8;
        if (index < 0 || index >= count) return 0;
        return Ptr(children.First + (nint)(index * 8));
    }

    private RunecraftRecipeRow[] ReadRunecraftRows(
        nint panel,
        nint viewport,
        Vector2 scroll,
        float windowWidth,
        float windowHeight)
    {
        if (!_reader.TryReadStruct<StdVector>(panel + Poe2.UiElement.Children, out var children))
            return [];

        var count = ((long)children.Last - (long)children.First) / 8;
        if (count <= 0 || count > 4000) return [];

        var rows = new List<RunecraftRecipeRow>((int)Math.Min(count, 64));
        for (long i = 0; i < count; i++)
        {
            var row = Ptr(children.First + (nint)(i * 8));
            if (row == 0 || !IsVisibleUiElement(row)) continue;

            var label = GetRunecraftChild(row, 0);
            if (label == 0) continue;
            var raw = ReadStdWString(label + Poe2.UiElement.Text);
            if (string.IsNullOrEmpty(raw)) continue;

            RunecraftParse.ParseNameAndCount(raw, out var qty, out var name);
            _runecraftNameToKeys.TryGetValue(name.Trim(), out var keys);
            if (!TryResolveRunecraftRowRect(row, viewport, scroll, windowWidth, windowHeight, out var rect))
                continue;

            var contentRight = ComputeRowContentRight(
                row, viewport, scroll, windowWidth, windowHeight, out _, out _);

            rows.Add(new RunecraftRecipeRow(row, raw, name, qty, keys.MetaId ?? "", keys.DdsArt ?? "", rect, contentRight));
        }

        return rows.Count == 0 ? [] : rows.ToArray();
    }

    private RunecraftRowProbe[] BuildRunecraftRowProbes(
        RunecraftPanelRead read,
        float windowWidth,
        float windowHeight,
        int maxRows)
    {
        if (!read.IsOpen || read.Rows.Length == 0) return [];

        var count = Math.Min(maxRows, read.Rows.Length);
        var probes = new RunecraftRowProbe[count];
        for (var i = 0; i < count; i++)
        {
            var row = read.Rows[i];
            var childProbes = ProbeRunecraftRowChildren(
                row.RowAddress, read.ViewportAddress, read.ViewportScroll, windowWidth, windowHeight, maxChildren: 6);
            ComputeRowContentRight(
                row.RowAddress, read.ViewportAddress, read.ViewportScroll, windowWidth, windowHeight,
                out var rightmostChildRight, out var childCount);

            probes[i] = new RunecraftRowProbe(
                row.RawLabel,
                row.Rect,
                row.ContentRightX,
                rightmostChildRight,
                childCount,
                childProbes);
        }

        return probes;
    }

    private RunecraftRowChildProbe[] ProbeRunecraftRowChildren(
        nint row,
        nint viewport,
        Vector2 scroll,
        float windowWidth,
        float windowHeight,
        int maxChildren)
    {
        if (!_reader.TryReadStruct<StdVector>(row + Poe2.UiElement.Children, out var children))
            return [];

        var count = (int)Math.Min(((long)children.Last - (long)children.First) / 8, maxChildren);
        if (count <= 0) return [];

        var list = new List<RunecraftRowChildProbe>(count);
        for (var i = 0; i < count; i++)
        {
            var child = Ptr(children.First + (nint)(i * 8));
            var visible = child != 0 && IsVisibleUiElement(child);
            UiRect rect = default;
            if (child != 0)
                TryResolveRunecraftRowRect(child, viewport, scroll, windowWidth, windowHeight, out rect);
            list.Add(new RunecraftRowChildProbe(i, rect, visible));
        }

        return list.ToArray();
    }

    private float ComputeRowContentRight(
        nint row,
        nint viewport,
        Vector2 scroll,
        float windowWidth,
        float windowHeight,
        out float rightmostChildRight,
        out int childCount)
    {
        rightmostChildRight = 0f;
        childCount = 0;
        if (!_reader.TryReadStruct<StdVector>(row + Poe2.UiElement.Children, out var children))
        {
            if (TryResolveRunecraftRowRect(row, viewport, scroll, windowWidth, windowHeight, out var solo))
                return solo.X + solo.W;
            return 0f;
        }

        var count = ((long)children.Last - (long)children.First) / 8;
        childCount = (int)Math.Min(count, 64);
        var maxRight = 0f;
        var any = false;

        for (long i = 0; i < count; i++)
        {
            var child = Ptr(children.First + (nint)(i * 8));
            if (child == 0 || !IsVisibleUiElement(child)) continue;
            if (!TryResolveRunecraftRowRect(child, viewport, scroll, windowWidth, windowHeight, out var childRect))
                continue;
            var right = childRect.X + childRect.W;
            if (!any || right > maxRight)
            {
                maxRight = right;
                any = true;
            }
        }

        rightmostChildRight = maxRight;
        if (any) return maxRight;

        if (TryResolveRunecraftRowRect(row, viewport, scroll, windowWidth, windowHeight, out var rowRect))
            return rowRect.X + rowRect.W;

        return 0f;
    }

    private bool TryResolveRunecraftRowRect(
        nint element,
        nint viewport,
        Vector2 scroll,
        float windowWidth,
        float windowHeight,
        out UiRect rect)
    {
        rect = default;
        var elementCache = new Dictionary<nint, UiElementProjection.Element>(32);

        if (!UiElementProjection.TryRead(_reader, element, out var leaf))
            return false;

        if (!TryGetRunecraftUnscaledPosition(
                element, viewport, scroll, windowWidth, windowHeight, 0, elementCache, out var unscaled))
            return false;

        var scale = UiElementProjection.ScalePair(leaf.ScaleIndex, leaf.LocalScaleMultiplier, windowWidth, windowHeight);
        rect = new UiRect(
            unscaled.X * scale.X,
            unscaled.Y * scale.Y,
            leaf.SizeW * scale.X,
            leaf.SizeH * scale.Y);
        return rect.W > 1f && rect.H > 1f;
    }

    private bool TryGetRunecraftUnscaledPosition(
        nint addr,
        nint viewport,
        Vector2 scroll,
        float windowWidth,
        float windowHeight,
        int depth,
        Dictionary<nint, UiElementProjection.Element> cache,
        out UiElementProjection.Point pos)
    {
        pos = default;
        if (!ReadRunecraftElement(addr, cache, out var el))
            return false;

        var local = new UiElementProjection.Point(el.RelativeX, el.RelativeY);
        if (el.Parent == 0 || depth >= 64)
        {
            pos = local;
            return true;
        }

        if (!ReadRunecraftElement(el.Parent, cache, out var parent))
        {
            pos = local;
            return false;
        }

        if (!TryGetRunecraftUnscaledPosition(
                el.Parent, viewport, scroll, windowWidth, windowHeight, depth + 1, cache, out var parentPos))
        {
            pos = local;
            return false;
        }

        if ((el.Flags & (1u << Poe2.UiElement.FlagModifyPosBit)) != 0)
        {
            parentPos = new UiElementProjection.Point(
                parentPos.X + parent.PositionModifierX,
                parentPos.Y + parent.PositionModifierY);
        }

        if (el.Parent == viewport)
        {
            parentPos = new UiElementProjection.Point(
                parentPos.X + scroll.X,
                parentPos.Y + scroll.Y);
        }

        if (parent.ScaleIndex == el.ScaleIndex &&
            Math.Abs(parent.LocalScaleMultiplier - el.LocalScaleMultiplier) < 0.0001f)
        {
            pos = new UiElementProjection.Point(parentPos.X + local.X, parentPos.Y + local.Y);
            return true;
        }

        var parentScale = UiElementProjection.ScalePair(parent.ScaleIndex, parent.LocalScaleMultiplier, windowWidth, windowHeight);
        var elScale = UiElementProjection.ScalePair(el.ScaleIndex, el.LocalScaleMultiplier, windowWidth, windowHeight);
        pos = new UiElementProjection.Point(
            parentPos.X * parentScale.X / elScale.X + local.X,
            parentPos.Y * parentScale.Y / elScale.Y + local.Y);
        return true;
    }

    private bool ReadRunecraftElement(
        nint address,
        Dictionary<nint, UiElementProjection.Element> cache,
        out UiElementProjection.Element element)
    {
        if (cache.TryGetValue(address, out element))
            return true;
        if (!UiElementProjection.TryRead(_reader, address, out element))
            return false;
        cache[address] = element;
        return true;
    }

    private Vector2 ReadRunecraftScrollOffset(nint viewport)
    {
        if (viewport == 0) return default;
        if (!_reader.TryReadStruct<float>(viewport + Poe2.UiElement.ScrollOffset, out var x)) return default;
        _reader.TryReadStruct<float>(viewport + Poe2.UiElement.ScrollOffset + 4, out var y);
        return new Vector2 { X = x, Y = y };
    }

    private void BuildRunecraftNameDictIfNeeded(nint panel)
    {
        if (_runecraftNameToKeys.Count > 0) return;
        var now = DateTime.UtcNow;
        if (now < _runecraftNameDictNextTryUtc) return;
        _runecraftNameDictNextTryUtc = now.AddSeconds(2);

        var recipeHandle = FindDatHandle(panel, 0, s => s.EndsWith("Balance/Expedition2Recipes.dat", StringComparison.Ordinal));
        var bitTable = FindDatHandle(panel, recipeHandle, s => s.EndsWith("Balance/BaseItemTypes.dat", StringComparison.Ordinal));
        if (bitTable == 0) return;

        var bitVec = Ptr(bitTable + 0x28);
        var bitBegin = Ptr(bitVec);
        var bitEnd = Ptr(bitVec + 8);
        if (bitBegin == 0 || bitEnd <= bitBegin) return;
        var bitCount = (bitEnd - bitBegin) / 0x168;
        if (bitCount <= 0 || bitCount > 200_000) return;

        var dict = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        for (long j = 0; j < bitCount; j++)
        {
            var row = bitBegin + (nint)(j * 0x168);
            var name = ReadUtf16Z(Ptr(row + 0x20), 64);
            if (name.Length < 2) continue;
            var metaId = RunecraftParse.LastMetaSegment(ReadUtf16Z(Ptr(row + 0x00), 128));
            var artSub = Ptr(row + 0x78);
            var ddsArt = artSub == 0 ? "" : RunecraftParse.ArtIdFromDdsPath(ReadUtf16Z(Ptr(artSub + 0x08), 128));
            if (metaId.Length == 0 && ddsArt.Length == 0) continue;
            dict[name.Trim()] = (metaId, ddsArt);
        }

        if (dict.Count > 0)
            _runecraftNameToKeys = dict;
    }

    private nint FindDatHandle(nint root1, nint root2, Func<string, bool> pathMatch)
    {
        var seen = new HashSet<long>();
        var queue = new Queue<(nint addr, int depth)>();
        if (root1 != 0 && seen.Add(root1)) queue.Enqueue((root1, 0));
        if (root2 != 0 && seen.Add(root2)) queue.Enqueue((root2, 0));
        var visited = 0;
        var buf = new byte[0x180];

        while (queue.Count > 0 && visited < 80_000)
        {
            var (addr, depth) = queue.Dequeue();
            visited++;

            if (IsExeModulePtr(Ptr(addr)))
            {
                var pathPtr = Ptr(addr + 0x08);
                if (pathPtr != 0)
                {
                    var path = ReadUtf16Z(pathPtr, 96);
                    if (pathMatch(path)) return addr;
                }
            }

            if (depth >= 8) continue;
            if (_reader.TryReadBytes(addr, buf) < buf.Length) continue;
            for (var o = 0; o + 8 <= buf.Length; o += 8)
            {
                var v = BitConverter.ToInt64(buf, o);
                if ((ulong)v < 0x10000 || (ulong)v > 0x7FFFFFFFFFFF) continue;
                if (seen.Add(v)) queue.Enqueue(((nint)v, depth + 1));
            }
        }

        return 0;
    }

    private string ReadUtf16Z(nint ptr, int maxChars)
    {
        if (ptr == 0) return "";
        return _reader.ReadStringUtf16(ptr, maxChars).TrimEnd('\0');
    }

    private static bool IsExeModulePtr(nint p) => (ulong)p >= 0x7FF000000000ul && (ulong)p < 0x800000000000ul;

    private bool TryResolveRuneStation(nint stateMachine, nint deviceAddr, out nint station, out string diag)
    {
        station = 0;
        diag = "";
        if (!_reader.TryReadStruct<StdVector>(stateMachine + Poe2.StateMachine.ListenerVec, out var vec))
        {
            diag = "listener vector unreadable";
            return false;
        }

        var count = ((long)vec.Last - (long)vec.First) / 8;
        if (count <= 0 || count > 256)
        {
            diag = $"listener count out of range ({count})";
            return false;
        }

        for (long i = 0; i < count; i++)
        {
            var node = Ptr(vec.First + (nint)(i * 8));
            if (node == 0) continue;
            var sub = Ptr(node);
            if (sub == 0) continue;
            var cand = sub - Poe2.RuneStation.ListenerSub;
            if (Ptr(cand + Poe2.RuneStation.Owner) == deviceAddr)
            {
                station = cand;
                return true;
            }
        }

        diag = $"no listener matched device ({count} checked)";
        return false;
    }

    private bool TryReadRunecraftAnchor(nint station, out int index, out int pos)
    {
        index = -1;
        pos = 0;
        _reader.TryReadStruct<int>(station + Poe2.RuneStation.AnchorPos, out pos);

        var rowPtr = Ptr(station + Poe2.RuneStation.AnchorRef);
        if (rowPtr == 0) return false;

        var holder = Ptr(station + Poe2.RuneStation.AnchorHolder);
        if (holder == 0) return false;
        var p1 = Ptr(holder + 0x28);
        if (p1 == 0) return false;
        var tableBase = Ptr(p1);
        if (tableBase == 0) return false;

        var delta = rowPtr - tableBase;
        if (delta < 0 || delta % Poe2.RuneStation.RuneStride != 0) return false;
        var i = delta / Poe2.RuneStation.RuneStride;
        if (i < 0 || i >= Poe2.RuneStation.RuneCount) return false;
        index = (int)i;
        return true;
    }

    private string ReadSelectedRecipeId(nint station)
    {
        var rowPtr = Ptr(station + Poe2.RuneStation.SelectedRecipe);
        if (rowPtr == 0) return "";
        return ReadUtf16Z(Ptr(rowPtr), 128).Trim();
    }
}

// Parsing helpers in Core (Overlay tests duplicate via RunecraftPriceMath).
internal static class RunecraftParse
{
    public static void ParseNameAndCount(string raw, out int count, out string name)
    {
        count = 1;
        name = raw?.Trim() ?? string.Empty;
        if (name.Length == 0) return;

        int i = 0;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        if (i > 0 && i < name.Length && (name[i] == 'x' || name[i] == 'X'))
        {
            if (int.TryParse(name.AsSpan(0, i), out var c) && c > 0)
            {
                count = c;
                name = name[(i + 1)..].TrimStart();
                return;
            }
        }

        if (name[^1] == ')')
        {
            int open = name.LastIndexOf('(');
            if (open > 0)
            {
                var inner = name.Substring(open + 1, name.Length - open - 2).Trim();
                if (int.TryParse(inner, out var c) && c > 0)
                {
                    count = c;
                    name = name[..open].TrimEnd();
                }
            }
        }
    }

    public static string LastMetaSegment(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    public static string ArtIdFromDdsPath(string path)
    {
        var seg = LastMetaSegment(path);
        int dot = seg.LastIndexOf('.');
        return dot > 0 ? seg[..dot] : seg;
    }
}

/// <summary>Viewport scroll helpers for runecraft row projection (unit-tested).</summary>
internal static class RunecraftScrollMath
{
    internal static UiElementProjection.Point ApplyUnscaledScroll(Vector2 scroll)
        => new(scroll.X, scroll.Y);

    internal static UiElementProjection.Point ApplyScreenScroll(
        Vector2 scroll,
        UiElementProjection.Point scale)
        => new(scroll.X * scale.X, scroll.Y * scale.Y);
}
