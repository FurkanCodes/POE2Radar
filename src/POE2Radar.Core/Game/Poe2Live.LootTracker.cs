namespace POE2Radar.Core.Game;

public sealed partial class Poe2Live
{
    public readonly record struct LootInventoryItem(
        string Key,
        string MetadataPath,
        string InternalName,
        Rarity Rarity,
        string RenderArt,
        string BaseName,
        int Count);

    public readonly record struct LootInventorySnapshot(
        bool Ok,
        nint InventoryAddress,
        int BoxesX,
        int BoxesY,
        LootInventoryItem[] Items)
    {
        public static readonly LootInventorySnapshot Failed = new(false, 0, 0, 0, []);
    }

    public LootInventorySnapshot ReadLootInventorySnapshot(nint areaInstance)
    {
        if (areaInstance == 0) return LootInventorySnapshot.Failed;

        var serverData = Ptr(areaInstance + Poe2.AreaInstance.ServerDataPtr);
        if (serverData == 0) return LootInventorySnapshot.Failed;

        if (!_reader.TryReadStruct<StdVector>(serverData + Poe2.ServerData.PlayerDataVector, out var playerVec))
            return LootInventorySnapshot.Failed;
        var playerData = Ptr(playerVec.First);
        if (playerData == 0) return LootInventorySnapshot.Failed;

        if (!_reader.TryReadStruct<StdVector>(playerData + Poe2.PlayerData.Inventories, out var invVec))
            return LootInventorySnapshot.Failed;

        var invCount = ((long)invVec.Last - (long)invVec.First) / Poe2.InventoryArrayEntry.Stride;
        if (invCount is <= 0 or > 4096) return LootInventorySnapshot.Failed;

        var inventory = (nint)0;
        for (long i = 0; i < invCount; i++)
        {
            var entry = invVec.First + (nint)(i * Poe2.InventoryArrayEntry.Stride);
            if (!_reader.TryReadStruct<int>(entry + Poe2.InventoryArrayEntry.Id, out var id)) continue;
            if (id != Poe2.InventoryArrayEntry.MainInventory1Id) continue;
            inventory = Ptr(entry + Poe2.InventoryArrayEntry.InventoryPtr);
            break;
        }

        if (inventory == 0) return LootInventorySnapshot.Failed;

        _reader.TryReadStruct<int>(inventory + Poe2.InventoryStruct.TotalBoxes, out var boxesX);
        _reader.TryReadStruct<int>(inventory + Poe2.InventoryStruct.TotalBoxes + 4, out var boxesY);

        if (!_reader.TryReadStruct<StdVector>(inventory + Poe2.InventoryStruct.ItemList, out var itemVec))
            return new LootInventorySnapshot(true, inventory, boxesX, boxesY, []);

        var slotCount = ((long)itemVec.Last - (long)itemVec.First) / 8;
        if (slotCount is <= 0 or > 4096)
            return new LootInventorySnapshot(true, inventory, boxesX, boxesY, []);

        var items = new List<LootInventoryItem>();
        var seen = new HashSet<nint>();
        for (long i = 0; i < slotCount; i++)
        {
            var invItem = Ptr(itemVec.First + (nint)(i * 8));
            if (invItem == 0 || !seen.Add(invItem)) continue;

            var itemEntity = Ptr(invItem + Poe2.InventoryItemStruct.ItemEntity);
            if (itemEntity == 0) continue;

            var details = Ptr(itemEntity + Poe2.Entity.EntityDetailsPtr);
            if (details == 0) continue;

            var metadata = ReadStdWString(details + Poe2.EntityDetails.Name);
            if (string.IsNullOrWhiteSpace(metadata)) continue;

            ReadLootItemFacts(itemEntity, out var stack, out var rarity, out var renderArt, out var baseName);
            var internalName = LastPathSegment(metadata);
            var key = BuildLootItemKey(rarity, metadata, renderArt);
            items.Add(new LootInventoryItem(key, metadata, internalName, rarity, renderArt, baseName, stack));
        }

        return new LootInventorySnapshot(true, inventory, boxesX, boxesY, items.ToArray());
    }

    private void ReadLootItemFacts(nint itemEntity, out int stack, out Rarity rarity, out string renderArt, out string baseName)
    {
        stack = 1;
        rarity = Rarity.Normal;
        renderArt = string.Empty;
        baseName = string.Empty;

        var stackComp = ResolveComponent(itemEntity, "Stack");
        if (stackComp != 0 &&
            _reader.TryReadStruct<int>(stackComp + Poe2.StackComponent.Count, out var count) &&
            count is > 1 and < 1_000_000)
        {
            stack = count;
        }

        var modsComp = ResolveComponent(itemEntity, "Mods");
        if (modsComp != 0 &&
            _reader.TryReadStruct<int>(modsComp + Poe2.ModsComponent.Rarity, out var r) &&
            r is >= 0 and <= 3)
        {
            rarity = (Rarity)r;
        }

        var baseComp = ResolveComponent(itemEntity, "Base");
        if (baseComp != 0)
        {
            var nameRow = Ptr(baseComp + Poe2.BaseComponent.NameRow);
            var namePtr = nameRow == 0 ? 0 : Ptr(nameRow + Poe2.BaseComponent.RowDisplayName);
            if (namePtr != 0)
                baseName = _reader.ReadStringUtf16(namePtr, 96).Trim();
        }

        var renderItem = ResolveComponent(itemEntity, "RenderItem");
        if (renderItem != 0)
        {
            var pathPtr = Ptr(renderItem + Poe2.RenderItemComponent.ResourcePath);
            if (pathPtr != 0)
                renderArt = LastPathSegmentWithoutExtension(_reader.ReadStringUtf16(pathPtr, 160));
        }
    }

    private static string BuildLootItemKey(Rarity rarity, string metadataPath, string renderArt)
    {
        const char sep = '\x1F';
        return string.IsNullOrWhiteSpace(renderArt)
            ? $"{(int)rarity}{sep}{metadataPath}"
            : $"{(int)rarity}{sep}{metadataPath}{sep}{renderArt}";
    }

    private static string LastPathSegment(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var slash = path.LastIndexOf('/');
        return slash >= 0 && slash < path.Length - 1 ? path[(slash + 1)..] : path;
    }

    private static string LastPathSegmentWithoutExtension(string path)
    {
        var seg = LastPathSegment(path);
        var dot = seg.LastIndexOf('.');
        return dot > 0 ? seg[..dot] : seg;
    }
}
