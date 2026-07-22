// Monolith valuation comes from the MordWraith/Gamehelper RunecraftHelper parity layer. The
// physical explosive route is POE2Radar integration code; upstream does not contain a placement
// planner. See RunecraftHelper.GPLv3.LICENSE.txt.
using System.Diagnostics;
using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Runecraft;

public enum ExpeditionTargetKind : byte
{
    RewardMarker,
    Remnant,
    Monolith,
    Sentinel,
}

public readonly record struct ExpeditionTarget(
    uint Id,
    NumVec2 Grid,
    float TerrainHeight,
    float Weight,
    ExpeditionTargetKind Kind,
    string Label,
    bool Primary = false);

public readonly record struct ExpeditionPlacement(
    NumVec2 Grid,
    float TerrainHeight,
    float CapturedWeight,
    int CapturedCount,
    bool Bridge,
    string Label);

public sealed record ExpeditionPlan(
    ExpeditionPlacement[] Placements,
    int TargetCount,
    int CapturedCount,
    float CapturedWeight,
    double ComputeMilliseconds,
    string Status)
{
    public static readonly ExpeditionPlan Empty = new([], 0, 0, 0f, 0d, "Waiting for encounter");
}

/// <summary>
/// Read-only Expedition route planner. It uses the radar's terrain and A* implementation and returns
/// placement guidance only; it never moves the player or sends input.
/// </summary>
public static class ExpeditionPlanner
{
    private const float Epsilon = 0.001f;
    private static readonly TimeSpan DefaultComputeBudget = TimeSpan.FromMilliseconds(1000);
    private static readonly (int X, int Y)[] Neighbors =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    ];

    public static ExpeditionPlan Build(
        Poe2Live.TerrainData terrain,
        NumVec2 start,
        IReadOnlyList<ExpeditionTarget> targets,
        int chargeBudget,
        float placementDistance,
        float blastRadius,
        CancellationToken cancellationToken = default,
        TimeSpan? computeBudget = null)
    {
        var started = Stopwatch.GetTimestamp();
        var budget = computeBudget.GetValueOrDefault(DefaultComputeBudget);
        if (budget <= TimeSpan.Zero) budget = DefaultComputeBudget;
        if (chargeBudget <= 0)
            return Finish([], targets.Count, 0, 0f, started, "No explosives remaining");
        if (terrain.Width <= 0 || terrain.Height <= 0 || terrain.Walkable.Length < terrain.Width * terrain.Height)
            return Finish([], targets.Count, 0, 0f, started, "Waiting for walkable terrain");
        if (targets.Count == 0 || !targets.Any(t => t.Weight > 0))
            return Finish([], targets.Count, 0, 0f, started, "No weighted Expedition targets found");

        placementDistance = MathF.Max(2f, placementDistance - 1f); // one-cell safety margin
        blastRadius = MathF.Max(1f, blastRadius);
        var reader = new TerrainCellReader(terrain);
        var astar = new AStar(terrain.Width, terrain.Height);
        var captured = new HashSet<uint>();
        var unreachable = new HashSet<uint>();
        var placements = new List<ExpeditionPlacement>(chargeBudget);
        var placementCells = new HashSet<int>();
        var current = Snap(start, terrain);
        var startCell = SnapToWalkable(
            reader,
            new PathCell((int)current.X, (int)current.Y),
            maxRadius: 32);
        if (startCell is null)
            return Finish([], targets.Count, 0, 0f, started, "No walkable Expedition start");
        current = new NumVec2(startCell.Value.X, startCell.Value.Y);
        var reachableMask = BuildReachableMask(
            reader,
            startCell.Value,
            cancellationToken,
            started,
            budget,
            out var connectivityBudgetReached);
        if (connectivityBudgetReached)
            return Finish(
                [], targets.Count, 0, 0f, started,
                "Planning budget reached before connectivity scan completed");
        var primaryOrder = OrderPrimarySpine(
            targets,
            current,
            reader,
            reachableMask,
            astar,
            cancellationToken,
            started,
            budget,
            out var orderingBudgetReached);
        if (orderingBudgetReached)
            return Finish(
                [], targets.Count, 0, 0f, started,
                "Planning budget reached while ordering runestone spine");
        var primaryIndex = 0;
        float totalWeight = 0f;
        int totalCaptured = 0;
        var budgetReached = false;

        for (var charge = 0; charge < chargeBudget; charge++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= budget)
            {
                budgetReached = true;
                break;
            }
            Candidate? bestCapture = null;
            Candidate? bestBridge = null;

            while (primaryIndex < primaryOrder.Count
                   && (captured.Contains(primaryOrder[primaryIndex].Id)
                       || unreachable.Contains(primaryOrder[primaryIndex].Id)))
                primaryIndex++;
            var spineGoal = primaryIndex < primaryOrder.Count
                ? primaryOrder[primaryIndex]
                : (ExpeditionTarget?)null;

            foreach (var target in targets)
            {
                if (target.Weight <= 0 || captured.Contains(target.Id) || unreachable.Contains(target.Id)) continue;
                // GameHelper routes a continuous spine through runestones/monoliths and other primary
                // anchors before spare explosives are allowed to pursue secondary reward markers.
                if (spineGoal is { } anchor && target.Id != anchor.Id) continue;
                cancellationToken.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(started) >= budget)
                {
                    budgetReached = true;
                    break;
                }

                var targetCell = SnapToWalkable(
                    reader,
                    new PathCell((int)MathF.Round(target.Grid.X), (int)MathF.Round(target.Grid.Y)),
                    maxRadius: 32);
                if (targetCell is null
                    || !reachableMask[targetCell.Value.Y * terrain.Width + targetCell.Value.X])
                {
                    unreachable.Add(target.Id);
                    continue;
                }

                var path = astar.FindPath(
                    reader,
                    new PathCell((int)MathF.Round(current.X), (int)MathF.Round(current.Y)),
                    targetCell.Value,
                    maxNodes: 250_000,
                    flatCost: true);
                if (!path.Found || path.Cells.Count == 0)
                {
                    unreachable.Add(target.Id);
                    continue;
                }

                var reachable = ReachablePrefix(path.Cells, placementDistance);
                if (reachable.Count == 0) continue;
                // Pick the furthest safe bridge. A cell touching a positive target is a real capture
                // decision and must pass the net-weight test below (important for build-breaking remnants).
                for (var i = reachable.Count - 1; i >= 0; i--)
                {
                    if (placementCells.Contains(reachable[i].Y * terrain.Width + reachable[i].X)) continue;
                    var bridge = ScoreCandidate(reachable[i], path.Cost, target, targets, captured, blastRadius, bridge: true);
                    if (bridge.CapturedPositive != 0 || MathF.Abs(bridge.NetWeight) > Epsilon) continue;
                    if (bestBridge is null || BetterBridge(bridge, bestBridge.Value)) bestBridge = bridge;
                    break;
                }

                foreach (var cell in reachable)
                {
                    if (placementCells.Contains(cell.Y * terrain.Width + cell.X)) continue;
                    var candidate = ScoreCandidate(cell, path.Cost, target, targets, captured, blastRadius, bridge: false);
                    if (candidate.CapturedPositive <= 0 || candidate.NetWeight <= 0) continue;
                    if (bestCapture is null || BetterCapture(candidate, bestCapture.Value)) bestCapture = candidate;
                }
            }

            var chosen = bestCapture ?? bestBridge;
            if (chosen is null) break;
            var c = chosen.Value;
            placementCells.Add((int)c.Grid.Y * terrain.Width + (int)c.Grid.X);
            var ids = new List<uint>();
            float capturedWeight = 0f;
            int positiveCount = 0;
            foreach (var target in targets)
            {
                if (captured.Contains(target.Id) || NumVec2.Distance(c.Grid, target.Grid) > blastRadius + Epsilon) continue;
                ids.Add(target.Id);
                capturedWeight += target.Weight;
                if (target.Weight > 0) positiveCount++;
            }
            foreach (var id in ids) captured.Add(id);

            var isBridge = positiveCount == 0;
            var label = isBridge
                ? $"Bridge toward {c.Goal.Label}"
                : JoinCapturedLabels(targets, ids);
            placements.Add(new ExpeditionPlacement(
                c.Grid,
                InterpolateHeight(c.Grid, targets, c.Goal.TerrainHeight),
                capturedWeight,
                positiveCount,
                isBridge,
                label));
            current = c.Grid;
            totalWeight += capturedWeight;
            totalCaptured += positiveCount;

            if (!targets.Any(t => t.Weight > 0 && !captured.Contains(t.Id) && !unreachable.Contains(t.Id)))
                break;
        }

        var remaining = targets.Count(t => t.Weight > 0 && !captured.Contains(t.Id));
        var status = budgetReached
            ? placements.Count == 0
                ? "Planning budget reached before a safe route was found"
                : "Planning budget reached; showing best partial route"
            : placements.Count == 0
            ? "No reachable Expedition targets"
            : remaining == 0
                ? "All reachable weighted targets covered"
                : $"Best route within {chargeBudget} remaining explosive{(chargeBudget == 1 ? "" : "s")}";
        return Finish(placements.ToArray(), targets.Count, totalCaptured, totalWeight, started, status);
    }

    private static List<ExpeditionTarget> OrderPrimarySpine(
        IReadOnlyList<ExpeditionTarget> targets,
        NumVec2 start,
        TerrainCellReader reader,
        bool[] reachableMask,
        AStar astar,
        CancellationToken cancellationToken,
        long started,
        TimeSpan budget,
        out bool budgetReached)
    {
        budgetReached = false;
        var anchors = targets
            .Where(t => t.Primary && t.Weight > 0)
            .Where(t =>
            {
                var c = SnapToWalkable(
                    reader,
                    new PathCell((int)MathF.Round(t.Grid.X), (int)MathF.Round(t.Grid.Y)),
                    maxRadius: 32);
                return c is { } cell && reachableMask[cell.Y * reader.Width + cell.X];
            })
            .ToList();
        if (anchors.Count == 0) return anchors;

        var points = new NumVec2[anchors.Count + 1];
        points[0] = start;
        for (var i = 0; i < anchors.Count; i++) points[i + 1] = anchors[i].Grid;
        var distance = new float[points.Length, points.Length];
        for (var i = 0; i < points.Length; i++)
        for (var j = i + 1; j < points.Length; j++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= budget)
            {
                budgetReached = true;
                return [];
            }
            var path = astar.FindPath(
                reader,
                new PathCell((int)MathF.Round(points[i].X), (int)MathF.Round(points[i].Y)),
                new PathCell((int)MathF.Round(points[j].X), (int)MathF.Round(points[j].Y)),
                maxNodes: 250_000,
                flatCost: true);
            var d = path.Found ? PathLength(path.Cells) : float.PositiveInfinity;
            distance[i, j] = d;
            distance[j, i] = d;
        }

        // Same open-tour model as GameHelper: nearest-neighbour seed from the detonator, then 2-opt.
        var remaining = Enumerable.Range(0, anchors.Count).ToList();
        var order = new List<int>(anchors.Count);
        var currentNode = 0;
        while (remaining.Count > 0)
        {
            var best = remaining
                .OrderBy(i => distance[currentNode, i + 1])
                .ThenByDescending(i => anchors[i].Weight)
                .First();
            if (!float.IsFinite(distance[currentNode, best + 1])) break;
            order.Add(best);
            remaining.Remove(best);
            currentNode = best + 1;
        }

        var improved = true;
        while (improved)
        {
            improved = false;
            for (var i = 0; i < order.Count - 1 && !improved; i++)
            for (var k = i + 1; k < order.Count; k++)
            {
                var prev = i == 0 ? 0 : order[i - 1] + 1;
                var first = order[i] + 1;
                var last = order[k] + 1;
                var oldCost = distance[prev, first];
                var newCost = distance[prev, last];
                if (k + 1 < order.Count)
                {
                    var next = order[k + 1] + 1;
                    oldCost += distance[last, next];
                    newCost += distance[first, next];
                }
                if (newCost + Epsilon >= oldCost) continue;
                order.Reverse(i, k - i + 1);
                improved = true;
                break;
            }
        }

        // GameHelper pins the Kalguur Sentinel first to maximize its encounter-wide uptime.
        var sentinel = order.FindIndex(i => anchors[i].Kind == ExpeditionTargetKind.Sentinel);
        if (sentinel > 0)
        {
            var pinned = order[sentinel];
            order.RemoveAt(sentinel);
            order.Insert(0, pinned);
        }
        return order.Select(i => anchors[i]).ToList();
    }

    private static float PathLength(IReadOnlyList<PathCell> cells)
    {
        var length = 0f;
        for (var i = 1; i < cells.Count; i++)
            length += cells[i - 1].X != cells[i].X && cells[i - 1].Y != cells[i].Y
                ? 1.4142136f
                : 1f;
        return length;
    }

    private static bool[] BuildReachableMask(
        TerrainCellReader reader,
        PathCell start,
        CancellationToken cancellationToken,
        long started,
        TimeSpan budget,
        out bool budgetReached)
    {
        budgetReached = false;
        var mask = new bool[reader.Width * reader.Height];
        var queue = new Queue<PathCell>();
        mask[start.Y * reader.Width + start.X] = true;
        queue.Enqueue(start);
        var visited = 0;
        while (queue.TryDequeue(out var cell))
        {
            if ((visited++ & 2047) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(started) >= budget)
                {
                    budgetReached = true;
                    break;
                }
            }
            foreach (var (dx, dy) in Neighbors)
            {
                var x = cell.X + dx;
                var y = cell.Y + dy;
                if ((uint)x >= (uint)reader.Width || (uint)y >= (uint)reader.Height) continue;
                var index = y * reader.Width + x;
                if (mask[index] || reader.Read(x, y) == 0) continue;
                if (dx != 0 && dy != 0
                    && (reader.Read(cell.X + dx, cell.Y) == 0
                        || reader.Read(cell.X, cell.Y + dy) == 0))
                    continue;
                mask[index] = true;
                queue.Enqueue(new PathCell(x, y));
            }
        }
        return mask;
    }

    private static PathCell? SnapToWalkable(
        TerrainCellReader reader,
        PathCell cell,
        int maxRadius)
    {
        var x = Math.Clamp(cell.X, 0, reader.Width - 1);
        var y = Math.Clamp(cell.Y, 0, reader.Height - 1);
        if (reader.Read(x, y) > 0) return new PathCell(x, y);
        for (var radius = 1; radius <= maxRadius; radius++)
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;
            var nx = x + dx;
            var ny = y + dy;
            if ((uint)nx >= (uint)reader.Width || (uint)ny >= (uint)reader.Height) continue;
            if (reader.Read(nx, ny) > 0) return new PathCell(nx, ny);
        }
        return null;
    }

    private readonly record struct Candidate(
        NumVec2 Grid,
        ExpeditionTarget Goal,
        float NetWeight,
        int CapturedPositive,
        float GoalProgress,
        float Priority);

    private static Candidate ScoreCandidate(
        PathCell cell,
        float pathCost,
        ExpeditionTarget goal,
        IReadOnlyList<ExpeditionTarget> targets,
        HashSet<uint> captured,
        float radius,
        bool bridge)
    {
        var grid = new NumVec2(cell.X, cell.Y);
        float net = 0f;
        int positives = 0;
        foreach (var target in targets)
        {
            if (captured.Contains(target.Id) || NumVec2.Distance(grid, target.Grid) > radius + Epsilon) continue;
            net += target.Weight;
            if (target.Weight > 0) positives++;
        }
        var remaining = NumVec2.Distance(grid, goal.Grid);
        var priority = (goal.Primary ? 2f : 1f) * goal.Weight / MathF.Max(1f, pathCost);
        return new Candidate(grid, goal, net, positives, -remaining, priority + (bridge ? 0f : 0.001f));
    }

    private static bool BetterCapture(Candidate a, Candidate b)
    {
        if (MathF.Abs(a.NetWeight - b.NetWeight) > Epsilon) return a.NetWeight > b.NetWeight;
        if (a.CapturedPositive != b.CapturedPositive) return a.CapturedPositive > b.CapturedPositive;
        if (MathF.Abs(a.Priority - b.Priority) > Epsilon) return a.Priority > b.Priority;
        return a.GoalProgress > b.GoalProgress;
    }

    private static bool BetterBridge(Candidate a, Candidate b)
    {
        if (MathF.Abs(a.Priority - b.Priority) > Epsilon) return a.Priority > b.Priority;
        return a.GoalProgress > b.GoalProgress;
    }

    private static List<PathCell> ReachablePrefix(IReadOnlyList<PathCell> cells, float maxDistance)
    {
        var result = new List<PathCell>();
        float distance = 0f;
        for (var i = 1; i < cells.Count; i++)
        {
            var prev = cells[i - 1];
            var next = cells[i];
            distance += prev.X != next.X && prev.Y != next.Y ? 1.4142136f : 1f;
            if (distance > maxDistance + Epsilon) break;
            result.Add(next);
        }
        return result;
    }

    private static NumVec2 Snap(NumVec2 point, Poe2Live.TerrainData terrain)
        => new(
            Math.Clamp(MathF.Round(point.X), 0, terrain.Width - 1),
            Math.Clamp(MathF.Round(point.Y), 0, terrain.Height - 1));

    private static float InterpolateHeight(NumVec2 grid, IReadOnlyList<ExpeditionTarget> targets, float fallback)
    {
        var nearest = targets.OrderBy(t => NumVec2.DistanceSquared(grid, t.Grid)).FirstOrDefault();
        return nearest.Id == 0 ? fallback : nearest.TerrainHeight;
    }

    private static string JoinCapturedLabels(IReadOnlyList<ExpeditionTarget> targets, IReadOnlyCollection<uint> ids)
    {
        var labels = targets.Where(t => ids.Contains(t.Id) && t.Weight > 0)
            .OrderByDescending(t => t.Weight)
            .Select(t => t.Label)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        return labels.Length == 0 ? "Coverage" : string.Join(" + ", labels);
    }

    private static ExpeditionPlan Finish(
        ExpeditionPlacement[] placements,
        int targetCount,
        int capturedCount,
        float capturedWeight,
        long started,
        string status)
        => new(
            placements,
            targetCount,
            capturedCount,
            capturedWeight,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            status);
}
