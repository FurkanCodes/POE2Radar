using System.Diagnostics;
using System.Runtime.InteropServices;
using POE2Radar.Core;
using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class AtlasPanelLocatorTests
{
    private const uint PanelFp = 0x00562EF5;
    private const uint GateFp = 0x00502EF1;
    private const uint NodeListFp = 0x00502EF3;
    private const uint VisibleMask = 0x800u;

    [Fact]
    public void TryResolveNodeList_FindsKbMouseChain()
    {
        using var process = ProcessHandle.AttachToProcess(Environment.ProcessId, Process.GetCurrentProcess().ProcessName);
        var reader = new MemoryReader(process);
        using var ui = new FakeUiMemory();

        var manager = ui.Element(visible: true, directChildren: 4);
        var panel = ui.ElementWithFlags(PanelFp | VisibleMask, visible: true, parent: manager);
        var gate = ui.ElementWithFlags(GateFp | VisibleMask, visible: true, parent: panel);
        var nodeList = ui.ElementWithFlags(NodeListFp | VisibleMask, visible: true, parent: gate, directChildren: 2);
        ui.SetChildren(manager, 4,
            (0, ui.Element(visible: false)),
            (1, panel),
            (2, ui.Element(visible: false)),
            (3, ui.Element(visible: false)));
        ui.SetChildren(panel, 1, (0, gate));
        ui.SetChildren(gate, 1, (0, nodeList));

        var igs = ui.Alloc(0x400);
        ui.WritePtr(igs + Poe2.InGameState.KeyboardUiRootStructPtr, manager);

        Assert.True(AtlasPanelLocator.TryResolveNodeList(reader, igs, out var resolved, out var mgr));
        Assert.Equal(nodeList, resolved);
        Assert.Equal(manager, mgr);
        Assert.True(AtlasPanelLocator.IsAtlasOpen(reader, igs));
    }

    [Fact]
    public void IsAtlasOpen_ReturnsFalse_WhenNodeListHidden()
    {
        using var process = ProcessHandle.AttachToProcess(Environment.ProcessId, Process.GetCurrentProcess().ProcessName);
        var reader = new MemoryReader(process);
        using var ui = new FakeUiMemory();

        var manager = ui.Element(visible: true, directChildren: 1);
        var panel = ui.ElementWithFlags(PanelFp | VisibleMask, visible: true, parent: manager);
        var gate = ui.ElementWithFlags(GateFp | VisibleMask, visible: true, parent: panel);
        var nodeList = ui.ElementWithFlags(NodeListFp, visible: false, parent: gate);
        ui.SetChildren(manager, 1, (0, panel));
        ui.SetChildren(panel, 1, (0, gate));
        ui.SetChildren(gate, 1, (0, nodeList));

        var igs = ui.Alloc(0x400);
        ui.WritePtr(igs + Poe2.InGameState.KeyboardUiRootStructPtr, manager);

        Assert.False(AtlasPanelLocator.IsAtlasOpen(reader, igs));
    }

    private sealed class FakeUiMemory : IDisposable
    {
        private const int ElementSize = 0x400;
        private readonly List<nint> _allocations = new();
        private readonly Dictionary<nint, nint> _childrenVectors = new();

        public nint Element(bool visible, nint parent = 0, int directChildren = 0)
            => ElementWithFlags(visible ? VisibleMask : 0u, visible, parent, directChildren);

        public nint ElementWithFlags(uint flags, bool visible, nint parent = 0, int directChildren = 0)
        {
            var el = Alloc(ElementSize);
            WritePtr(el + Poe2.UiElement.Self, el);
            WritePtr(el + Poe2.UiElement.Parent, parent);
            WriteFlags(el, flags, visible);
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

        public nint Alloc(int bytes)
        {
            var ptr = Marshal.AllocHGlobal(bytes);
            for (var i = 0; i < bytes; i++)
                Marshal.WriteByte(ptr, i, 0);
            _allocations.Add(ptr);
            return ptr;
        }

        public void WritePtr(nint address, nint value)
            => Marshal.WriteIntPtr(address, value);

        public void Dispose()
        {
            foreach (var allocation in _allocations.ToArray())
                Free(allocation);
            _allocations.Clear();
            _childrenVectors.Clear();
        }

        private void Free(nint ptr)
        {
            if (!_allocations.Remove(ptr)) return;
            Marshal.FreeHGlobal(ptr);
        }

        private static void WriteFlags(nint element, uint flags, bool visible)
        {
            if (visible && (flags & VisibleMask) == 0)
                flags |= VisibleMask;
            else if (!visible)
                flags &= ~VisibleMask;
            Marshal.WriteInt32(element + Poe2.UiElement.Flags, unchecked((int)flags));
        }
    }
}
