using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

internal static class MapProjectionMotion
{
    internal static NumVec2 PlayerReference(
        MapFrame frame,
        bool smoothingEnabled,
        NumVec2 smoothedPlayerGrid,
        NumVec2 rawPlayerGrid)
        // The game map frame follows the live player coordinate. Mixing that live frame with a
        // delayed player origin shifts every icon, path, and terrain point while the player moves.
        => rawPlayerGrid;
}
