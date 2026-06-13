using POE2Radar.Core.Game;
using POE2Radar.Overlay;
using POE2Radar.Overlay.Config;
using NumVec2 = System.Numerics.Vector2;
using GameVec3 = POE2Radar.Core.Game.Vector3;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class EntityUnderCursorPickerTests
{
    private static Poe2Live.EntityDot Dot(float gridX, float gridY, float worldX, float worldY, string metadata = "Metadata/Monsters/Foo")
        => new(
            Id: 1,
            Address: 0x1000,
            Grid: new NumVec2(gridX, gridY),
            World: new GameVec3 { X = worldX, Y = worldY, Z = 0f },
            TerrainHeight: 0f,
            Category: Poe2Live.EntityCategory.Monster,
            Metadata: metadata,
            HpCur: 0,
            HpMax: 0,
            Poi: false,
            Reaction: 0,
            Rarity: Poe2Live.Rarity.Normal,
            Opened: false,
            IconComplete: false);

    [Fact]
    public void TryPick_WorldView_FindsEntityNearProjectedScreenPoint()
    {
        var entities = new[] { Dot(100f, 100f, 10f, 20f) };
        var camera = new float[16];
        camera[0] = 1f;
        camera[5] = 1f;
        camera[12] = -10f;
        camera[13] = -20f;
        camera[15] = 1f;

        var ok = EntityUnderCursorPicker.TryPick(
            new NumVec2(960f, 540f),
            1920,
            1080,
            default,
            default,
            new NumVec2(100f, 100f),
            entities,
            importantOnly: false,
            new RadarStyles(),
            resolve: null,
            globalIconScale: 1f,
            camera,
            out var picked);

        Assert.True(ok);
        Assert.Equal(entities[0].Metadata, picked.Metadata);
    }

    [Fact]
    public void TryPick_WorksWhenShowMonstersWouldHaveBlocked()
    {
        var center = new NumVec2(960f, 540f);
        var frame = new MapFrame(center, 1f, 1920f, 1080f, 0, 0f, NumVec2.Zero, IsMinimap: false);
        var entities = new[] { Dot(100f, 100f, 0f, 0f) };

        var ok = EntityUnderCursorPicker.TryPick(
            center,
            1920,
            1080,
            frame,
            default,
            new NumVec2(100f, 100f),
            entities,
            importantOnly: false,
            new RadarStyles(),
            resolve: null,
            globalIconScale: 1f,
            cameraMatrix: null,
            out var picked);

        Assert.True(ok);
        Assert.Equal(entities[0].Metadata, picked.Metadata);
    }

    [Fact]
    public void TryPick_Minimap_UsesScreenClipRect()
    {
        var miniCenter = new NumVec2(1750f, 150f);
        var miniFrame = new MapFrame(
            miniCenter,
            2f,
            200f,
            200f,
            0,
            0f,
            new NumVec2(1650f, 50f),
            IsMinimap: true);
        var entities = new[] { Dot(100f, 100f, 0f, 0f) };

        var ok = EntityUnderCursorPicker.TryPick(
            miniCenter,
            1920,
            1080,
            default,
            miniFrame,
            new NumVec2(100f, 100f),
            entities,
            importantOnly: false,
            new RadarStyles(),
            resolve: null,
            globalIconScale: 1f,
            cameraMatrix: null,
            out var picked);

        Assert.True(ok);
        Assert.Equal(entities[0].Metadata, picked.Metadata);
    }
}
