using System.Runtime.InteropServices;
using System.Text;
using POE2Radar.Core;
using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class RitualOverlayPerformanceTests : IDisposable
{
    private readonly List<nint> _allocations = [];

    [Fact]
    public void OverlayGeometry_DoesNotRereadItemPricingMetadata()
    {
        const int slotCount = 8;
        using var process = ProcessHandle.AttachToProcess(Environment.ProcessId);
        var reader = new MemoryReader(process);
        var live = new Poe2Live(reader, 0);
        var grid = BuildRewardGrid(slotCount);

        var readsBefore = reader.ReadCount;
        var slots = live.ReadRitualOverlaySlots(grid, 2560, 1600);
        var reads = reader.ReadCount - readsBefore;

        Assert.Equal(slotCount, slots.Length);
        Assert.All(slots, slot => Assert.True(slot.Rect.W > 1f && slot.Rect.H > 1f));

        // The overlay pass only needs tile identity and screen geometry. At eight tiles,
        // rereading metadata and resolving four item components pushes this above 200 RPM calls.
        Assert.True(reads <= 50, $"Overlay geometry used {reads} cross-process reads for {slotCount} tiles.");
    }

    [Fact]
    public void BatchedProjectionRead_MatchesFieldByFieldReader()
    {
        using var process = ProcessHandle.AttachToProcess(Environment.ProcessId);
        var reader = new MemoryReader(process);
        var address = Alloc(0x300);
        var flags = (1u << Poe2.UiElement.FlagVisibleBit) | (1u << Poe2.UiElement.FlagModifyPosBit);

        WritePtr(address + Poe2.UiElement.Self, address);
        WritePtr(address + Poe2.UiElement.Parent, 0);
        WriteUInt32(address + Poe2.UiElement.Flags, flags);
        WriteFloat(address + Poe2.UiElement.RelativePos, 123.5f);
        WriteFloat(address + Poe2.UiElement.RelativePos + 4, -27.25f);
        WriteFloat(address + Poe2.UiElement.UiPositionModifier, 4.5f);
        WriteFloat(address + Poe2.UiElement.UiPositionModifier + 4, -9.75f);
        WriteFloat(address + Poe2.UiElement.LocalScaleMul, 1.25f);
        Marshal.WriteByte(address + Poe2.UiElement.UiScaleIndex, 3);
        WriteFloat(address + Poe2.UiElement.SizeW, 84f);
        WriteFloat(address + Poe2.UiElement.SizeH, 96f);

        Assert.True(UiElementProjection.TryRead(reader, address, out var individual));
        Assert.True(UiElementProjection.TryReadBatch(reader, address, out var batched));
        Assert.Equal(individual, batched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RitualPanelGate_ReadsOnlyFixedChainVisibility(bool visible)
    {
        using var process = ProcessHandle.AttachToProcess(Environment.ProcessId);
        var reader = new MemoryReader(process);
        var live = new Poe2Live(reader, 0);
        var inGameState = BuildRitualPanel(visible);

        var readsBefore = reader.ReadCount;
        var open = live.IsRitualPanelOpen(inGameState);
        var reads = reader.ReadCount - readsBefore;

        Assert.Equal(visible, open);
        Assert.True(reads <= 20, $"Ritual panel gate used {reads} cross-process reads.");
    }

    [Fact]
    public void RitualPanelGate_ReturnsFalse_WhenPanelIsLocallyVisibleButAncestorIsHidden()
    {
        using var process = ProcessHandle.AttachToProcess(Environment.ProcessId);
        var reader = new MemoryReader(process);
        var live = new Poe2Live(reader, 0);
        var inGameState = BuildRitualPanel(visible: true, branchVisible: false);

        Assert.False(live.IsRitualPanelOpen(inGameState));
    }

    private nint BuildRewardGrid(int slotCount)
    {
        var grid = Alloc(0x600);
        WritePtr(grid + Poe2.UiElement.Self, grid);
        WriteUInt32(grid + Poe2.UiElement.Flags, 1u << Poe2.UiElement.FlagVisibleBit);
        WriteFloat(grid + Poe2.UiElement.LocalScaleMul, 1f);
        WriteFloat(grid + Poe2.UiElement.SizeW, 800f);
        WriteFloat(grid + Poe2.UiElement.SizeH, 600f);

        var children = Alloc(slotCount * nint.Size);
        WritePtr(grid + Poe2.UiElement.Children, children);
        WritePtr(grid + Poe2.UiElement.ChildrenEnd, children + slotCount * nint.Size);

        for (var i = 0; i < slotCount; i++)
        {
            var tile = Alloc(0x600);
            var item = Alloc(0x100);
            var details = Alloc(0x100);

            WritePtr(children + i * nint.Size, tile);
            WritePtr(tile + Poe2.UiElement.Self, tile);
            WritePtr(tile + Poe2.UiElement.Parent, grid);
            WriteUInt32(tile + Poe2.UiElement.Flags, 1u << Poe2.UiElement.FlagVisibleBit);
            WriteFloat(tile + Poe2.UiElement.RelativePos, 50f + i * 90f);
            WriteFloat(tile + Poe2.UiElement.RelativePos + 4, 100f);
            WriteFloat(tile + Poe2.UiElement.LocalScaleMul, 1f);
            WriteFloat(tile + Poe2.UiElement.SizeW, 80f);
            WriteFloat(tile + Poe2.UiElement.SizeH, 100f);
            Marshal.WriteByte(tile + Poe2.UiElement.UiScaleIndex, 2);
            WritePtr(tile + Poe2.RitualUi.TileItemEntity, item);

            WritePtr(item + Poe2.Entity.EntityDetailsPtr, details);
            WriteInlineStdWString(details + Poe2.EntityDetails.Name, $"Item{i:000}");
        }

        return grid;
    }

    private nint BuildRitualPanel(bool visible, bool branchVisible = true)
    {
        var inGameState = Alloc(0x400);
        var gameUi = Alloc(0x400);
        var gameUiChildren = Alloc(77 * nint.Size);
        var branch = Alloc(0x400);
        var branchChildren = Alloc(14 * nint.Size);
        var panel = Alloc(0x600);

        WritePtr(inGameState + Poe2.InGameState.KeyboardUiRootStructPtr, gameUi);
        WritePtr(gameUi + Poe2.UiElement.Self, gameUi);
        WriteUInt32(gameUi + Poe2.UiElement.Flags, 1u << Poe2.UiElement.FlagVisibleBit);
        WritePtr(gameUi + Poe2.UiElement.Children, gameUiChildren);
        WritePtr(gameUi + Poe2.UiElement.ChildrenEnd, gameUiChildren + 77 * nint.Size);
        WritePtr(gameUiChildren + 76 * nint.Size, branch);
        WritePtr(branch + Poe2.UiElement.Self, branch);
        WritePtr(branch + Poe2.UiElement.Parent, gameUi);
        WriteUInt32(branch + Poe2.UiElement.Flags, branchVisible ? 1u << Poe2.UiElement.FlagVisibleBit : 0u);
        WritePtr(branch + Poe2.UiElement.Children, branchChildren);
        WritePtr(branch + Poe2.UiElement.ChildrenEnd, branchChildren + 14 * nint.Size);
        WritePtr(branchChildren + 13 * nint.Size, panel);
        WritePtr(panel + Poe2.UiElement.Self, panel);
        WritePtr(panel + Poe2.UiElement.Parent, branch);
        WriteUInt32(panel + Poe2.UiElement.Flags, visible ? 1u << Poe2.UiElement.FlagVisibleBit : 0u);

        return inGameState;
    }

    private nint Alloc(int bytes)
    {
        var address = Marshal.AllocHGlobal(bytes);
        Marshal.Copy(new byte[bytes], 0, address, bytes);
        _allocations.Add(address);
        return address;
    }

    private static void WritePtr(nint address, nint value) => Marshal.WriteInt64(address, value);
    private static void WriteUInt32(nint address, uint value) => Marshal.WriteInt32(address, unchecked((int)value));
    private static void WriteFloat(nint address, float value) => Marshal.WriteInt32(address, BitConverter.SingleToInt32Bits(value));

    private static void WriteInlineStdWString(nint address, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value + '\0');
        Marshal.Copy(bytes, 0, address, bytes.Length);
        Marshal.WriteInt32(address + 0x10, value.Length);
    }

    public void Dispose()
    {
        foreach (var address in _allocations)
            Marshal.FreeHGlobal(address);
    }
}
