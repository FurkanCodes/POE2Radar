using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay;

/// <summary>Named presets for cells on <c>Overlay/icons.png</c> (64×64 GameHelper2 sprite sheet).</summary>
public static class SpriteCatalog
{
    private const float DefaultScale = 1.25f;

    public static SpriteIconRef Boss() => Cell(6, 57);
    public static SpriteIconRef RareMonster() => Cell(4, 57);
    public static SpriteIconRef MagicMonster() => Cell(4, 57);
    public static SpriteIconRef NormalMonster() => Cell(0, 14);
    public static SpriteIconRef Player() => Cell(2, 0);
    public static SpriteIconRef Npc() => Cell(3, 0);
    public static SpriteIconRef ChestRare() => Cell(4, 48);
    public static SpriteIconRef ChestUnique() => Cell(8, 38);
    public static SpriteIconRef Transition() => Cell(1, 37);
    public static SpriteIconRef Waypoint() => Cell(12, 44);
    public static SpriteIconRef Checkpoint() => Cell(0, 0);
    public static SpriteIconRef MapMarker() => Cell(12, 44);
    public static SpriteIconRef QuestObject() => Cell(12, 44);
    public static SpriteIconRef QuestMarker() => Cell(12, 44);
    public static SpriteIconRef Portal() => Cell(5, 0);
    public static SpriteIconRef TownPortal() => Cell(5, 0);
    public static SpriteIconRef Stash() => Cell(4, 48);
    public static SpriteIconRef Bridge() => Cell(1, 37);
    public static SpriteIconRef Landmark() => Cell(1, 37);
    public static SpriteIconRef Expedition() => Cell(5, 38);
    public static SpriteIconRef Ritual() => Cell(10, 44);
    public static SpriteIconRef Breach() => Cell(11, 44);
    public static SpriteIconRef Strongbox() => Cell(8, 38);
    public static SpriteIconRef Essence() => Cell(7, 45);
    public static SpriteIconRef Shrine() => Cell(7, 0);
    public static SpriteIconRef Delirium() => Cell(6, 0);
    public static SpriteIconRef Abyss() => Cell(0, 0);
    public static SpriteIconRef SummoningCircle() => Cell(1, 37);
    public static SpriteIconRef Wisp() => Cell(12, 44);
    public static SpriteIconRef RogueExile() => Cell(4, 57);

    public static SpriteIconRef Cell(int col, int row, float scale = DefaultScale)
        => SpriteIconRef.Cell(col, row, scale);
}
