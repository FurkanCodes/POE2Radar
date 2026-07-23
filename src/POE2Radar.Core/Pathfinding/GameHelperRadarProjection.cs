using System.Numerics;

namespace POE2Radar.Core.Pathfinding;

/// <summary>
/// Map-frame math ported from MordWraith/Gamehelper's GPL-3.0 Radar plugin,
/// commit 554e8123d4997c4a5acbc8a4b19e1b585fe8b2d0.
/// </summary>
public static class GameHelperRadarProjection
{
    public const float BaseResolutionX = 2560f;
    public const float BaseResolutionY = 1600f;
    public const float LargeMapXBias = 0.6f;
    public const float LargeMapYBias = 0.3f;
    public const float LargeMapScaleBaseline = 0.187812f;
    public const float MiniMapXBias = -5f;
    public const float MiniMapZoomBaseline = 0.748f;
    public const float ScaleDivisor = 240f;

    private static readonly float BaseDiagonal =
        MathF.Sqrt((BaseResolutionX * BaseResolutionX) + (BaseResolutionY * BaseResolutionY));

    /// <summary>GameHelper: LargeMap.Center + Shift + DefaultShift + calibrated/user offsets.</summary>
    public static Vector2 LargeMapCenter(
        Vector2 mapCenter,
        Vector2 shift,
        Vector2 defaultShift,
        Vector2 userOffset)
        => mapCenter
           + shift
           + defaultShift
           + new Vector2(LargeMapXBias, LargeMapYBias)
           + userOffset;

    /// <summary>GameHelper: MiniMap.Position + Size/2 + DefaultShift + Shift + calibrated X offset.</summary>
    public static Vector2 MiniMapCenter(
        Vector2 position,
        Vector2 size,
        Vector2 shift,
        Vector2 defaultShift,
        float userXOffset)
    {
        var center = position + (size / 2f) + defaultShift + shift;
        center.X += MiniMapXBias + userXOffset;
        return center;
    }

    /// <summary>GameHelper's height-scaled base-resolution diagonal.</summary>
    public static float DiagonalLength(float mapHeight)
        => BaseDiagonal * mapHeight / BaseResolutionY;

    /// <summary>Effective large-map grid-to-screen scale produced by GameHelper's Helper.</summary>
    public static float LargeMapScale(float mapHeight, float zoom, float largeMapScaleMultiplier)
        => DiagonalLength(mapHeight)
           * (largeMapScaleMultiplier * zoom * LargeMapScaleBaseline)
           / ScaleDivisor;

    /// <summary>Effective minimap grid-to-screen scale produced by GameHelper's Helper.</summary>
    public static float MiniMapScale(float mapHeight, float zoom, float miniMapZoomMultiplier)
        => DiagonalLength(mapHeight)
           * (zoom * miniMapZoomMultiplier * MiniMapZoomBaseline)
           / ScaleDivisor;
}
