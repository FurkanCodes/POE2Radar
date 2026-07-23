using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

internal static class MapProjectionMotion
{
    internal static NumVec2 PlayerReference(
        MapFrame frame,
        bool smoothingEnabled,
        NumVec2 smoothedPlayerGrid,
        NumVec2 rawPlayerGrid)
        => !frame.IsMinimap && smoothingEnabled
            ? smoothedPlayerGrid
            : rawPlayerGrid;
}
