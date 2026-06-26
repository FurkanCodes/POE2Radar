using POE2Radar.Core.Game;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

/// <summary>Shared large-map / minimap projection math for tick + render-time map lock.</summary>
public static class MapFrameBuilder
{
    public const float MapScaleDivisor = 677f;

    public static (NumVec2 Center, float Scale) LargeMapProjection(
        int windowWidth,
        int windowHeight,
        float shiftX,
        float shiftY,
        float zoom,
        float offsetX,
        float offsetY,
        float scaleMul)
    {
        var h = MathF.Max(1f, windowHeight);
        var (cx, cy) = MapViewportLogic.MapProjectionCenter(
            windowWidth,
            windowHeight,
            shiftX,
            shiftY,
            offsetX,
            offsetY,
            minimapClip: false,
            clipLeft: 0,
            clipTop: 0,
            clipRight: 0,
            clipBottom: 0);
        var scale = (zoom > 0f ? zoom : 1f) * (h / MapScaleDivisor) * scaleMul;
        return (new NumVec2(cx, cy), scale);
    }

    public static (NumVec2 Center, float Scale) MiniMapProjection(
        int windowWidth,
        int windowHeight,
        float shiftX,
        float shiftY,
        float zoom,
        float scaleMul,
        float clipLeft,
        float clipTop,
        float clipRight,
        float clipBottom)
    {
        var (cx, cy) = MapViewportLogic.MapProjectionCenter(
            windowWidth,
            windowHeight,
            shiftX,
            shiftY,
            offsetX: 0f,
            offsetY: 0f,
            minimapClip: true,
            clipLeft,
            clipTop,
            clipRight,
            clipBottom);
        var referenceSide = MathF.Max(1f, MathF.Min(clipRight - clipLeft, clipBottom - clipTop));
        var scale = (zoom > 0f ? zoom : 1f) * (referenceSide / MapScaleDivisor) * scaleMul;
        return (new NumVec2(cx, cy), scale);
    }
}
