using POE2Radar.Core.Game;
using NumVec2 = System.Numerics.Vector2;
using GameVec3 = POE2Radar.Core.Game.Vector3;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class EntityDotTests
{
    [Fact]
    public void IsSleeping_DefaultsToFalse()
    {
        var dot = new Poe2Live.EntityDot(
            Id: 1,
            Address: 0x1000,
            Grid: new NumVec2(10f, 20f),
            World: new GameVec3 { X = 100f, Y = 200f, Z = 0f },
            TerrainHeight: 0f,
            Category: Poe2Live.EntityCategory.Monster,
            Metadata: "Metadata/Monsters/Test",
            HpCur: 100,
            HpMax: 100,
            Poi: true,
            Reaction: 0,
            Rarity: Poe2Live.Rarity.Normal,
            Opened: false,
            IconComplete: false);

        Assert.False(dot.IsSleeping);
    }

    [Fact]
    public void IsSleeping_CanBeTrue()
    {
        var dot = new Poe2Live.EntityDot(
            Id: 1,
            Address: 0x1000,
            Grid: new NumVec2(10f, 20f),
            World: new GameVec3 { X = 100f, Y = 200f, Z = 0f },
            TerrainHeight: 0f,
            Category: Poe2Live.EntityCategory.Monster,
            Metadata: "Metadata/Monsters/Test",
            HpCur: 0,
            HpMax: 0,
            Poi: true,
            Reaction: 0,
            Rarity: Poe2Live.Rarity.Normal,
            Opened: false,
            IconComplete: false,
            IsSleeping: true);

        Assert.True(dot.IsSleeping);
    }
}
