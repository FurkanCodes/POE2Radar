// Sekhema-specific behavior is derived from MordWraith/Gamehelper's GPL-3.0 SekhemaHelper.
// Upstream snapshot: 7e7a23571c494090cbc6a7faafa633e17762a78d. See the bundled notice/license.
using System.Numerics;
using System.Text;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Sekhema;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private const string SekhemaPortalPath =
        "Metadata/Monsters/MarakethSanctumTrial/Hazards/PortalPlatform";
    private const string SekhemaLeverPath =
        "Metadata/Terrain/Gallows/Leagues/Sanctum/Objects/SanctumGenericLever";
    private const string SekhemaCrystalSuffix = "Hazards/HourglassLethal";

    private SekhemaView _sekhemaView = SekhemaView.Empty;
    private Poe2Live.SekhemaFloorRead _sekhemaFloor = Poe2Live.SekhemaFloorRead.Closed;
    private Poe2Live.SekhemaResources _sekhemaResources = Poe2Live.SekhemaResources.Unknown;
    private readonly HashSet<uint> _sekhemaUsedObjects = [];
    private uint _sekhemaAreaHash;
    private DateTime _sekhemaNextReadUtc = DateTime.MinValue;
    private Task<SekhemaRouteLeg[]>? _sekhemaRouteTask;
    private string _sekhemaRouteTaskSignature = "";
    private string _sekhemaRouteRequestSignature = "";
    private string _sekhemaRouteSignature = "";
    private SekhemaRouteLeg[] _sekhemaRoute = [];
    private int _sekhemaReadCrashLogged;
    private int _sekhemaRouteCrashLogged;

    private void RefreshSekhemaSafe(
        LiveFrameState live,
        WorldSnapshot snapshot,
        int windowWidth,
        int windowHeight,
        bool drawActive)
    {
        try
        {
            RefreshSekhema(live, snapshot, windowWidth, windowHeight, drawActive);
        }
        catch (Exception ex)
        {
            ClearSekhema();
            if (Interlocked.Exchange(ref _sekhemaReadCrashLogged, 1) == 0)
                Diagnostics.CrashLog.Write("Sekhema read error (base radar kept alive)", ex);
        }
    }

    private void RefreshSekhema(
        LiveFrameState live,
        WorldSnapshot snapshot,
        int windowWidth,
        int windowHeight,
        bool drawActive)
    {
        var settings = _settings.Sekhema;
        if (!settings.Enabled || !live.InGame || !drawActive)
        {
            ClearSekhema();
            return;
        }

        if (_sekhemaAreaHash != snapshot.AreaHash)
        {
            _sekhemaAreaHash = snapshot.AreaHash;
            _sekhemaUsedObjects.Clear();
            _sekhemaResources = Poe2Live.SekhemaResources.Unknown;
            _sekhemaRoute = [];
            _sekhemaRouteRequestSignature = "";
            _sekhemaRouteSignature = "";
        }

        CompleteSekhemaRoute();
        var now = DateTime.UtcNow;
        if (now < _sekhemaNextReadUtc) return;
        _sekhemaNextReadUtc = now.AddMilliseconds(125);

        _sekhemaFloor = _live.ReadSekhemaFloor(
            live.InGameState,
            live.LocalPlayer,
            windowWidth,
            windowHeight);
        _sekhemaResources = MergeSekhemaResources(_sekhemaResources, _sekhemaFloor.Resources);
        if (!IsSekhemaContext(snapshot, _sekhemaFloor))
        {
            ClearSekhema(resetCadence: false);
            return;
        }

        var roomHighlights = BuildSekhemaRoomHighlights(_sekhemaFloor, settings);
        var markers = new List<SekhemaMapMarker>();
        var crystals = new List<(uint Id, NumVec2 Grid, float Height, nint Address, bool Active)>();
        var chestCandidates = new List<SekhemaLogic.ChestCandidate>();

        foreach (var entity in snapshot.SekhemaEntities)
        {
            var metadata = entity.Metadata;
            if (settings.DrawHazardRoute &&
                metadata.EndsWith(SekhemaCrystalSuffix, StringComparison.Ordinal))
            {
                crystals.Add((
                    entity.Id,
                    entity.Grid,
                    entity.TerrainHeight,
                    entity.Address,
                    _live.IsSekhemaCrystalActive(entity.Address)));
                continue;
            }

            var portal = settings.ShowPortals &&
                         string.Equals(metadata, SekhemaPortalPath, StringComparison.Ordinal);
            var lever = settings.ShowLevers &&
                        string.Equals(metadata, SekhemaLeverPath, StringComparison.Ordinal);
            if (portal || lever)
            {
                if (!_sekhemaUsedObjects.Contains(entity.Id) && _live.IsSekhemaOneShotUsed(entity.Address))
                    _sekhemaUsedObjects.Add(entity.Id);
                if (!_sekhemaUsedObjects.Contains(entity.Id))
                {
                    markers.Add(new SekhemaMapMarker(
                        entity.Grid,
                        entity.TerrainHeight,
                        portal ? SekhemaMarkerKind.Portal : SekhemaMarkerKind.Lever,
                        portal ? "Portal" : "Lever"));
                }
                continue;
            }

            if (!settings.DrawChestPriority || entity.Opened ||
                !SekhemaLogic.TryParseChest(metadata, out var tier, out var content, out var quality))
                continue;
            var priority = SekhemaLogic.ChestPriority(settings, content);
            chestCandidates.Add(new SekhemaLogic.ChestCandidate(
                entity.Id,
                tier,
                content,
                quality,
                entity.Grid,
                entity.TerrainHeight,
                NumVec2.Distance(live.PlayerGrid, entity.Grid),
                priority));
        }

        foreach (var chest in SekhemaLogic.SelectChests(
                     chestCandidates,
                     _sekhemaResources.BronzeKeys,
                     _sekhemaResources.SilverKeys,
                     _sekhemaResources.GoldKeys))
        {
            var suffix = chest.Quality == 3 ? " P" : chest.Quality == 2 ? " S" : "";
            markers.Add(new SekhemaMapMarker(
                chest.Grid,
                chest.TerrainHeight,
                chest.Tier switch
                {
                    SekhemaLogic.ChestTier.Gold => SekhemaMarkerKind.ChestGold,
                    SekhemaLogic.ChestTier.Silver => SekhemaMarkerKind.ChestSilver,
                    _ => SekhemaMarkerKind.ChestBronze,
                },
                chest.Content + suffix));
        }

        var activeCrystals = SelectActiveSekhemaCrystals(crystals, live.PlayerGrid, settings);
        StartSekhemaRoute(snapshot, live.PlayerGrid, activeCrystals, settings);
        CompleteSekhemaRoute();
        var routeView = BuildSekhemaRouteView(_sekhemaRoute, activeCrystals, live.PlayerTerrainHeight);
        var crystalView = settings.Debug
            ? crystals.Select(crystal => new SekhemaCrystalView(
                crystal.Id,
                crystal.Grid,
                crystal.Height,
                crystal.Active)).ToArray()
            : [];

        _sekhemaView = new SekhemaView(
            true,
            _sekhemaFloor.IsOpen,
            roomHighlights,
            [.. markers],
            crystalView,
            routeView,
            _sekhemaResources,
            _sekhemaFloor.Status);
    }

    private static bool IsSekhemaContext(
        WorldSnapshot snapshot,
        Poe2Live.SekhemaFloorRead floor)
    {
        if (snapshot.AreaCode.StartsWith("Sanctum_", StringComparison.OrdinalIgnoreCase))
            return true;
        return floor.IsOpen || snapshot.SekhemaEntities.Length > 0;
    }

    private static bool IsSekhemaEntity(Poe2Live.EntityDot entity)
        => !entity.IsSleeping &&
           (entity.Metadata.Contains("MarakethSanctum", StringComparison.OrdinalIgnoreCase) ||
            entity.Metadata.Contains("/Sanctum/", StringComparison.OrdinalIgnoreCase));

    private SekhemaRoomHighlight[] BuildSekhemaRoomHighlights(
        Poe2Live.SekhemaFloorRead floor,
        Config.SekhemaSettings settings)
    {
        if (!floor.IsOpen || !floor.IsValid) return [];
        var weights = new Dictionary<(int Layer, int Room), double>();
        var debug = new Dictionary<(int Layer, int Room), string>();
        foreach (var layer in floor.Layers)
        foreach (var room in layer)
        {
            var score = SekhemaLogic.ScoreRoom(room, settings, _sekhemaResources, floor.PlayerStats);
            weights[(room.Layer, room.Index)] = score.Weight;
            debug[(room.Layer, room.Index)] = score.Debug;
        }

        var best = settings.DrawBestPath
            ? SekhemaLogic.FindBestPath(floor, weights).ToHashSet()
            : [];
        var highlights = new List<SekhemaRoomHighlight>();
        foreach (var layer in floor.Layers)
        foreach (var room in layer)
        {
            if (!room.HasWidget) continue;
            var isCurrent = room.Layer == floor.PlayerLayer && room.Index == floor.PlayerRoom;
            var onBestPath = !isCurrent && best.Contains((room.Layer, room.Index));
            var debugText = settings.Debug
                ? $"W:{weights[(room.Layer, room.Index)]:F0}\n{debug[(room.Layer, room.Index)]}"
                : "";
            if (onBestPath || debugText.Length > 0)
                highlights.Add(new SekhemaRoomHighlight(room.WidgetRect, onBestPath, debugText));
        }
        return [.. highlights];
    }

    private List<(uint Id, NumVec2 Grid, float Height)> SelectActiveSekhemaCrystals(
        IReadOnlyList<(uint Id, NumVec2 Grid, float Height, nint Address, bool Active)> all,
        NumVec2 player,
        Config.SekhemaSettings settings)
    {
        if (all.Count == 0) return [];

        var forcedIds = ParseSekhemaIds(settings.Debug && settings.HazardDebugCrystalIds.Length > 0
            ? settings.HazardDebugCrystalIds
            : "");
        if (forcedIds.Count > 0)
            return all.Where(crystal => forcedIds.Contains(crystal.Id))
                .Select(crystal => (crystal.Id, crystal.Grid, crystal.Height))
                .ToList();

        var routeIndices = SekhemaLogic.SelectRouteCrystals(
            all.Select(crystal => (crystal.Id, crystal.Grid, crystal.Active)).ToArray(),
            player,
            settings);
        return routeIndices
            .Select(index => all[index])
            .Select(crystal => (crystal.Id, crystal.Grid, crystal.Height))
            .ToList();
    }

    private void StartSekhemaRoute(
        WorldSnapshot snapshot,
        NumVec2 player,
        IReadOnlyList<(uint Id, NumVec2 Grid, float Height)> crystals,
        Config.SekhemaSettings settings)
    {
        if (!settings.DrawHazardRoute || crystals.Count == 0)
        {
            _sekhemaRoute = [];
            _sekhemaRouteRequestSignature = "";
            _sekhemaRouteSignature = "";
            return;
        }

        var signature = new StringBuilder()
            .Append(snapshot.AreaHash).Append('|')
            .Append(settings.HazardWalkableRoute ? 'W' : 'S').Append('|')
            .Append((int)(player.X / 20)).Append(',').Append((int)(player.Y / 20)).Append('|');
        foreach (var crystal in crystals.OrderBy(crystal => crystal.Id))
            signature.Append(crystal.Id).Append('@').Append((int)crystal.Grid.X).Append(',')
                .Append((int)crystal.Grid.Y).Append(';');
        var value = signature.ToString();
        _sekhemaRouteRequestSignature = value;
        if (_sekhemaRouteTask is { IsCompleted: false }) return;
        if (value == _sekhemaRouteSignature) return;
        _sekhemaRouteTaskSignature = value;

        var terrain = snapshot.Terrain;
        var points = crystals.Select(crystal => crystal.Grid).ToArray();
        var doors = terrain is null ? [] : snapshot.DoorOverrides;
        var follow = settings.HazardWalkableRoute;
        _sekhemaRouteTask = Task.Run(() =>
            SekhemaRoutePlanner.Build(terrain, player, points, doors, follow));
    }

    private void CompleteSekhemaRoute()
    {
        if (_sekhemaRouteTask is not { IsCompleted: true } completed) return;
        if (completed.IsCompletedSuccessfully &&
            string.Equals(_sekhemaRouteTaskSignature, _sekhemaRouteRequestSignature, StringComparison.Ordinal))
        {
            _sekhemaRoute = completed.Result;
            _sekhemaRouteSignature = _sekhemaRouteTaskSignature;
        }
        else if (completed.IsFaulted &&
                 Interlocked.Exchange(ref _sekhemaRouteCrashLogged, 1) == 0)
        {
            Diagnostics.CrashLog.Write(
                "Sekhema route compute error (straight/map overlay kept alive)",
                completed.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown route task failure"));
        }
        _sekhemaRouteTask = null;
        _sekhemaRouteTaskSignature = "";
    }

    private static SekhemaRouteLegView[] BuildSekhemaRouteView(
        IReadOnlyList<SekhemaRouteLeg> legs,
        IReadOnlyList<(uint Id, NumVec2 Grid, float Height)> crystals,
        float playerHeight)
    {
        if (legs.Count == 0) return [];
        var result = new SekhemaRouteLegView[legs.Count];
        var startHeight = playerHeight;
        for (var i = 0; i < legs.Count; i++)
        {
            var endpoint = legs[i].Points.Length > 0 ? legs[i].Points[^1] : NumVec2.Zero;
            var endHeight = crystals
                .OrderBy(crystal => NumVec2.DistanceSquared(crystal.Grid, endpoint))
                .Select(crystal => crystal.Height)
                .FirstOrDefault(startHeight);
            var straightDistance = legs[i].Points.Length >= 2
                ? NumVec2.Distance(legs[i].Points[0], legs[i].Points[^1])
                : 0f;
            result[i] = new SekhemaRouteLegView(
                legs[i].Points,
                startHeight,
                endHeight,
                legs[i].Walkable,
                straightDistance);
            startHeight = endHeight;
        }
        return result;
    }

    private static HashSet<uint> ParseSekhemaIds(string raw)
    {
        var result = new HashSet<uint>();
        foreach (var token in raw.Split([',', ' ', ';', '\t'], StringSplitOptions.RemoveEmptyEntries))
            if (uint.TryParse(token, out var id)) result.Add(id);
        return result;
    }

    private static Poe2Live.SekhemaResources MergeSekhemaResources(
        Poe2Live.SekhemaResources oldValue,
        Poe2Live.SekhemaResources newValue)
        => new(
            newValue.Water >= 0 ? newValue.Water : oldValue.Water,
            newValue.BronzeKeys >= 0 ? newValue.BronzeKeys : oldValue.BronzeKeys,
            newValue.SilverKeys >= 0 ? newValue.SilverKeys : oldValue.SilverKeys,
            newValue.GoldKeys >= 0 ? newValue.GoldKeys : oldValue.GoldKeys,
            newValue.HonourPercent >= 0 ? newValue.HonourPercent : oldValue.HonourPercent);

    private void ClearSekhema(bool resetCadence = true)
    {
        _sekhemaView = SekhemaView.Empty;
        _sekhemaFloor = Poe2Live.SekhemaFloorRead.Closed;
        if (resetCadence)
            _sekhemaNextReadUtc = DateTime.MinValue;
        _sekhemaRoute = [];
        _sekhemaRouteRequestSignature = "";
        _sekhemaRouteSignature = "";
    }
}
