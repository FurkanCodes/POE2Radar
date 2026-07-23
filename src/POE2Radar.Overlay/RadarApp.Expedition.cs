using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Runecraft;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private const string ExpeditionDetonator = "Metadata/MiscellaneousObjects/Expedition/ExpeditionDetonator";
    private const string ExpeditionExplosive = "Metadata/MiscellaneousObjects/Expedition/ExpeditionExplosive";
    private const string ExpeditionMarker = "Metadata/MiscellaneousObjects/Expedition/ExpeditionMarker";
    private const string ExpeditionRelic = "Metadata/MiscellaneousObjects/Expedition/ExpeditionRelic";

    private static readonly IReadOnlyDictionary<string, float> ExpeditionDefaultGrandRewardWeights =
        new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["RewardChestCurrencyRare"] = 40f,
            ["RewardChestArmour"] = 10f,
            ["RewardChestWeapons"] = 10f,
            ["RewardChestCurrency"] = 25f,
        };

    private sealed record CachedExpeditionEntity(
        uint Id,
        nint Address,
        NumVec2 Grid,
        POE2Radar.Core.Game.Vector3 World,
        float TerrainHeight,
        string Metadata,
        Poe2Live.ExpeditionEntityInfo Info);

    private readonly Dictionary<uint, CachedExpeditionEntity> _expeditionEntities = new();
    private nint _expeditionArea;
    private DateTime _nextExpeditionScanUtc = DateTime.MinValue;
    private Task<ExpeditionPlan>? _expeditionPlanTask;
    private CancellationTokenSource? _expeditionPlanCts;
    private int _expeditionPlanTaskFingerprint;
    private int _expeditionPlanTaskBasePlaced;
    private NumVec2 _expeditionPlanTaskStartGrid;
    private float _expeditionPlanTaskStartHeight;
    private int _expeditionFingerprint;
    private int _expeditionPlanBasePlaced;
    private NumVec2 _expeditionPlanStartGrid;
    private float _expeditionPlanStartHeight;
    private bool _expeditionHasLockedPlan;
    private bool _expeditionManualRunRequested;
    private ExpeditionPlannerView _expeditionView = ExpeditionPlannerView.Empty;

    internal static bool IsExpeditionPlannerEntity(string metadata)
        => metadata.Equals(ExpeditionDetonator, StringComparison.OrdinalIgnoreCase)
           || metadata.Equals(ExpeditionExplosive, StringComparison.OrdinalIgnoreCase)
           || metadata.Equals(ExpeditionMarker, StringComparison.OrdinalIgnoreCase)
           || metadata.Equals(ExpeditionRelic, StringComparison.OrdinalIgnoreCase)
           || metadata.StartsWith("Metadata/Chests/LeaguesExpedition", StringComparison.OrdinalIgnoreCase)
           || metadata.Contains("Expedition2Encounter", StringComparison.OrdinalIgnoreCase)
           || metadata.Contains("Sentinel/SentinelRandomEncounterObject", StringComparison.OrdinalIgnoreCase)
           || metadata.Contains("DevourerSegment", StringComparison.OrdinalIgnoreCase);

    private void RefreshExpeditionPlanner(LiveFrameState live, WorldSnapshot snap)
    {
        if (!_settings.Runecraft.ShowExpeditionPlanner || !live.InGame)
        {
            ClearExpeditionPlanner();
            return;
        }
        if (live.AreaInstance != _expeditionArea)
        {
            CancelExpeditionPlan();
            _expeditionArea = live.AreaInstance;
            _expeditionEntities.Clear();
            _expeditionFingerprint = 0;
            _expeditionPlanBasePlaced = 0;
            _expeditionPlanStartGrid = NumVec2.Zero;
            _expeditionPlanStartHeight = 0f;
            _expeditionHasLockedPlan = false;
            _expeditionManualRunRequested = false;
            _expeditionView = ExpeditionPlannerView.Empty;
        }

        if (DateTime.UtcNow < _nextExpeditionScanUtc)
            return;
        _nextExpeditionScanUtc = DateTime.UtcNow.AddMilliseconds(500);

        foreach (var e in snap.Entities)
        {
            if (!IsExpeditionPlannerEntity(e.Metadata)) continue;
            if (_expeditionEntities.TryGetValue(e.Id, out var old) && old.Address == e.Address)
            {
                _expeditionEntities[e.Id] = old with
                {
                    Grid = e.Grid,
                    World = e.World,
                    TerrainHeight = e.TerrainHeight,
                    Info = e.Metadata.Contains("DevourerSegment", StringComparison.OrdinalIgnoreCase)
                        ? _live.ReadExpeditionEntityInfo(e.Address)
                        : old.Info,
                };
                continue;
            }
            _expeditionEntities[e.Id] = new CachedExpeditionEntity(
                e.Id, e.Address, e.Grid, e.World, e.TerrainHeight, e.Metadata,
                _live.ReadExpeditionEntityInfo(e.Address));
        }

        var detonator = _expeditionEntities.Values
            .FirstOrDefault(e => e.Metadata.Equals(ExpeditionDetonator, StringComparison.OrdinalIgnoreCase));
        if (detonator is null || _live.IsExpeditionDetonated(detonator.Address))
        {
            ClearExpeditionPlanner();
            return;
        }

        var controller = _live.ReadExpeditionController(live.AreaInstance);
        var total = controller.Resolved
            ? controller.Total
            : Math.Clamp(_settings.Runecraft.ExpeditionManualCharges, 1, 64);
        var cachedCharges = _expeditionEntities.Values
            .Where(e => e.Metadata.Equals(ExpeditionExplosive, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Id)
            .ToArray();
        var placed = controller.Resolved
            ? controller.Placed
            : Math.Min(total, cachedCharges.Length);
        var grand = total >= 10;
        var mapMods = _live.ReadExpeditionMapModifiers(live.AreaInstance);
        var baseDistance = grand ? 108f : 90f;
        var baseRadius = grand ? 37f : 30f;
        var effectiveDistance = baseDistance * (1f + mapMods.PlacementRangePercent / 100f);
        var effectiveRadius = baseRadius * (1f + mapMods.BlastRadiusPercent / 100f);

        var targets = BuildExpeditionTargets(grand);
        var fingerprint = ExpeditionFingerprint(
            snap.AreaHash, total, mapMods, detonator.Grid, targets,
            snap.Terrain?.Width ?? 0, snap.Terrain?.Height ?? 0);
        ApplyCompletedExpeditionPlan();
        var inputsChanged = fingerprint != _expeditionFingerprint;

        if (snap.Terrain is null)
        {
            var waiting = ViewToPlan(_expeditionView) with { Status = "Waiting for walkable terrain" };
            _expeditionView = BuildExpeditionView(
                detonator, controller, total, placed, mapMods, effectiveDistance, effectiveRadius, grand,
                targets.Count, waiting, planning: false, _expeditionPlanBasePlaced,
                _expeditionHasLockedPlan ? _expeditionPlanStartGrid : detonator.Grid,
                _expeditionHasLockedPlan ? _expeditionPlanStartHeight : detonator.TerrainHeight);
            return;
        }

        if (ShouldStartExpeditionPlan(_expeditionManualRunRequested, _expeditionPlanTask is not null))
        {
            _expeditionManualRunRequested = false;
            _expeditionFingerprint = fingerprint;
            _expeditionPlanCts = new CancellationTokenSource();
            _expeditionPlanTaskFingerprint = fingerprint;
            _expeditionPlanTaskBasePlaced = 0;
            _expeditionPlanTaskStartGrid = detonator.Grid;
            _expeditionPlanTaskStartHeight = detonator.TerrainHeight;
            var terrain = snap.Terrain;
            var targetCopy = targets.ToArray();
            var doors = BuildExpeditionDoorOverrides(terrain, snap.DoorOverrides, _expeditionEntities.Values);
            var token = _expeditionPlanCts.Token;
            _expeditionPlanTask = Task.Run(
                () => ExpeditionPlanner.Build(
                    terrain, detonator.Grid, targetCopy, total, effectiveDistance, effectiveRadius, token,
                    doorOverrides: doors,
                    markerCoverageMode: !grand,
                    minMarkers: grand ? Math.Max(1, _settings.Runecraft.ExpeditionMinMarkersPerSpareCharge) : 1,
                    startTerrainHeight: detonator.TerrainHeight),
                token);
        }

        var current = ViewToPlan(_expeditionView);
        if (_expeditionPlanTask is not null)
            current = current with { Status = _expeditionHasLockedPlan
                ? "Calculating new route; current plan remains locked"
                : "Calculating route…" };
        else if (_expeditionHasLockedPlan && inputsChanged)
            current = current with { Status = "Settings or targets changed · press Run* to rebuild the route" };
        else if (!_expeditionHasLockedPlan)
            current = current with { Status = "Press Run to build the complete explosive chain" };
        _expeditionView = BuildExpeditionView(
            detonator, controller, total, placed, mapMods, effectiveDistance, effectiveRadius, grand,
            targets.Count, current, planning: _expeditionPlanTask is not null, _expeditionPlanBasePlaced,
            _expeditionHasLockedPlan ? _expeditionPlanStartGrid : detonator.Grid,
            _expeditionHasLockedPlan ? _expeditionPlanStartHeight : detonator.TerrainHeight);
    }

    private List<ExpeditionTarget> BuildExpeditionTargets(bool grand)
    {
        var s = _settings.Runecraft;
        var preferred = new HashSet<string>(s.ExpeditionPreferredRelicMods ?? [], StringComparer.OrdinalIgnoreCase);
        var dangerous = new HashSet<string>(s.ExpeditionDangerousRelicMods ?? [], StringComparer.OrdinalIgnoreCase);
        var markerOffsets = _expeditionEntities.Values
            .Where(e => e.Metadata.Equals(ExpeditionMarker, StringComparison.OrdinalIgnoreCase))
            .Select(e => (int)MathF.Round(e.World.Z - e.TerrainHeight))
            .ToArray();
        var markerBaseline = markerOffsets.Length == 0
            ? 0
            : markerOffsets.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        var hasLogbookMarker = _expeditionEntities.Values
            .Where(e => e.Metadata.Equals(ExpeditionMarker, StringComparison.OrdinalIgnoreCase))
            .Any(e => markerBaseline - (int)MathF.Round(e.World.Z - e.TerrainHeight) >= 45);
        var monolithValues = _runecraftMonoliths.ToDictionary(v => v.DeviceAddress, v => (float)v.Best);

        var result = new List<ExpeditionTarget>(_expeditionEntities.Count);
        foreach (var e in _expeditionEntities.Values)
        {
            float weight;
            ExpeditionTargetKind kind;
            string label;
            bool primary = false;

            if (e.Metadata.Equals(ExpeditionMarker, StringComparison.OrdinalIgnoreCase))
            {
                kind = ExpeditionTargetKind.RewardMarker;
                if (grand)
                    (weight, label) = GrandMarkerWeight(e.Info.IconName, s.ExpeditionRewardWeights);
                else
                    (weight, label) = NormalMarkerWeight(markerBaseline, e, s);
            }
            else if (e.Metadata.Equals(ExpeditionRelic, StringComparison.OrdinalIgnoreCase))
            {
                kind = ExpeditionTargetKind.Remnant;
                var good = e.Info.ModIds.Count(preferred.Contains);
                var bad = e.Info.ModIds.Count(dangerous.Contains);
                weight = good * s.ExpeditionPreferredRelicWeight - bad * s.ExpeditionDangerousRelicPenalty;
                label = bad > 0 ? $"Remnant ({good} good, {bad} danger)" : $"Remnant ({good} preferred)";
                primary = good > 0;
            }
            else if (e.Metadata.Contains("Expedition2Encounter", StringComparison.OrdinalIgnoreCase))
            {
                kind = ExpeditionTargetKind.Monolith;
                monolithValues.TryGetValue(e.Address, out var value);
                if (value <= 0 || value < s.ExpeditionMonolithMinExalted) continue;
                weight = value;
                label = value > 0 ? $"Runeshape {value:F1} ex" : "Runeshape monolith";
                primary = true;
            }
            else if (e.Metadata.Contains("Sentinel/SentinelRandomEncounterObject", StringComparison.OrdinalIgnoreCase))
            {
                kind = ExpeditionTargetKind.Sentinel;
                if (grand || !hasLogbookMarker) continue;
                weight = s.ExpeditionLogbookMarkerWeight * 1.5f + 1f;
                label = "Kalguur Sentinel";
                primary = true;
            }
            else continue;

            if (MathF.Abs(weight) < 0.001f) continue;
            result.Add(new ExpeditionTarget(e.Id, e.Grid, e.TerrainHeight, weight, kind, label, primary));
        }
        return result;
    }

    private static (float Weight, string Label) NormalMarkerWeight(
        int baseline, CachedExpeditionEntity marker, Config.RunecraftSettings s)
    {
        var pole = (int)MathF.Round(marker.World.Z - marker.TerrainHeight);
        var delta = baseline - pole;
        if (delta >= 45) return (s.ExpeditionLogbookMarkerWeight, "Logbook marker");
        if (delta >= 24) return (s.ExpeditionGoldMarkerWeight, "Gold marker");
        if (delta >= 19) return (s.ExpeditionMagicMarkerWeight, "Magic marker");
        if (delta >= 4) return (s.ExpeditionWhiteMarkerWeight, "White marker");
        return (s.ExpeditionTinyMarkerWeight, "Small marker");
    }

    private static (float Weight, string Label) GrandMarkerWeight(
        string iconName,
        IReadOnlyDictionary<string, float>? overrides)
    {
        var icon = iconName ?? "";
        return (
            ExpeditionGrandRewardWeight(icon, overrides),
            icon.Length == 0 ? "Reward marker" : FriendlyIconName(icon));
    }

    internal static float ExpeditionGrandRewardWeight(
        string icon,
        IReadOnlyDictionary<string, float>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(icon, out var configured))
            return configured;
        return ExpeditionDefaultGrandRewardWeights.TryGetValue(icon, out var value) ? value : 1f;
    }

    private static string FriendlyIconName(string icon)
    {
        const string prefix = "RewardChest";
        return icon.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? icon[prefix.Length..] + " reward"
            : icon;
    }

    private static PathCell[] BuildExpeditionDoorOverrides(
        Poe2Live.TerrainData terrain,
        IReadOnlyCollection<PathCell> existing,
        IEnumerable<CachedExpeditionEntity> entities)
    {
        var result = existing.ToHashSet();
        var gates = entities
            .Where(e => e.Metadata.Contains("DevourerSegment", StringComparison.OrdinalIgnoreCase)
                        && e.Info.IsBlocked.HasValue)
            .ToArray();

        // GameHelper first treats every TriggerableBlockage as traversable, then removes the
        // footprint of the Devourer gates that are currently closed.
        foreach (var gate in gates)
        {
            var x = (int)MathF.Round(gate.Grid.X);
            var y = (int)MathF.Round(gate.Grid.Y);
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
                result.Add(new PathCell(x + dx, y + dy));
        }
        foreach (var gate in gates.Where(g => g.Info.IsBlocked == true))
        {
            var x = (int)MathF.Round(gate.Grid.X);
            var y = (int)MathF.Round(gate.Grid.Y);
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
                result.Remove(new PathCell(x + dx, y + dy));
            foreach (var cell in FloodExpeditionGateFootprint(terrain, x, y, 1_200, 36, 7))
                result.Remove(cell);
        }
        return result.ToArray();
    }

    private static IReadOnlyList<PathCell> FloodExpeditionGateFootprint(
        Poe2Live.TerrainData terrain,
        int centerX,
        int centerY,
        int maxCells,
        int maxRadius,
        int diskRadius)
    {
        var startX = -1;
        var startY = -1;
        for (var radius = 0; radius <= 4 && startX < 0; radius++)
        for (var dy = -radius; dy <= radius && startX < 0; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var x = centerX + dx;
            var y = centerY + dy;
            if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height) continue;
            if (terrain.Walkable[y * terrain.Width + x] != 0) continue;
            startX = x;
            startY = y;
            break;
        }
        if (startX < 0) return [];

        var seen = new HashSet<PathCell> { new(startX, startY) };
        var stack = new Stack<PathCell>();
        stack.Push(new PathCell(startX, startY));
        var footprint = new List<PathCell>();
        while (stack.TryPop(out var cell))
        {
            if (Math.Abs(cell.X - centerX) > maxRadius || Math.Abs(cell.Y - centerY) > maxRadius) continue;
            footprint.Add(cell);
            if (footprint.Count > maxCells)
            {
                var disk = new List<PathCell>();
                for (var dy = -diskRadius; dy <= diskRadius; dy++)
                for (var dx = -diskRadius; dx <= diskRadius; dx++)
                    if (dx * dx + dy * dy <= diskRadius * diskRadius
                        && centerX + dx >= 0 && centerY + dy >= 0)
                        disk.Add(new PathCell(centerX + dx, centerY + dy));
                return disk;
            }
            TryPush(cell.X + 1, cell.Y);
            TryPush(cell.X - 1, cell.Y);
            TryPush(cell.X, cell.Y + 1);
            TryPush(cell.X, cell.Y - 1);
        }
        return footprint;

        void TryPush(int x, int y)
        {
            if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height) return;
            var next = new PathCell(x, y);
            if (seen.Contains(next) || terrain.Walkable[y * terrain.Width + x] != 0) return;
            seen.Add(next);
            stack.Push(next);
        }
    }

    private static int ExpeditionFingerprint(
        uint areaHash,
        int total,
        Poe2Live.ExpeditionMapModifiersRead mapMods,
        NumVec2 start,
        IReadOnlyList<ExpeditionTarget> targets,
        int terrainWidth,
        int terrainHeight)
    {
        var h = new HashCode();
        h.Add(areaHash); h.Add(total);
        h.Add(mapMods.PlacementRangePercent); h.Add(mapMods.BlastRadiusPercent);
        h.Add((int)start.X); h.Add((int)start.Y); h.Add(terrainWidth); h.Add(terrainHeight);
        foreach (var t in targets.OrderBy(t => t.Id))
        {
            h.Add(t.Id); h.Add((int)t.Grid.X); h.Add((int)t.Grid.Y); h.Add(t.Weight); h.Add(t.Primary);
        }
        return h.ToHashCode();
    }

    internal static bool ShouldStartExpeditionPlan(
        bool manualRunRequested,
        bool taskRunning)
        => manualRunRequested && !taskRunning;

    internal static int ExpeditionNextRouteIndex(int placed, int routeLength)
    {
        var next = Math.Max(0, placed);
        return next < routeLength ? next : -1;
    }

    private void RequestExpeditionPlanFromUi()
        => _expeditionManualRunRequested = true;

    private void ApplyCompletedExpeditionPlan()
    {
        if (_expeditionPlanTask is null || !_expeditionPlanTask.IsCompleted) return;
        var task = _expeditionPlanTask;
        var completedFingerprint = _expeditionPlanTaskFingerprint;
        _expeditionPlanTask = null;
        _expeditionPlanCts?.Dispose();
        _expeditionPlanCts = null;
        if (task.IsCompletedSuccessfully)
        {
            var p = task.Result;
            _expeditionFingerprint = completedFingerprint;
            _expeditionHasLockedPlan = true;
            _expeditionPlanBasePlaced = _expeditionPlanTaskBasePlaced;
            _expeditionPlanStartGrid = _expeditionPlanTaskStartGrid;
            _expeditionPlanStartHeight = _expeditionPlanTaskStartHeight;
            _expeditionView = _expeditionView with
            {
                Planning = false,
                TargetCount = p.TargetCount,
                CapturedCount = p.CapturedCount,
                CapturedWeight = p.CapturedWeight,
                ComputeMilliseconds = p.ComputeMilliseconds,
                Status = p.Status,
                Route = p.Placements.Select(x => new ExpeditionPlacementView(
                    x.Grid, x.TerrainHeight, x.CapturedWeight, x.CapturedCount, x.Bridge, x.Label)).ToArray(),
            };
        }
        else if (!task.IsCompletedSuccessfully)
        {
            _expeditionView = _expeditionView with
            {
                Planning = false,
                Status = _expeditionHasLockedPlan
                    ? "Recalculation interrupted; current plan remains locked"
                    : "Planner interrupted; retrying…",
            };
        }
        else if (_expeditionHasLockedPlan)
        {
            _expeditionView = _expeditionView with
            {
                Planning = false,
                Status = "Encounter changed during calculation; press Run to retry",
            };
        }
    }

    private static ExpeditionPlan ViewToPlan(ExpeditionPlannerView view)
        => new(
            view.Route.Select(x => new ExpeditionPlacement(
                x.Grid, x.TerrainHeight, x.CapturedWeight, x.CapturedCount, x.Bridge, x.Label)).ToArray(),
            view.TargetCount, view.CapturedCount, view.CapturedWeight, view.ComputeMilliseconds, view.Status);

    private static ExpeditionPlannerView BuildExpeditionView(
        CachedExpeditionEntity detonator,
        Poe2Live.ExpeditionControllerRead controller,
        int total,
        int placed,
        Poe2Live.ExpeditionMapModifiersRead mapMods,
        float distance,
        float radius,
        bool grand,
        int targetCount,
        ExpeditionPlan plan,
        bool planning,
        int planBasePlaced,
        NumVec2 routeStartGrid,
        float routeStartHeight)
        => new(
            true,
            planning,
            controller.Resolved,
            controller.Resolved ? "encounter state" : "manual fallback",
            total,
            placed,
            planBasePlaced,
            mapMods.PlacementRangePercent,
            mapMods.BlastRadiusPercent,
            distance,
            radius,
            grand ? "Grand" : "Normal",
            targetCount,
            plan.CapturedCount,
            plan.CapturedWeight,
            plan.ComputeMilliseconds,
            plan.Status,
            routeStartGrid,
            routeStartHeight,
            plan.Placements.Select(x => new ExpeditionPlacementView(
                x.Grid, x.TerrainHeight, x.CapturedWeight, x.CapturedCount, x.Bridge, x.Label)).ToArray());

    private void CancelExpeditionPlan()
    {
        _expeditionPlanCts?.Cancel();
        _expeditionPlanCts?.Dispose();
        _expeditionPlanCts = null;
        _expeditionPlanTask = null;
        _expeditionPlanTaskFingerprint = 0;
        _expeditionPlanTaskBasePlaced = 0;
        _expeditionPlanTaskStartGrid = NumVec2.Zero;
        _expeditionPlanTaskStartHeight = 0f;
    }

    private void ClearExpeditionPlanner()
    {
        CancelExpeditionPlan();
        _expeditionFingerprint = 0;
        _expeditionPlanBasePlaced = 0;
        _expeditionPlanStartGrid = NumVec2.Zero;
        _expeditionPlanStartHeight = 0f;
        _expeditionHasLockedPlan = false;
        _expeditionManualRunRequested = false;
        _expeditionView = ExpeditionPlannerView.Empty;
    }
}
