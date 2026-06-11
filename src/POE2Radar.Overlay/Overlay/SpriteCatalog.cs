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

    /// <summary>Atlas node marker from biome / content icon type (GameHelper2 sheet).</summary>
    public static SpriteIconRef AtlasNode(int iconType, int biome)
    {
        if (iconType is > 0 and < 256)
        {
            var col = iconType % 16;
            var row = iconType / 16;
            if (col < 16 && row < 64) return Cell(col, row);
        }
        return biome switch
        {
            1 => Cell(1, 37),
            2 => Cell(4, 48),
            3 => Cell(8, 38),
            4 => Cell(6, 57),
            5 => Cell(12, 44),
            >= 6 => Cell(10, 44),
            _ => MapMarker(),
        };
    }

    public static SpriteIconRef Cell(int col, int row, float scale = DefaultScale)
        => SpriteIconRef.Cell(col, row, scale);

    /// <summary>Map a display-rule shape name to its icons.png cell (fallback when Sprite is unset).</summary>
    public static SpriteIconRef? ForShape(string? shape)
    {
        if (string.IsNullOrEmpty(shape)) return null;
        return ShapeSprites.TryGetValue(shape, out var spr) ? spr : null;
    }

    /// <summary>Shape names → icons.png cell (for dashboard sprite previews).</summary>
    public static IReadOnlyDictionary<string, SpriteIconRef> ShapeSpritesMap => ShapeSprites;

    private static readonly Dictionary<string, SpriteIconRef> ShapeSprites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Circle"] = NormalMonster(),
        ["Diamond"] = RareMonster(),
        ["Star"] = Boss(),
        ["Square"] = Strongbox(),
        ["Plus"] = Shrine(),
        ["Triangle"] = RareMonster(),
        ["Hexagon"] = Boss(),
        ["Pentagon"] = NormalMonster(),
        ["Cross"] = Shrine(),
        ["Ring"] = Checkpoint(),
        ["Heart"] = ChestRare(),
        ["Shield"] = Strongbox(),
        ["Gem"] = ChestRare(),
        ["ArrowUp"] = Portal(),
        ["TriangleDown"] = Delirium(),
        ["Exclamation"] = MapMarker(),
        ["Droplet"] = ChestRare(),
        ["Waypoint"] = Waypoint(),
        ["Checkpoint"] = Checkpoint(),
        ["MapMarker"] = MapMarker(),
        ["QuestMarker"] = QuestMarker(),
        ["Portal"] = Portal(),
        ["TownPortal"] = TownPortal(),
        ["Stash"] = Stash(),
        ["Bridge"] = Bridge(),
    };
}
