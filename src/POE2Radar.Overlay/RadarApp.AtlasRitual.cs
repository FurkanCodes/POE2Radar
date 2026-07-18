using System.Text;
using NumVec2 = System.Numerics.Vector2;
using POE2Radar.Core.Game;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private AtlasRitualPredictionMark[] _atlasRitualPredPublish = Array.Empty<AtlasRitualPredictionMark>();
    private AtlasRitualPlannerRow[] _atlasRitualPlannerPublish = Array.Empty<AtlasRitualPlannerRow>();
    private readonly Dictionary<nint, bool> _atlasRitualSpecialCache = new();
    private AtlasRitualPlanner.Plan? _atlasRitualPlanCache;
    private string? _atlasRitualPlanSignature;
    private bool _atlasRitualLineActive;

    /// <summary>
    /// Exact Atlas2 ritual-line prediction. Before the first pick, hovering an eligible start previews
    /// the reachable deterministic rewards. The planner enumerates complete chains from every eligible
    /// start, or from the committed frontier once a line is active.
    /// </summary>
    private void UpdateAtlasRitualLine(IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes)
    {
        _atlasRitualLineActive = false;
        _atlasRitualPredPublish = Array.Empty<AtlasRitualPredictionMark>();
        _atlasRitualPlannerPublish = Array.Empty<AtlasRitualPlannerRow>();

        if (!_settings.AtlasShowRitualPrediction && !_settings.AtlasShowRitualPlanner)
            return;
        if (!_atlas.TryReadRitualLine(out var state))
        {
            _atlasRitualPlanCache = null;
            _atlasRitualPlanSignature = null;
            return;
        }

        _atlasRitualLineActive = true;
        var pool = AtlasRitualPrediction.FilterPool(state.Stats);
        if (pool.Count == 0) return;
        var additionalMaps = Math.Max(0,
            AtlasRitualPrediction.StatValue(state.Stats, AtlasRitualPrediction.StatAdditionalMaps));
        var lineLength = AtlasRitualPrediction.BaseLineLength + additionalMaps;
        var secondChance = Math.Clamp(
            AtlasRitualPrediction.StatValue(state.Stats, AtlasRitualPrediction.StatSecondModChance),
            0,
            100);

        var nodeInfos = new List<AtlasRitualPlanner.NodeInfo>(nodes.Count);
        var blocked = new HashSet<(int X, int Y)>();
        foreach (var node in nodes)
        {
            var special = IsRitualSpecialNodeCached(node.Element);
            var isBlocked = node.Completed || special;
            nodeInfos.Add(new AtlasRitualPlanner.NodeInfo(
                node.GridX,
                node.GridY,
                string.IsNullOrWhiteSpace(node.MapName) ? $"({node.GridX},{node.GridY})" : node.MapName,
                node.AccessibleNow,
                isBlocked));
            if (isBlocked)
                blocked.Add(node.Grid);
        }

        if (_settings.AtlasShowRitualPrediction
            && state.Committed.Count == 0
            && state.Pending.Count == 0
            && RitualHoverGrid(nodes, blocked) is { } hover)
        {
            var predictions = AtlasRitualPlanner.BuildHoverPredictions(
                state,
                hover,
                blocked,
                lineLength,
                pool,
                secondChance);
            if (predictions.Count > 0)
            {
                var byGrid = nodes.ToDictionary(node => node.Grid);
                var marks = new List<AtlasRitualPredictionMark>(predictions.Count);
                foreach (var pair in predictions)
                {
                    if (!byGrid.TryGetValue(pair.Key, out var node)) continue;
                    var center = node.ScreenCenter;
                    if (!float.IsFinite(center.X) || !float.IsFinite(center.Y)) continue;
                    marks.Add(new AtlasRitualPredictionMark(
                        center.X,
                        center.Y,
                        pair.Value.Display,
                        pair.Key.X,
                        pair.Key.Y));
                }
                _atlasRitualPredPublish = marks.ToArray();
            }
        }

        if (!_settings.AtlasShowRitualPlanner)
            return;

        var signature = RitualPlanSignature(state, nodes, lineLength, secondChance);
        if (_atlasRitualPlanCache is null || !string.Equals(signature, _atlasRitualPlanSignature, StringComparison.Ordinal))
        {
            _atlasRitualPlanCache = AtlasRitualPlanner.BuildChains(
                state,
                nodeInfos,
                lineLength,
                pool,
                secondChance,
                _settings.AtlasRitualRewardWeights,
                maxPaths: 8192);
            _atlasRitualPlanSignature = signature;
        }

        var plan = _atlasRitualPlanCache;
        if (plan is null || plan.Chains.Count == 0) return;
        var centers = nodes.ToDictionary(node => node.Grid, node =>
        {
            var center = node.ScreenCenter;
            return new NumVec2(center.X, center.Y);
        });
        var rows = new List<AtlasRitualPlannerRow>(200);
        foreach (var chain in plan.Chains)
        {
            var points = new List<NumVec2>(chain.Nodes.Count);
            var complete = true;
            foreach (var grid in chain.Nodes)
            {
                if (!centers.TryGetValue(grid, out var center)
                    || !float.IsFinite(center.X)
                    || !float.IsFinite(center.Y))
                {
                    complete = false;
                    break;
                }
                points.Add(center);
            }
            if (!complete || points.Count < 2) continue;
            rows.Add(new AtlasRitualPlannerRow(
                chain.Key,
                chain.PathLine,
                chain.ModsLine,
                chain.Weight,
                points.ToArray(),
                chain.Rewards.Select(reward => reward.Display).ToArray()));
            if (rows.Count >= 200) break;
        }
        _atlasRitualPlannerPublish = rows.ToArray();
    }

    private (int X, int Y)? RitualHoverGrid(
        IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes,
        IReadOnlySet<(int X, int Y)> blocked)
    {
        if (!TryGetCursorClient(out var cursor)) return null;
        foreach (var node in nodes)
        {
            if (!node.AccessibleNow || blocked.Contains(node.Grid)) continue;
            var width = node.ScreenW > 1 ? node.ScreenW : 40f;
            var height = node.ScreenH > 1 ? node.ScreenH : 40f;
            if (cursor.X >= node.ScreenX && cursor.X <= node.ScreenX + width
                && cursor.Y >= node.ScreenY && cursor.Y <= node.ScreenY + height)
                return node.Grid;
        }
        return null;
    }

    private bool IsRitualSpecialNodeCached(nint node)
    {
        if (_atlasRitualSpecialCache.TryGetValue(node, out var special))
            return special;
        special = IsRitualSpecialNode(node);
        _atlasRitualSpecialCache[node] = special;
        return special;
    }

    /// <summary>Atlas2 IsRitualSpecialNode: data row category at +0x7C nonzero means ineligible.</summary>
    private bool IsRitualSpecialNode(nint node)
    {
        if (node == 0) return true;
        var row = _reader.ReadPointer(node + Poe2.AtlasNode.MapNodeId);
        if (row == 0) return true;
        return !_reader.TryReadStruct<int>(row + 0x7C, out var category) || category != 0;
    }

    private string RitualPlanSignature(
        Poe2Atlas.RitualLineSnapshot state,
        IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes,
        int lineLength,
        int secondChance)
    {
        var signature = new StringBuilder();
        signature.Append(state.LineId).Append('#').Append(lineLength).Append('#').Append(secondChance);
        foreach (var grid in state.Committed)
            signature.Append(';').Append(grid.X).Append(',').Append(grid.Y);
        signature.Append('|').Append(nodes.Count);
        foreach (var node in nodes.Where(node => node.AccessibleNow || node.Completed))
            signature.Append(';').Append(node.GridX).Append(',').Append(node.GridY)
                .Append(':').Append(node.AccessibleNow ? 'A' : 'C');
        foreach (var pair in _settings.AtlasRitualRewardWeights.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            signature.Append('|').Append(pair.Key).Append('=').Append(pair.Value);
        return signature.ToString();
    }
}

public readonly record struct AtlasRitualPredictionMark(
    float ScreenX,
    float ScreenY,
    string Text,
    int GridX,
    int GridY);

public readonly record struct AtlasRitualPlannerRow(
    string Key,
    string PathLine,
    string ModsLine,
    int Weight,
    IReadOnlyList<NumVec2> Points,
    IReadOnlyList<string> Rewards);
