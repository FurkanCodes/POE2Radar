using System.Diagnostics;
using System.Runtime.InteropServices;
using POE2Radar.Core;
using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class AtlasPanelResolverTests
{
    [Fact]
    public void ScoreCandidate_PrefersExpectedChildCount()
    {
        var perfect = AtlasPanelResolver.ScoreCandidate(Poe2.AtlasPanel.UiRootChildIndex, Poe2.AtlasPanel.ExpectedChildCount, 0);
        var off = AtlasPanelResolver.ScoreCandidate(Poe2.AtlasPanel.UiRootChildIndex, Poe2.AtlasPanel.ExpectedChildCount + 6, 0);
        Assert.True(perfect > off);
    }

    [Fact]
    public void ScoreCandidate_MapPanelBeatsEndgameShellWhenBothToggled()
    {
        var mapOpen = AtlasPanelResolver.ScoreCandidate(17, 8, toggleCount: 1, visibleNow: true);
        var shell = AtlasPanelResolver.ScoreCandidate(12, 24, toggleCount: 1, visibleNow: false);
        Assert.True(mapOpen > shell);
    }

    [Fact]
    public void ScoreCandidate_ToggleHistoryWinsOverChildCount()
    {
        var toggled = AtlasPanelResolver.ScoreCandidate(5, 12, toggleCount: 2);
        var perfectNoToggle = AtlasPanelResolver.ScoreCandidate(22, Poe2.AtlasPanel.ExpectedChildCount, 0);
        Assert.True(toggled > perfectNoToggle);
    }

    [Fact]
    public void ScoreCandidate_VisibleNowBoostsFirstOpenDiscovery()
    {
        var visible = AtlasPanelResolver.ScoreCandidate(17, 8, 0, visibleNow: true);
        var hidden = AtlasPanelResolver.ScoreCandidate(17, 8, 0, visibleNow: false);
        Assert.True(visible > hidden);
    }

    [Fact]
    public void PickBestIndex_SelectsHighestScoringCandidate()
    {
        var candidates = new List<(int Index, int ChildCount, int ToggleCount, bool VisibleNow)>
        {
            (12, 24, 1, false),
            (17, 8, 1, true),
            (10, 4, 0, false),
        };
        Assert.Equal(17, AtlasPanelResolver.PickBestIndex(candidates));
    }

    [Fact]
    public void PickBestIndex_FallsBackToHardcodedWhenOnlyItScores()
    {
        var candidates = new List<(int Index, int ChildCount, int ToggleCount, bool VisibleNow)>
        {
            (Poe2.AtlasPanel.UiRootChildIndex, Poe2.AtlasPanel.ExpectedChildCount, 0, true),
            (3, 4, 0, false),
        };
        Assert.Equal(Poe2.AtlasPanel.UiRootChildIndex, AtlasPanelResolver.PickBestIndex(candidates));
    }

    [Fact]
    public void PickBestIndex_ReturnsNegativeWhenEmpty()
    {
        Assert.Equal(-1, AtlasPanelResolver.PickBestIndex(Array.Empty<(int, int, int, bool)>()));
    }

    [Fact]
    public void TryResolvePanel_DropsCachedPanel_WhenUiRootChildIsReplaced()
    {
        AtlasPanelResolver.Invalidate();
        using var process = ProcessHandle.AttachToProcess(Environment.ProcessId, Process.GetCurrentProcess().ProcessName);
        var reader = new MemoryReader(process);
        using var ui = new FakeUiMemory();

        var root = ui.Element(visible: true);
        var oldPanel = ui.Element(visible: false, parent: root, directChildren: Poe2.AtlasPanel.ExpectedChildCount);
        var newPanel = ui.Element(visible: true, parent: root, directChildren: Poe2.AtlasPanel.ExpectedChildCount);
        ui.SetChildren(root, Poe2.AtlasPanel.UiRootChildIndex + 1, (Poe2.AtlasPanel.UiRootChildIndex, oldPanel));

        Assert.True(AtlasPanelResolver.TryResolvePanel(reader, root, out var resolved, out _));
        Assert.Equal(oldPanel, resolved);

        ui.SetChild(root, Poe2.AtlasPanel.UiRootChildIndex, newPanel);

        Assert.True(AtlasPanelResolver.TryResolvePanel(reader, root, out resolved, out _));
        Assert.Equal(newPanel, resolved);
    }

    [Fact]
    public void IsPanelOpen_ReturnsFalse_WhenParentIsHidden()
    {
        AtlasPanelResolver.Invalidate();
        using var process = ProcessHandle.AttachToProcess(Environment.ProcessId, Process.GetCurrentProcess().ProcessName);
        var reader = new MemoryReader(process);
        using var ui = new FakeUiMemory();

        var root = ui.Element(visible: false);
        var panel = ui.Element(visible: true, parent: root, directChildren: Poe2.AtlasPanel.ExpectedChildCount);
        ui.SetChildren(root, Poe2.AtlasPanel.UiRootChildIndex + 1, (Poe2.AtlasPanel.UiRootChildIndex, panel));

        Assert.False(AtlasPanelResolver.IsPanelOpen(reader, root));
    }

    private sealed class FakeUiMemory : IDisposable
    {
        private const int ElementSize = 0x400;
        private readonly List<nint> _allocations = new();
        private readonly Dictionary<nint, nint> _childrenVectors = new();

        public nint Element(bool visible, nint parent = 0, int directChildren = 0)
        {
            var el = Alloc(ElementSize);
            WritePtr(el + Poe2.UiElement.Self, el);
            WritePtr(el + Poe2.UiElement.Parent, parent);
            WriteFlags(el, visible);
            if (directChildren > 0)
                SetChildren(el, directChildren);
            return el;
        }

        public void SetChildren(nint parent, int count, params (int Index, nint Child)[] children)
        {
            if (_childrenVectors.Remove(parent, out var previous))
                Free(previous);

            var vector = Alloc(Math.Max(1, count) * nint.Size);
            for (var i = 0; i < count; i++)
                WritePtr(vector + i * nint.Size, 0);
            foreach (var (index, child) in children)
                WritePtr(vector + index * nint.Size, child);

            _childrenVectors[parent] = vector;
            WritePtr(parent + Poe2.UiElement.Children, vector);
            WritePtr(parent + Poe2.UiElement.ChildrenEnd, vector + count * nint.Size);
        }

        public void SetChild(nint parent, int index, nint child)
        {
            var vector = _childrenVectors[parent];
            WritePtr(vector + index * nint.Size, child);
        }

        public void Dispose()
        {
            foreach (var allocation in _allocations.ToArray())
                Free(allocation);
            _allocations.Clear();
            _childrenVectors.Clear();
            AtlasPanelResolver.Invalidate();
        }

        private nint Alloc(int bytes)
        {
            var ptr = Marshal.AllocHGlobal(bytes);
            for (var i = 0; i < bytes; i++)
                Marshal.WriteByte(ptr, i, 0);
            _allocations.Add(ptr);
            return ptr;
        }

        private void Free(nint ptr)
        {
            if (!_allocations.Remove(ptr)) return;
            Marshal.FreeHGlobal(ptr);
        }

        private static void WritePtr(nint address, nint value)
            => Marshal.WriteIntPtr(address, value);

        private static void WriteFlags(nint element, bool visible)
        {
            var flags = visible ? 1u << Poe2.UiElement.FlagVisibleBit : 0u;
            Marshal.WriteInt32(element + Poe2.UiElement.Flags, unchecked((int)flags));
        }
    }
}
