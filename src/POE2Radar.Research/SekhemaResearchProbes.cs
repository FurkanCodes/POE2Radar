using POE2Radar.Core;
using POE2Radar.Core.Game;

namespace POE2Radar.Research;

/// <summary>
/// Read-only live validation for the Sekhema feature. This deliberately lives in Research: the
/// overlay consumes only the validated offsets/read layer and never carries discovery tooling.
/// </summary>
internal static class SekhemaResearchProbes
{
    private const string CrystalSuffix = "Hazards/HourglassLethal";
    private const string PortalPath =
        "Metadata/Monsters/MarakethSanctumTrial/Hazards/PortalPlatform";
    private const string LeverPath =
        "Metadata/Terrain/Gallows/Leagues/Sanctum/Objects/SanctumGenericLever";

    public static int Run(
        ProcessHandle process,
        MemoryReader reader,
        nint gameStateSlot,
        bool watch,
        bool assertVisible,
        int intervalMs,
        int clientWidth,
        int clientHeight)
    {
        if (gameStateSlot == 0)
        {
            Console.Error.WriteLine("Could not lock GameState slot (in game?).");
            return 1;
        }

        var live = new Poe2Live(reader, gameStateSlot);
        var width = clientWidth > 0 ? clientWidth : 1920;
        var height = clientHeight > 0 ? clientHeight : 1080;
        var cancelled = false;
        ConsoleCancelEventHandler? cancel = null;
        if (watch)
        {
            cancel = (_, e) =>
            {
                e.Cancel = true;
                cancelled = true;
            };
            Console.CancelKeyPress += cancel;
        }

        try
        {
            do
            {
                if (!live.TryResolve(out var inGameState, out var areaInstance, out var localPlayer))
                {
                    Console.WriteLine("Not in game - enter a PoE2 zone.");
                    if (!watch) return 1;
                }
                else
                {
                    var visibilityMismatch = PrintSnapshot(
                        reader,
                        live,
                        inGameState,
                        areaInstance,
                        localPlayer,
                        width,
                        height,
                        visibilityOnly: assertVisible);
                    if (assertVisible && visibilityMismatch)
                    {
                        Console.Error.WriteLine(
                            "FAIL Sekhema reader reports an open floor map while its UI hierarchy is hidden.");
                        return 2;
                    }
                }

                if (!watch || cancelled) return 0;
                Console.WriteLine($"--- watch {intervalMs}ms (Ctrl+C to stop) ---");
                Thread.Sleep(Math.Clamp(intervalMs, 100, 5000));
            }
            while (!cancelled);
        }
        finally
        {
            if (cancel is not null)
                Console.CancelKeyPress -= cancel;
        }

        return 0;
    }

    private static bool PrintSnapshot(
        MemoryReader reader,
        Poe2Live live,
        nint inGameState,
        nint areaInstance,
        nint localPlayer,
        int width,
        int height,
        bool visibilityOnly)
    {
        var floor = live.ReadSekhemaFloor(inGameState, localPlayer, width, height);
        var panelLocalVisible = IsLocallyVisible(reader, floor.PanelAddress);
        var panelHierarchyVisible = IsHierarchyVisible(reader, floor.PanelAddress);
        var widgetAddresses = floor.Layers
            .SelectMany(layer => layer)
            .Where(room => room.WidgetAddress != 0)
            .Select(room => room.WidgetAddress)
            .ToArray();
        var locallyVisibleWidgets = widgetAddresses.Count(address => IsLocallyVisible(reader, address));
        var hierarchyVisibleWidgets = widgetAddresses.Count(address => IsHierarchyVisible(reader, address));
        var playerGrid = live.PlayerGrid(localPlayer);

        Console.WriteLine(
            $"[{DateTime.Now:HH:mm:ss}] {live.AreaCode(areaInstance)} " +
            $"hash=0x{live.AreaHash(areaInstance):X8} client={width}x{height}");
        Console.WriteLine(
            $"  chain IGS=0x{inGameState:X} Area=0x{areaInstance:X} Player=0x{localPlayer:X} " +
            $"grid={(playerGrid is { } grid ? $"({grid.X:F1},{grid.Y:F1})" : "?")}");
        Console.WriteLine(
            $"  map open={floor.IsOpen} valid={floor.IsValid} panel=0x{floor.PanelAddress:X} " +
            $"floor=0x{floor.FloorDataAddress:X} player=({floor.PlayerLayer},{floor.PlayerRoom})");
        Console.WriteLine(
            $"  visibility panelLocal={panelLocalVisible} panelHierarchy={panelHierarchyVisible} " +
            $"widgetsLocal={locallyVisibleWidgets}/{widgetAddresses.Length} " +
            $"widgetsHierarchy={hierarchyVisibleWidgets}/{widgetAddresses.Length}");
        if (visibilityOnly)
            return floor.IsOpen && (!panelHierarchyVisible || hierarchyVisibleWidgets == 0);

        var (entities, awakeCount, sleepingCount) = live.Entities(areaInstance);
        var sekhema = entities
            .Where(entity =>
                !entity.IsSleeping &&
                (entity.Metadata.Contains("MarakethSanctum", StringComparison.OrdinalIgnoreCase) ||
                 entity.Metadata.Contains("/Sanctum/", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Console.WriteLine($"  status: {floor.Status}");
        Console.WriteLine(
            $"  resources water={Value(floor.Resources.Water)} " +
            $"honour={Percent(floor.Resources.HonourPercent)} " +
            $"keys={Value(floor.Resources.BronzeKeys)}/{Value(floor.Resources.SilverKeys)}/{Value(floor.Resources.GoldKeys)} B/S/G");
        Console.WriteLine(
            $"  stats evasion={floor.PlayerStats.Evasion} es={floor.PlayerStats.EnergyShield} " +
            $"armour={floor.PlayerStats.Armour} life={floor.PlayerStats.Life} " +
            $"qotf={floor.PlayerStats.HasQueenOfTheForest}");

        for (var layer = 0; layer < floor.Layers.Length; layer++)
        {
            Console.WriteLine($"  layer {layer}: rooms={floor.Layers[layer].Length}");
            foreach (var room in floor.Layers[layer])
            {
                var rect = room.HasWidget
                    ? $"({room.WidgetRect.X:F0},{room.WidgetRect.Y:F0} {room.WidgetRect.W:F0}x{room.WidgetRect.H:F0})"
                    : "none";
                Console.WriteLine(
                    $"    ({room.Layer},{room.Index}) chosen={room.IsChosen} next=[{string.Join(',', room.NextConnections)}] " +
                    $"type='{room.RoomType}' reward='{room.Reward}' affliction='{room.Affliction}' " +
                    $"widget=0x{room.WidgetAddress:X} rect={rect}");
            }
        }

        var crystals = sekhema.Where(entity =>
            entity.Metadata.EndsWith(CrystalSuffix, StringComparison.Ordinal)).ToArray();
        var portals = sekhema.Where(entity =>
            string.Equals(entity.Metadata, PortalPath, StringComparison.Ordinal)).ToArray();
        var levers = sekhema.Where(entity =>
            string.Equals(entity.Metadata, LeverPath, StringComparison.Ordinal)).ToArray();
        var chests = sekhema.Where(entity =>
            entity.Metadata.Contains("/MarakethSanctum/", StringComparison.Ordinal) &&
            entity.Metadata.Contains("Chest", StringComparison.Ordinal)).ToArray();

        Console.WriteLine(
            $"  entities awake={awakeCount} sleeping={sleepingCount} sekhema-awake={sekhema.Length} " +
            $"crystals={crystals.Length} chests={chests.Length} portals={portals.Length} levers={levers.Length}");
        foreach (var crystal in crystals)
        {
            var state = live.ReadSekhemaCrystalState(crystal.Address);
            Console.WriteLine(
                $"    crystal id={crystal.Id} grid=({crystal.Grid.X:F1},{crystal.Grid.Y:F1}) " +
                $"active={live.IsSekhemaCrystalActive(crystal.Address)} " +
                $"sm=0x{state.StateMachine:X} used={(state.UsedRead ? state.Used : -1)} " +
                $"states=[{state.Dump}]");
        }
        foreach (var oneShot in portals.Concat(levers))
            Console.WriteLine(
                $"    {(oneShot.Metadata == PortalPath ? "portal" : "lever")} id={oneShot.Id} " +
                $"grid=({oneShot.Grid.X:F1},{oneShot.Grid.Y:F1}) used={live.IsSekhemaOneShotUsed(oneShot.Address)}");
        foreach (var chest in chests)
            Console.WriteLine(
                $"    chest id={chest.Id} grid=({chest.Grid.X:F1},{chest.Grid.Y:F1}) " +
                $"opened={chest.Opened} metadata='{chest.Metadata}'");

        if (!floor.IsOpen)
            Console.WriteLine("  INFO Open the Trial floor map to validate layers, room widgets, and classification.");

        return floor.IsOpen && (!panelHierarchyVisible || hierarchyVisibleWidgets == 0);
    }

    private static string Value(int value) => value >= 0 ? value.ToString() : "?";
    private static string Percent(double value) => value >= 0 ? $"{value:F1}%" : "?";

    private static bool IsLocallyVisible(MemoryReader reader, nint element)
        => element != 0 &&
           reader.TryReadStruct<uint>(element + Poe2.UiElement.Flags, out var flags) &&
           (flags & (1u << Poe2.UiElement.FlagVisibleBit)) != 0;

    private static bool IsHierarchyVisible(MemoryReader reader, nint element)
    {
        var current = element;
        var visited = new HashSet<nint>();
        for (var depth = 0; depth < 64 && current != 0 && visited.Add(current); depth++)
        {
            if (!IsLocallyVisible(reader, current))
                return false;
            if (!reader.TryReadStruct<nint>(current + Poe2.UiElement.Parent, out var parent) ||
                parent == 0 ||
                parent == current)
                return true;
            current = parent;
        }
        return current == 0;
    }
}
