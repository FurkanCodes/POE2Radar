using POE2Radar.Core.Game;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

/// <summary>Shared large-map projection math for tick + render-time map lock.</summary>
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
            offsetY);
        var scale = (zoom > 0f ? zoom : 1f) * (h / MapScaleDivisor) * scaleMul;
        return (new NumVec2(cx, cy), scale);
    }
}
