using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

internal static class MapProjectionMotion
{
    internal static NumVec2 PlayerReference(
        bool smoothingEnabled,
        NumVec2 smoothedPlayerGrid,
        NumVec2 rawPlayerGrid)
        => smoothingEnabled ? smoothedPlayerGrid : rawPlayerGrid;

    internal static NumVec2 PlayerReference(RenderContext context)
        => PlayerReference(
            context.SmoothOverlayMotion,
            context.PlayerGrid,
            context.RawPlayerGrid);
}
