using NumVec2 = System.Numerics.Vector2;
using POE2Radar.Core.Pathfinding;

namespace POE2Radar.Overlay.Navigation;

/// <summary>
/// Per-target route maintenance for the draw-only navigation overlay. ONE per selected target id.
///
/// <para>Splits navigation work into two halves: a CHEAP per-tick <see cref="Maintain"/> that just
/// advances a cursor along the already-planned waypoints (no A*), and a <see cref="ShouldReplan"/>
/// predicate that fires a full background replan only on a meaningful trigger (off-path, walking the
/// wrong way, the goal moved, or staleness), debounced by a cooldown. The expensive A* runs on the
/// <see cref="BackgroundReplanner"/> worker; this tracker only ever reads its own waypoints. Owned by
/// the tick thread — single-threaded, no locking.</para>
/// </summary>
public sealed class RouteTracker
{
    // ── Trigger thresholds (grid cells / seconds). ──
    private const double ReplanCooldownSec   = 1.0;   // min spacing between replans for this target
    private const double StaleSec            = 8.0;   // force a refresh even if nothing else fired
    private const float  OffPathCells        = 18f;   // perpendicular distance that counts as "off the path"
    private const float  GoalMovedCells      = 8f;    // goal drift (entity targets move) that forces a replan
    private const int    ForwardWindow       = 12;    // # of waypoints ahead we scan for cursor/off-path
    private const double HeadingWindowSec    = 0.3;   // sliding window for the player's heading estimate
    private const float  HeadingMinCells     = 2f;    // ignore heading below this magnitude (standing still)
    private const float  NegativeProgressDot = -0.3f; // heading·toGoal below this = walking the wrong way
    private const float  SimpleReplanPlayerCells = 30f; // v1.3.0: replan after this much player travel

    /// <summary>Current smoothed waypoints (full path; <see cref="_cursor"/> marks how far we've walked).</summary>
    private List<(int x, int y)> _waypoints = new();
    private int _cursor;
    private DateTime _lastReplanUtc = DateTime.MinValue;
    private NumVec2 _lastGoal = new(float.MinValue, float.MinValue);
    private NumVec2 _lastReplanPlayerGrid = new(float.MinValue, float.MinValue);

    // Short heading history (recent player positions + capture times, ~HeadingWindowSec window).
    private readonly List<(NumVec2 pos, DateTime at)> _history = new();

    /// <summary>True while a background replan for this target is enqueued/running (set by the owner).</summary>
    public bool ReplanInFlight { get; set; }

    public RoutePlanStatus Status { get; private set; } = RoutePlanStatus.Unplanned;
    public string FailureReason { get; private set; } = "";
    public (int x, int y)? ResolvedGoal { get; private set; }
    public double LastPlanMilliseconds { get; private set; }

    /// <summary>Full smoothed waypoint list (cursor is an index into this list).</summary>
    public IReadOnlyList<(int x, int y)> AllWaypoints => _waypoints;

    /// <summary>Waypoints from the cursor onward — what the renderer draws for this target.</summary>
    public IReadOnlyList<(int x, int y)> CurrentPoints
    {
        get
        {
            if (_waypoints.Count == 0) return Array.Empty<(int x, int y)>();
            if (_cursor <= 0) return _waypoints.ToArray();
            return _waypoints.GetRange(_cursor, _waypoints.Count - _cursor);
        }
    }

    /// <summary>
    /// CHEAP per-tick maintenance: project the player onto the path and advance the cursor past
    /// waypoints already walked, scanning only a small forward window. Also records the player
    /// position into the heading history (dropping samples older than the heading window). No A*.
    /// </summary>
    public void Maintain(NumVec2 playerGrid)
    {
        PushHistory(playerGrid);

        if (_waypoints.Count == 0) return;

        // Find the nearest forward segment within the window and snap the cursor to its far end so
        // CurrentPoints starts near the player. We only ever move the cursor forward.
        var bestDist = float.MaxValue;
        var bestEnd = _cursor;
        var last = Math.Min(_waypoints.Count - 1, _cursor + ForwardWindow);
        for (var i = _cursor; i < last; i++)
        {
            var a = ToVec(_waypoints[i]);
            var b = ToVec(_waypoints[i + 1]);
            var d = PointSegmentDistance(playerGrid, a, b);
            if (d < bestDist) { bestDist = d; bestEnd = i; }
        }

        // Snap onto the chosen segment's start; if the player is essentially at/after the segment's
        // far endpoint, consume it too. Cursor only advances.
        if (bestEnd > _cursor) _cursor = bestEnd;
    }

    /// <summary>
    /// Should we kick off a full background replan? True when the cooldown has elapsed AND any
    /// trigger fires: off-path, negative progress (walking away), the goal moved, or staleness.
    /// When <paramref name="simpleMode"/> is true, uses v1.3.0-style triggers only.
    /// </summary>
    public bool ShouldReplan(NumVec2 playerGrid, NumVec2 currentGoalGrid, bool simpleMode = false)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastReplanUtc).TotalSeconds < ReplanCooldownSec) return false;

        if (_waypoints.Count == 0) return true;
        if (Status != RoutePlanStatus.Planned) return true;
        if (GoalMoved(currentGoalGrid)) return true;

        if (simpleMode)
            return PlayerMovedSinceReplan(playerGrid);

        if ((now - _lastReplanUtc).TotalSeconds > StaleSec) return true;
        if (OffPath(playerGrid)) return true;
        if (NegativeProgress(playerGrid)) return true;
        return false;
    }

    /// <summary>Swap in a freshly-planned path: reset the cursor, stamp the replan time + goal, clear in-flight.</summary>
    public void ApplyResult(IReadOnlyList<(int x, int y)> waypoints, NumVec2 goal, NumVec2? replanPlayerGrid = null)
        => ApplyResult(
            waypoints.Count > 0 ? RoutePlanStatus.Planned : RoutePlanStatus.NoPath,
            waypoints,
            goal,
            waypoints.Count > 0 ? ((int)MathF.Round(goal.X), (int)MathF.Round(goal.Y)) : null,
            waypoints.Count > 0 ? "" : "no path",
            0,
            replanPlayerGrid);

    public void ApplyResult(BackgroundReplanner.Result result, NumVec2 playerGrid)
        => ApplyResult(
            result.Status,
            result.Waypoints,
            new NumVec2(result.Goal.x, result.Goal.y),
            result.ResolvedGoal,
            result.FailureReason,
            result.PlanMilliseconds,
            playerGrid);

    private void ApplyResult(
        RoutePlanStatus status,
        IReadOnlyList<(int x, int y)> waypoints,
        NumVec2 goal,
        (int x, int y)? resolvedGoal,
        string failureReason,
        double planMilliseconds,
        NumVec2? replanPlayerGrid = null)
    {
        _waypoints = status == RoutePlanStatus.Planned
            ? new List<(int x, int y)>(waypoints)
            : new List<(int x, int y)>();
        _cursor = 0;
        _lastReplanUtc = DateTime.UtcNow;
        _lastGoal = goal;
        if (status == RoutePlanStatus.Planned && replanPlayerGrid is { } pg)
            _lastReplanPlayerGrid = pg;
        Status = status;
        FailureReason = failureReason ?? "";
        ResolvedGoal = resolvedGoal;
        LastPlanMilliseconds = planMilliseconds;
        ReplanInFlight = false;
    }

    /// <summary>Mark this target as having a replan request in flight (stamps the cooldown clock).</summary>
    public void MarkReplanRequested()
    {
        ReplanInFlight = true;
        Status = RoutePlanStatus.Planning;
        FailureReason = "";
        _lastReplanUtc = DateTime.UtcNow;
    }

    public void MarkWaitingForTerrain()
    {
        ClearRoute(RoutePlanStatus.WaitingForTerrain, "waiting for terrain");
    }

    public void MarkTargetUnavailable()
    {
        ClearRoute(RoutePlanStatus.TargetUnavailable, "target unavailable");
    }

    private void ClearRoute(RoutePlanStatus status, string reason)
    {
        _waypoints.Clear();
        _cursor = 0;
        Status = status;
        FailureReason = reason;
        ResolvedGoal = null;
        LastPlanMilliseconds = 0;
        ReplanInFlight = false;
    }

    // ── Triggers ──────────────────────────────────────────────────────────────────────────────

    private bool GoalMoved(NumVec2 goal)
        => _lastGoal.X > float.MinValue && NumVec2.Distance(goal, _lastGoal) > GoalMovedCells;

    private bool PlayerMovedSinceReplan(NumVec2 playerGrid)
    {
        if (_lastReplanPlayerGrid.X <= float.MinValue) return true;
        return NumVec2.Distance(playerGrid, _lastReplanPlayerGrid) > SimpleReplanPlayerCells;
    }

    private bool OffPath(NumVec2 playerGrid)
    {
        // Minimum perpendicular distance to the nearest segment in the forward window.
        var bestDist = float.MaxValue;
        var last = Math.Min(_waypoints.Count - 1, _cursor + ForwardWindow);
        for (var i = _cursor; i < last; i++)
        {
            var a = ToVec(_waypoints[i]);
            var b = ToVec(_waypoints[i + 1]);
            var d = PointSegmentDistance(playerGrid, a, b);
            if (d < bestDist) bestDist = d;
        }
        // Single-waypoint path (or cursor at the end): fall back to point distance to that waypoint.
        if (bestDist == float.MaxValue)
        {
            var only = ToVec(_waypoints[Math.Min(_cursor, _waypoints.Count - 1)]);
            bestDist = NumVec2.Distance(playerGrid, only);
        }
        return bestDist > OffPathCells;
    }

    private bool NegativeProgress(NumVec2 playerGrid)
    {
        if (_history.Count == 0) return false;
        var heading = playerGrid - _history[0].pos;     // oldest → current over the window
        if (heading.Length() < HeadingMinCells) return false;

        // Direction to the next un-walked waypoint.
        var nextIdx = Math.Min(_cursor + 1, _waypoints.Count - 1);
        var toGoal = ToVec(_waypoints[nextIdx]) - playerGrid;
        if (toGoal.Length() < 1e-3f) return false;

        var dot = NumVec2.Dot(NumVec2.Normalize(heading), NumVec2.Normalize(toGoal));
        return dot < NegativeProgressDot;
    }

    // ── Heading history ──────────────────────────────────────────────────────────────────────

    private void PushHistory(NumVec2 playerGrid)
    {
        var now = DateTime.UtcNow;
        _history.Add((playerGrid, now));
        var cutoff = now.AddSeconds(-HeadingWindowSec);
        // Drop samples older than the window, but always keep at least one (the oldest in-window).
        var drop = 0;
        while (drop < _history.Count - 1 && _history[drop].at < cutoff) drop++;
        if (drop > 0) _history.RemoveRange(0, drop);
    }

    // ── Geometry helpers ───────────────────────────────────────────────────────────────────────

    private static NumVec2 ToVec((int x, int y) c) => new(c.x, c.y);

    /// <summary>Distance from point p to segment [a,b].</summary>
    private static float PointSegmentDistance(NumVec2 p, NumVec2 a, NumVec2 b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared();
        if (lenSq < 1e-6f) return NumVec2.Distance(p, a);
        var t = Math.Clamp(NumVec2.Dot(p - a, ab) / lenSq, 0f, 1f);
        var proj = a + ab * t;
        return NumVec2.Distance(p, proj);
    }
}
