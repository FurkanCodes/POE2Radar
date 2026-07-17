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

    public static ExpeditionPlan Build(
        Poe2Live.TerrainData terrain,
        NumVec2 start,
        IReadOnlyList<ExpeditionTarget> targets,
        int chargeBudget,
        float placementDistance,
        float blastRadius,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
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
        var current = Snap(start, terrain);
        float totalWeight = 0f;
        int totalCaptured = 0;

        for (var charge = 0; charge < chargeBudget; charge++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Candidate? bestCapture = null;
            Candidate? bestBridge = null;

            foreach (var target in targets)
            {
                if (target.Weight <= 0 || captured.Contains(target.Id) || unreachable.Contains(target.Id)) continue;
                cancellationToken.ThrowIfCancellationRequested();

                var path = astar.FindPath(
                    reader,
                    new PathCell((int)MathF.Round(current.X), (int)MathF.Round(current.Y)),
                    new PathCell((int)MathF.Round(target.Grid.X), (int)MathF.Round(target.Grid.Y)),
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
                    var bridge = ScoreCandidate(reachable[i], path.Cost, target, targets, captured, blastRadius, bridge: true);
                    if (bridge.CapturedPositive != 0 || MathF.Abs(bridge.NetWeight) > Epsilon) continue;
                    if (bestBridge is null || BetterBridge(bridge, bestBridge.Value)) bestBridge = bridge;
                    break;
                }

                foreach (var cell in reachable)
                {
                    var candidate = ScoreCandidate(cell, path.Cost, target, targets, captured, blastRadius, bridge: false);
                    if (candidate.CapturedPositive <= 0 || candidate.NetWeight <= 0) continue;
                    if (bestCapture is null || BetterCapture(candidate, bestCapture.Value)) bestCapture = candidate;
                }
            }

            var chosen = bestCapture ?? bestBridge;
            if (chosen is null) break;
            var c = chosen.Value;
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
        var status = placements.Count == 0
            ? "No reachable Expedition targets"
            : remaining == 0
                ? "All reachable weighted targets covered"
                : $"Best route within {chargeBudget} remaining explosive{(chargeBudget == 1 ? "" : "s")}";
        return Finish(placements.ToArray(), targets.Count, totalCaptured, totalWeight, started, status);
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
