using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

internal static class MapProjectionMotion
{
    internal static NumVec2 PlayerReference(
        bool smoothingEnabled,
        NumVec2 smoothedPlayerGrid,
        NumVec2 rawPlayerGrid)
        // The in-game map texture follows the raw player coordinate. Delaying only our
        // coordinate makes every icon/path drift away from the game's rendered location.
        => rawPlayerGrid;

    internal static NumVec2 PlayerReference(RenderContext context)
        => PlayerReference(
            context.SmoothOverlayMotion,
            context.PlayerGrid,
            context.RawPlayerGrid);
}
