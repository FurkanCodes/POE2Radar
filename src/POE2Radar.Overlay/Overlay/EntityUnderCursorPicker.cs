using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

/// <summary>Hit-test entities under the cursor on the large map, minimap, and 3D world view.</summary>
public static class EntityUnderCursorPicker
{
    private const float WorldHitRadiusPx = 28f;

    public static bool TryPick(
        NumVec2 cursorClient,
        int windowWidth,
        int windowHeight,
        MapFrame largeMap,
        MapFrame miniMap,
        NumVec2 playerGrid,
        IReadOnlyList<Poe2Live.EntityDot> entities,
        bool showMonsters,
        bool importantOnly,
        RadarStyles styles,
        Func<Poe2Live.EntityDot, DisplayRule?>? resolve,
        float globalIconScale,
        float[]? cameraMatrix,
        out Poe2Live.EntityDot picked)
    {
        picked = default;
        if (!showMonsters || entities.Count == 0) return false;

        float bestDist = float.MaxValue;
        Poe2Live.EntityDot? best = null;

        if (MapEntityPicker.TryPickDistance(
                cursorClient, largeMap, miniMap, playerGrid, entities, importantOnly, styles, resolve,
                globalIconScale, ref bestDist, out var mapHit) && mapHit is { } m)
            best = m;

        if (cameraMatrix is { Length: >= 16 } && windowWidth > 0 && windowHeight > 0)
            ScoreWorld(cursorClient, windowWidth, windowHeight, entities, importantOnly, styles, resolve,
                cameraMatrix, ref bestDist, ref best);

        if (best is not { } hit) return false;
        picked = hit;
        return true;
    }

    private static void ScoreWorld(
        NumVec2 cursor,
        int windowWidth,
        int windowHeight,
        IReadOnlyList<Poe2Live.EntityDot> entities,
        bool importantOnly,
        RadarStyles styles,
        Func<Poe2Live.EntityDot, DisplayRule?>? resolve,
        float[] m,
        ref float bestDist,
        ref Poe2Live.EntityDot? best)
    {
        float W = windowWidth, H = windowHeight;

        foreach (var e in entities)
        {
            if (e.IconComplete) continue;
            var rule = resolve?.Invoke(e);
            if (rule is { Hide: true }) continue;
            if (importantOnly && EntityImportanceHelper.IsTrash(
                    EntityImportanceHelper.Classify(e, styles))) continue;

            var w = e.World;
            var cw = w.X * m[3] + w.Y * m[7] + w.Z * m[11] + m[15];
            if (cw <= 0.0001f) continue;

            var cx = w.X * m[0] + w.Y * m[4] + w.Z * m[8] + m[12];
            var cy = w.X * m[1] + w.Y * m[5] + w.Z * m[9] + m[13];
            var sx = (cx / cw / 2f + 0.5f) * W;
            var sy = (0.5f - cy / cw / 2f) * H;
            if (!float.IsFinite(sx) || !float.IsFinite(sy)) continue;
            if (sx < -40 || sy < -40 || sx > W + 40 || sy > H + 40) continue;

            var depthScale = Math.Clamp(1f / MathF.Max(0.15f, cw), 0.6f, 1.4f);
            var hit = WorldHitRadiusPx * depthScale;
            var dx = cursor.X - sx;
            var dy = cursor.Y - sy;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > hit || dist >= bestDist) continue;

            bestDist = dist;
            best = e;
        }
    }
}
