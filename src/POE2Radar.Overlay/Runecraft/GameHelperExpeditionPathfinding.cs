// Ported from the Expedition planner embedded in MordWraith/Gamehelper's RunecraftHelper.dll.
// The original plugin is GPL-3.0; see RunecraftHelper.GPLv3.LICENSE.txt in this directory.
using POE2Radar.Core.Game;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Runecraft;

internal static class GameHelperExpeditionLineWalker
{
    internal static bool IsWalkable(
        Poe2Live.TerrainData terrain,
        int x,
        int y,
        HashSet<(int X, int Y)>? forcedWalkable = null)
    {
        if (forcedWalkable?.Contains((x, y)) == true) return true;
        if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height) return false;
        var index = y * terrain.Width + x;
        return (uint)index < (uint)terrain.Walkable.Length && terrain.Walkable[index] != 0;
    }

    internal static bool IsLineClear(
        Poe2Live.TerrainData terrain,
        NumVec2 start,
        NumVec2 end,
        HashSet<(int X, int Y)>? forcedWalkable = null)
    {
        var x0 = (int)MathF.Round(start.X);
        var y0 = (int)MathF.Round(start.Y);
        var x1 = (int)MathF.Round(end.X);
        var y1 = (int)MathF.Round(end.Y);
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx - dy;

        while (true)
        {
            if (!IsWalkable(terrain, x0, y0, forcedWalkable)) return false;
            if (x0 == x1 && y0 == y1) return true;
            var doubled = error * 2;
            if (doubled > -dy) { error -= dy; x0 += sx; }
            if (doubled < dx) { error += dx; y0 += sy; }
        }
    }
}

internal static class GameHelperExpeditionPathfinder
{
    private const int DefaultMaxIterations = 1_000_000;
    private const float DefaultMaxDistance = 2_500f;
    private const int DefaultSnapRadius = 300;
    private static readonly (int Dx, int Dy, float Cost)[] Neighbors =
    [
        (0, -1, 1f), (1, -1, 1.4142135f), (1, 0, 1f), (1, 1, 1.4142135f),
        (0, 1, 1f), (-1, 1, 1.4142135f), (-1, 0, 1f), (-1, -1, 1.4142135f),
    ];

    internal static List<NumVec2>? FindPath(
        Poe2Live.TerrainData terrain,
        NumVec2 start,
        NumVec2 end,
        HashSet<(int X, int Y)>? forcedWalkable = null,
        int maxIterations = DefaultMaxIterations,
        float maxCost = float.MaxValue,
        CancellationToken cancellationToken = default)
    {
        var startX = (int)MathF.Round(start.X);
        var startY = (int)MathF.Round(start.Y);
        var endX = (int)MathF.Round(end.X);
        var endY = (int)MathF.Round(end.Y);
        if (!GameHelperExpeditionLineWalker.IsWalkable(terrain, startX, startY, forcedWalkable)
            && !TryFindNearestWalkable(terrain, startX, startY, forcedWalkable, out startX, out startY))
            return null;
        if (MathF.Sqrt((endX - startX) * (endX - startX) + (endY - startY) * (endY - startY))
            > DefaultMaxDistance)
            return null;
        if (!GameHelperExpeditionLineWalker.IsWalkable(terrain, endX, endY, forcedWalkable)
            && !TryFindNearestWalkable(terrain, endX, endY, forcedWalkable, out endX, out endY))
            return null;
        if (startX == endX && startY == endY) return [new NumVec2(startX, startY)];

        var startCell = (startX, startY);
        var endCell = (endX, endY);
        var open = new PriorityQueue<(int X, int Y), float>();
        var cameFrom = new Dictionary<(int X, int Y), (int X, int Y)>();
        var cost = new Dictionary<(int X, int Y), float> { [startCell] = 0f };
        open.Enqueue(startCell, Heuristic(startX, startY, endX, endY));

        var iterations = 0;
        while (open.TryDequeue(out var current, out var priority) && iterations++ < maxIterations)
        {
            if ((iterations & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (priority > maxCost) return null;
            if (current == endCell)
                return SmoothPath(terrain, ReconstructPath(cameFrom, current, startCell), forcedWalkable);

            var currentCost = cost[current];
            foreach (var (dx, dy, stepCost) in Neighbors)
            {
                var x = current.X + dx;
                var y = current.Y + dy;
                if (!GameHelperExpeditionLineWalker.IsWalkable(terrain, x, y, forcedWalkable)) continue;
                if (dx != 0 && dy != 0
                    && (!GameHelperExpeditionLineWalker.IsWalkable(terrain, current.X + dx, current.Y, forcedWalkable)
                        || !GameHelperExpeditionLineWalker.IsWalkable(terrain, current.X, current.Y + dy, forcedWalkable)))
                    continue;

                var next = (x, y);
                var nextCost = currentCost + stepCost;
                if (cost.TryGetValue(next, out var oldCost) && nextCost >= oldCost) continue;
                cameFrom[next] = current;
                cost[next] = nextCost;
                open.Enqueue(next, nextCost + Heuristic(x, y, endX, endY));
            }
        }
        return null;
    }

    internal static float FindPathCost(
        Poe2Live.TerrainData terrain,
        NumVec2 start,
        NumVec2 end,
        HashSet<(int X, int Y)>? forcedWalkable = null,
        int maxIterations = DefaultMaxIterations,
        CancellationToken cancellationToken = default)
    {
        var startX = (int)MathF.Round(start.X);
        var startY = (int)MathF.Round(start.Y);
        var endX = (int)MathF.Round(end.X);
        var endY = (int)MathF.Round(end.Y);
        if (!GameHelperExpeditionLineWalker.IsWalkable(terrain, startX, startY, forcedWalkable)
            && !TryFindNearestWalkable(terrain, startX, startY, forcedWalkable, out startX, out startY))
            return -1f;
        if (!GameHelperExpeditionLineWalker.IsWalkable(terrain, endX, endY, forcedWalkable)
            && !TryFindNearestWalkable(terrain, endX, endY, forcedWalkable, out endX, out endY))
            return -1f;
        if (startX == endX && startY == endY) return 0f;

        var startCell = (startX, startY);
        var endCell = (endX, endY);
        var open = new PriorityQueue<(int X, int Y), float>();
        var cost = new Dictionary<(int X, int Y), float> { [startCell] = 0f };
        open.Enqueue(startCell, Heuristic(startX, startY, endX, endY));
        var iterations = 0;
        while (open.TryDequeue(out var current, out _) && iterations++ < maxIterations)
        {
            if ((iterations & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (current == endCell) return cost[current];
            var currentCost = cost[current];
            foreach (var (dx, dy, stepCost) in Neighbors)
            {
                var x = current.X + dx;
                var y = current.Y + dy;
                if (!GameHelperExpeditionLineWalker.IsWalkable(terrain, x, y, forcedWalkable)) continue;
                if (dx != 0 && dy != 0
                    && (!GameHelperExpeditionLineWalker.IsWalkable(terrain, current.X + dx, current.Y, forcedWalkable)
                        || !GameHelperExpeditionLineWalker.IsWalkable(terrain, current.X, current.Y + dy, forcedWalkable)))
                    continue;
                var next = (x, y);
                var nextCost = currentCost + stepCost;
                if (cost.TryGetValue(next, out var oldCost) && nextCost >= oldCost) continue;
                cost[next] = nextCost;
                open.Enqueue(next, nextCost + Heuristic(x, y, endX, endY));
            }
        }
        return -1f;
    }

    private static float Heuristic(int x, int y, int endX, int endY)
    {
        var dx = endX - x;
        var dy = endY - y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static bool TryFindNearestWalkable(
        Poe2Live.TerrainData terrain,
        int x,
        int y,
        HashSet<(int X, int Y)>? forcedWalkable,
        out int resultX,
        out int resultY,
        int maxRadius = DefaultSnapRadius)
    {
        if (GameHelperExpeditionLineWalker.IsWalkable(terrain, x, y, forcedWalkable))
        {
            resultX = x; resultY = y; return true;
        }
        for (var radius = 1; radius <= maxRadius; radius++)
        {
            for (var offset = -radius; offset <= radius; offset++)
            {
                if (GameHelperExpeditionLineWalker.IsWalkable(terrain, x + offset, y - radius, forcedWalkable))
                { resultX = x + offset; resultY = y - radius; return true; }
                if (GameHelperExpeditionLineWalker.IsWalkable(terrain, x + offset, y + radius, forcedWalkable))
                { resultX = x + offset; resultY = y + radius; return true; }
            }
            for (var offset = -radius + 1; offset <= radius - 1; offset++)
            {
                if (GameHelperExpeditionLineWalker.IsWalkable(terrain, x - radius, y + offset, forcedWalkable))
                { resultX = x - radius; resultY = y + offset; return true; }
                if (GameHelperExpeditionLineWalker.IsWalkable(terrain, x + radius, y + offset, forcedWalkable))
                { resultX = x + radius; resultY = y + offset; return true; }
            }
        }
        resultX = 0; resultY = 0; return false;
    }

    private static List<NumVec2> ReconstructPath(
        Dictionary<(int X, int Y), (int X, int Y)> cameFrom,
        (int X, int Y) current,
        (int X, int Y) start)
    {
        var path = new List<NumVec2> { new(current.X, current.Y) };
        while (current != start)
        {
            current = cameFrom[current];
            path.Add(new NumVec2(current.X, current.Y));
        }
        path.Reverse();
        return path;
    }

    private static List<NumVec2> SmoothPath(
        Poe2Live.TerrainData terrain,
        List<NumVec2> rawPath,
        HashSet<(int X, int Y)>? forcedWalkable)
    {
        if (rawPath.Count <= 2) return rawPath;
        var result = new List<NumVec2> { rawPath[0] };
        var index = 0;
        while (index < rawPath.Count - 1)
        {
            var next = index + 1;
            for (var candidate = rawPath.Count - 1; candidate > index; candidate--)
            {
                if (!GameHelperExpeditionLineWalker.IsLineClear(
                        terrain, rawPath[index], rawPath[candidate], forcedWalkable)) continue;
                next = candidate;
                break;
            }
            index = next;
            result.Add(rawPath[index]);
        }
        return result;
    }
}
