using System.Linq;
using NumVec2 = System.Numerics.Vector2;
using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Navigation;
using POE2Radar.Overlay.Web;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    /// <summary>Background loop: expensive entity/terrain/nav reads at the configured world cadence.</summary>
    private void WorldReaderLoop()
    {
        while (!_shutdown)
        {
            try { RunWorldTick(); }
            catch (Exception ex) { Console.Error.WriteLine($"World tick: {ex.Message}"); }
            var hz = _settings.LowImpactMode && !IsGameFocused() && !_renderingEnabled
                ? _settings.InactiveRefreshHz
                : _settings.WorldRefreshHz;
            Thread.Sleep(PerformanceCadence.SleepMillisecondsForHz(PerformanceCadence.ClampHz(hz, 1, 60)));
        }
    }

    private void RunWorldTick()
    {
        var worldStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var inGame = _worldLive.TryResolve(out var inGameState, out var areaInstance, out var localPlayer);
        if (!inGame)
        {
            _snapshot = WorldSnapshot.Empty;
            _state = RadarState.Empty;
            _worldTickMs = 0;
            if (_atlasOpen) CloseAtlasSession();
            return;
        }

        if (areaInstance != _lastAreaInstance)
        {
            _worldTerrain = null;
            if (_lastAreaInstance != 0 && (_atlasOpen || _atlasMarksPublish.Count > 0))
                CloseAtlasSession();
            _lastAreaInstance = areaInstance;
        }
        _areaInstanceForApi = areaInstance;
        _inGameStateForApi = inGameState;
        _areaHash = _worldLive.AreaHash(areaInstance);
        var areaHash = _areaHash;
        var areaLevel = _worldLive.AreaLevel(areaInstance);
        _areaCode = _worldLive.AreaCode(areaInstance);
        var charLevel = _worldLive.PlayerLevel(localPlayer);
        _charLevel = charLevel;
        var player = _worldLive.PlayerGrid(localPlayer) ?? NumVec2.Zero;

        if (!_atlasWarmupDone)
        {
            _atlas.GetAtlasPanelDiag(inGameState);
            _atlasWarmupDone = true;
        }

        var atlasStart = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateAtlas(inGameState);
        _atlasUpdateMs = (float)System.Diagnostics.Stopwatch.GetElapsedTime(atlasStart).TotalMilliseconds;

        var entities = new List<Poe2Live.EntityDot>();
        IReadOnlyList<Poe2Live.Landmark> landmarks = Array.Empty<Poe2Live.Landmark>();
        var hpSpecs = new List<HpBarSpec>();

        if (!_atlasOpen)
        {
            _worldTerrain ??= _worldLive.Terrain(areaInstance);
            var (entityDots, awakeCount, sleepingCount) = _worldLive.Entities(areaInstance);
            entities = entityDots;
            _worldDoorOverrides = BuildDoorOverrides(entities);
            if (_settings.ShowPerfStats && (awakeCount != _lastLoggedAwakeCount || sleepingCount != _lastLoggedSleepingCount))
            {
                Console.WriteLine($"[entities] awake={awakeCount} sleeping={sleepingCount} total={entities.Count}");
                _lastLoggedAwakeCount = awakeCount;
                _lastLoggedSleepingCount = sleepingCount;
            }
            if (localPlayer != 0)
                entities = entities.Where(e => e.Address != localPlayer).ToList();
            if (_hidden.Count > 0)
                entities = entities.Where(e =>
                    !_hidden.IsHidden(e.Metadata)
                    || IsExpeditionPlannerEntity(e.Metadata)).ToList();
            if (_settings.EntityDrawRadiusGrid > 0)
                entities = entities.Where(e =>
                    NumVec2.Distance(e.Grid, player) <= _settings.EntityDrawRadiusGrid
                    || IsExpeditionPlannerEntity(e.Metadata)).ToList();

            if (_landmarkPatterns.Generation != _landmarkGen)
            {
                _landmarkGen = _landmarkPatterns.Generation;
                _worldLive.InvalidateLandmarks();
                _mainLandmarksInvalid = true;
            }
            if (_ruleEngine.Generation != _displayRulesGen)
            {
                _displayRulesGen = _ruleEngine.Generation;
                _worldLive.InvalidateLandmarks();
                _mainLandmarksInvalid = true;
            }
            if (_landmarkStore.Generation != _landmarkStoreGen)
            {
                _landmarkStoreGen = _landmarkStore.Generation;
                _worldLive.InvalidateLandmarks();
                _mainLandmarksInvalid = true;
            }
            if (_settings.LandmarkClusterGap != _appliedClusterGap)
            {
                _appliedClusterGap = _settings.LandmarkClusterGap;
                _worldLive.LandmarkClusterGap = _appliedClusterGap;
                _worldLive.InvalidateLandmarks();
                _mainLandmarksInvalid = true;
            }

            landmarks = _worldLive.Landmarks(areaInstance);
            var serverIcons = _worldLive.ServerMinimapIcons(areaInstance);
            _entities = entities;
            _landmarks = landmarks;
            _serverIcons = serverIcons;
            hpSpecs = BuildHpSpecsFrom(entities);

            _navTargets = BuildNavTargets(player);
            RefreshTargetSnapshots(_navTargets);

            if (areaInstance != _navTargetsArea)
            {
                _navTargetsArea = areaInstance;
                OnAreaChanged(player);
            }

            AutoSelectNavigable(player);
            WorldMaintainRoutes(player, landmarks, entities);
        }
        else
        {
            _entities = entities;
            _landmarks = landmarks;
        }

        var selected = SnapshotSelection();
        var legend = BuildLegend(selected, player);
        var entityArray = entities.ToArray();
        var landmarkArray = landmarks as Poe2Live.Landmark[] ?? landmarks.ToArray();
        var navTargetArray = _navTargets.ToArray();
        var hpSpecArray = hpSpecs.ToArray();
        var legendArray = legend.ToArray();
        var selectedIds = selected.ToArray();
        var mapEntities = BuildMapEntityRenderItems(entityArray, _areaCode);
        var mapLandmarks = BuildMapLandmarkRenderItems(landmarkArray, entityArray, _areaCode);
        var serverIconArray = _serverIcons as Poe2Live.ServerMinimapIcon[] ?? _serverIcons.ToArray();
        var mapServerIcons = BuildMapServerIconRenderItems(serverIconArray, entityArray, landmarkArray, _areaCode);

        _snapshot = new WorldSnapshot(
            true, areaHash, areaLevel, _areaCode, charLevel,
            entityArray, landmarkArray, _worldTerrain, navTargetArray, hpSpecArray, legendArray,
            selectedIds, _renderPathSnapshot, mapEntities, mapLandmarks, serverIconArray, mapServerIcons);

        _state = new RadarState(inGame, areaHash, areaLevel, false, 0, player, entityArray, landmarkArray,
            _hpPct, _manaPct, _esPct, _autoFlask, _flaskNote, _areaCode, _charName, charLevel, _perfSnapshot,
            MapDiag: _mapDiag);

        _worldTickMs = (float)System.Diagnostics.Stopwatch.GetElapsedTime(worldStart).TotalMilliseconds;
    }

    private void WorldMaintainRoutes(
        NumVec2 player,
        IReadOnlyList<Poe2Live.Landmark> landmarks,
        IReadOnlyList<Poe2Live.EntityDot> entities)
    {
        var selected = SnapshotSelection();
        lock (_trackerGate)
        {
            ReconcileTrackers(selected, landmarks, entities, player);

            foreach (var id in selected)
            {
                if (!_trackers.TryGetValue(id, out var tracker)) continue;
                if (TryResolveTargetInfo(id, _navTargets, landmarks, entities, _areaHash, out var info))
                {
                if (!tracker.ReplanInFlight && tracker.ShouldReplan(player, info.Grid))
                    EnqueueReplan(id, tracker, info, player);
                }
                else
                {
                    tracker.MarkTargetUnavailable();
                }
            }

            DrainReplannerResults(player);
        }
    }

    private void DrainReplannerResults(NumVec2 player)
    {
        if (!_replanner.TryDrainResults(out var results)) return;
        foreach (var r in results)
        {
            if (!_trackers.TryGetValue(r.TargetId, out var tracker)) continue;
            tracker.ApplyResult(r, player);
            if (_settings.ShowPerfStats)
            {
                var status = r.Status == RoutePlanStatus.Planned
                    ? $"{r.Waypoints.Count} waypoints{(r.IsPartial ? " (partial)" : "")}"
                    : $"{r.Status} ({r.FailureReason})";
                Console.WriteLine($"replan: {TargetLabel(r.TargetId)} = {status}, {r.PlanMilliseconds:F1} ms");
                if (r.Status != RoutePlanStatus.Planned)
                {
                    var targetSleeping = IsTargetSleeping(r.TargetId);
                    var dist = NumVec2.Distance(new NumVec2(r.Goal.x, r.Goal.y), player);
                    Console.WriteLine($"  diag: targetSleeping={targetSleeping} playerGrid=({player.X:F1},{player.Y:F1}) start=({r.StartCell.x},{r.StartCell.y}) goal=({r.GoalCell.x},{r.GoalCell.y}) dist={dist:F1} candidates={r.CandidateCount} terrain={r.TerrainWidth}x{r.TerrainHeight} startSnapped={r.StartSnapped}");
                }
            }
        }
    }

    private bool IsTargetSleeping(string id)
    {
        if (!id.StartsWith("e:", StringComparison.Ordinal) || !uint.TryParse(id.AsSpan(2), out var entityId))
            return false;
        try
        {
            // Take a snapshot reference to avoid races if the world thread swaps _entities mid-loop.
            var snapshot = _entities;
            foreach (var e in snapshot)
                if (e.Id == entityId)
                    return e.IsSleeping;
        }
        catch (InvalidOperationException)
        {
            // Collection was modified mid-enumeration; ignore this diagnostic tick.
        }
        return false;
    }

    private void BuildRenderPaths(NumVec2 player, WorldSnapshot snap)
    {
        _renderPaths.Clear();
        var selected = SnapshotSelection();
        lock (_trackerGate)
        {
            DrainReplannerResults(player);

            for (var i = 0; i < selected.Count; i++)
            {
                var id = selected[i];
                if (!_trackers.TryGetValue(id, out var tracker)) continue;
                tracker.Maintain(player);
                var pts = tracker.CurrentPoints;
                var fullPts = tracker.Status == RoutePlanStatus.Planned
                    ? tracker.AllWaypoints.ToArray()
                    : Array.Empty<(int, int)>();
                (int x, int y)? liveGoal = null;
                if (tracker.Status == RoutePlanStatus.Planned && tracker.ResolvedGoal is { } resolved)
                {
                    liveGoal = resolved;
                }

                var slot = Math.Min(i, MaxSelectedTargets - 1);
                var drawable = NavigationPathBuilder.HasDrawablePath(pts, liveGoal, tracker.Status);
                var drawPts = drawable
                    ? NavigationPathBuilder.BuildForwardPath(player, pts, liveGoal)
                    : new List<(int x, int y)>();
                var pathDist = drawable ? SumPathGridDistance(drawPts.Count > 0 ? drawPts : pts) : -1f;
                if (TryResolveTargetInfo(id, snap.NavTargets, snap.Landmarks, snap.Entities, snap.AreaHash, out var info))
                {
                    var dist = NumVec2.Distance(info.Grid, player);
                    _renderPaths.Add(new SelectedPath(slot, id, info.Label, info.IsEntity, info.Status, dist, pathDist,
                        drawable ? pts.ToArray() : Array.Empty<(int, int)>(),
                        drawable ? fullPts : Array.Empty<(int, int)>(),
                        drawable ? liveGoal : null,
                        tracker.Status,
                        tracker.ResolvedGoal,
                        tracker.FailureReason,
                        tracker.LastPlanMilliseconds));
                }
                else
                {
                    _renderPaths.Add(new SelectedPath(slot, id, id, id.StartsWith("e:", StringComparison.Ordinal),
                        NavTargetStatus.NoPath, -1f, pathDist,
                        Array.Empty<(int, int)>(),
                        Array.Empty<(int, int)>(),
                        null,
                        RoutePlanStatus.TargetUnavailable,
                        null,
                        "target unavailable",
                        tracker.LastPlanMilliseconds));
                }
            }
        }
        _renderPathSnapshot = _renderPaths.Count > 0 ? _renderPaths.ToArray() : Array.Empty<SelectedPath>();
    }

    private MapEntityRenderItem[] BuildMapEntityRenderItems(Poe2Live.EntityDot[] entities, string areaCode)
    {
        if (entities.Length == 0) return Array.Empty<MapEntityRenderItem>();

        var items = new List<MapEntityRenderItem>(entities.Length);
        foreach (var e in entities)
        {
            var rule = _ruleEngine.Resolve(e, areaCode, _settings.ImportantOnly, entities);
            if (rule is { Hide: true }) continue;
            if (ChestDisplayPolicy.ShouldHideNonRarePlainChest(e)) continue;
            if (_settings.ImportantOnly && EntityImportanceHelper.IsTrash(EntityImportanceHelper.Classify(e, _settings.Styles, rule)))
                continue;

            string label;
            SpriteIconRef? sprite;
            string? shape;
            float size;
            string color;
            float opacity;
            if (rule is not null)
            {
                (sprite, shape, size, color, opacity) = (rule.Sprite, rule.Shape, rule.Size, rule.Color, rule.Opacity);
                label = EntityDisplayHelper.FormatEntityLabel(e, rule, entities, areaCode);
            }
            else if (ChestDisplayPolicy.IsPlainChestEntity(e) && e.Rarity == Poe2Live.Rarity.Rare)
            {
                var st = _settings.Styles.ChestRare;
                (sprite, shape, size, color, opacity) = (st.Sprite, st.Shape, st.Size, st.Color, st.Opacity);
                label = "Rare";
            }
            else
            {
                (sprite, shape, size, color, opacity) = EntityDrawStyleFor(e, _settings.Styles);
                label = EntityDisplayHelper.FormatEntityLabel(e, rule, entities, areaCode);
            }
            items.Add(new MapEntityRenderItem(
                "e:" + e.Id,
                e.Grid,
                e.TerrainHeight,
                size,
                PackColor(color, opacity),
                sprite?.Clone(),
                shape,
                label));
        }

        return items.Count > 0 ? items.ToArray() : Array.Empty<MapEntityRenderItem>();
    }

    private MapEntityRenderItem[] BuildMapServerIconRenderItems(
        Poe2Live.ServerMinimapIcon[] icons,
        Poe2Live.EntityDot[] entities,
        Poe2Live.Landmark[] landmarks,
        string areaCode)
    {
        if (icons.Length == 0) return Array.Empty<MapEntityRenderItem>();

        var items = new List<MapEntityRenderItem>(icons.Length);
        foreach (var icon in icons)
        {
            var e = ToEntityDot(icon);
            var rule = _ruleEngine.Resolve(e, areaCode, _settings.ImportantOnly, entities);
            // Server icons are opt-in: hide unless an enabled display rule explicitly matches.
            if (rule is null or { Hide: true }) continue;

            // De-duplicate against nearby live entities/landmarks that already mark the same spot.
            if (IsNearExistingTarget(icon.Grid, entities, landmarks)) continue;

            var (sprite, shape, size, color, opacity) = (rule.Sprite, rule.Shape, rule.Size, rule.Color, rule.Opacity);

            var label = rule is { Name: var rn } && ChestDisplayPolicy.IsPlainChestRule(rn)
                ? ""
                : rule?.Label is { Length: > 0 } ruleLabel ? ruleLabel : icon.Name;
            items.Add(new MapEntityRenderItem(
                "s:" + icon.Key,
                icon.Grid,
                0f,
                size,
                PackColor(color, opacity),
                sprite?.Clone(),
                shape,
                label));
        }

        return items.Count > 0 ? items.ToArray() : Array.Empty<MapEntityRenderItem>();
    }

    private static bool IsNearExistingTarget(NumVec2 grid, Poe2Live.EntityDot[] entities, Poe2Live.Landmark[] landmarks, float radius = 5f)
    {
        var r2 = radius * radius;
        foreach (var e in entities)
            if (NumVec2.DistanceSquared(e.Grid, grid) <= r2)
                return true;
        foreach (var lm in landmarks)
            if (NumVec2.DistanceSquared(lm.Center, grid) <= r2)
                return true;
        return false;
    }

    private MapLandmarkRenderItem[] BuildMapLandmarkRenderItems(
        Poe2Live.Landmark[] landmarks,
        Poe2Live.EntityDot[] entities,
        string areaCode)
    {
        if (landmarks.Length == 0) return Array.Empty<MapLandmarkRenderItem>();

        var items = new List<MapLandmarkRenderItem>(landmarks.Length);
        foreach (var lm in landmarks)
        {
            var tr = _displayRules.ResolveTile(lm.Path, requireMatch: false);
            if (tr is { Hide: true }) continue;

            var color = tr?.Color ?? _settings.Styles.Landmark.Color;
            var opacity = tr?.Opacity ?? _settings.Styles.Landmark.Opacity;
            var lmCurated = tr?.Label is { Length: > 0 } tileLbl
                ? tileLbl
                : (_settings.UseCuratedLandmarks ? lm.CuratedName : null);
            var label = EntityDisplayHelper.FormatLandmarkLabel(lm.Path, lmCurated, lm.Name, entities, areaCode);
            if (label.Length > 0
                && !EntityDisplayHelper.ShouldDrawBossLandmarkLabel(
                    lm.Path,
                    label,
                    lm.Center,
                    entities,
                    e => _ruleEngine.Resolve(e, areaCode, _settings.ImportantOnly, entities),
                    areaCode))
            {
                label = "";
            }

            items.Add(new MapLandmarkRenderItem(
                "t:" + lm.Key,
                lm.Center,
                tr?.Size ?? _settings.Styles.Landmark.Size,
                PackColor(color, opacity),
                (tr?.Sprite ?? _settings.Styles.Landmark.Sprite)?.Clone(),
                tr?.Shape ?? _settings.Styles.Landmark.Shape,
                label));
        }

        return items.Count > 0 ? items.ToArray() : Array.Empty<MapLandmarkRenderItem>();
    }

    private static uint PackColor(string hex, float opacity)
    {
        byte r = 255, g = 255, b = 255;
        if (!string.IsNullOrWhiteSpace(hex))
        {
            var h = hex.Trim();
            if (h.StartsWith("#")) h = h[1..];
            if (h.Length == 6 && int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v))
            {
                r = (byte)((v >> 16) & 0xFF);
                g = (byte)((v >> 8) & 0xFF);
                b = (byte)(v & 0xFF);
            }
        }

        var a = (byte)Math.Clamp((int)MathF.Round(Math.Clamp(opacity, 0f, 1f) * 255f), 0, 255);
        return (uint)(a << 24 | b << 16 | g << 8 | r);
    }

    private static (SpriteIconRef? Sprite, string? Shape, float Size, string Color, float Opacity) EntityDrawStyleFor(
        Poe2Live.EntityDot e,
        RadarStyles styles)
    {
        var s = e.Category switch
        {
            Poe2Live.EntityCategory.Chest when e.Rarity == Poe2Live.Rarity.Unique => styles.ChestUnique,
            Poe2Live.EntityCategory.Chest when e.Rarity == Poe2Live.Rarity.Rare => styles.ChestRare,
            Poe2Live.EntityCategory.Chest => styles.ChestRare,
            Poe2Live.EntityCategory.Npc => styles.Npc,
            Poe2Live.EntityCategory.Transition => styles.Transition,
            Poe2Live.EntityCategory.Monster when e.Rarity == Poe2Live.Rarity.Unique => styles.MonsterUnique,
            Poe2Live.EntityCategory.Monster when e.Rarity == Poe2Live.Rarity.Rare => styles.MonsterRare,
            Poe2Live.EntityCategory.Monster when e.Rarity == Poe2Live.Rarity.Magic => styles.MonsterMagic,
            Poe2Live.EntityCategory.Monster => styles.MonsterNormal,
            _ => styles.Poi,
        };
        return (s.Sprite, s.Shape, s.Size, s.Color, s.Opacity);
    }

    private void ReconcileTrackers(
        List<string> selected,
        IReadOnlyList<Poe2Live.Landmark> landmarks,
        IReadOnlyList<Poe2Live.EntityDot> entities,
        NumVec2 player)
    {
        if (_trackers.Count > 0)
        {
            var live = new HashSet<string>(selected);
            var stale = _trackers.Keys.Where(k => !live.Contains(k)).ToList();
            foreach (var id in stale) _trackers.Remove(id);
        }

        foreach (var id in selected)
        {
            if (_trackers.ContainsKey(id)) continue;
            var tracker = new RouteTracker();
            _trackers[id] = tracker;
            if (TryResolveTargetInfo(id, _navTargets, landmarks, entities, _areaHash, out var info))
                EnqueueReplan(id, tracker, info, player);
            else
                tracker.MarkTargetUnavailable();
        }
    }

    private static PathCell[] BuildDoorOverrides(IReadOnlyList<Poe2Live.EntityDot> entities)
    {
        if (entities.Count == 0) return Array.Empty<PathCell>();

        var cells = new HashSet<PathCell>();
        foreach (var e in entities)
        {
            if (!LooksLikeDoorOverride(e.Metadata)) continue;
            var cx = (int)MathF.Round(e.Grid.X);
            var cy = (int)MathF.Round(e.Grid.Y);
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                    cells.Add(new PathCell(cx + dx, cy + dy));
        }

        return cells.Count > 0 ? cells.ToArray() : Array.Empty<PathCell>();
    }

    private static bool LooksLikeDoorOverride(string metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return false;
        return metadata.Contains("Door", StringComparison.OrdinalIgnoreCase)
            || metadata.Contains("Blockage", StringComparison.OrdinalIgnoreCase)
            || metadata.Contains("Barricade", StringComparison.OrdinalIgnoreCase);
    }
}
