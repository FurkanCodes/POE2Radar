using POE2Radar.Core.Game;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

/// <summary>
/// Converts deterministic Ritual plans into UI rows. Prediction text is independent from Atlas
/// projection: an off-screen node may prevent drawing a route, but must never hide its predictions.
/// </summary>
public static class AtlasRitualPresentation
{
    public static AtlasRitualPlannerRow[] BuildRows(
        IReadOnlyList<AtlasRitualPlanner.Chain> chains,
        IReadOnlyDictionary<(int X, int Y), string> names,
        IReadOnlyDictionary<(int X, int Y), NumVec2> centers)
    {
        var rows = new AtlasRitualPlannerRow[chains.Count];
        for (var index = 0; index < chains.Count; index++)
        {
            var chain = chains[index];
            var points = new NumVec2[chain.Nodes.Count];
            var routeDrawable = points.Length >= 2;
            for (var step = 0; step < chain.Nodes.Count; step++)
            {
                if (!centers.TryGetValue(chain.Nodes[step], out var center)
                    || !float.IsFinite(center.X)
                    || !float.IsFinite(center.Y))
                {
                    routeDrawable = false;
                    break;
                }
                points[step] = center;
            }

            rows[index] = new AtlasRitualPlannerRow(
                chain.Key,
                chain.PathLine,
                chain.ModsLine,
                chain.Weight,
                chain.Nodes.Select(grid => names.TryGetValue(grid, out var name)
                    ? name
                    : $"({grid.X},{grid.Y})").ToArray(),
                routeDrawable ? points : Array.Empty<NumVec2>(),
                chain.Rewards.Select(reward => reward.Display).ToArray(),
                chain.Nodes.Select(grid => new AtlasRitualGridNode(grid.X, grid.Y)).ToArray(),
                chain.Rewards.Select(reward =>
                    string.Join(' ', new[] { reward.First, reward.Second, reward.Display }
                        .Where(text => !string.IsNullOrWhiteSpace(text)))).ToArray());
        }
        return rows;
    }

    /// <summary>
    /// Resolves a saved line against the latest Atlas projection. A selected line stores stable grid
    /// identities; its screen points are deliberately not reused after the Atlas pans or zooms.
    /// </summary>
    public static NumVec2[] ResolveRoutePoints(
        AtlasRitualPlannerRow row,
        IReadOnlyDictionary<(int X, int Y), NumVec2> currentCenters)
    {
        if (row.GridNodes.Count < 2)
            return Array.Empty<NumVec2>();

        var points = new NumVec2[row.GridNodes.Count];
        for (var index = 0; index < row.GridNodes.Count; index++)
        {
            var grid = row.GridNodes[index];
            if (!currentCenters.TryGetValue((grid.X, grid.Y), out var center)
                || !float.IsFinite(center.X)
                || !float.IsFinite(center.Y))
                return Array.Empty<NumVec2>();
            points[index] = center;
        }
        return points;
    }

    /// <summary>Maps matching reward steps to their destination Atlas nodes.</summary>
    public static AtlasRitualRewardMatch[] FindRewardMatches(
        IReadOnlyList<AtlasRitualPlannerRow> rows,
        string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<AtlasRitualRewardMatch>();

        var matches = new List<AtlasRitualRewardMatch>();
        var seen = new HashSet<(AtlasRitualGridNode Grid, string Label)>();
        foreach (var row in rows)
        {
            var count = Math.Min(
                row.Rewards.Count,
                Math.Min(row.RewardSearchTexts.Count, Math.Max(0, row.GridNodes.Count - 1)));
            for (var index = 0; index < count; index++)
            {
                if (!AtlasRitualPlanner.MatchesRewardQuery([row.RewardSearchTexts[index]], query))
                    continue;
                var match = new AtlasRitualRewardMatch(row.GridNodes[index + 1], row.Rewards[index]);
                if (seen.Add((match.Grid, match.Label)))
                    matches.Add(match);
            }
        }
        return matches.ToArray();
    }
}
