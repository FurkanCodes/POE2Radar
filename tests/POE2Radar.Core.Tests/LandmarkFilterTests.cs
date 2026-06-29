using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class LandmarkFilterTests
{
    [Theory]
    [InlineData("Metadata/Terrain/Interlude/Part3/P3_2/KriarVillage_Bossroom.tdt")]
    [InlineData("Metadata/Terrain/Maps/Foo/Tiles/BossArena_01.tdt")]
    [InlineData("Metadata/Terrain/Maps/Foo/Tiles/boss_room_gate.tdt")]
    public void BuiltInLandmarkLabel_KeepsBossRoomTiles(string path)
        => Assert.Equal("Boss", Poe2Live.BuiltInLandmarkLabel(path));

    [Theory]
    [InlineData("Metadata/Terrain/Maps/Foo/Tiles/Vault_Door_01.tdt")]
    [InlineData("Metadata/Terrain/Maps/Foo/Tiles/arena_decor_01.tdt")]
    [InlineData("Metadata/Terrain/Maps/Foo/Tiles/UniqueWall_01.tdt")]
    public void BuiltInLandmarkLabel_DoesNotRestoreOldGenericNoise(string path)
        => Assert.Null(Poe2Live.BuiltInLandmarkLabel(path));
}
