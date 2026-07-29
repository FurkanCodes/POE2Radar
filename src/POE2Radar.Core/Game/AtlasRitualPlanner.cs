using System.Text;
using System.Text.RegularExpressions;

namespace POE2Radar.Core.Game;

/// <summary>Pure ritual-line reachability and chain enumeration over the client candidate table.</summary>
public static class AtlasRitualPlanner
{
    public readonly record struct NodeInfo(int GridX, int GridY, string Name, bool Accessible, bool Blocked)
    {
        public (int X, int Y) Grid => (GridX, GridY);
    }

    public readonly record struct Reward(string First, string Second)
    {
        public string Display => string.IsNullOrEmpty(Second)
            ? ShortModLabel(First)
            : $"{ShortModLabel(First)} + {ShortModLabel(Second)}";
    }

    public sealed record Chain(
        string Key,
        IReadOnlyList<(int X, int Y)> Nodes,
        IReadOnlyList<Reward> Rewards,
        string PathLine,
        string ModsLine,
        int Weight);

    public sealed record Plan(
        IReadOnlyList<Chain> Chains,
        int StartCount,
        int EnumeratedCount,
        bool Capped,
        int LineLength);

    /// <summary>
    /// Select the small set of complete lines shown by the overlay. The plan is already ordered by
    /// configured reward weight, so presentation must preserve that order instead of re-ranking it.
    /// </summary>
    public static IReadOnlyList<Chain> SelectDisplayChains(
        Plan plan,
        int maxChoices,
        Func<Chain, bool>? predicate = null,
        IReadOnlySet<(int X, int Y)>? priorityNodes = null)
    {
        if (maxChoices <= 0 || plan.Chains.Count == 0)
            return Array.Empty<Chain>();

        var candidates = plan.Chains
            .Where(chain => chain.Nodes.Count >= 2
                && chain.Rewards.Count == chain.Nodes.Count - 1
                && (predicate is null || predicate(chain)))
            .Select((chain, index) => new
            {
                Chain = chain,
                OriginalIndex = index,
                RootVisible = priorityNodes?.Contains(chain.Nodes[0]) == true,
                VisibleNodeCount = priorityNodes is null
                    ? 0
                    : chain.Nodes.Count(priorityNodes.Contains),
            });

        if (priorityNodes is { Count: > 0 })
        {
            candidates = candidates
                .OrderByDescending(candidate => candidate.RootVisible)
                .ThenByDescending(candidate => candidate.VisibleNodeCount)
                .ThenBy(candidate => candidate.OriginalIndex);
        }

        return candidates
            .Select(candidate => candidate.Chain)
            .Take(maxChoices)
            .ToArray();
    }

    /// <summary>
    /// Matches pasted in-game modifier text as well as the compact labels used by the planner UI.
    /// Comma and pipe separated terms are OR alternatives.
    /// </summary>
    public static bool MatchesRewardQuery(Chain chain, string? query)
        => MatchesRewardQuery(
            chain.Rewards.SelectMany(reward => new[] { reward.First, reward.Second, reward.Display }),
            query);

    public static bool MatchesRewardQuery(IEnumerable<string?> rewardTexts, string? query)
    {
        var terms = (query ?? "")
            .Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
            return false;

        var texts = rewardTexts
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToArray();
        return terms.Any(term => texts.Any(text =>
            text.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    public static IReadOnlyDictionary<(int X, int Y), Reward> BuildHoverPredictions(
        Poe2Atlas.RitualLineSnapshot state,
        (int X, int Y) start,
        IReadOnlySet<(int X, int Y)> blocked,
        int lineLength,
        IReadOnlyList<AtlasRitualPrediction.RitualMod> pool,
        int secondChance,
        int maxNodes = 4000)
    {
        var result = new Dictionary<(int, int), Reward>();
        var maxDepth = Math.Min(16, Math.Max(0, lineLength - 1));
        if (maxDepth <= 0 || pool.Count == 0 || !state.Candidates.ContainsKey(start))
            return result;

        var reached = new HashSet<(int, int)> { start };
        var initialVisited = new HashSet<(int, int)> { start };
        var queue = new Queue<((int X, int Y) Node, HashSet<(int X, int Y)> Visited, int Depth)>();
        queue.Enqueue((start, initialVisited, 0));
        var budget = maxNodes;

        while (queue.Count > 0 && budget > 0)
        {
            var (node, visited, depth) = queue.Dequeue();
            if (depth >= maxDepth || !state.Candidates.TryGetValue(node, out var raw) || raw.Count == 0)
                continue;
            var candidates = raw.Where(candidate => !visited.Contains(candidate))
                .OrderBy(candidate => candidate.X)
                .ThenBy(candidate => candidate.Y)
                .ToList();
            var committedCount = (uint)visited.Count;
            for (var index = 0; index < candidates.Count && budget > 0; index++)
            {
                var candidate = candidates[index];
                if (reached.Contains(candidate)) continue;
                if (blocked.Contains(candidate))
                {
                    reached.Add(candidate);
                    continue;
                }

                var reachVisited = new HashSet<(int, int)>(visited) { candidate };
                if (!CanComplete(state.Candidates, blocked, candidate, reachVisited, maxDepth - depth - 1))
                    continue;

                reached.Add(candidate);
                var roll = AtlasRitualPrediction.PredictMods(
                    state.LineId,
                    committedCount,
                    (uint)index,
                    pool,
                    secondChance);
                if (!string.IsNullOrEmpty(roll.First))
                {
                    result[candidate] = new Reward(roll.First, roll.Second);
                    budget--;
                }

                var childVisited = new HashSet<(int, int)>(visited) { candidate };
                queue.Enqueue((candidate, childVisited, depth + 1));
            }
        }

        return result;
    }

    public static Plan BuildChains(
        Poe2Atlas.RitualLineSnapshot state,
        IReadOnlyList<NodeInfo> nodes,
        int lineLength,
        IReadOnlyList<AtlasRitualPrediction.RitualMod> pool,
        int secondChance,
        IReadOnlyDictionary<string, int>? rewardWeights = null,
        int maxPaths = 8192)
    {
        var blocked = nodes.Where(node => node.Blocked).Select(node => node.Grid).ToHashSet();
        var names = nodes.ToDictionary(node => node.Grid, node => node.Name);
        var lineActive = state.Committed.Count > 0;
        var roots = lineActive
            ? [state.Committed[^1]]
            : nodes.Where(node => node.Accessible && !node.Blocked).Select(node => node.Grid)
                .OrderBy(grid => grid.X).ThenBy(grid => grid.Y).ToList();
        if (roots.Count == 0 || pool.Count == 0)
            return new Plan(Array.Empty<Chain>(), roots.Count, 0, false, lineLength);

        var prefixCount = lineActive ? state.Committed.Count : 1;
        var maxDepth = Math.Max(0, lineLength - prefixCount);
        if (maxDepth == 0)
            return new Plan(Array.Empty<Chain>(), roots.Count, 0, false, lineLength);

        var chains = new List<Chain>();
        var rollCache = new Dictionary<(uint Count, uint Index), Reward>();
        var enumerated = 0;
        var capped = false;
        var perStart = Math.Max(32, maxPaths / roots.Count);

        Reward Roll(uint committedCount, uint candidateIndex)
        {
            if (rollCache.TryGetValue((committedCount, candidateIndex), out var cached))
                return cached;
            var value = AtlasRitualPrediction.PredictMods(
                state.LineId,
                committedCount,
                candidateIndex,
                pool,
                secondChance);
            var reward = new Reward(value.First, value.Second);
            rollCache[(committedCount, candidateIndex)] = reward;
            return reward;
        }

        foreach (var root in roots)
        {
            var path = lineActive
                ? new List<(int, int)>(state.Committed)
                : [root];
            var rewards = new List<Reward>();
            var visited = new HashSet<(int, int)>(path);
            var emittedForStart = 0;

            void Emit()
            {
                if (rewards.Count < maxDepth) return;
                enumerated++;
                if (chains.Count >= maxPaths || emittedForStart >= perStart)
                {
                    capped = true;
                    return;
                }
                emittedForStart++;

                var chainNodes = path.GetRange(prefixCount - 1, path.Count - prefixCount + 1);
                var key = string.Join('|', chainNodes.Select(grid => $"{grid.Item1},{grid.Item2}")) + "|";
                var pathLine = string.Join("  >  ", chainNodes.Select(grid =>
                    names.TryGetValue(grid, out var name) && !string.IsNullOrWhiteSpace(name)
                        ? name
                        : $"({grid.Item1},{grid.Item2})"));
                var modsLine = string.Join("   -   ", rewards.Select(reward => reward.Display));
                var weight = ScoreRewards(rewards, rewardWeights);
                chains.Add(new Chain(key, chainNodes.ToArray(), rewards.ToArray(), pathLine, modsLine, weight));
            }

            void Walk((int X, int Y) node, int depth)
            {
                if (depth >= maxDepth || chains.Count >= maxPaths || emittedForStart >= perStart)
                {
                    Emit();
                    return;
                }
                if (!state.Candidates.TryGetValue(node, out var raw) || raw.Count == 0)
                {
                    Emit();
                    return;
                }

                var candidates = raw.Where(candidate => !visited.Contains(candidate))
                    .OrderBy(candidate => candidate.X)
                    .ThenBy(candidate => candidate.Y)
                    .ToList();
                var committedCount = (uint)(prefixCount + depth);
                var any = false;
                for (var index = 0; index < candidates.Count
                    && chains.Count < maxPaths
                    && emittedForStart < perStart; index++)
                {
                    var candidate = candidates[index];
                    if (blocked.Contains(candidate))
                        continue;
                    var reachVisited = new HashSet<(int, int)>(visited) { candidate };
                    if (!CanComplete(state.Candidates, blocked, candidate, reachVisited, maxDepth - depth - 1))
                        continue;
                    var reward = Roll(committedCount, (uint)index);
                    if (string.IsNullOrEmpty(reward.First))
                        continue;
                    any = true;
                    path.Add(candidate);
                    rewards.Add(reward);
                    visited.Add(candidate);
                    Walk(candidate, depth + 1);
                    visited.Remove(candidate);
                    rewards.RemoveAt(rewards.Count - 1);
                    path.RemoveAt(path.Count - 1);
                }
                if (!any)
                    Emit();
            }

            Walk(root, 0);
            if (chains.Count >= maxPaths)
                break;
        }

        chains.Sort((left, right) =>
        {
            var byWeight = right.Weight.CompareTo(left.Weight);
            return byWeight != 0
                ? byWeight
                : string.Compare(left.PathLine, right.PathLine, StringComparison.OrdinalIgnoreCase);
        });
        return new Plan(chains, roots.Count, enumerated, capped, lineLength);
    }

    public static bool CanComplete(
        IReadOnlyDictionary<(int X, int Y), List<(int X, int Y)>> candidates,
        IReadOnlySet<(int X, int Y)> blocked,
        (int X, int Y) node,
        HashSet<(int X, int Y)> visited,
        int remaining)
    {
        if (remaining <= 0) return true;
        if (!candidates.TryGetValue(node, out var raw)) return false;
        foreach (var candidate in raw)
        {
            if (blocked.Contains(candidate) || visited.Contains(candidate))
                continue;
            visited.Add(candidate);
            var complete = CanComplete(candidates, blocked, candidate, visited, remaining - 1);
            visited.Remove(candidate);
            if (complete) return true;
        }
        return false;
    }

    public static string ShortModLabel(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var match = Regex.Match(text, @"^(\d+) (.+?Orbs?.*)$");
        if (match.Success) return $"{match.Groups[2].Value} x{match.Groups[1].Value}";
        if (text.StartsWith("Omen of ", StringComparison.OrdinalIgnoreCase))
            return "Omen: " + text["Omen of ".Length..];
        if (text.StartsWith("Contains a very rare Unique", StringComparison.OrdinalIgnoreCase))
            return "Very Rare Unique";
        if (text.StartsWith("Contains ", StringComparison.OrdinalIgnoreCase))
            return text["Contains ".Length..];
        if (text.Contains("additional pack", StringComparison.OrdinalIgnoreCase))
            return "+Monster Packs";
        if (text.Contains("no Cost the first time", StringComparison.OrdinalIgnoreCase))
            return "+Free Reroll";
        if (text.Contains("additional Favour reroll", StringComparison.OrdinalIgnoreCase))
            return "+1 Reroll";
        if (text.Contains("reduced Tribute", StringComparison.OrdinalIgnoreCase))
            return "-Reroll Cost";
        if (text.Contains("increased Tribute", StringComparison.OrdinalIgnoreCase))
            return "+25% Tribute";
        if (text.Contains("increased number of Favours", StringComparison.OrdinalIgnoreCase))
            return "+Favours";
        return text;
    }

    private static int ScoreRewards(
        IReadOnlyList<Reward> rewards,
        IReadOnlyDictionary<string, int>? rewardWeights)
    {
        if (rewardWeights is null || rewardWeights.Count == 0) return 0;
        var score = 0;
        foreach (var reward in rewards)
        {
            foreach (var pair in rewardWeights)
            {
                if (reward.First.Contains(pair.Key, StringComparison.OrdinalIgnoreCase)
                    || reward.Second.Contains(pair.Key, StringComparison.OrdinalIgnoreCase)
                    || reward.Display.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
                    score += pair.Value;
            }
        }
        return score;
    }
}
