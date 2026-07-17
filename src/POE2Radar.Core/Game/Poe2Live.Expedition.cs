namespace POE2Radar.Core.Game;

public sealed partial class Poe2Live
{
    public readonly record struct ExpeditionControllerRead(
        bool Resolved, int Total, int Placed, string Source)
    {
        public static readonly ExpeditionControllerRead Missing = new(false, 0, 0, "manual");
    }

    public readonly record struct ExpeditionMapModifiersRead(
        bool Resolved, int PlacementRangePercent, int BlastRadiusPercent);

    public readonly record struct ExpeditionEntityInfo(
        string IconName, string[] ModIds, bool? IsBlocked);

    /// <summary>
    /// Reads the encounter controller from ServerData. This is deliberately independent of the
    /// Expedition HUD tree, so keyboard/mouse and controller layouts use the same authoritative state.
    /// </summary>
    public ExpeditionControllerRead ReadExpeditionController(nint areaInstance)
    {
        if (areaInstance == 0) return ExpeditionControllerRead.Missing;
        var serverData = Ptr(areaInstance + Poe2.AreaInstance.ServerDataPtr);
        if (serverData == 0) return ExpeditionControllerRead.Missing;

        var controller = Ptr(serverData + Poe2.ExpeditionController.ServerDataPointer);
        return TryReadExpeditionControllerAt(controller, out var value)
            ? value
            : ExpeditionControllerRead.Missing;
    }

    private bool TryReadExpeditionControllerAt(nint controller, out ExpeditionControllerRead value)
    {
        value = ExpeditionControllerRead.Missing;
        if (controller == 0 || !_reader.TryReadStruct<int>(controller + Poe2.ExpeditionController.TotalExplosives, out var rawTotal))
            return false;
        if (!_reader.TryReadStruct<StdVector>(controller + Poe2.ExpeditionController.PlacedExplosives, out var placed))
            return false;
        if (!TryParseExpeditionController(rawTotal, placed.First, placed.Last, out var total, out var used))
            return false;
        value = new ExpeditionControllerRead(true, total, used, "encounter");
        return true;
    }

    public static bool TryParseExpeditionController(
        int rawTotal, nint first, nint last, out int total, out int placed)
    {
        total = rawTotal & 0xFF;
        placed = 0;
        if (total is < 1 or > 64) return false;
        if (first == 0 && last == 0) return true;
        if (first == 0 || last == 0 || last < first) return false;
        var span = (long)last - (long)first;
        if (span < 0 || span % IntPtr.Size != 0) return false;
        var count = span / IntPtr.Size;
        if (count < 0 || count > total) return false;
        placed = (int)count;
        return true;
    }

    public ExpeditionMapModifiersRead ReadExpeditionMapModifiers(nint areaInstance)
    {
        if (areaInstance == 0 ||
            !_reader.TryReadStruct<StdVector>(areaInstance + Poe2.AreaInstance.MapStats, out var vec))
            return default;

        var span = (long)vec.Last - (long)vec.First;
        var count = span / 8;
        if (vec.First == 0 || span < 0 || span % 8 != 0 || count is < 0 or > 1024)
            return default;

        int placement = 0, radius = 0;
        try
        {
            foreach (var stat in _reader.ReadArray<StatArrayStruct>(vec.First, (int)count))
            {
                if (stat.Key == Poe2.ExpeditionStats.PlacementRangePercent) placement += stat.Value;
                else if (stat.Key == Poe2.ExpeditionStats.ExplosiveRadiusPercent) radius += stat.Value;
            }
        }
        catch { return default; }
        return new ExpeditionMapModifiersRead(true, placement, radius);
    }

    public ExpeditionEntityInfo ReadExpeditionEntityInfo(nint entity)
    {
        if (entity == 0) return new ExpeditionEntityInfo("", [], null);

        var iconName = "";
        var icon = ResolveComponent(entity, "MinimapIcon");
        if (icon != 0)
        {
            var row = Ptr(icon + Poe2.MinimapIcon.IconRow);
            var name = row == 0 ? 0 : Ptr(row);
            if (name != 0) iconName = _reader.ReadStringUtf16(name, 128).TrimEnd('\0');
        }

        var modIds = new List<string>(8);
        var omp = ResolveComponent(entity, "ObjectMagicProperties");
        if (omp != 0)
        {
            AddExpeditionModIds(modIds, omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Implicit);
            AddExpeditionModIds(modIds, omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Explicit);
            AddExpeditionModIds(modIds, omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Enchant);
            AddExpeditionModIds(modIds, omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Hellscape);
            AddExpeditionModIds(modIds, omp + Poe2.ObjectMagicProperties.Mods + Poe2.ModVectors.Crucible);
        }

        bool? blocked = null;
        var blockage = ResolveComponent(entity, "TriggerableBlockage");
        if (blockage != 0 && _reader.TryReadStruct<byte>(blockage + Poe2.TriggerableBlockage.IsBlocked, out var blockedByte))
            blocked = blockedByte != 0;

        return new ExpeditionEntityInfo(iconName, modIds.Distinct(StringComparer.Ordinal).ToArray(), blocked);
    }

    public bool IsExpeditionDetonated(nint detonatorEntity)
    {
        var stateMachine = ResolveComponent(detonatorEntity, "StateMachine");
        if (stateMachine == 0) return false;
        foreach (var state in ReadStateMachineStates(stateMachine, out _))
            if (string.Equals(state.Name, "activated", StringComparison.OrdinalIgnoreCase))
                return state.Value != 0;
        return false;
    }

    private void AddExpeditionModIds(List<string> ids, nint vectorAddress)
    {
        if (!_reader.TryReadStruct<StdVector>(vectorAddress, out var vec)) return;
        var count = ((long)vec.Last - (long)vec.First) / Poe2.ModVectors.EntryStride;
        if (vec.First == 0 || count is <= 0 or > 64) return;
        try
        {
            foreach (var mod in _reader.ReadArray<ModArrayStruct>(vec.First, (int)count))
            {
                if (mod.ModsPtr == 0) continue;
                var id = ReadModTemplate(mod.ModsPtr).Trim();
                if (id.Length > 0) ids.Add(id);
            }
        }
        catch { }
    }
}
