using NumVec2 = System.Numerics.Vector2;
using POE2Radar.Core.Game;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private const int AmanamuDiscoveryBudgetPerWorldTick = 4;
    private const long AmanamuMissingGraceMs = 1500;

    private readonly Dictionary<uint, AmanamuTracked> _amanamuTracked = new();
    private readonly HashSet<uint> _amanamuSeen = new();
    private readonly List<Poe2Live.EntityDot> _amanamuCandidates = new();
    private volatile AmanamuAlert[] _amanamuAlerts = [];
    private nint _amanamuArea;
    private int _amanamuProbeCursor;

    private readonly record struct AmanamuTracked(AmanamuAlert Alert, long LastSeenMs);

    private void UpdateAmanamuAlerts(
        IReadOnlyList<Poe2Live.EntityDot> entities,
        IReadOnlyList<Poe2Live.ServerMinimapIcon> serverIcons,
        NumVec2 player,
        nint areaInstance)
    {
        // This is the hard performance gate: when disabled, no candidate component is touched.
        if (!_settings.Amanamu.Enabled)
        {
            ClearAmanamuAlerts();
            return;
        }

        if (areaInstance != _amanamuArea)
        {
            ClearAmanamuAlerts();
            _amanamuArea = areaInstance;
        }

        // Do not spend discovery reads outside Abyss content. These signals are already present in
        // the normal world snapshot, so the gate itself costs no ReadProcessMemory calls.
        var abyssSignal = _amanamuTracked.Count > 0
                          || entities.Any(static e =>
                              e.Metadata.Contains("Abyss", StringComparison.OrdinalIgnoreCase))
                          || serverIcons.Any(static i =>
                              i.Name.Contains("Abyss", StringComparison.OrdinalIgnoreCase));
        if (!abyssSignal)
        {
            PublishAmanamuAlerts(Environment.TickCount64);
            return;
        }

        var maxDistance = Math.Max(0, _settings.Amanamu.MaxDistanceGrid);
        _amanamuCandidates.Clear();
        foreach (var entity in entities)
        {
            if (entity.Category != Poe2Live.EntityCategory.Monster
                || entity.IsSleeping
                || !entity.IsAlive
                || entity.IsFriendly
                || (_settings.Amanamu.OnlyRareOrUnique
                    && entity.Rarity is not (Poe2Live.Rarity.Rare or Poe2Live.Rarity.Unique))
                || (maxDistance > 0 && NumVec2.Distance(entity.Grid, player) > maxDistance))
                continue;
            _amanamuCandidates.Add(entity);
        }

        var now = Environment.TickCount64;
        _amanamuSeen.Clear();
        if (_amanamuCandidates.Count > 0)
        {
            var start = _amanamuProbeCursor % _amanamuCandidates.Count;
            var discoveryBudget = AmanamuDiscoveryBudgetPerWorldTick;
            var unknownsVisited = 0;

            for (var offset = 0; offset < _amanamuCandidates.Count; offset++)
            {
                var entity = _amanamuCandidates[(start + offset) % _amanamuCandidates.Count];
                var known = _worldLive.IsKnownAmanamu(entity.Address);
                var allowDiscovery = !known && discoveryBudget > 0;
                if (allowDiscovery)
                {
                    discoveryBudget--;
                    unknownsVisited++;
                }

                var state = _worldLive.ReadAmanamuState(entity, allowDiscovery);
                if (!state.IsTarget) continue;

                var distance = NumVec2.Distance(entity.Grid, player);
                var alert = new AmanamuAlert(
                    entity.Id,
                    entity.Grid,
                    new System.Numerics.Vector3(entity.World.X, entity.World.Y, entity.World.Z),
                    state.InsideCloud,
                    distance);
                _amanamuTracked[entity.Id] = new AmanamuTracked(alert, now);
                _amanamuSeen.Add(entity.Id);
            }

            _amanamuProbeCursor = (start + Math.Max(1, unknownsVisited)) % _amanamuCandidates.Count;
        }

        PublishAmanamuAlerts(now);
    }

    private void PublishAmanamuAlerts(long now)
    {
        foreach (var id in _amanamuTracked.Keys.ToArray())
        {
            var tracked = _amanamuTracked[id];
            if (!_amanamuSeen.Contains(id) && now - tracked.LastSeenMs > AmanamuMissingGraceMs)
                _amanamuTracked.Remove(id);
        }

        _amanamuAlerts = _amanamuTracked.Values
            .Select(static t => t.Alert)
            .OrderBy(static a => a.DistanceGrid)
            .ToArray();
    }

    private void ClearAmanamuAlerts()
    {
        _amanamuTracked.Clear();
        _amanamuSeen.Clear();
        _amanamuCandidates.Clear();
        _amanamuAlerts = [];
        _amanamuProbeCursor = 0;
        _amanamuArea = 0;
    }

    private AmanamuView BuildAmanamuView()
    {
        var s = _settings.Amanamu;
        return new AmanamuView(
            s.Enabled,
            s.ShowWorldOverlay,
            s.ShowMapMarkers,
            s.DrawLabels,
            s.DrawOffscreenArrows,
            s.DrawCircle,
            Math.Clamp(s.CircleRadius, 8f, 160f),
            Math.Clamp(s.LabelYOffset, 0f, 240f),
            Math.Clamp(s.ArrowEdgeMargin, 20f, 240f),
            s.InsideCloudColor,
            s.OutsideCloudColor,
            _amanamuAlerts);
    }
}
