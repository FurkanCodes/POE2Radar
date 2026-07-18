namespace POE2Radar.Core.Game;

public sealed partial class Poe2Live
{
    private Poe2UiAnchors.BranchKind _stashValueProbeHint;
    private nint _stashValueLastRoot;

    public enum StashValuePanel : byte
    {
        Stash = 0,
        Inventory = 1,
    }

    public readonly record struct StashValueSlot(
        nint TileElement,
        nint ItemEntity,
        UiRect Rect,
        StashValuePanel Panel,
        bool Hovered,
        string FullItemPath,
        string InternalName,
        string BaseItemName,
        Rarity Rarity,
        string ArtBasename,
        int StackCount,
        bool Identified,
        bool Corrupted,
        string[] ModLines,
        StashItemMod[] Mods,
        StashItemStats Stats);

    public readonly record struct StashItemMod(string Id, float Value0, float Value1, bool Explicit);

    public readonly record struct StashItemStats(
        int ItemRarity,
        int PackSize,
        int MonsterRarity,
        int MonsterEffectiveness,
        int WaystoneDropChance,
        int Quality,
        bool HasMemoryStats);

    public readonly record struct StashValueRead(
        bool AnyHovered,
        int ScannedNodes,
        int CandidateSlots,
        StashValueSlot[] Slots)
    {
        public static readonly StashValueRead Empty = new(false, 0, 0, []);
    }

    public StashValueRead ReadStashValueSlots(
        nint inGameState,
        float windowWidth,
        float windowHeight,
        System.Numerics.Vector2? mouseClient)
    {
        if (inGameState == 0 || windowWidth <= 0 || windowHeight <= 0)
            return StashValueRead.Empty;

        DiscoverGameUiAnchors(inGameState, out var gameUi, out var controllerGameUi);
        var uiRoot = GetUiRoot(inGameState);
        var fixedRoot = Ptr(inGameState + Poe2.InGameState.UiRoot);

        Span<nint> roots = stackalloc nint[6];
        var rootCount = UiBranchCandidates.Fill(
            roots, gameUi, controllerGameUi, uiRoot, fixedRoot, _stashValueProbeHint, _stashValueLastRoot);

        var visited = new HashSet<nint>();
        var seenTiles = new HashSet<nint>();
        var slots = new List<StashValueSlot>(128);
        var anyHovered = false;
        var scanned = 0;
        var candidates = 0;
        nint bestRoot = 0;
        var bestSlots = 0;

        for (var i = 0; i < rootCount; i++)
        {
            var before = slots.Count;
            ScanStashValueRoot(
                roots[i],
                windowWidth,
                windowHeight,
                mouseClient,
                visited,
                seenTiles,
                slots,
                ref anyHovered,
                ref scanned,
                ref candidates);

            var found = slots.Count - before;
            if (found > bestSlots)
            {
                bestSlots = found;
                bestRoot = roots[i];
            }
        }

        if (bestRoot != 0)
        {
            _stashValueLastRoot = bestRoot;
            var kind = UiBranchCandidates.BranchForRoot(bestRoot, gameUi, controllerGameUi);
            if (kind != Poe2UiAnchors.BranchKind.None)
                _stashValueProbeHint = kind;
        }

        return slots.Count == 0
            ? new StashValueRead(anyHovered, scanned, candidates, [])
            : new StashValueRead(anyHovered, scanned, candidates, slots.ToArray());
    }

    /// <summary>Forget the successful KBM/controller branch after an input-mode transition.</summary>
    public void InvalidateStashValueUiCache()
    {
        _stashValueProbeHint = Poe2UiAnchors.BranchKind.None;
        _stashValueLastRoot = 0;
    }

    private void ScanStashValueRoot(
        nint root,
        float windowWidth,
        float windowHeight,
        System.Numerics.Vector2? mouseClient,
        HashSet<nint> visited,
        HashSet<nint> seenTiles,
        List<StashValueSlot> slots,
        ref bool anyHovered,
        ref int scanned,
        ref int candidates)
    {
        if (root == 0) return;

        var queue = new Queue<nint>();
        queue.Enqueue(root);
        var elementCache = new Dictionary<nint, UiElementProjection.Element>(512);
        var parentCache = new Dictionary<nint, UiElementProjection.Point>(256);

        while (queue.Count > 0 && scanned < 30_000)
        {
            var el = queue.Dequeue();
            if (el == 0 || !visited.Add(el)) continue;
            scanned++;

            if (el != root && !IsVisibleUiElement(el)) continue;

            foreach (var child in ReadChildren(el, maxCount: 8192))
                queue.Enqueue(child);

            var item = Ptr(el + Poe2.RitualUi.TileItemEntity);
            if (item == 0 || !seenTiles.Add(el) || !LooksLikeEntity(item))
                continue;

            candidates++;
            var fullPath = ReadMetadata(item);
            if (!fullPath.StartsWith("Metadata/Items", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!UiElementProjection.TryGetRect(el, TryReadProjectionElement, windowWidth, windowHeight, elementCache, parentCache, out var projected))
                continue;

            if (!LooksLikeInventorySlotRect(projected, windowWidth, windowHeight))
                continue;

            var rect = new UiRect(projected.X, projected.Y, projected.W, projected.H);
            var hovered = mouseClient is { } mouse && PointInRect(mouse, rect);
            anyHovered |= hovered;

            var panel = rect.X + rect.W * 0.5f < windowWidth * 0.5f
                ? StashValuePanel.Stash
                : StashValuePanel.Inventory;

            slots.Add(ReadStashValueSlot(el, item, rect, panel, hovered, fullPath));
        }

        bool TryReadProjectionElement(nint address, out UiElementProjection.Element element)
            => UiElementProjection.TryRead(_reader, address, out element);
    }

    private StashValueSlot ReadStashValueSlot(
        nint tile,
        nint item,
        UiRect rect,
        StashValuePanel panel,
        bool hovered,
        string fullPath)
    {
        var internalName = PathBasename(fullPath);
        var rarity = Rarity.Normal;
        var baseName = "";
        var art = "";
        var stack = 1;
        var identified = true;
        var corrupted = false;

        var modsComp = ResolveComponent(item, "Mods");
        if (modsComp != 0 && _reader.TryReadStruct<int>(modsComp + Poe2.ModsComponent.Rarity, out var r) && r is >= 0 and <= 3)
            rarity = (Rarity)r;
        if (modsComp != 0 && _reader.TryReadStruct<int>(modsComp + Poe2.ModsComponent.Identified, out var idf))
            identified = idf != 0;
        corrupted = ResolveComponent(item, "Corrupted") != 0;

        var baseComp = ResolveComponent(item, "Base");
        if (baseComp != 0)
        {
            var nameRow = Ptr(baseComp + Poe2.BaseComponent.NameRow);
            var namePtr = nameRow == 0 ? 0 : Ptr(nameRow + Poe2.BaseComponent.RowDisplayName);
            if (namePtr != 0)
                baseName = _reader.ReadStringUtf16(namePtr, 96).Trim();
        }

        var renderItem = ResolveComponent(item, "RenderItem");
        if (renderItem != 0)
        {
            var pathPtr = Ptr(renderItem + Poe2.RenderItemComponent.ResourcePath);
            if (pathPtr != 0)
                art = PathBasename(_reader.ReadStringUtf16(pathPtr, 160));
        }

        var stackComp = ResolveComponent(item, "Stack");
        if (stackComp != 0 && _reader.TryReadStruct<int>(stackComp + Poe2.StackComponent.Count, out var count) && count > 1 && count < 1_000_000)
            stack = count;

        var rawMods = ReadStashItemMods(modsComp);
        var mods = rawMods.Length == 0
            ? []
            : rawMods.Select(m => FormatModLine(m.Id, m.Value0, m.Value1)).ToArray();
        var stats = ReadStashItemStats(item, modsComp);
        return new StashValueSlot(tile, item, rect, panel, hovered, fullPath, internalName, baseName, rarity, art, stack, identified, corrupted, mods, rawMods, stats);
    }

    private StashItemMod[] ReadStashItemMods(nint modsComp)
    {
        if (modsComp == 0) return [];
        var result = new List<StashItemMod>(10);
        Add(modsComp + Poe2.ModsComponent.Mods + Poe2.ModVectors.Implicit, false);
        Add(modsComp + Poe2.ModsComponent.Mods + Poe2.ModVectors.Explicit, true);
        Add(modsComp + Poe2.ModsComponent.Mods + Poe2.ModVectors.Enchant, false);
        return result.Count == 0 ? [] : result.ToArray();

        void Add(nint vectorAddress, bool explicitMod)
        {
            if (!_reader.TryReadStruct<StdVector>(vectorAddress, out var vec)) return;
            var count = ((long)vec.Last - (long)vec.First) / Poe2.ModVectors.EntryStride;
            if (vec.First == 0 || count is <= 0 or > 64) return;

            ModArrayStruct[] entries;
            try { entries = _reader.ReadArray<ModArrayStruct>(vec.First, (int)count); }
            catch { return; }

            foreach (var entry in entries)
            {
                if (entry.ModsPtr == 0) continue;
                // Keep entries even when the template string fails — crafting needs accurate counts
                // (tablets especially). Skipping empty IDs under-counts and makes Augment/Exalt
                // hit "cannot be increased any further".
                var id = ReadModTemplate(entry.ModsPtr).Trim();
                if (id.Length == 0) id = "_";
                var (v0, v1) = ReadModValues(entry.Values, entry.Value0);
                result.Add(new StashItemMod(id, v0, v1, explicitMod));
            }
        }
    }

    private StashItemStats ReadStashItemStats(nint item, nint modsComp)
    {
        var rarity = 0;
        var pack = 0;
        var monsterRarity = 0;
        var effectiveness = 0;
        var dropChance = 0;
        var hasMemoryStats = false;

        if (modsComp != 0 && _reader.TryReadStruct<StdVector>(modsComp + Poe2.ModsComponent.StatsFromMods, out var vec))
        {
            var count = ((long)vec.Last - (long)vec.First) / 8;
            if (vec.First != 0 && count is > 0 and <= 512)
            {
                hasMemoryStats = true;
                try
                {
                    foreach (var stat in _reader.ReadArray<StatArrayStruct>(vec.First, (int)count))
                    {
                        switch (stat.Key)
                        {
                            case 8206: rarity = stat.Value; break;
                            case 8207: pack = stat.Value; break;
                            case 8208: monsterRarity = stat.Value; break;
                            case 8209: effectiveness = stat.Value; break;
                            case 8210: dropChance = stat.Value; break;
                        }
                    }
                }
                catch { /* transient vector mutation */ }
            }
        }

        var quality = 0;
        var qualityComp = ResolveComponent(item, "Quality");
        if (qualityComp != 0)
            _reader.TryReadStruct<int>(qualityComp + Poe2.QualityComponent.Value, out quality);

        quality = Math.Clamp(quality, 0, 100);
        return new StashItemStats(rarity, pack, monsterRarity, effectiveness, dropChance, quality, hasMemoryStats);
    }

    private static bool PointInRect(System.Numerics.Vector2 p, UiRect r)
        => p.X >= r.X && p.X <= r.X + r.W && p.Y >= r.Y && p.Y <= r.Y + r.H;

    private static bool LooksLikeInventorySlotRect(UiElementProjection.Rect rect, float windowWidth, float windowHeight)
    {
        if (rect.X < -5f || rect.Y < -5f || rect.X > windowWidth + 5f || rect.Y > windowHeight + 5f)
            return false;
        if (rect.W < 18f || rect.H < 18f || rect.W > 260f || rect.H > 260f)
            return false;
        var ratio = rect.W / rect.H;
        return ratio is > 0.22f and < 4.5f;
    }
}
