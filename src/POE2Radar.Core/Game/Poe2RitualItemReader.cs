// Ritual item identity reads — derived from GameHelper RitualHelper (GPL-3.0).
namespace POE2Radar.Core.Game;

/// <summary>Read item metadata from a Ritual reward tile's item-entity pointer (+0x4F8).</summary>
public static class Poe2RitualItemReader
{
    public enum ItemRarity : byte { Normal, Magic, Rare, Unique }

    public readonly record struct RitualItemIdentity(
        string InternalBasename,
        string FullPath,
        string? BaseName,
        string? ArtBasename,
        ItemRarity Rarity,
        IReadOnlyList<string> ModLines,
        string DisplayName);

    public static bool TryRead(MemoryReader reader, nint itemEntity, Func<string, string?>? prettyNameLookup, out RitualItemIdentity identity)
    {
        identity = default;
        if (itemEntity == 0) return false;

        var fullPath = ReadMetadata(reader, itemEntity);
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        var parts = fullPath.Split('/');
        var internalBasename = parts.Length > 0 ? parts[^1] : fullPath;
        var rarity = ItemRarity.Normal;
        var modsComp = ResolveComponent(reader, itemEntity, "Mods");
        if (modsComp != 0
            && reader.TryReadStruct<int>(modsComp + Poe2.ModsComponent.Rarity, out var r)
            && r is >= 0 and <= 3)
            rarity = (ItemRarity)r;

        string? baseName = null;
        var baseComp = ResolveComponent(reader, itemEntity, "Base");
        if (baseComp != 0)
        {
            var nameRow = Ptr(reader, baseComp + Poe2.BaseComponent.NameRow);
            var namePtr = nameRow == 0 ? 0 : Ptr(reader, nameRow + Poe2.BaseComponent.RowDisplayName);
            if (namePtr != 0)
            {
                var s = reader.ReadStringUtf16(namePtr, 64);
                if (!string.IsNullOrWhiteSpace(s)) baseName = s.Trim();
            }
        }

        string? artBasename = null;
        var renderItem = ResolveComponent(reader, itemEntity, "RenderItem");
        if (renderItem != 0)
        {
            var pathPtr = Ptr(reader, renderItem + Poe2.RenderItemComponent.ResourcePath);
            if (pathPtr != 0)
                artBasename = ArtBasename(reader.ReadStringUtf16(pathPtr, 128));
        }

        var display = prettyNameLookup?.Invoke(internalBasename) ?? internalBasename;
        if (rarity != ItemRarity.Unique && !string.IsNullOrWhiteSpace(baseName))
            display = baseName;

        identity = new RitualItemIdentity(
            internalBasename, fullPath, baseName, artBasename, rarity,
            Array.Empty<string>(), display);
        return true;
    }

    private static string ReadMetadata(MemoryReader reader, nint entity)
    {
        var details = Ptr(reader, entity + Poe2.Entity.EntityDetailsPtr);
        if (details == 0) return "";
        var namePtr = Ptr(reader, details + Poe2.EntityDetails.Name);
        return namePtr == 0 ? "" : ReadStdWString(reader, namePtr);
    }

    private static string? ArtBasename(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var slash = path.LastIndexOf('/');
        var start = slash >= 0 ? slash + 1 : 0;
        var dot = path.LastIndexOf('.');
        var end = dot > start ? dot : path.Length;
        if (end <= start) return null;
        var name = path[start..end];
        return name.Length >= 2 ? name : null;
    }

    private static nint ResolveComponent(MemoryReader reader, nint entity, string name)
    {
        var details = Ptr(reader, entity + Poe2.Entity.EntityDetailsPtr);
        if (details == 0) return 0;
        var lookup = Ptr(reader, details + Poe2.EntityDetails.ComponentLookUpPtr);
        if (lookup == 0) return 0;
        if (!reader.TryReadStruct<StdVector>(entity + Poe2.Entity.ComponentList, out var compList)) return 0;
        var compCount = ((long)compList.Last - (long)compList.First) / 8;
        if (compCount is <= 0 or > 256) return 0;

        var bFirst = Ptr(reader, lookup + Poe2.ComponentLookUp.NameAndIndexBucket);
        if (!reader.TryReadStruct<nint>(lookup + Poe2.ComponentLookUp.NameAndIndexBucket + 8, out var bLast)) return 0;
        var entries = ((long)bLast - (long)bFirst) / Poe2.ComponentLookUp.EntryStride;
        if (bFirst == 0 || entries is <= 0 or > 256) return 0;

        for (long i = 0; i < entries; i++)
        {
            var e = bFirst + (nint)(i * Poe2.ComponentLookUp.EntryStride);
            var namePtr = Ptr(reader, e);
            if (!reader.TryReadStruct<int>(e + 8, out var index)) continue;
            if (index < 0 || index >= compCount) continue;
            if (reader.ReadStringUtf8(namePtr, 32) != name) continue;
            return Ptr(reader, compList.First + (nint)(index * 8));
        }
        return 0;
    }

    private static string ReadStdWString(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<long>(addr + 0x10, out var len) || len <= 0 || len > 1024) return "";
        if (len < 8) return reader.ReadStringUtf16(addr, (int)len);
        var ptr = Ptr(reader, addr);
        return ptr == 0 ? "" : reader.ReadStringUtf16(ptr, (int)len);
    }

    private static nint Ptr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }
}
