using NumVec2 = System.Numerics.Vector2;
using POE2Radar.Core.Game;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private AtlasFogShip[] _atlasFogShipsPublish = Array.Empty<AtlasFogShip>();
    private AtlasLeylineSeg[] _atlasLeylinePublish = Array.Empty<AtlasLeylineSeg>();
    private AtlasIslandRumours.Manifest[] _atlasIslandManifestsPublish = Array.Empty<AtlasIslandRumours.Manifest>();
    private Dictionary<(int X, int Y), AtlasIslandRumours.Manifest>? _atlasIslandManifestsByChunk;
    private Dictionary<(int X, int Y), int>? _atlasIslandAnchorIndexByChunk;
    private HashSet<(int X, int Y)>? _atlasIslandPriorityChunks;
    private int _atlasIslandAnchorNodeCount;
    private string _atlasIslandPrioritySignature = "";
    private bool _atlasIslandManifestsReady;

    /// <summary>
    /// Atlas2 Uncharted Waters: ocean region buttons define revealed and fogged 16x16 chunks.
    /// If their layout drifts after a patch, fall back to the older hidden-ocean-node heuristic.
    /// </summary>
    private void UpdateAtlasUncharted(IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes)
    {
        var showRumours = _settings.AtlasShowIslandRumours;
        if (!_settings.AtlasShowShipsInFog && !_settings.AtlasShowUnchartedLeylines && !showRumours)
        {
            _atlasFogShipsPublish = Array.Empty<AtlasFogShip>();
            _atlasLeylinePublish = Array.Empty<AtlasLeylineSeg>();
            return;
        }

        if (showRumours)
            EnsureAtlasIslandRumours(nodes);

        // Rumours never add a process read. Reuse the existing ocean-button read when ships or
        // leylines already need it; otherwise anchor manifests to the normal Atlas node snapshot.
        List<AtlasFogShip> targets;
        if (_settings.AtlasShowShipsInFog || _settings.AtlasShowUnchartedLeylines)
        {
            var buttons = _atlas.ReadOceanButtons(OverlayWidth, OverlayHeight);
            targets = buttons.Count > 0
                ? BuildOceanButtonTargets(buttons, nodes)
                : BuildOceanNodeFallbackTargets(nodes);
        }
        else
        {
            targets = BuildRumourNodeTargets(nodes);
        }
        _atlasFogShipsPublish = targets.Count > 0 ? targets.ToArray() : Array.Empty<AtlasFogShip>();

        if (!_settings.AtlasShowUnchartedLeylines || targets.Count == 0)
        {
            _atlasLeylinePublish = Array.Empty<AtlasLeylineSeg>();
            return;
        }

        // Hover is resolved on the ImGui thread; prebuild graph segments for every ocean chunk.
        var byGrid = nodes
            .GroupBy(node => (node.GridX, node.GridY))
            .ToDictionary(group => group.Key, group => group.First());
        var segs = new List<AtlasLeylineSeg>(256);
        foreach (var chunk in targets.Select(target => (target.ChunkX, target.ChunkY)).Distinct())
        {
            var chunkNodes = nodes
                .Where(node => (node.GridX >> 4) == chunk.ChunkX
                    && (node.GridY >> 4) == chunk.ChunkY)
                .ToList();
            foreach (var a in chunkNodes)
            {
                if (!_atlas.TryGetNeighbors(a.GridX, a.GridY, out var neighbors)
                    || neighbors is null)
                    continue;
                foreach (var (nx, ny) in neighbors)
                {
                    if (nx < a.GridX || (nx == a.GridX && ny < a.GridY))
                        continue; // emit each undirected edge once
                    if ((nx >> 4) != chunk.ChunkX || (ny >> 4) != chunk.ChunkY)
                        continue;
                    if (!byGrid.TryGetValue((nx, ny), out var b))
                        continue;
                    var ca = a.ScreenCenter;
                    var cb = b.ScreenCenter;
                    if (!float.IsFinite(ca.X) || !float.IsFinite(ca.Y)
                        || !float.IsFinite(cb.X) || !float.IsFinite(cb.Y))
                        continue;
                    segs.Add(new AtlasLeylineSeg(
                        chunk.ChunkX,
                        chunk.ChunkY,
                        ca.X,
                        ca.Y,
                        cb.X,
                        cb.Y));
                }
            }
        }
        _atlasLeylinePublish = segs.Count > 0 ? segs.ToArray() : Array.Empty<AtlasLeylineSeg>();
    }

    private List<AtlasFogShip> BuildRumourNodeTargets(IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes)
    {
        if (_atlasIslandManifestsByChunk is not { Count: > 0 } manifestsByChunk
            || _atlasIslandAnchorIndexByChunk is not { Count: > 0 } anchorIndices)
            return new List<AtlasFogShip>(0);

        if (_atlasIslandAnchorNodeCount != nodes.Count)
        {
            RebuildAtlasIslandAnchorIndices(nodes, manifestsByChunk);
            anchorIndices = _atlasIslandAnchorIndexByChunk!;
        }

        var targets = new List<AtlasFogShip>(anchorIndices.Count);
        var anchorOrderDrifted = false;
        foreach (var (chunk, index) in anchorIndices)
        {
            if ((uint)index >= (uint)nodes.Count)
            {
                anchorOrderDrifted = true;
                continue;
            }
            var node = nodes[index];
            if ((node.GridX >> 4) != chunk.X || (node.GridY >> 4) != chunk.Y)
            {
                anchorOrderDrifted = true;
                continue;
            }
            var center = node.ScreenCenter;
            if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
                continue;
            targets.Add(AttachIslandRumours(new AtlasFogShip(
                chunk.X,
                chunk.Y,
                center.X,
                center.Y,
                _settings.AtlasShipIconSize,
                DrawIcon: true)));
        }
        if (anchorOrderDrifted)
            RebuildAtlasIslandAnchorIndices(nodes, manifestsByChunk);
        return targets;
    }

    private void EnsureAtlasIslandRumours(IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes)
    {
        if (!_atlasIslandManifestsReady && _atlas.AllTagsResolved)
        {
            _atlasIslandManifestsPublish = AtlasIslandRumours.Build(nodes);
            _atlasIslandManifestsByChunk = _atlasIslandManifestsPublish.ToDictionary(
                manifest => (manifest.ChunkX, manifest.ChunkY));
            RebuildAtlasIslandAnchorIndices(nodes, _atlasIslandManifestsByChunk);
            _atlasIslandManifestsReady = true;
            _atlasIslandPrioritySignature = "";
        }

        var signature = _settings.AtlasIslandRumourPriorityFilter ?? "";
        if (_atlasIslandManifestsReady
            && !string.Equals(signature, _atlasIslandPrioritySignature, StringComparison.Ordinal))
            RebuildAtlasIslandPriorities(signature);
    }

    private void RebuildAtlasIslandAnchorIndices(
        IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes,
        IReadOnlyDictionary<(int X, int Y), AtlasIslandRumours.Manifest> manifestsByChunk)
    {
        var anchors = new Dictionary<(int X, int Y), (int Index, int Distance)>(manifestsByChunk.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var chunk = (node.GridX >> 4, node.GridY >> 4);
            if (!manifestsByChunk.ContainsKey(chunk))
                continue;
            var dx = node.GridX - ((chunk.Item1 << 4) + 8);
            var dy = node.GridY - ((chunk.Item2 << 4) + 8);
            var distance = dx * dx + dy * dy;
            if (!anchors.TryGetValue(chunk, out var current) || distance < current.Distance)
                anchors[chunk] = (i, distance);
        }
        _atlasIslandAnchorIndexByChunk = anchors.ToDictionary(entry => entry.Key, entry => entry.Value.Index);
        _atlasIslandAnchorNodeCount = nodes.Count;
    }

    private void RebuildAtlasIslandPriorities(string signature)
    {
        var terms = signature.Split(
            ['|', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var priorityChunks = new HashSet<(int X, int Y)>();
        if (terms.Length > 0)
        {
            foreach (var manifest in _atlasIslandManifestsPublish)
            {
                foreach (var row in manifest.Rows)
                {
                    if (!terms.Any(term =>
                            row.Definition.Rumour.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || row.Definition.Destination.Contains(term, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    priorityChunks.Add((manifest.ChunkX, manifest.ChunkY));
                    break;
                }
            }
        }
        _atlasIslandPriorityChunks = priorityChunks;
        _atlasIslandPrioritySignature = signature;
    }

    private AtlasFogShip AttachIslandRumours(AtlasFogShip ship)
    {
        if (!_settings.AtlasShowIslandRumours
            || _atlasIslandManifestsByChunk is not { } manifestsByChunk
            || !manifestsByChunk.TryGetValue((ship.ChunkX, ship.ChunkY), out var manifest))
            return ship;
        return ship with
        {
            Manifest = manifest,
            Priority = _atlasIslandPriorityChunks?.Contains((ship.ChunkX, ship.ChunkY)) == true,
        };
    }

    private void ResetAtlasIslandRumours()
    {
        _atlasIslandManifestsPublish = Array.Empty<AtlasIslandRumours.Manifest>();
        _atlasIslandManifestsByChunk = null;
        _atlasIslandAnchorIndexByChunk = null;
        _atlasIslandPriorityChunks = null;
        _atlasIslandAnchorNodeCount = 0;
        _atlasIslandPrioritySignature = "";
        _atlasIslandManifestsReady = false;
    }

    private List<AtlasFogShip> BuildOceanButtonTargets(
        IReadOnlyList<Poe2Atlas.AtlasOceanButton> buttons,
        IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes)
    {
        var targets = new List<AtlasFogShip>(buttons.Count);
        var visibleChunks = buttons
            .Where(button => button.Visible)
            .Select(button => button.Chunk)
            .ToHashSet();

        // A visible region button is the original Atlas2 leyline hover target.
        if (_settings.AtlasShowUnchartedLeylines || _settings.AtlasShowIslandRumours)
        {
            foreach (var button in buttons.Where(button => button.Visible))
            {
                if (!float.IsFinite(button.ScreenX) || !float.IsFinite(button.ScreenY)
                    || !float.IsFinite(button.ScreenW) || !float.IsFinite(button.ScreenH)
                    || button.ScreenW <= 0f || button.ScreenH <= 0f)
                    continue;
                targets.Add(AttachIslandRumours(new AtlasFogShip(
                    button.Chunk.X,
                    button.Chunk.Y,
                    button.ScreenX + button.ScreenW * 0.5f,
                    button.ScreenY + button.ScreenH * 0.5f,
                    _settings.AtlasShipIconSize,
                    DrawIcon: false,
                    HitWidth: button.ScreenW,
                    HitHeight: button.ScreenH)));
            }
        }

        var byGrid = nodes
            .GroupBy(node => (node.GridX, node.GridY))
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var group in buttons
                     .Where(button => !button.Visible && !visibleChunks.Contains(button.Chunk))
                     .GroupBy(button => button.Chunk))
        {
            var first = group.First();
            var anchorGrid = (first.GridX, first.GridY);
            Poe2Atlas.AtlasNodeLive? anchor = null;
            if (byGrid.TryGetValue(anchorGrid, out var exact))
            {
                anchor = exact;
            }
            else
            {
                anchor = nodes
                    .Where(node => (node.GridX >> 4) == group.Key.X
                        && (node.GridY >> 4) == group.Key.Y)
                    .OrderBy(node =>
                    {
                        var dx = node.GridX - anchorGrid.GridX;
                        var dy = node.GridY - anchorGrid.GridY;
                        return dx * dx + dy * dy;
                    })
                    .FirstOrDefault();
            }

            if (anchor is not { } anchorNode)
                continue;
            var center = anchorNode.ScreenCenter;
            if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
                continue;
            targets.Add(AttachIslandRumours(new AtlasFogShip(
                group.Key.X,
                group.Key.Y,
                center.X,
                center.Y,
                _settings.AtlasShipIconSize,
                DrawIcon: _settings.AtlasShowShipsInFog || _settings.AtlasShowIslandRumours)));
        }
        return targets;
    }

    private List<AtlasFogShip> BuildOceanNodeFallbackTargets(
        IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes)
    {
        // Ocean biome id 10 in Atlas2 biome.json.
        const byte oceanBiome = 10;
        var targets = new List<AtlasFogShip>();
        foreach (var group in nodes
                     .Where(node => !node.Visible && node.Biome == oceanBiome)
                     .GroupBy(node => (node.GridX >> 4, node.GridY >> 4)))
        {
            var anchor = group.OrderBy(node => Math.Abs(node.GridX) + Math.Abs(node.GridY)).First();
            var center = anchor.ScreenCenter;
            if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
                continue;
            targets.Add(AttachIslandRumours(new AtlasFogShip(
                group.Key.Item1,
                group.Key.Item2,
                center.X,
                center.Y,
                _settings.AtlasShipIconSize,
                DrawIcon: _settings.AtlasShowShipsInFog || _settings.AtlasShowIslandRumours)));
        }
        return targets;
    }
}

public readonly record struct AtlasFogShip(
    int ChunkX,
    int ChunkY,
    float ScreenX,
    float ScreenY,
    float Size,
    bool DrawIcon = true,
    float HitWidth = 0f,
    float HitHeight = 0f,
    AtlasIslandRumours.Manifest? Manifest = null,
    bool Priority = false);

public readonly record struct AtlasLeylineSeg(
    int ChunkX,
    int ChunkY,
    float X0,
    float Y0,
    float X1,
    float Y1);
