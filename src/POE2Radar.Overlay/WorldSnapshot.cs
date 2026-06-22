using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay;

public readonly record struct HpBarSpec(nint Entity, Poe2Live.Rarity Rarity, float Width, uint Fill, float BorderWidth, uint Border);

/// <summary>World-rate priced ground-item label spec; render thread re-reads live world position.</summary>
public readonly record struct ItemLabelSpec(nint Entity, string Name, string Value, bool Highlight, bool ShowName);

public readonly record struct MapEntityRenderItem(
    string Key,
    System.Numerics.Vector2 Grid,
    float TerrainHeight,
    float Size,
    uint Color,
    SpriteIconRef? Sprite,
    string? Shape,
    string Label);

public readonly record struct MapLandmarkRenderItem(
    string Key,
    System.Numerics.Vector2 Center,
    float Size,
    uint Color,
    SpriteIconRef? Sprite,
    string? Shape,
    string Label);

public sealed record WorldSnapshot(
    bool InGame,
    uint AreaHash,
    int AreaLevel,
    string AreaCode,
    int CharLevel,
    Poe2Live.EntityDot[] Entities,
    Poe2Live.Landmark[] Landmarks,
    Poe2Live.TerrainData? Terrain,
    NavTarget[] NavTargets,
    HpBarSpec[] HpSpecs,
    LegendEntry[] Legend,
    string[] SelectedIds,
    SelectedPath[] SelectedPaths,
    MapEntityRenderItem[] MapEntities,
    MapLandmarkRenderItem[] MapLandmarks,
    Poe2Live.ServerMinimapIcon[] ServerIcons,
    MapEntityRenderItem[] MapServerIcons,
    ItemLabelSpec[] ItemLabels)
{
    public static readonly WorldSnapshot Empty = new(
        false, 0, 0, "", 0,
        Array.Empty<Poe2Live.EntityDot>(),
        Array.Empty<Poe2Live.Landmark>(),
        null,
        Array.Empty<NavTarget>(),
        Array.Empty<HpBarSpec>(),
        Array.Empty<LegendEntry>(),
        Array.Empty<string>(),
        Array.Empty<SelectedPath>(),
        Array.Empty<MapEntityRenderItem>(),
        Array.Empty<MapLandmarkRenderItem>(),
        Array.Empty<Poe2Live.ServerMinimapIcon>(),
        Array.Empty<MapEntityRenderItem>(),
        Array.Empty<ItemLabelSpec>());
}
