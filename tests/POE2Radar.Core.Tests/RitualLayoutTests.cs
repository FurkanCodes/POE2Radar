using System.Runtime.InteropServices;
using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class RitualLayoutTests
{
    [Fact]
    public void RewardTile_ItemPointerOffsetMatchesGameHelperLayout()
        => Assert.Equal(0x4F8, Poe2.RitualUi.TileItemEntity);

    [Fact]
    public void ModArrayStruct_MatchesGameHelperStrideAndOffsets()
    {
        Assert.Equal(0x40, Marshal.SizeOf<ModArrayStruct>());
        Assert.Equal(0x40, Poe2.ModVectors.EntryStride);
        Assert.Equal(0x00, Marshal.OffsetOf<ModArrayStruct>(nameof(ModArrayStruct.Values)).ToInt32());
        Assert.Equal(0x18, Marshal.OffsetOf<ModArrayStruct>(nameof(ModArrayStruct.Value0)).ToInt32());
        Assert.Equal(0x28, Marshal.OffsetOf<ModArrayStruct>(nameof(ModArrayStruct.ModsPtr)).ToInt32());
    }
}
