using System.Diagnostics;
using System.Runtime.InteropServices;
using POE2Radar.Core;
using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class SekhemaVisibilityTests
{
    [Fact]
    public void ReadSekhemaFloor_ReturnsClosed_WhenPanelParentIsHidden()
    {
        using var process = ProcessHandle.AttachToProcess(
            Environment.ProcessId,
            Process.GetCurrentProcess().ProcessName);
        var reader = new MemoryReader(process);
        using var ui = new FakeSekhemaUi();
        var live = new Poe2Live(reader, 0);

        var floor = live.ReadSekhemaFloor(ui.InGameState, 0, 1920, 1080);

        Assert.False(floor.IsOpen);
        Assert.Equal(ui.Panel, floor.PanelAddress);
    }

    private sealed class FakeSekhemaUi : IDisposable
    {
        private const int ElementSize = 0x500;
        private const int InGameStateSize = 0x400;
        private readonly List<nint> _allocations = new();

        public FakeSekhemaUi()
        {
            InGameState = Alloc(InGameStateSize);
            var gameUi = Element(visible: true);
            var hiddenBranch = Element(visible: false, parent: gameUi);
            Panel = Element(visible: true, parent: hiddenBranch);

            SetChildren(
                gameUi,
                Poe2.Sekhema.GameUiPanelChild + 1,
                (Poe2.Sekhema.GameUiPanelChild, hiddenBranch));
            SetChildren(hiddenBranch, 1, (Poe2.Sekhema.PanelChild, Panel));
            WritePtr(
                InGameState + Poe2.InGameState.KeyboardUiRootStructPtr,
                gameUi);
        }

        public nint InGameState { get; }
        public nint Panel { get; }

        public void Dispose()
        {
            foreach (var allocation in _allocations)
                Marshal.FreeHGlobal(allocation);
            _allocations.Clear();
        }

        private nint Element(bool visible, nint parent = 0)
        {
            var element = Alloc(ElementSize);
            WritePtr(element + Poe2.UiElement.Self, element);
            WritePtr(element + Poe2.UiElement.Parent, parent);
            var flags = visible ? 1u << Poe2.UiElement.FlagVisibleBit : 0u;
            Marshal.WriteInt32(
                element + Poe2.UiElement.Flags,
                unchecked((int)flags));
            return element;
        }

        private void SetChildren(
            nint parent,
            int count,
            params (int Index, nint Child)[] children)
        {
            var vector = Alloc(Math.Max(1, count) * nint.Size);
            foreach (var (index, child) in children)
                WritePtr(vector + index * nint.Size, child);
            WritePtr(parent + Poe2.UiElement.Children, vector);
            WritePtr(parent + Poe2.UiElement.ChildrenEnd, vector + count * nint.Size);
        }

        private nint Alloc(int bytes)
        {
            var address = Marshal.AllocHGlobal(bytes);
            for (var i = 0; i < bytes; i++)
                Marshal.WriteByte(address, i, 0);
            _allocations.Add(address);
            return address;
        }

        private static void WritePtr(nint address, nint value)
            => Marshal.WriteIntPtr(address, value);
    }
}
