// Sekhema-specific behavior is derived from MordWraith/Gamehelper's GPL-3.0 SekhemaHelper.
// Upstream snapshot: 7e7a23571c494090cbc6a7faafa633e17762a78d. See the bundled notice/license.
using System.Numerics;
using POE2Radar.Core.Pathfinding;

namespace POE2Radar.Core.Game;

public sealed partial class Poe2Live
{
    public sealed class SekhemaRoomRead
    {
        public int Layer { get; init; }
        public int Index { get; init; }
        public int[] NextConnections { get; init; } = [];
        public bool IsChosen { get; set; }
        public nint Address { get; init; }
        public nint WidgetAddress { get; set; }
        public UiRect WidgetRect { get; set; }
        public bool HasWidget { get; set; }
        public string RoomType { get; set; } = "";
        public string Reward { get; set; } = "";
        public string Affliction { get; set; } = "";
    }

    public readonly record struct SekhemaResources(
        int Water,
        int BronzeKeys,
        int SilverKeys,
        int GoldKeys,
        double HonourPercent)
    {
        public static readonly SekhemaResources Unknown = new(-1, -1, -1, -1, -1);
    }

    public readonly record struct SekhemaPlayerStats(
        int Evasion,
        int EnergyShield,
        int Armour,
        int Life,
        bool HasQueenOfTheForest);

    public sealed record SekhemaFloorRead(
        bool IsOpen,
        bool IsValid,
        nint PanelAddress,
        nint FloorDataAddress,
        int PlayerLayer,
        int PlayerRoom,
        SekhemaRoomRead[][] Layers,
        SekhemaResources Resources,
        SekhemaPlayerStats PlayerStats,
        string Status)
    {
        public static readonly SekhemaFloorRead Closed = new(
            false, false, 0, 0, -1, -1, [], SekhemaResources.Unknown, default, "Sekhema map closed");
    }

    private static readonly int[] SekhemaWaterPath = [1, 0, 0, 1];
    private static readonly int[] SekhemaBronzePath = [1, 0, 1, 1];
    private static readonly int[] SekhemaSilverPath = [1, 0, 2, 1];
    private static readonly int[] SekhemaGoldPath = [1, 0, 3, 1];
    private static readonly int[] SekhemaHudBronzePath = [13, 1, 1];
    private static readonly int[] SekhemaHudSilverPath = [13, 2, 1];
    private static readonly int[] SekhemaHudGoldPath = [13, 3, 1];
    private static readonly int[] SekhemaHonourPath = [13, 5, 1];
    private static readonly uint[] SekhemaWaterFingerprint =
    [
        Poe2.Sekhema.WaterFp0,
        Poe2.Sekhema.WaterFp1,
        Poe2.Sekhema.WaterFp2,
        Poe2.Sekhema.WaterFp3,
    ];
    private static readonly uint[] SekhemaHonourFingerprint =
    [
        Poe2.Sekhema.HonourFp0,
        Poe2.Sekhema.HonourFp1,
        Poe2.Sekhema.HonourFp2,
    ];

    private static readonly Dictionary<string, string> SekhemaRoomAliases = new(StringComparer.Ordinal)
    {
        ["Arena"] = "Hourglass",
        ["Lair"] = "Chalice",
        ["Explore"] = "Escape",
    };
    private GameCullResolver? _sekhemaGameCull;

    public SekhemaFloorRead ReadSekhemaFloor(
        nint inGameState,
        nint localPlayer,
        float windowWidth,
        float windowHeight)
    {
        if (inGameState == 0) return SekhemaFloorRead.Closed;

        ResolveSekhemaUi(inGameState, out var gameUi, out var panel);

        var resources = ReadSekhemaResources(gameUi, panel);
        var stats = ReadSekhemaPlayerStats(localPlayer);
        if (panel == 0 || !UiElementVisibility.IsHierarchicallyVisible(_reader, panel))
            return SekhemaFloorRead.Closed with
            {
                PanelAddress = panel,
                Resources = resources,
                PlayerStats = stats,
                Status = panel == 0 ? "Sekhema panel unavailable" : "Sekhema map closed",
            };

        if (!TryResolveSekhemaFloorData(panel, out var floorData, out var layerCount))
        {
            var parent = Ptr(panel + Poe2.UiElement.Parent);
            if (!TryResolveSekhemaFloorData(parent, out floorData, out layerCount))
                return new SekhemaFloorRead(
                    true, false, panel, 0, -1, -1, [], resources, stats,
                    $"Floor data unavailable (panel 0x{panel:X})");
        }

        if (!_reader.TryReadStruct<StdVector>(floorData + Poe2.Sekhema.FloorLayers, out var layersVector))
            return new SekhemaFloorRead(
                true, false, panel, floorData, -1, -1, [], resources, stats, "Floor layer vector unreadable");

        var layers = new SekhemaRoomRead[layerCount][];
        for (var layer = 0; layer < layerCount; layer++)
        {
            var layerAddress = layersVector.First + (nint)(layer * Poe2.Sekhema.LayerStride);
            if (!_reader.TryReadStruct<StdVector>(layerAddress, out var roomsVector))
            {
                layers[layer] = [];
                continue;
            }

            var roomCount = VectorCount(roomsVector, Poe2.Sekhema.RoomStride, 64);
            var rooms = new SekhemaRoomRead[roomCount];
            for (var room = 0; room < roomCount; room++)
            {
                var roomAddress = roomsVector.First + (nint)(room * Poe2.Sekhema.RoomStride);
                var connections = ReadSekhemaConnections(roomAddress);
                rooms[room] = new SekhemaRoomRead
                {
                    Layer = layer,
                    Index = room,
                    Address = roomAddress,
                    NextConnections = connections,
                };
            }
            layers[layer] = rooms;
        }

        var counter = 0;
        _reader.TryReadStruct<byte>(floorData + Poe2.Sekhema.FloorCounter, out var counterByte);
        counter = counterByte & 7;
        for (var layer = 0; layer < layers.Length; layer++)
        {
            if (!_reader.TryReadStruct<byte>(floorData + Poe2.Sekhema.FloorChoices + layer, out var chosen))
                continue;
            if (chosen != byte.MaxValue && chosen < layers[layer].Length)
                layers[layer][chosen].IsChosen = true;
        }

        var playerLayer = counter > 0 && counter - 1 < layers.Length ? counter - 1 : -1;
        var playerRoom = -1;
        if (playerLayer >= 0 &&
            _reader.TryReadStruct<byte>(floorData + Poe2.Sekhema.FloorChoices + playerLayer, out var current) &&
            current != byte.MaxValue)
            playerRoom = current;

        var widgetCount = AttachSekhemaWidgets(panel, layers, windowWidth, windowHeight);
        ClassifySekhemaRooms(floorData, layers);
        return new SekhemaFloorRead(
            true,
            layers.Length > 0,
            panel,
            floorData,
            playerLayer,
            playerRoom,
            layers,
            resources,
            stats,
            $"layers={layers.Length} widgets={widgetCount} player=({playerLayer},{playerRoom})");
    }

    public SekhemaResources ReadSekhemaResources(nint inGameState)
    {
        if (inGameState == 0) return SekhemaResources.Unknown;
        ResolveSekhemaUi(inGameState, out var gameUi, out var panel);
        return ReadSekhemaResources(gameUi, panel);
    }

    public bool IsSekhemaOneShotUsed(nint entity)
    {
        var stateMachine = ResolveComponent(entity, "StateMachine");
        return stateMachine != 0 &&
               _reader.TryReadStruct<byte>(stateMachine + Poe2.StateMachine.Used, out var used) &&
               used != 0;
    }

    public bool IsSekhemaCrystalActive(nint entity)
    {
        var state = ReadSekhemaCrystalState(entity);
        if (state.StateMachine == 0) return false;
        if (state.UsedRead && state.Used != 0) return false;

        var hasDeactivated = false;
        var deactivated = false;
        var hasTargetable = false;
        var targetable = false;
        foreach (var value in state.States)
        {
            if (string.Equals(value.Name, "deactivated", StringComparison.Ordinal))
            {
                hasDeactivated = true;
                deactivated = value.Value != 0;
            }
            else if (string.Equals(value.Name, "targetable", StringComparison.Ordinal))
            {
                hasTargetable = true;
                targetable = value.Value != 0;
            }
        }
        return hasDeactivated ? !deactivated : hasTargetable && targetable;
    }

    public SekhemaCrystalStateRead ReadSekhemaCrystalState(nint entity)
    {
        var stateMachine = ResolveComponent(entity, "StateMachine");
        if (stateMachine == 0)
            return new SekhemaCrystalStateRead(0, false, 0, [], "");

        var usedRead = _reader.TryReadStruct<byte>(
            stateMachine + Poe2.StateMachine.Used,
            out var used);
        var states = ReadStateMachineStates(stateMachine, out var dump);
        return new SekhemaCrystalStateRead(stateMachine, usedRead, used, states, dump);
    }

    public readonly record struct SekhemaCrystalStateRead(
        nint StateMachine,
        bool UsedRead,
        byte Used,
        RunecraftStateValue[] States,
        string Dump);

    public PathCell[] ReadSekhemaDoorOverrides(IEnumerable<EntityDot> entities)
    {
        var cells = new HashSet<PathCell>();
        foreach (var entity in entities)
        {
            if (entity.Address == 0) continue;
            var isDoor = entity.Metadata.Contains("Door", StringComparison.OrdinalIgnoreCase);
            if (!isDoor && ResolveComponent(entity.Address, "TriggerableBlockage") == 0) continue;
            var gx = (int)MathF.Round(entity.Grid.X);
            var gy = (int)MathF.Round(entity.Grid.Y);
            for (var y = -2; y <= 2; y++)
            for (var x = -2; x <= 2; x++)
                cells.Add(new PathCell(gx + x, gy + y));
        }
        return [.. cells];
    }

    private nint ResolveSekhemaPanel(nint gameUi)
    {
        var branch = ChildAt(gameUi, Poe2.Sekhema.GameUiPanelChild);
        return ChildAt(branch, Poe2.Sekhema.PanelChild);
    }

    private void ResolveSekhemaUi(nint inGameState, out nint gameUi, out nint panel)
    {
        DiscoverGameUiAnchors(inGameState, out var keyboardUi, out var controllerUi);
        gameUi = keyboardUi;
        panel = ResolveSekhemaPanel(keyboardUi);
        if (panel != 0) return;

        var controllerPanel = ResolveSekhemaPanel(controllerUi);
        if (controllerPanel != 0)
        {
            gameUi = controllerUi;
            panel = controllerPanel;
            return;
        }

        if (gameUi == 0)
            gameUi = controllerUi;
    }

    private bool TryResolveSekhemaFloorData(nint panel, out nint floorData, out int layerCount)
    {
        floorData = 0;
        layerCount = 0;
        if (panel == 0) return false;
        var floorObject = Ptr(panel + Poe2.Sekhema.PanelFloorObject);
        if (floorObject == 0) return false;

        _reader.TryReadStruct<byte>(floorObject + Poe2.Sekhema.FloorObjectVariantFlag, out var variant);
        var firstOffset = variant != 0 ? Poe2.Sekhema.FloorDataActive : Poe2.Sekhema.FloorDataAlternate;
        var secondOffset = variant != 0 ? Poe2.Sekhema.FloorDataAlternate : Poe2.Sekhema.FloorDataActive;
        if (TryFloorData(floorObject + firstOffset, out layerCount))
        {
            floorData = floorObject + firstOffset;
            return true;
        }
        if (!TryFloorData(floorObject + secondOffset, out layerCount)) return false;
        floorData = floorObject + secondOffset;
        return true;
    }

    private bool TryFloorData(nint address, out int layerCount)
    {
        layerCount = 0;
        if (!_reader.TryReadStruct<StdVector>(address + Poe2.Sekhema.FloorLayers, out var vector))
            return false;
        layerCount = VectorCount(vector, Poe2.Sekhema.LayerStride, 64);
        return layerCount > 0;
    }

    private int[] ReadSekhemaConnections(nint roomAddress)
    {
        var first = Ptr(roomAddress);
        if (first == 0 || !_reader.TryReadStruct<nint>(roomAddress + 8, out var last)) return [];
        var count = (long)last - (long)first;
        if (count <= 0 || count > 16) return [];
        var bytes = new byte[count];
        if (_reader.TryReadBytes(first, bytes) != bytes.Length) return [];
        var result = new int[bytes.Length];
        for (var i = 0; i < bytes.Length; i++) result[i] = bytes[i];
        return result;
    }

    private int AttachSekhemaWidgets(
        nint panel,
        SekhemaRoomRead[][] layers,
        float windowWidth,
        float windowHeight)
    {
        var container = FindSekhemaLayersContainer(panel, layers.Length);
        if (container == 0) return 0;
        var count = 0;
        var horizontalCull = (_sekhemaGameCull ??= new GameCullResolver(_reader))
            .Read(windowWidth, windowHeight);
        var elementCache = new Dictionary<nint, UiElementProjection.Element>(128);
        var parentCache = new Dictionary<nint, UiElementProjection.Point>(32);
        for (var layer = 0; layer < layers.Length; layer++)
        {
            var layerWidget = ChildAt(container, layer);
            if (layerWidget == 0) continue;
            for (var room = 0; room < layers[layer].Length; room++)
            {
                var widget = ChildAt(layerWidget, room);
                if (widget == 0) continue;
                var target = layers[layer][room];
                target.WidgetAddress = widget;
                if (IsUiElementVisible(widget) &&
                    UiElementProjection.TryGetRect(
                        widget,
                        TryReadProjectionElement,
                        windowWidth,
                        windowHeight,
                        elementCache,
                        parentCache,
                        out var rect,
                        horizontalCull))
                {
                    target.WidgetRect = new UiRect(rect.X, rect.Y, rect.W, rect.H);
                    target.HasWidget = true;
                    count++;
                }
            }
        }
        return count;

        bool TryReadProjectionElement(nint address, out UiElementProjection.Element element)
            => UiElementProjection.TryReadBatch(_reader, address, out element);
    }

    private nint FindSekhemaLayersContainer(nint root, int layerCount)
    {
        if (root == 0 || layerCount <= 0) return 0;
        var queue = new Queue<(nint Address, int Depth)>();
        var visited = new HashSet<nint>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (address, depth) = queue.Dequeue();
            if (address == 0 || depth > 5 || !visited.Add(address)) continue;
            if (!TryChildVector(address, out var children, out var count)) continue;
            if (count == layerCount)
            {
                var first = ChildAt(address, 0);
                if (first != 0 && TryChildVector(first, out _, out var childCount) && childCount > 0)
                    return address;
            }
            if (count > 64) continue;
            for (var i = 0; i < count; i++)
            {
                var child = Ptr(children.First + (nint)(i * 8));
                if (child != 0) queue.Enqueue((child, depth + 1));
            }
        }
        return 0;
    }

    private void ClassifySekhemaRooms(nint floorData, SekhemaRoomRead[][] layers)
    {
        if (!_reader.TryReadStruct<StdVector>(
                floorData + Poe2.Sekhema.FloorClassifications,
                out var classifications))
            return;
        var count = VectorCount(classifications, Poe2.Sekhema.ClassificationStride, 512);
        for (var i = 0; i < count; i++)
        {
            var entry = classifications.First + (nint)(i * Poe2.Sekhema.ClassificationStride);
            _reader.TryReadStruct<byte>(entry, out var layer);
            _reader.TryReadStruct<byte>(entry + 1, out var room);
            if (layer >= layers.Length || room >= layers[layer].Length) continue;
            var target = layers[layer][room];

            for (var slot = 0; slot < 3; slot++)
            {
                var row = Ptr(entry + 8 + slot * 16);
                var table = Ptr(entry + 16 + slot * 16);
                if (row == 0 || table == 0) continue;
                var tableNamePtr = Ptr(table + 8);
                if (tableNamePtr == 0) continue;
                var tableName = _reader.ReadStringUtf16(tableNamePtr, 96);
                if (tableName.Contains("SanctumPersistentEffects", StringComparison.OrdinalIgnoreCase))
                {
                    var afflictionPtr = Ptr(row + 40);
                    if (afflictionPtr != 0)
                        target.Affliction = _reader.ReadStringUtf16(afflictionPtr, 48);
                }
                else if (tableName.Contains("SanctumRooms", StringComparison.OrdinalIgnoreCase))
                {
                    var idPtr = Ptr(row);
                    if (idPtr == 0) continue;
                    var id = _reader.ReadStringUtf16(idPtr, 64);
                    if (id.Contains("Treasure", StringComparison.OrdinalIgnoreCase))
                        target.Reward = MapSekhemaReward(id);
                    else
                        target.RoomType = ExtractSekhemaRoomType(id);
                }
            }
        }
    }

    private SekhemaResources ReadSekhemaResources(nint gameUi, nint panel)
    {
        var waterLeaf = ResolveSekhemaLeaf(
            panel,
            SekhemaWaterPath,
            SekhemaWaterFingerprint,
            IsSekhemaNumberLeaf);
        var water = ReadSekhemaTextNumber(waterLeaf);
        var bronze = ReadSekhemaTextNumber(WalkChildPath(panel, SekhemaBronzePath));
        var silver = ReadSekhemaTextNumber(WalkChildPath(panel, SekhemaSilverPath));
        var gold = ReadSekhemaTextNumber(WalkChildPath(panel, SekhemaGoldPath));
        if (bronze < 0) bronze = ReadSekhemaTextNumber(WalkChildPath(gameUi, SekhemaHudBronzePath));
        if (silver < 0) silver = ReadSekhemaTextNumber(WalkChildPath(gameUi, SekhemaHudSilverPath));
        if (gold < 0) gold = ReadSekhemaTextNumber(WalkChildPath(gameUi, SekhemaHudGoldPath));
        var honourLeaf = ResolveSekhemaLeaf(
            gameUi,
            SekhemaHonourPath,
            SekhemaHonourFingerprint,
            element => SekhemaHonourPercent(element) >= 0);
        var honour = SekhemaHonourPercent(honourLeaf);
        return new SekhemaResources(water, bronze, silver, gold, honour);
    }

    private SekhemaPlayerStats ReadSekhemaPlayerStats(nint localPlayer)
    {
        if (localPlayer == 0) return default;
        var stats = ResolveComponent(localPlayer, "Stats");
        if (stats == 0) return default;

        var items = ReadSekhemaStatsMap(Ptr(stats + Poe2.Sekhema.StatsChangedByItemsPtr));
        var buffs = ReadSekhemaStatsMap(Ptr(stats + Poe2.Sekhema.StatsChangedByBuffsPtr));
        var evasion = GetStat(items, 276);
        var energyShield = GetStat(items, 241);
        var armour = GetStat(items, 235);
        var life = GetStat(items, 239);
        var qotf = GetStat(items, 9490) + GetStat(buffs, 9490) > 0;
        return new SekhemaPlayerStats(evasion, energyShield, armour, life, qotf);
    }

    private Dictionary<int, int> ReadSekhemaStatsMap(nint container)
    {
        var result = new Dictionary<int, int>();
        if (container == 0 ||
            !_reader.TryReadStruct<StdVector>(container + Poe2.Sekhema.StatsVectorInContainer, out var vector))
            return result;
        var count = VectorCount(vector, Poe2.Sekhema.StatEntryStride, 32768);
        if (count <= 0) return result;
        var bytes = new byte[count * Poe2.Sekhema.StatEntryStride];
        if (_reader.TryReadBytes(vector.First, bytes) != bytes.Length) return result;
        for (var i = 0; i < count; i++)
        {
            var offset = i * Poe2.Sekhema.StatEntryStride;
            result[BitConverter.ToInt32(bytes, offset)] = BitConverter.ToInt32(bytes, offset + 4);
        }
        return result;
    }

    private nint WalkChildPath(nint root, int[] path)
    {
        var current = root;
        foreach (var index in path)
        {
            current = ChildAt(current, index);
            if (current == 0) return 0;
        }
        return current;
    }

    private nint ResolveSekhemaLeaf(
        nint root,
        int[] indexPath,
        uint[] fingerprint,
        Func<nint, bool> terminalValid)
    {
        if (root == 0) return 0;

        var indexed = WalkChildPath(root, indexPath);
        if (indexed != 0 && terminalValid(indexed))
            return indexed;

        return fingerprint.Length == indexPath.Length
            ? WalkSekhemaFingerprint(root, fingerprint, 0, terminalValid)
            : 0;
    }

    private nint WalkSekhemaFingerprint(
        nint parent,
        uint[] fingerprint,
        int step,
        Func<nint, bool> terminalValid)
    {
        if (step == fingerprint.Length)
            return terminalValid(parent) ? parent : 0;
        if (!TryChildVector(parent, out var children, out var count))
            return 0;

        const uint visibleMask = 1u << Poe2.UiElement.FlagVisibleBit;
        var expected = fingerprint[step] & ~visibleMask;
        for (var pass = 0; pass < 2; pass++)
        {
            var wantVisible = pass == 0;
            for (var index = 0; index < count; index++)
            {
                var child = Ptr(children.First + (nint)(index * 8));
                if (child == 0 ||
                    !_reader.TryReadStruct<uint>(child + Poe2.UiElement.Flags, out var flags) ||
                    (flags & ~visibleMask) != expected ||
                    ((flags & visibleMask) != 0) != wantVisible)
                    continue;

                var result = WalkSekhemaFingerprint(child, fingerprint, step + 1, terminalValid);
                if (result != 0) return result;
            }
        }
        return 0;
    }

    private nint ChildAt(nint parent, int index)
    {
        if (parent == 0 || index < 0 || !TryChildVector(parent, out var vector, out var count) || index >= count)
            return 0;
        var child = Ptr(vector.First + (nint)(index * 8));
        if (child == 0) return 0;
        var self = Ptr(child + Poe2.UiElement.Self);
        return self == 0 || self == child ? child : 0;
    }

    private bool TryChildVector(nint parent, out StdVector vector, out int count)
    {
        vector = default;
        count = 0;
        if (parent == 0 || !_reader.TryReadStruct<StdVector>(parent + Poe2.UiElement.Children, out vector))
            return false;
        count = VectorCount(vector, 8, 4000);
        return count > 0;
    }

    private int ReadSekhemaTextNumber(nint element)
        => element == 0 ? -1 : ParseSekhemaDigits(ReadStdWString(element + Poe2.Sekhema.ResourceText));

    private bool IsSekhemaNumberLeaf(nint element)
        => ReadSekhemaTextNumber(element) >= 0;

    private double SekhemaHonourPercent(nint fill)
    {
        if (fill == 0) return -1;
        var frame = Ptr(fill + Poe2.UiElement.Parent);
        if (frame == 0) return -1;
        if (!_reader.TryReadStruct<float>(frame + Poe2.UiElement.SizeW, out var frameWidth) ||
            !_reader.TryReadStruct<float>(fill + Poe2.UiElement.SizeW, out var fillWidth) ||
            !float.IsFinite(frameWidth) || !float.IsFinite(fillWidth) || frameWidth <= 1f || fillWidth < 0f)
            return -1;
        var percent = fillWidth / frameWidth * 100.0;
        return percent is >= 0 and <= 105 ? Math.Min(percent, 100) : -1;
    }

    private static int ParseSekhemaDigits(string text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        var value = 0;
        var found = false;
        foreach (var c in text)
        {
            if (c is < '0' or > '9') continue;
            found = true;
            var digit = c - '0';
            if (value > (int.MaxValue - digit) / 10) return -1;
            value = value * 10 + digit;
        }
        return found ? value : -1;
    }

    private static int VectorCount(StdVector vector, int stride, int max)
    {
        var bytes = (long)vector.Last - (long)vector.First;
        if (vector.First == 0 || vector.Last == 0 || bytes <= 0 || bytes % stride != 0) return 0;
        var count = bytes / stride;
        return count is > 0 and <= int.MaxValue && count <= max ? (int)count : 0;
    }

    private static int GetStat(IReadOnlyDictionary<int, int> stats, int key)
        => stats.TryGetValue(key, out var value) ? value : 0;

    internal static string ExtractSekhemaRoomType(string id)
    {
        var parts = id.Split('_');
        if (parts.Length < 2) return "";
        return SekhemaRoomAliases.TryGetValue(parts[1], out var alias) ? alias : parts[1];
    }

    internal static string MapSekhemaReward(string id)
    {
        var text = id.ToLowerInvariant();
        if (text.Contains("key"))
            return text.Contains("gold") ? "Gold Key" : text.Contains("silver") ? "Silver Key" : "Bronze Key";
        if (text.Contains("chest") || text.Contains("cache"))
            return text.Contains("gold") ? "Golden Cache" : text.Contains("silver") ? "Silver Cache" : "Bronze Cache";
        if (text.Contains("water") || text.Contains("fountain"))
            return text.Contains("legend") || text.Contains("major") || text.Contains("large")
                ? "Large Fountain"
                : "Fountain";
        if (text.Contains("merchant")) return "Merchant";
        if (text.Contains("pledge")) return "Pledge to Kochai";
        if (text.Contains("honor") || text.Contains("honour")) return "Honour";
        if (text.Contains("boon")) return "Boon";
        if (text.Contains("curse")) return "Curse";
        if (text.Contains("random")) return "Random";
        return "";
    }
}
