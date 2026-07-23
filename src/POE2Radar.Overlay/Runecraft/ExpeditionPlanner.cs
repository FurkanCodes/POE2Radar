// Planner logic ported from the Expedition planner embedded in MordWraith/Gamehelper's
// RunecraftHelper.dll. The original plugin is GPL-3.0; see RunecraftHelper.GPLv3.LICENSE.txt.
using System.Collections.Concurrent;
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
    string Label,
    bool Sentinel = false);

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
/// Read-only port of GameHelper RunecraftHelper's strict-spine Expedition route planner. It plans
/// the entire explosive chain once from the detonator. Placed explosives advance the displayed
/// route index; they do not cause this solver to rebuild the chain.
/// </summary>
public static class ExpeditionPlanner
{
    private const float Epsilon = 0.001f;
    private const float SpineSampleStep = 4f;

    private sealed class Inputs
    {
        public required Poe2Live.TerrainData Terrain;
        public HashSet<(int X, int Y)>? Doors;
        public NumVec2 Detonator;
        public float DetonatorHeight;
        public int Budget;
        public float EffectiveDistance;
        public float EffectiveRadius;
        public float StepDistance;
        public bool MarkerCoverageMode;
        public int MinMarkers = 2;
        public required CancellationToken CancellationToken;
        public required long Started;
        public required TimeSpan ComputeBudget;
        public readonly List<NumVec2> Positions = [];
        public readonly List<float> Heights = [];
        public readonly List<double> Weights = [];
        public readonly List<bool> Primary = [];
        public readonly List<bool> Sentinel = [];
        public readonly List<string> Labels = [];
        public readonly PathCache Cache = new();
        public bool BudgetReached;
    }

    private readonly record struct RoutePoint(
        NumVec2 Grid,
        float Height,
        double Marginal,
        int Captured,
        bool Bridge,
        string Label,
        bool Sentinel);

    private sealed class PathCache
    {
        private readonly ConcurrentDictionary<object, int> _doorIds =
            new(ReferenceEqualityComparer.Instance);
        private readonly ConcurrentDictionary<int, int[]?> _components = new();
        private readonly object _componentsLock = new();
        private int _nextDoorId;

        public readonly ConcurrentDictionary<(int, int, int, int, int), float> Length = new();
        public readonly ConcurrentDictionary<(int, int, int, int, int), List<NumVec2>?> Path = new();
        public readonly ConcurrentDictionary<(int, int, int, int, int), float> ReachLength = new();

        public int DoorId(HashSet<(int X, int Y)>? doors)
            => doors is null ? 0 : _doorIds.GetOrAdd(doors, _ => Interlocked.Increment(ref _nextDoorId));

        public int[]? Components(
            Poe2Live.TerrainData terrain,
            HashSet<(int X, int Y)>? doors,
            int doorId)
        {
            lock (_componentsLock)
            {
                if (_components.TryGetValue(doorId, out var cached)) return cached;
                int[]? labels = null;
                var cells = (long)terrain.Width * terrain.Height;
                if (terrain.Width > 0 && terrain.Height > 0 && cells <= 16_000_000)
                {
                    labels = Enumerable.Repeat(-1, checked((int)cells)).ToArray();
                    var stack = new Stack<int>();
                    var nextLabel = 0;
                    for (var y = 0; y < terrain.Height; y++)
                    for (var x = 0; x < terrain.Width; x++)
                    {
                        var index = y * terrain.Width + x;
                        if (labels[index] != -1) continue;
                        if (!GameHelperExpeditionLineWalker.IsWalkable(terrain, x, y, doors))
                        {
                            labels[index] = -2;
                            continue;
                        }
                        var label = labels[index] = nextLabel++;
                        stack.Push(index);
                        while (stack.TryPop(out var cell))
                        {
                            var cx = cell % terrain.Width;
                            var cy = cell / terrain.Width;
                            for (var dy = -1; dy <= 1; dy++)
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                var nx = cx + dx;
                                var ny = cy + dy;
                                if ((uint)nx >= (uint)terrain.Width || (uint)ny >= (uint)terrain.Height) continue;
                                var next = ny * terrain.Width + nx;
                                if (labels[next] != -1) continue;
                                if (!GameHelperExpeditionLineWalker.IsWalkable(terrain, nx, ny, doors))
                                {
                                    labels[next] = -2;
                                    continue;
                                }
                                labels[next] = label;
                                stack.Push(next);
                            }
                        }
                    }
                }
                _components[doorId] = labels;
                return labels;
            }
        }
    }

    public static ExpeditionPlan Build(
        Poe2Live.TerrainData terrain,
        NumVec2 start,
        IReadOnlyList<ExpeditionTarget> targets,
        int chargeBudget,
        float placementDistance,
        float blastRadius,
        CancellationToken cancellationToken = default,
        TimeSpan? computeBudget = null,
        IReadOnlyCollection<PathCell>? doorOverrides = null,
        bool markerCoverageMode = false,
        int minMarkers = 2,
        float startTerrainHeight = 0f)
    {
        var started = Stopwatch.GetTimestamp();
        var budget = computeBudget.GetValueOrDefault(TimeSpan.FromSeconds(15));
        if (budget <= TimeSpan.Zero) budget = TimeSpan.FromSeconds(15);
        if (chargeBudget <= 0)
            return Finish([], targets.Count, 0, 0f, started, "No explosives available");
        if (terrain.Width <= 0 || terrain.Height <= 0
            || terrain.Walkable.Length < terrain.Width * terrain.Height)
            return Finish([], targets.Count, 0, 0f, started, "Waiting for walkable terrain");

        var input = new Inputs
        {
            Terrain = terrain,
            Doors = BuildDoorSet(doorOverrides),
            Detonator = start,
            DetonatorHeight = startTerrainHeight,
            Budget = chargeBudget,
            EffectiveDistance = placementDistance,
            EffectiveRadius = blastRadius,
            StepDistance = Math.Max(1f, placementDistance - 1f),
            MarkerCoverageMode = markerCoverageMode,
            MinMarkers = Math.Max(1, minMarkers),
            CancellationToken = cancellationToken,
            Started = started,
            ComputeBudget = budget,
        };

        foreach (var target in targets)
        {
            if (target.Weight <= 0f) continue;
            input.Positions.Add(target.Grid);
            input.Heights.Add(target.TerrainHeight);
            input.Weights.Add(target.Weight);
            input.Primary.Add(target.Primary);
            input.Sentinel.Add(target.Kind == ExpeditionTargetKind.Sentinel);
            input.Labels.Add(target.Label);
        }
        if (!input.Primary.Any(static primary => primary))
            for (var i = 0; i < input.Primary.Count; i++) input.Primary[i] = true;
        if (input.Positions.Count == 0)
            return Finish([], 0, 0, 0f, started, "No weighted Expedition targets found");

        var route = ComputeRoute(input, out var covered, out var weight, out var phase);
        var placements = route.Select(p => new ExpeditionPlacement(
            p.Grid, p.Height, (float)p.Marginal, p.Captured, p.Bridge, p.Label, p.Sentinel)).ToArray();
        var status = input.BudgetReached
            ? placements.Length == 0
                ? "Planning budget reached before a route was found"
                : $"{phase} · planning budget reached; showing partial route"
            : phase;
        return Finish(placements, input.Positions.Count, covered, (float)weight, started, status);
    }

    private static HashSet<(int X, int Y)>? BuildDoorSet(IReadOnlyCollection<PathCell>? overrides)
    {
        if (overrides is null || overrides.Count == 0) return null;
        return overrides.Select(c => (c.X, c.Y)).ToHashSet();
    }

    private static List<RoutePoint> ComputeRoute(
        Inputs input,
        out int covered,
        out double weight,
        out string phase)
    {
        var targetCount = input.Positions.Count;
        var primaryIndices = Enumerable.Range(0, targetCount).Where(i => input.Primary[i]).ToList();
        if (input.Budget <= 0 || targetCount == 0 || primaryIndices.Count == 0)
        {
            covered = 0; weight = 0; phase = "No valuable route anchors"; return [];
        }

        var ordered = TourOrder(input, primaryIndices.Select(i => input.Positions[i]).ToList());
        for (var i = primaryIndices.Count - 1; i >= 0; i--)
        {
            if (!input.Sentinel[primaryIndices[i]]) continue;
            var sentinel = input.Positions[primaryIndices[i]];
            if (ordered.Count == 0 || ordered[0] != sentinel)
            {
                ordered.Remove(sentinel);
                ordered.Insert(0, sentinel);
            }
        }

        BuildSpinePolyline(input, ordered, out var points, out var heights, out var anchorIndices);
        var captured = new bool[targetCount];
        var radiusSquared = input.EffectiveRadius * input.EffectiveRadius;
        var route = PlaceAlongSpine(
            input, points, heights, anchorIndices, ordered, radiusSquared, captured, out weight);
        var primaryCovered = primaryIndices.Count(i => captured[i]);
        weight += SpareOptimize(input, route, captured, radiusSquared);

        covered = 0;
        var actuallyCovered = new bool[targetCount];
        foreach (var point in route)
        for (var i = 0; i < targetCount; i++)
        {
            if (actuallyCovered[i] || DistanceSquared(point.Grid, input.Positions[i]) > radiusSquared) continue;
            actuallyCovered[i] = true;
            covered++;
        }
        phase = $"spine: anchors {primaryCovered}/{primaryIndices.Count}, {route.Count} charges, " +
                $"{input.Budget - route.Count} spare";
        return route;
    }

    private static void BuildSpinePolyline(
        Inputs input,
        List<NumVec2> ordered,
        out List<NumVec2> points,
        out List<float> heights,
        out List<int> anchorIndices)
    {
        points = [input.Detonator];
        heights = [input.DetonatorHeight];
        anchorIndices = [];
        var previous = input.Detonator;
        var previousHeight = input.DetonatorHeight;
        foreach (var anchor in ordered)
        {
            CheckBudget(input);
            var anchorHeight = previousHeight;
            var targetIndex = input.Positions.IndexOf(anchor);
            if (targetIndex >= 0) anchorHeight = input.Heights[targetIndex];
            var found = GameHelperExpeditionLineWalker.IsLineClear(
                    input.Terrain, previous, anchor, input.Doors)
                ? new List<NumVec2> { previous, anchor }
                : GameHelperExpeditionPathfinder.FindPath(
                    input.Terrain, previous, anchor, input.Doors,
                    cancellationToken: input.CancellationToken);
            var path = found is { Count: >= 2 } ? found : [previous, anchor];
            var totalLength = PolylineLength(path);
            var traversed = 0f;
            for (var i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];
                var length = NumVec2.Distance(from, to);
                var samples = Math.Max(1, (int)MathF.Ceiling(length / SpineSampleStep));
                for (var sample = 1; sample <= samples; sample++)
                {
                    var amount = (float)sample / samples;
                    traversed += length / samples;
                    var along = totalLength > 0f ? traversed / totalLength : 1f;
                    points.Add(NumVec2.Lerp(from, to, amount));
                    heights.Add(previousHeight + (anchorHeight - previousHeight) * along);
                }
            }
            anchorIndices.Add(points.Count - 1);
            previous = anchor;
            previousHeight = anchorHeight;
        }
    }

    private static List<RoutePoint> PlaceAlongSpine(
        Inputs input,
        List<NumVec2> points,
        List<float> heights,
        List<int> anchorIndices,
        List<NumVec2> ordered,
        float radiusSquared,
        bool[] captured,
        out double weight)
    {
        weight = 0;
        var route = new List<RoutePoint>();
        if (points.Count == 0 || ordered.Count == 0) return route;
        var targetIndexByAnchor = ordered.Select(anchor => input.Positions.IndexOf(anchor)).ToArray();
        var lookAhead = (int)MathF.Ceiling(2f * input.EffectiveRadius / SpineSampleStep) + 4;
        var previous = input.Detonator;
        var spineIndex = 0;
        var anchorIndex = 0;
        var guard = 0;
        while (anchorIndex < ordered.Count && route.Count < input.Budget
               && ++guard <= points.Count + ordered.Count + 8)
        {
            CheckBudget(input);
            var targetIndex = targetIndexByAnchor[anchorIndex];
            if (targetIndex >= 0 && captured[targetIndex]) { anchorIndex++; continue; }
            var targetSpineIndex = Math.Min(anchorIndices[anchorIndex], points.Count - 1);
            var anchor = targetIndex >= 0 ? input.Positions[targetIndex] : points[targetSpineIndex];
            var bestSpineIndex = -1;
            var bestReach = 0f;
            var bestGain = double.NegativeInfinity;
            NumVec2 bestPoint = default;
            var bestHeight = 0f;

            for (var candidateIndex = Math.Min(points.Count - 1, targetSpineIndex + lookAhead);
                 candidateIndex > spineIndex;
                 candidateIndex--)
            {
                if (DistanceSquared(points[candidateIndex], anchor) > radiusSquared) continue;
                var gain = CoverGain(input, captured, points[candidateIndex], radiusSquared, out _)
                           + candidateIndex * 1e-6;
                if (gain <= bestGain) continue;
                var reach = Reach(input, previous, points[candidateIndex], input.EffectiveDistance);
                if (reach < 0f) continue;
                bestGain = gain;
                bestSpineIndex = candidateIndex;
                bestReach = reach;
                bestPoint = points[candidateIndex];
                bestHeight = heights[candidateIndex];
            }

            var nearbyDistance = 2f * input.EffectiveRadius;
            for (var i = 0; i < input.Positions.Count; i++)
            {
                if (captured[i] || input.Primary[i]) continue;
                var distance = NumVec2.Distance(anchor, input.Positions[i]);
                if (distance < Epsilon || distance > nearbyDistance) continue;
                var direction = (input.Positions[i] - anchor) / distance;
                var candidate = anchor + direction * Math.Min(distance, input.EffectiveRadius * 0.999f);
                if (DistanceSquared(candidate, anchor) > radiusSquared
                    || DistanceSquared(candidate, input.Positions[i]) > radiusSquared
                    || !IsWalkable(input, candidate))
                    continue;
                var gain = CoverGain(input, captured, candidate, radiusSquared, out _);
                if (gain <= bestGain) continue;
                var reach = Reach(input, previous, candidate, input.EffectiveDistance);
                if (reach < 0f) continue;
                bestGain = gain;
                bestReach = reach;
                bestPoint = candidate;
                bestSpineIndex = targetSpineIndex;
                bestHeight = targetIndex >= 0 ? input.Heights[targetIndex] : heights[targetSpineIndex];
            }

            if (bestSpineIndex >= 0)
            {
                var sentinel = targetIndex >= 0 && input.Sentinel[targetIndex];
                weight += Commit(input, captured, route, bestPoint, bestHeight, radiusSquared,
                    bridge: false, sentinel);
                previous = bestPoint;
                spineIndex = bestSpineIndex;
                while (anchorIndex < ordered.Count
                       && (targetIndexByAnchor[anchorIndex] < 0
                           || captured[targetIndexByAnchor[anchorIndex]]))
                    anchorIndex++;
                continue;
            }

            var bridgeIndex = -1;
            var bridgeReach = 0f;
            for (var candidateIndex = targetSpineIndex; candidateIndex > spineIndex; candidateIndex--)
            {
                var reach = Reach(input, previous, points[candidateIndex], input.EffectiveDistance);
                if (reach < 0f) continue;
                bridgeIndex = candidateIndex;
                bridgeReach = reach;
                break;
            }
            if (bridgeIndex < 0) { anchorIndex++; continue; }
            var backwardLook = (int)MathF.Ceiling(input.EffectiveRadius / SpineSampleStep);
            var selected = bridgeIndex;
            var mostCovered = CountUncovered(input.Positions, captured, points[bridgeIndex], radiusSquared);
            for (var i = bridgeIndex - 1; i >= Math.Max(spineIndex + 1, bridgeIndex - backwardLook); i--)
            {
                var count = CountUncovered(input.Positions, captured, points[i], radiusSquared);
                if (count <= mostCovered) continue;
                mostCovered = count;
                selected = i;
            }
            var selectedReach = selected == bridgeIndex
                ? bridgeReach
                : Reach(input, previous, points[selected], input.EffectiveDistance);
            weight += Commit(input, captured, route, points[selected], heights[selected], radiusSquared,
                bridge: true, sentinel: false, selectedReach);
            previous = points[selected];
            spineIndex = selected;
        }
        return route;
    }

    private static double SpareOptimize(
        Inputs input,
        List<RoutePoint> route,
        bool[] captured,
        float radiusSquared)
    {
        var originalSpare = input.Budget - route.Count;
        if (originalSpare <= 0) return 0;
        var addedWeight = 0d;
        var guard = 0;
        while (input.Budget - route.Count > 0 && guard++ < originalSpare + 8)
        {
            CheckBudget(input);
            var available = input.Budget - route.Count;
            var minEdge = 0;
            for (var i = 0; i < route.Count; i++) if (route[i].Sentinel) minEdge = i;

            NumVec2 bestCluster = default;
            var bestPerCharge = 0d;
            var bestHeight = 0f;
            var bestPath = float.MaxValue;
            var bestEdge = -1;
            for (var i = 0; i < input.Positions.Count; i++)
            {
                if (captured[i]) continue;
                var count = 0;
                var gain = 0d;
                for (var j = 0; j < input.Positions.Count; j++)
                {
                    if (captured[j] || DistanceSquared(input.Positions[i], input.Positions[j]) > radiusSquared) continue;
                    count++;
                    gain += input.Weights[j];
                }
                if (count < input.MinMarkers || gain <= 0d) continue;
                var edge = BestDetourEdge(input, route, input.Positions[i], minEdge,
                    out var outboundPath, out _, out var hops);
                if (edge < 0 || hops > available) continue;
                var perCharge = gain / hops;
                if (perCharge <= bestPerCharge + 1e-9
                    && (perCharge <= bestPerCharge - 1e-9 || outboundPath >= bestPath))
                    continue;
                bestPerCharge = perCharge;
                bestCluster = input.Positions[i];
                bestHeight = input.Heights[i];
                bestPath = outboundPath;
                bestEdge = edge;
            }
            if (bestPerCharge <= 0d || bestEdge < 0) break;

            var current = route[bestEdge].Grid;
            var currentHeight = route[bestEdge].Height;
            var terminalSpur = bestEdge >= route.Count - 1;
            var beforeCaptured = (bool[])captured.Clone();
            var beforeWeight = addedWeight;
            var detour = new List<RoutePoint>();
            var failed = false;
            var harvested = false;
            while (input.Budget - (route.Count + detour.Count) > 0)
            {
                CheckBudget(input);
                if (Reach(input, current, bestCluster, input.EffectiveDistance) >= 0f)
                {
                    var point = MaxCoverPoint(input, captured, current, bestCluster, radiusSquared,
                        input.EffectiveDistance, out var reach);
                    addedWeight += Commit(input, captured, detour, point, bestHeight, radiusSquared,
                        bridge: false, sentinel: false, reach);
                    current = point;
                    harvested = true;
                    break;
                }
                if (!StepToward(input, current, bestCluster, input.StepDistance, out var step))
                { failed = true; break; }

                var fullDistance = FullPath(input, current, bestCluster);
                var estimatedHops = Math.Max(1, (int)MathF.Ceiling(fullDistance / input.EffectiveDistance));
                var selected = step;
                var selectedGain = CoverGain(input, captured, step, radiusSquared, out _);
                var selectedReach = Reach(input, current, step, input.EffectiveDistance);
                for (var i = 0; i < input.Positions.Count; i++)
                {
                    if (captured[i]) continue;
                    var distance = NumVec2.Distance(current, input.Positions[i]);
                    if (distance - input.EffectiveRadius > input.EffectiveDistance) continue;
                    var maxLerp = distance > 1f ? Math.Min(1f, input.EffectiveRadius / distance) : 1f;
                    for (var amount = 0f; amount <= maxLerp + Epsilon; amount += 0.1f)
                    {
                        var candidate = NumVec2.Lerp(input.Positions[i], current, amount);
                        if (!IsWalkable(input, candidate)) continue;
                        var reach = Reach(input, current, candidate, input.EffectiveDistance);
                        if (reach < 0f) continue;
                        var gain = CoverGain(input, captured, candidate, radiusSquared, out _);
                        if (gain > selectedGain)
                        {
                            var remaining = FullPath(input, candidate, bestCluster);
                            if (remaining >= 0f
                                && 1 + (int)MathF.Ceiling(remaining / input.EffectiveDistance) <= estimatedHops)
                            {
                                selected = candidate;
                                selectedGain = gain;
                                selectedReach = reach;
                            }
                        }
                        break;
                    }
                }
                if (selectedReach < 0f) { failed = true; break; }
                var height = NearestUncapturedHeight(input, captured, selected, currentHeight);
                addedWeight += Commit(input, captured, detour, selected, height, radiusSquared,
                    bridge: true, sentinel: false, selectedReach);
                current = selected;
            }
            if (!harvested) failed = true;

            if (!failed && !terminalSpur)
            {
                var rejoin = route[bestEdge + 1].Grid;
                while (Reach(input, current, rejoin, input.EffectiveDistance) < 0f
                       && input.Budget - (route.Count + detour.Count) > 0)
                {
                    if (!StepToward(input, current, rejoin, input.StepDistance, out var step))
                    { failed = true; break; }
                    var reach = Reach(input, current, step, input.EffectiveDistance);
                    if (reach < 0f) { failed = true; break; }
                    var height = NearestUncapturedHeight(input, captured, step, currentHeight);
                    addedWeight += Commit(input, captured, detour, step, height, radiusSquared,
                        bridge: true, sentinel: false, reach);
                    current = step;
                }
                if (!failed && Reach(input, current, rejoin, input.EffectiveDistance) < 0f) failed = true;
            }
            if (failed)
            {
                Array.Copy(beforeCaptured, captured, captured.Length);
                addedWeight = beforeWeight;
                break;
            }
            if (detour.Count > 0) route.InsertRange(bestEdge + 1, detour);
        }
        return addedWeight;
    }

    private static int BestDetourEdge(
        Inputs input,
        List<RoutePoint> route,
        NumVec2 candidate,
        int minEdge,
        out float outboundPath,
        out float reconnectPath,
        out int totalHops)
    {
        outboundPath = -1f; reconnectPath = 0f; totalHops = 0;
        if (route.Count == 0) return -1;
        var nearestIndices = Enumerable.Range(minEdge, route.Count - minEdge)
            .OrderBy(i => NumVec2.Distance(route[i].Grid, candidate)).Take(5);
        var bestEdge = -1;
        var bestHops = int.MaxValue;
        var bestOutbound = -1f;
        var bestReconnect = 0f;
        foreach (var edge in nearestIndices)
        {
            var outbound = FullPath(input, route[edge].Grid, candidate);
            if (outbound < 0f) continue;
            var outboundHops = Math.Max(1, (int)MathF.Ceiling(outbound / input.EffectiveDistance));
            var reconnect = 0f;
            var reconnectHops = 0;
            if (edge != route.Count - 1)
            {
                reconnect = FullPath(input, candidate, route[edge + 1].Grid);
                if (reconnect < 0f) continue;
                reconnectHops = Math.Max(0, (int)MathF.Ceiling(reconnect / input.EffectiveDistance) - 1);
            }
            var hops = outboundHops + reconnectHops;
            if (hops >= bestHops) continue;
            bestHops = hops;
            bestEdge = edge;
            bestOutbound = outbound;
            bestReconnect = reconnect;
        }
        if (bestEdge < 0) return -1;
        outboundPath = bestOutbound;
        reconnectPath = bestReconnect;
        totalHops = Math.Max(1, bestHops);
        return bestEdge;
    }

    private static List<NumVec2> TourOrder(Inputs input, List<NumVec2> stops)
    {
        if (stops.Count <= 2) return [.. stops];
        var count = stops.Count;
        var distances = new float[count, count];
        var fromDetonator = new float[count];
        Parallel.For(0, count, new ParallelOptions { CancellationToken = input.CancellationToken }, i =>
        {
            fromDetonator[i] = Distance(input, input.Detonator, stops[i]);
            for (var j = i + 1; j < count; j++)
            {
                var distance = Distance(input, stops[i], stops[j]);
                distances[i, j] = distance;
                distances[j, i] = distance;
            }
        });

        var visited = new bool[count];
        var order = new List<int>(count);
        var previous = -1;
        for (var step = 0; step < count; step++)
        {
            CheckBudget(input);
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                if (visited[i]) continue;
                var distance = previous < 0 ? fromDetonator[i] : distances[previous, i];
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            if (best < 0) break;
            visited[best] = true;
            order.Add(best);
            previous = best;
        }

        var improved = true;
        var passes = 0;
        while (improved && passes++ < 60)
        {
            improved = false;
            for (var i = 0; i < order.Count - 1; i++)
            for (var k = i + 1; k < order.Count; k++)
            {
                var oldFirst = i == 0 ? fromDetonator[order[i]] : distances[order[i - 1], order[i]];
                var newFirst = i == 0 ? fromDetonator[order[k]] : distances[order[i - 1], order[k]];
                var hasNext = k + 1 < order.Count;
                var oldLast = hasNext ? distances[order[k], order[k + 1]] : 0f;
                var newLast = hasNext ? distances[order[i], order[k + 1]] : 0f;
                if (newFirst + newLast + Epsilon >= oldFirst + oldLast) continue;
                order.Reverse(i, k - i + 1);
                improved = true;
            }
        }
        return order.Select(i => stops[i]).ToList();

        float Distance(Inputs inp, NumVec2 a, NumVec2 b)
        {
            var path = FullPath(inp, a, b);
            return path >= 0f ? path : NumVec2.Distance(a, b) * 4f;
        }
    }

    private static RoutePoint MakeRoutePoint(
        Inputs input,
        bool[] captured,
        NumVec2 point,
        float height,
        float radiusSquared,
        bool bridge,
        bool sentinel,
        out double marginal)
    {
        marginal = 0d;
        var count = 0;
        var labels = new List<string>();
        for (var i = 0; i < input.Positions.Count; i++)
        {
            if (captured[i] || DistanceSquared(point, input.Positions[i]) > radiusSquared) continue;
            captured[i] = true;
            count++;
            marginal += input.Weights[i];
            if (!string.IsNullOrWhiteSpace(input.Labels[i])) labels.Add(input.Labels[i]);
        }
        var label = bridge && count == 0
            ? "Bridge toward anchor"
            : labels.Count == 0
                ? $"Cover {count} target{(count == 1 ? "" : "s")}"
                : string.Join(" + ", labels.Distinct(StringComparer.Ordinal).Take(3));
        return new RoutePoint(point, height, marginal, count, bridge && count == 0, label, sentinel);
    }

    private static double Commit(
        Inputs input,
        bool[] captured,
        List<RoutePoint> route,
        NumVec2 point,
        float height,
        float radiusSquared,
        bool bridge,
        bool sentinel,
        float reach = 0f)
    {
        _ = reach; // Retained to mirror the source planner's reach-aware placement calls.
        var routePoint = MakeRoutePoint(
            input, captured, point, height, radiusSquared, bridge, sentinel, out var marginal);
        route.Add(routePoint);
        return marginal;
    }

    private static NumVec2 MaxCoverPoint(
        Inputs input,
        bool[] captured,
        NumVec2 node,
        NumVec2 initial,
        float radiusSquared,
        float effectiveDistance,
        out float reach)
    {
        var result = initial;
        var bestGain = CoverGain(input, captured, initial, radiusSquared, out _);
        reach = Reach(input, node, initial, effectiveDistance);
        var nearby = new List<int>();
        var totalWeight = 0d;
        var weightedX = 0d;
        var weightedY = 0d;
        for (var i = 0; i < input.Positions.Count; i++)
        {
            if (captured[i] || DistanceSquared(initial, input.Positions[i]) > 4f * radiusSquared) continue;
            nearby.Add(i);
            totalWeight += input.Weights[i];
            weightedX += input.Positions[i].X * input.Weights[i];
            weightedY += input.Positions[i].Y * input.Weights[i];
        }
        var candidates = new List<NumVec2>();
        for (var i = 0; i < nearby.Count; i++)
        {
            candidates.Add(NumVec2.Lerp(initial, input.Positions[nearby[i]], 0.5f));
            for (var j = i + 1; j < nearby.Count; j++)
                candidates.Add(NumVec2.Lerp(input.Positions[nearby[i]], input.Positions[nearby[j]], 0.5f));
        }
        if (totalWeight > 0d)
            candidates.Add(new NumVec2((float)(weightedX / totalWeight), (float)(weightedY / totalWeight)));
        foreach (var candidate in candidates)
        {
            var gain = CoverGain(input, captured, candidate, radiusSquared, out _);
            if (gain <= bestGain) continue;
            var candidateReach = Reach(input, node, candidate, effectiveDistance);
            if (candidateReach < 0f) continue;
            result = candidate;
            bestGain = gain;
            reach = candidateReach;
        }
        return result;
    }

    private static double CoverGain(Inputs input, bool[] captured, NumVec2 point, float radiusSquared, out int count)
    {
        var gain = 0d;
        count = 0;
        for (var i = 0; i < input.Positions.Count; i++)
        {
            if (captured[i] || DistanceSquared(point, input.Positions[i]) > radiusSquared) continue;
            gain += input.Weights[i];
            count++;
        }
        return gain;
    }

    private static int CountUncovered(
        List<NumVec2> positions,
        bool[] captured,
        NumVec2 point,
        float radiusSquared)
    {
        var count = 0;
        for (var i = 0; i < positions.Count; i++)
            if (!captured[i] && DistanceSquared(point, positions[i]) <= radiusSquared) count++;
        return count;
    }

    private static float Reach(Inputs input, NumVec2 from, NumVec2 to, float maxDistance)
    {
        var key = PathKey(input, from, to);
        if (input.Cache.ReachLength.TryGetValue(key, out var cached)) return cached;
        if (GameHelperExpeditionLineWalker.IsLineClear(input.Terrain, from, to, input.Doors))
        {
            var direct = NumVec2.Distance(from, to);
            var result = direct <= maxDistance ? direct : -1f;
            input.Cache.ReachLength[key] = result;
            return result;
        }
        var path = GameHelperExpeditionPathfinder.FindPath(
            input.Terrain, from, to, input.Doors, maxCost: maxDistance * 1.5f,
            cancellationToken: input.CancellationToken);
        var length = path is { Count: >= 2 } ? PolylineLength(path) : -1f;
        if (length > maxDistance) length = -1f;
        input.Cache.ReachLength[key] = length;
        return length;
    }

    private static float FullPath(Inputs input, NumVec2 from, NumVec2 to)
    {
        var key = PathKey(input, from, to);
        if (input.Cache.Length.TryGetValue(key, out var cached)) return cached;

        var doorId = input.Cache.DoorId(input.Doors);
        var components = input.Cache.Components(input.Terrain, input.Doors, doorId);
        var x1 = (int)MathF.Round(from.X);
        var y1 = (int)MathF.Round(from.Y);
        var x2 = (int)MathF.Round(to.X);
        var y2 = (int)MathF.Round(to.Y);
        if (components is not null
            && (uint)x1 < (uint)input.Terrain.Width && (uint)y1 < (uint)input.Terrain.Height
            && (uint)x2 < (uint)input.Terrain.Width && (uint)y2 < (uint)input.Terrain.Height)
        {
            var first = components[y1 * input.Terrain.Width + x1];
            var second = components[y2 * input.Terrain.Width + x2];
            if (first >= 0 && second >= 0 && first != second)
            {
                input.Cache.Length[key] = -1f;
                return -1f;
            }
        }

        if (GameHelperExpeditionLineWalker.IsLineClear(input.Terrain, from, to, input.Doors))
        {
            var direct = NumVec2.Distance(from, to);
            input.Cache.Length[key] = direct;
            return direct;
        }

        var path = GameHelperExpeditionPathfinder.FindPath(
            input.Terrain, from, to, input.Doors, cancellationToken: input.CancellationToken);
        var length = path is { Count: >= 2 } ? PolylineLength(path) : -1f;
        input.Cache.Length[key] = length;
        return length;
    }

    private static bool StepToward(
        Inputs input,
        NumVec2 from,
        NumVec2 toward,
        float maxDistance,
        out NumVec2 step)
    {
        step = from;
        var key = PathKey(input, from, toward);
        if (GameHelperExpeditionLineWalker.IsLineClear(input.Terrain, from, toward, input.Doors))
        {
            var distance = NumVec2.Distance(from, toward);
            if (distance <= 1f) return false;
            step = NumVec2.Lerp(from, toward, Math.Min(1f, maxDistance / distance));
            return NumVec2.Distance(from, step) > 1f;
        }
        if (!input.Cache.Path.TryGetValue(key, out var path))
        {
            path = GameHelperExpeditionPathfinder.FindPath(
                input.Terrain, from, toward, input.Doors, cancellationToken: input.CancellationToken);
            input.Cache.Path[key] = path;
        }
        if (path is not { Count: >= 2 }) return false;
        var travelled = 0f;
        var lastSample = path[0];
        var lastWalkable = from;
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1];
            var b = path[i];
            var segment = NumVec2.Distance(a, b);
            var samples = Math.Max(1, (int)MathF.Ceiling(segment));
            for (var sample = 1; sample <= samples; sample++)
            {
                var point = NumVec2.Lerp(a, b, (float)sample / samples);
                var delta = NumVec2.Distance(lastSample, point);
                if (travelled + delta > maxDistance)
                {
                    var fraction = delta <= Epsilon ? 0f : (maxDistance - travelled) / delta;
                    var limit = NumVec2.Lerp(lastSample, point, fraction);
                    if (IsWalkable(input, limit)) lastWalkable = limit;
                    step = lastWalkable;
                    return NumVec2.Distance(from, step) > 1f;
                }
                travelled += delta;
                lastSample = point;
                if (IsWalkable(input, point)) lastWalkable = point;
            }
        }
        step = lastWalkable;
        return NumVec2.Distance(from, step) > 1f;
    }

    private static (int, int, int, int, int) PathKey(Inputs input, NumVec2 a, NumVec2 b)
        => (input.Cache.DoorId(input.Doors),
            (int)MathF.Round(a.X), (int)MathF.Round(a.Y),
            (int)MathF.Round(b.X), (int)MathF.Round(b.Y));

    private static bool IsWalkable(Inputs input, NumVec2 point)
        => GameHelperExpeditionLineWalker.IsWalkable(
            input.Terrain, (int)(point.X + 0.5f), (int)(point.Y + 0.5f), input.Doors);

    private static float NearestUncapturedHeight(
        Inputs input,
        bool[] captured,
        NumVec2 point,
        float fallback)
    {
        for (var i = 0; i < input.Positions.Count; i++)
            if (!captured[i] && DistanceSquared(point, input.Positions[i]) <= 4f) return input.Heights[i];
        return fallback;
    }

    private static float PolylineLength(IReadOnlyList<NumVec2> points)
    {
        var length = 0f;
        for (var i = 1; i < points.Count; i++) length += NumVec2.Distance(points[i - 1], points[i]);
        return length;
    }

    private static float DistanceSquared(NumVec2 a, NumVec2 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static void CheckBudget(Inputs input)
    {
        input.CancellationToken.ThrowIfCancellationRequested();
        if (Stopwatch.GetElapsedTime(input.Started) >= input.ComputeBudget)
            input.BudgetReached = true;
    }

    private static ExpeditionPlan Finish(
        ExpeditionPlacement[] placements,
        int targetCount,
        int capturedCount,
        float capturedWeight,
        long started,
        string status)
        => new(placements, targetCount, capturedCount, capturedWeight,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds, status);
}
