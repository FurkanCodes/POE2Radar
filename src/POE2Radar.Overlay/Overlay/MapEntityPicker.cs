using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;
using NumVec2 = System.Numerics.Vector2;
using GameVec2 = POE2Radar.Core.Game.Vector2;

namespace POE2Radar.Overlay;

/// <summary>Hit-test map-projected entity icons under the cursor (large map + minimap).</summary>
public static class MapEntityPicker
{
    private const float HitMarginPx = 14f;

    public static bool TryPick(
        NumVec2 cursorClient,
        MapFrame largeMap,
        MapFrame miniMap,
        NumVec2 playerGrid,
        IReadOnlyList<Poe2Live.EntityDot> entities,
        bool importantOnly,
        RadarStyles styles,
        Func<Poe2Live.EntityDot, DisplayRule?>? resolve,
        float globalIconScale,
        out Poe2Live.EntityDot picked)
    {
        picked = default;
        if (entities.Count == 0) return false;

        float bestDist = float.MaxValue;
        if (!TryPickDistance(cursorClient, largeMap, miniMap, playerGrid, entities, importantOnly, styles,
                resolve, globalIconScale, ref bestDist, out var best) || best is not { } hit)
            return false;

        picked = hit;
        return true;
    }

    internal static bool TryPickDistance(
        NumVec2 cursorClient,
        MapFrame largeMap,
        MapFrame miniMap,
        NumVec2 playerGrid,
        IReadOnlyList<Poe2Live.EntityDot> entities,
        bool importantOnly,
        RadarStyles styles,
        Func<Poe2Live.EntityDot, DisplayRule?>? resolve,
        float globalIconScale,
        ref float bestDist,
        out Poe2Live.EntityDot? picked)
    {
        picked = null;

        ScoreFrame(cursorClient, largeMap, playerGrid, entities, importantOnly, styles, resolve, globalIconScale,
            ref bestDist, ref picked);

        if (miniMap.IsMinimap && miniMap.Width > 1f && miniMap.Height > 1f)
            ScoreFrame(cursorClient, miniMap, playerGrid, entities, importantOnly, styles, resolve, globalIconScale,
                ref bestDist, ref picked);

        return picked is not null;
    }

    private static void ScoreFrame(
        NumVec2 cursor,
        MapFrame frame,
        NumVec2 playerGrid,
        IReadOnlyList<Poe2Live.EntityDot> entities,
        bool importantOnly,
        RadarStyles styles,
        Func<Poe2Live.EntityDot, DisplayRule?>? resolve,
        float globalIconScale,
        ref float bestDist,
        ref Poe2Live.EntityDot? best)
    {
        var center = frame.Center;
        var scale = MathF.Max(0.01f, frame.Scale);
        var projectionOrigin = playerGrid;
        float clipL, clipT, clipR, clipB;
        if (frame.IsMinimap && frame.Width > 1f && frame.Height > 1f)
        {
            clipL = frame.Position.X;
            clipT = frame.Position.Y;
            clipR = clipL + frame.Width;
            clipB = clipT + frame.Height;
        }
        else
        {
            clipL = -40f;
            clipT = -40f;
            clipR = frame.Width + 40f;
            clipB = frame.Height + 40f;
        }

        foreach (var e in entities)
        {
            var rule = resolve?.Invoke(e);
            if (rule is { Hide: true }) continue;
            if (importantOnly && EntityImportanceHelper.IsTrash(
                    EntityImportanceHelper.Classify(e, styles))) continue;

            var p = Project(e.Grid, projectionOrigin, center, scale, e.TerrainHeight - frame.PlayerTerrainHeight);
            if (p.X < clipL - 40f || p.Y < clipT - 40f || p.X > clipR + 40f || p.Y > clipB + 40f) continue;

            var radius = (rule?.Size ?? DefaultEntityRadius(e)) * globalIconScale;
            var hit = radius + HitMarginPx;
            var dx = cursor.X - p.X;
            var dy = cursor.Y - p.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > hit || dist >= bestDist) continue;

            bestDist = dist;
            best = e;
        }
    }

    private static NumVec2 Project(NumVec2 cell, NumVec2 player, NumVec2 center, float scale, float deltaWorldZ)
    {
        var d = cell - player;
        var md = MapProjection.GridDeltaToMapDelta(new GameVec2 { X = d.X, Y = d.Y }, scale, deltaWorldZ);
        return new NumVec2(center.X + md.X, center.Y + md.Y);
    }

    private static float DefaultEntityRadius(Poe2Live.EntityDot e) => e.Category switch
    {
        Poe2Live.EntityCategory.Monster => e.Rarity is Poe2Live.Rarity.Rare or Poe2Live.Rarity.Unique ? 4.4f : 3.2f,
        Poe2Live.EntityCategory.Player => 4.2f,
        Poe2Live.EntityCategory.Npc => 3.8f,
        Poe2Live.EntityCategory.Chest => 3.5f,
        Poe2Live.EntityCategory.Transition => 4.8f,
        _ => 3f,
    };
}
