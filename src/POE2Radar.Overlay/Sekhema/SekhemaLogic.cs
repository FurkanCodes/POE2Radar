// Sekhema-specific behavior is derived from MordWraith/Gamehelper's GPL-3.0 SekhemaHelper.
// Upstream snapshot: 7e7a23571c494090cbc6a7faafa633e17762a78d. See the bundled notice/license.
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Config;
using Vector2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay.Sekhema;

internal readonly record struct SekhemaRoomScore(double Weight, string Debug);

internal static partial class SekhemaLogic
{
    private const double BaseRoomWeight = 1_000_000;
    private const double SuppressedRewardPenalty = -300;

    [GeneratedRegex("(Bronze|Silver|Gold)Chest([A-Za-z]+?)([123])?$", RegexOptions.CultureInvariant)]
    private static partial Regex ChestRegex();

    internal static SekhemaProfileSettings ActiveProfile(SekhemaSettings settings)
    {
        if (settings.Profiles.TryGetValue(settings.CurrentProfile, out var profile) && profile is not null)
            return profile;
        var fallback = SekhemaProfileSettings.CreateDefault();
        settings.Profiles["Default"] = fallback;
        settings.CurrentProfile = "Default";
        return fallback;
    }

    internal static SekhemaRoomScore ScoreRoom(
        Poe2Live.SekhemaRoomRead room,
        SekhemaSettings settings,
        Poe2Live.SekhemaResources resources,
        Poe2Live.SekhemaPlayerStats stats)
    {
        var profile = ActiveProfile(settings);
        var debug = new StringBuilder();
        double weight = BaseRoomWeight;

        if (room.RoomType.Length > 0 && profile.RoomTypeWeights.TryGetValue(room.RoomType, out var roomWeight))
        {
            weight += roomWeight;
            debug.Append(room.RoomType).Append(':').AppendLine(roomWeight.ToString("0"));
        }

        if (room.Affliction.Length > 0)
        {
            var dynamicWeight = DynamicAffliction(room.Affliction, stats);
            if (dynamicWeight is { } dyn)
            {
                weight += dyn;
                debug.Append(room.Affliction).Append(':').Append(dyn.ToString("0")).AppendLine(" (dyn)");
            }
            else if (profile.AfflictionWeights.TryGetValue(room.Affliction, out var afflictionWeight))
            {
                weight += afflictionWeight;
                debug.Append(room.Affliction).Append(':').AppendLine(afflictionWeight.ToString("0"));
            }
            else
            {
                debug.Append(room.Affliction).AppendLine(":0 (unlisted)");
            }
        }

        if (room.Reward.Length > 0 && profile.RewardWeights.TryGetValue(room.Reward, out var rewardWeight))
        {
            if (settings.SuppressMerchantLowWater &&
                room.Reward == "Merchant" &&
                resources.Water >= 0 &&
                resources.Water < settings.MerchantWaterThreshold)
            {
                rewardWeight = (float)SuppressedRewardPenalty;
                debug.Append(room.Reward).Append(':').Append(rewardWeight.ToString("0"))
                    .Append(" (low water ").Append(resources.Water).Append('<')
                    .Append(settings.MerchantWaterThreshold).AppendLine(")");
            }
            else if (settings.SuppressHonourRestoreHighPct &&
                     room.Reward.StartsWith("Honour", StringComparison.Ordinal) &&
                     resources.HonourPercent >= 0 &&
                     resources.HonourPercent > settings.HonourRestoreThresholdPct)
            {
                rewardWeight = (float)SuppressedRewardPenalty;
                debug.Append(room.Reward).Append(':').Append(rewardWeight.ToString("0"))
                    .Append(" (honour ").Append(resources.HonourPercent.ToString("0")).Append("%>")
                    .Append(settings.HonourRestoreThresholdPct).AppendLine("%)");
            }
            else
            {
                debug.Append(room.Reward).Append(':').AppendLine(rewardWeight.ToString("0"));
            }
            weight += rewardWeight;
        }

        debug.Append("Connectivity:").Append(room.NextConnections.Length).AppendLine(" (tiebreak)");
        return new SekhemaRoomScore(weight, debug.ToString());
    }

    internal static List<(int Layer, int Room)> FindBestPath(
        Poe2Live.SekhemaFloorRead floor,
        IReadOnlyDictionary<(int Layer, int Room), double> weights)
    {
        var result = new List<(int, int)>();
        if (!floor.IsValid || floor.Layers.Length == 0) return result;

        var startLayer = floor.PlayerLayer >= 0 ? floor.PlayerLayer : 0;
        var startRoom = floor.PlayerRoom;
        if (startLayer >= floor.Layers.Length)
        {
            startLayer = 0;
            startRoom = -1;
        }

        var scores = new Dictionary<(int, int), double>();
        var connectivity = new Dictionary<(int, int), long>();
        var previous = new Dictionary<(int, int), int>();
        if (startRoom >= 0 && startRoom < floor.Layers[startLayer].Length)
        {
            scores[(startLayer, startRoom)] = Weight(startLayer, startRoom);
            connectivity[(startLayer, startRoom)] = Connections(startLayer, startRoom);
        }
        else
        {
            for (var room = 0; room < floor.Layers[startLayer].Length; room++)
            {
                scores[(startLayer, room)] = Weight(startLayer, room);
                connectivity[(startLayer, room)] = Connections(startLayer, room);
            }
        }

        for (var layer = startLayer + 1; layer < floor.Layers.Length; layer++)
        {
            var priorLayer = floor.Layers[layer - 1];
            for (var priorRoom = 0; priorRoom < priorLayer.Length; priorRoom++)
            {
                if (!scores.TryGetValue((layer - 1, priorRoom), out var priorScore)) continue;
                var priorConnectivity = connectivity[(layer - 1, priorRoom)];
                foreach (var next in priorLayer[priorRoom].NextConnections)
                {
                    if (next < 0 || next >= floor.Layers[layer].Length) continue;
                    var candidateScore = priorScore + Weight(layer, next);
                    var candidateConnectivity = priorConnectivity + Connections(layer, next);
                    if (!scores.TryGetValue((layer, next), out var oldScore) ||
                        Better(candidateScore, candidateConnectivity, oldScore, connectivity[(layer, next)]))
                    {
                        scores[(layer, next)] = candidateScore;
                        connectivity[(layer, next)] = candidateConnectivity;
                        previous[(layer, next)] = priorRoom;
                    }
                }
            }
        }

        var lastLayer = -1;
        for (var layer = floor.Layers.Length - 1; layer >= 0; layer--)
        {
            if (Enumerable.Range(0, floor.Layers[layer].Length).Any(room => scores.ContainsKey((layer, room))))
            {
                lastLayer = layer;
                break;
            }
        }
        if (lastLayer < 0) return result;

        var bestRoom = -1;
        var bestScore = double.MinValue;
        var bestConnectivity = long.MinValue;
        for (var room = 0; room < floor.Layers[lastLayer].Length; room++)
        {
            if (!scores.TryGetValue((lastLayer, room), out var score)) continue;
            var conn = connectivity[(lastLayer, room)];
            if (bestRoom < 0 || Better(score, conn, bestScore, bestConnectivity))
            {
                bestRoom = room;
                bestScore = score;
                bestConnectivity = conn;
            }
        }
        if (bestRoom < 0) return result;

        for (var layer = lastLayer; layer >= startLayer;)
        {
            result.Add((layer, bestRoom));
            if (layer <= startLayer || !previous.TryGetValue((layer, bestRoom), out var prior)) break;
            bestRoom = prior;
            layer--;
        }
        result.Reverse();
        return result;

        double Weight(int layer, int room)
            => weights.TryGetValue((layer, room), out var value) ? value : 0;
        int Connections(int layer, int room)
            => floor.Layers[layer][room].NextConnections.Length;
    }

    private static bool Better(double newReward, long newConnections, double oldReward, long oldConnections)
    {
        const double epsilon = 0.000001;
        if (newReward > oldReward + epsilon) return true;
        if (newReward < oldReward - epsilon) return false;
        return newConnections > oldConnections;
    }

    private static double? DynamicAffliction(string name, Poe2Live.SekhemaPlayerStats stats)
    {
        var mitigation = Math.Max(stats.Armour + stats.Evasion, 4000.0);
        var armourRelevance = stats.Armour / mitigation;
        var evasionRelevance = stats.Evasion / mitigation;
        var lifePool = stats.EnergyShield + stats.Life;
        var esRelevance = lifePool > 0 ? stats.EnergyShield / (double)lifePool : 0;
        return name switch
        {
            "Sharpened Arrowhead" => -5000 * armourRelevance,
            "Iron Manacles" => -5000 * evasionRelevance,
            "Shattered Shield" => -5000 * esRelevance * 0.5,
            "Corrosive Concoction" => -5000 * (armourRelevance + evasionRelevance) - 5000 * esRelevance,
            "Worn Sandals" when stats.HasQueenOfTheForest => 0,
            _ => null,
        };
    }

    internal enum ChestTier { Bronze, Silver, Gold }

    internal readonly record struct ChestCandidate(
        uint Id,
        ChestTier Tier,
        string Content,
        int Quality,
        Vector2 Grid,
        float TerrainHeight,
        float Distance,
        int Priority);

    internal static bool TryParseChest(string metadata, out ChestTier tier, out string content, out int quality)
    {
        tier = ChestTier.Bronze;
        content = "";
        quality = 1;
        const string fragment = "/MarakethSanctum/";
        var index = metadata.IndexOf(fragment, StringComparison.Ordinal);
        if (index < 0) return false;
        var match = ChestRegex().Match(metadata[(index + fragment.Length)..]);
        if (!match.Success) return false;
        tier = match.Groups[1].Value switch
        {
            "Gold" => ChestTier.Gold,
            "Silver" => ChestTier.Silver,
            _ => ChestTier.Bronze,
        };
        content = match.Groups[2].Value;
        if (match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out var parsed))
            quality = parsed;
        return content.Length > 0;
    }

    internal static int ChestPriority(SekhemaSettings settings, string content)
    {
        if (content.Length == 0 || settings.ChestDisabledContent.Contains(content)) return 0;
        for (var i = 0; i < settings.ChestPriorityOrder.Count; i++)
        {
            if (string.Equals(settings.ChestPriorityOrder[i], content, StringComparison.OrdinalIgnoreCase))
                return settings.ChestPriorityOrder.Count - i;
        }
        return 0;
    }

    internal static ChestCandidate[] SelectChests(
        IEnumerable<ChestCandidate> source,
        int bronzeKeys,
        int silverKeys,
        int goldKeys)
    {
        var selected = new List<ChestCandidate>();
        Select(ChestTier.Bronze, bronzeKeys);
        Select(ChestTier.Silver, silverKeys);
        Select(ChestTier.Gold, goldKeys);
        return [.. selected];

        void Select(ChestTier tier, int keys)
        {
            if (keys <= 0) return;
            selected.AddRange(source
                .Where(chest => chest.Tier == tier && chest.Priority > 0)
                .OrderByDescending(chest => chest.Priority)
                .ThenByDescending(chest => chest.Quality)
                .ThenBy(chest => chest.Distance)
                .Take(keys));
        }
    }

    internal static List<int> PlayerCrystalRoom(
        IReadOnlyList<(uint Id, Vector2 Grid)> crystals,
        Vector2 player,
        int maxIdGap)
    {
        if (crystals.Count <= 1 || maxIdGap <= 0)
            return Enumerable.Range(0, crystals.Count).ToList();

        var sorted = Enumerable.Range(0, crystals.Count).OrderBy(i => crystals[i].Id).ToArray();
        var groups = new List<List<int>>();
        var current = new List<int> { sorted[0] };
        for (var i = 1; i < sorted.Length; i++)
        {
            if (crystals[sorted[i]].Id - crystals[sorted[i - 1]].Id > (uint)maxIdGap)
            {
                groups.Add(current);
                current = [];
            }
            current.Add(sorted[i]);
        }
        groups.Add(current);
        return groups.MinBy(group => group.Min(i => Vector2.DistanceSquared(player, crystals[i].Grid))) ?? [];
    }

    internal static bool PlayerInsideCrystalRoom(
        IReadOnlyList<Vector2> crystals,
        Vector2 player,
        float margin)
    {
        if (crystals.Count == 0) return false;
        var minX = crystals.Min(p => p.X);
        var minY = crystals.Min(p => p.Y);
        var maxX = crystals.Max(p => p.X);
        var maxY = crystals.Max(p => p.Y);
        return player.X >= minX - margin && player.X <= maxX + margin &&
               player.Y >= minY - margin && player.Y <= maxY + margin;
    }

    internal static List<int> SelectRouteCrystals(
        IReadOnlyList<(uint Id, Vector2 Grid, bool Active)> crystals,
        Vector2 player,
        SekhemaSettings settings)
    {
        var room = PlayerCrystalRoom(
            crystals.Select(crystal => (crystal.Id, crystal.Grid)).ToArray(),
            player,
            settings.HazardIdGroupGap);
        if (!PlayerInsideCrystalRoom(
                room.Select(index => crystals[index].Grid).ToArray(),
                player,
                settings.HazardRoomMargin))
            return [];

        return room
            .Where(index => crystals[index].Active)
            .ToList();
    }
}

internal readonly record struct SekhemaRouteLeg(Vector2[] Points, bool Walkable);

internal static class SekhemaRoutePlanner
{
    internal static SekhemaRouteLeg[] Build(
        Poe2Live.TerrainData? terrain,
        Vector2 player,
        IReadOnlyList<Vector2> crystals,
        IReadOnlyList<PathCell> forcedWalkable,
        bool followWalkable)
    {
        if (crystals.Count == 0) return [];
        if (!followWalkable || terrain is null)
            return BuildStraight(player, crystals, EuclideanOrder(crystals, player));

        var cells = new TerrainCellReader(terrain, forcedWalkable);
        var astar = new AStar(cells.Width, cells.Height);
        var pathCache = new Dictionary<(long, long), PathCell[]>();
        var order = RouteOrder(crystals, player, Distance);
        var legs = new SekhemaRouteLeg[order.Count];
        var from = player;
        for (var i = 0; i < order.Count; i++)
        {
            var to = crystals[order[i]];
            var path = Find(from, to);
            legs[i] = path.Length >= 2
                ? new SekhemaRouteLeg(path.Select(p => new Vector2(p.X, p.Y)).ToArray(), true)
                : new SekhemaRouteLeg([from, to], false);
            from = to;
        }
        return legs;

        float Distance(Vector2 a, Vector2 b)
        {
            var path = Find(a, b);
            if (path.Length < 2) return Vector2.Distance(a, b);
            float distance = 0;
            for (var i = 1; i < path.Length; i++)
                distance += Vector2.Distance(
                    new Vector2(path[i - 1].X, path[i - 1].Y),
                    new Vector2(path[i].X, path[i].Y));
            return distance;
        }

        PathCell[] Find(Vector2 a, Vector2 b)
        {
            if (Vector2.Distance(a, b) > 2500) return [];
            var key = Key(a, b);
            if (pathCache.TryGetValue(key, out var cached))
                return Orient(cached, a);

            var start = NearestWalkable(cells, a, 300);
            var end = NearestWalkable(cells, b, 300);
            if (start is null || end is null)
            {
                pathCache[key] = [];
                return [];
            }

            var found = astar.FindPath(cells, start.Value, end.Value, 1_000_000, flatCost: true);
            if (!found.Found)
            {
                pathCache[key] = [];
                return [];
            }
            var smooth = PathSmoother.Smooth(cells, found.Cells).ToArray();
            pathCache[key] = smooth;
            return Orient(smooth, a);
        }
    }

    private static SekhemaRouteLeg[] BuildStraight(Vector2 start, IReadOnlyList<Vector2> points, List<int> order)
    {
        var result = new SekhemaRouteLeg[order.Count];
        var from = start;
        for (var i = 0; i < order.Count; i++)
        {
            var to = points[order[i]];
            result[i] = new SekhemaRouteLeg([from, to], false);
            from = to;
        }
        return result;
    }

    private static PathCell? NearestWalkable(TerrainCellReader cells, Vector2 point, int radius)
    {
        var x = (int)MathF.Round(point.X);
        var y = (int)MathF.Round(point.Y);
        if (cells.Read(x, y) != 0) return new PathCell(x, y);
        for (var r = 1; r <= radius; r++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                if (cells.Read(x + dx, y - r) != 0) return new PathCell(x + dx, y - r);
                if (cells.Read(x + dx, y + r) != 0) return new PathCell(x + dx, y + r);
            }
            for (var dy = -r + 1; dy < r; dy++)
            {
                if (cells.Read(x - r, y + dy) != 0) return new PathCell(x - r, y + dy);
                if (cells.Read(x + r, y + dy) != 0) return new PathCell(x + r, y + dy);
            }
        }
        return null;
    }

    private static List<int> EuclideanOrder(IReadOnlyList<Vector2> points, Vector2 start)
        => RouteOrder(points, start, Vector2.Distance);

    private static List<int> RouteOrder(
        IReadOnlyList<Vector2> points,
        Vector2 start,
        Func<Vector2, Vector2, float> distance)
    {
        var order = new List<int>(points.Count);
        var used = new bool[points.Count];
        var current = start;
        for (var step = 0; step < points.Count; step++)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < points.Count; i++)
            {
                if (used[i]) continue;
                var candidate = distance(current, points[i]);
                if (candidate >= bestDistance) continue;
                bestDistance = candidate;
                best = i;
            }
            if (best < 0) break;
            used[best] = true;
            order.Add(best);
            current = points[best];
        }

        for (var iteration = 0; iteration < 64; iteration++)
        {
            var changed = false;
            for (var i = 0; i < order.Count - 1; i++)
            {
                var before = i == 0 ? start : points[order[i - 1]];
                for (var j = i + 1; j < order.Count; j++)
                {
                    var first = points[order[i]];
                    var last = points[order[j]];
                    var after = j + 1 < order.Count ? points[order[j + 1]] : last;
                    var oldDistance = distance(before, first) +
                                      (j + 1 < order.Count ? distance(last, after) : 0);
                    var newDistance = distance(before, last) +
                                      (j + 1 < order.Count ? distance(first, after) : 0);
                    if (newDistance + 0.01f >= oldDistance) continue;
                    order.Reverse(i, j - i + 1);
                    changed = true;
                }
            }
            if (!changed) break;
        }
        return order;
    }

    private static (long, long) Key(Vector2 a, Vector2 b)
    {
        var first = ((long)(int)a.X << 32) | (uint)(int)a.Y;
        var second = ((long)(int)b.X << 32) | (uint)(int)b.Y;
        return first <= second ? (first, second) : (second, first);
    }

    private static PathCell[] Orient(PathCell[] path, Vector2 start)
    {
        if (path.Length < 2) return path;
        var first = new Vector2(path[0].X, path[0].Y);
        var last = new Vector2(path[^1].X, path[^1].Y);
        if (Vector2.DistanceSquared(first, start) <= Vector2.DistanceSquared(last, start)) return path;
        var reversed = path.ToArray();
        Array.Reverse(reversed);
        return reversed;
    }
}
