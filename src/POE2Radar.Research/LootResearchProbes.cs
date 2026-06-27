using POE2Radar.Core;
using POE2Radar.Core.Game;

namespace POE2Radar.Research;

internal static class LootResearchProbes
{
    public static int RunItem(ProcessHandle process, MemoryReader reader, nint gameStateSlot)
    {
        var live = new Poe2Live(reader, gameStateSlot);
        if (!live.TryResolve(out _, out var area, out _)) { Console.Error.WriteLine("Not in game."); return 1; }
        var (dots, _, _) = live.Entities(area);
        var items = dots.Where(d => d.Metadata.Contains("WorldItem", StringComparison.Ordinal)).Take(20).ToList();
        Console.WriteLine($"WorldItem entities: {items.Count} (showing up to 20)");
        foreach (var e in items)
            Console.WriteLine($"  id={e.Id} art={e.ItemArt ?? "-"} name={e.ItemName ?? "-"} idf={e.ItemIdentified} rarity={e.Rarity} meta={e.Metadata}");
        return 0;
    }

    public static int RunGroundLabels(ProcessHandle process, MemoryReader reader, nint gameStateSlot)
    {
        var live = new Poe2Live(reader, gameStateSlot);
        if (!live.TryResolve(out var igs, out _, out _)) { Console.Error.WriteLine("Not in game."); return 1; }
        GetClientSize(process, out var w, out var h);
        var tags = live.ScanLootLabels(igs, 5000);
        Console.WriteLine($"Visible loot-tag candidates: {tags.Count}");
        foreach (var (el, text) in tags.Take(40))
        {
            var ok = live.TryUiElementRect(el, w, h, out var x, out var y, out var rw, out var rh, requireFirstLine: text);
            Console.WriteLine($"  0x{el:X} \"{text}\" rect={(ok ? $"{x:F0},{y:F0} {rw:F0}x{rh:F0}" : "fail")}");
        }
        return 0;
    }

    public static int RunRuneforge(ProcessHandle process, MemoryReader reader, nint gameStateSlot)
    {
        var live = new Poe2Live(reader, gameStateSlot);
        if (!live.TryResolve(out var igs, out _, out _)) { Console.Error.WriteLine("Not in game."); return 1; }
        GetClientSize(process, out var w, out var h);
        var forge = new Poe2Runeforge(reader);
        var rewards = forge.ReadRewards(igs, w, h);
        Console.WriteLine($"Runeforge panel open={forge.PanelOpen} rewards={rewards.Count}");
        foreach (var r in rewards)
            Console.WriteLine($"  {r.Count}x {r.Name} @ ({r.X:F0},{r.Y:F0}) {r.W:F0}x{r.H:F0}");
        return 0;
    }

    public static int RunMonolith(ProcessHandle process, MemoryReader reader, nint gameStateSlot)
    {
        var live = new Poe2Live(reader, gameStateSlot);
        if (!live.TryResolve(out _, out var area, out _)) { Console.Error.WriteLine("Not in game."); return 1; }
        var level = live.AreaLevel(area);
        var (dots, _, _) = live.Entities(area);
        var monos = dots.Where(d => d.Metadata.Contains("Expedition2Encounter", StringComparison.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"Expedition2Encounter entities: {monos.Count} (areaLevel={level})");
        foreach (var e in monos)
        {
            var m = live.ReadMonolith(e.Address);
            Console.WriteLine($"  id={e.Id} resolved={m.Resolved} holes={m.HoleCount} anchorIdx={m.AnchorIdx} pos={m.AnchorPos} unique={m.IsUnique} collected={m.Collected}");
        }
        return 0;
    }

    public static int RunLeague(ProcessHandle process, MemoryReader reader, nint gameStateSlot)
    {
        var live = new Poe2Live(reader, gameStateSlot);
        if (!live.TryResolve(out _, out var area, out _)) { Console.Error.WriteLine("Not in game."); return 1; }
        var league = live.LeagueName(area);
        Console.WriteLine($"ServerData.League (+0x{Poe2.ServerData.League:X}): \"{league}\"");
        Console.WriteLine("Paste-ready offset block:");
        Console.WriteLine($"    public static class ServerData {{ public const int League = 0x{Poe2.ServerData.League:X}; // ✓ validated {DateTime.UtcNow:yyyy-MM-dd} }}");
        return 0;
    }

    public static int RunRitualHelper(ProcessHandle process, MemoryReader reader, nint gameStateSlot)
    {
        var live = new Poe2Live(reader, gameStateSlot);
        if (!live.TryResolve(out var igs, out _, out _)) { Console.Error.WriteLine("Not in game."); return 1; }
        GetClientSize(process, out var w, out var h);

        Console.WriteLine("=== Ritual Helper probe ===");
        Console.WriteLine($"Fast chain: GameUi.children[{Poe2.Ritual.FastChainChildA}].children[{Poe2.Ritual.FastChainChildB}]");
        Console.WriteLine($"Tile item ptr: UiElement + 0x{Poe2.Ritual.TileItemEntityPtr:X}");
        Console.WriteLine($"Signatures: {string.Join(", ", Poe2.Ritual.SignatureTexts.Select(s => $"\"{s}\""))}");

        if (!Poe2UiAnchors.TryDiscover(reader, igs, out var gameUi, out var controllerUi))
            Console.WriteLine("WARN: Could not discover GameUi / GameUiController anchors.");
        else
            Console.WriteLine($"Anchors: GameUi=0x{gameUi:X} Controller=0x{controllerUi:X}");

        var ritual = new Poe2RitualRewards(reader);
        var rewards = ritual.ReadRewards(igs, w, h);
        Console.WriteLine($"Panel open={ritual.PanelOpen} path={ritual.LastPathKind} tiles={rewards.Count}");

        foreach (var (i, r) in rewards.Select((t, idx) => (idx, t)))
        {
            var item = r.Item;
            Console.WriteLine($"  [{i}] rect=({r.X:F0},{r.Y:F0}) {r.W:F0}x{r.H:F0}");
            Console.WriteLine($"       rarity={item.Rarity} base=\"{item.BaseName ?? "-"}\" art=\"{item.ArtBasename ?? "-"}\"");
            Console.WriteLine($"       display=\"{item.DisplayName}\" internal=\"{item.InternalBasename}\"");
            Console.WriteLine($"       path=\"{item.FullPath}\"");
        }

        if (gameUi != 0)
        {
            var a = ChildAt(reader, gameUi, Poe2.Ritual.FastChainChildA);
            var b = a != 0 ? ChildAt(reader, a, Poe2.Ritual.FastChainChildB) : 0;
            Console.WriteLine($"KBM fast chain: child[{Poe2.Ritual.FastChainChildA}]=0x{a:X} child[{Poe2.Ritual.FastChainChildB}]=0x{b:X}");
        }
        if (controllerUi != 0)
        {
            var a = ChildAt(reader, controllerUi, Poe2.Ritual.FastChainChildA);
            var b = a != 0 ? ChildAt(reader, a, Poe2.Ritual.FastChainChildB) : 0;
            Console.WriteLine($"Controller fast chain: child[{Poe2.Ritual.FastChainChildA}]=0x{a:X} child[{Poe2.Ritual.FastChainChildB}]=0x{b:X}");
        }

        Console.WriteLine();
        Console.WriteLine("Paste-ready offset block:");
        Console.WriteLine("    public static class Ritual");
        Console.WriteLine("    {");
        Console.WriteLine($"        public const int FastChainChildA = {Poe2.Ritual.FastChainChildA};");
        Console.WriteLine($"        public const int FastChainChildB = {Poe2.Ritual.FastChainChildB};");
        Console.WriteLine($"        public const int TileItemEntityPtr = 0x{Poe2.Ritual.TileItemEntityPtr:X};");
        Console.WriteLine("    }");
        return 0;
    }

    private static nint ChildAt(MemoryReader reader, nint parent, int index)
    {
        if (parent == 0) return 0;
        var first = Ptr(reader, parent + Poe2.UiElement.Children);
        if (first == 0) return 0;
        if (!reader.TryReadStruct<nint>(parent + Poe2.UiElement.ChildrenEnd, out var last)) return 0;
        var n = ((long)last - (long)first) / 8;
        if (index < 0 || index >= n) return 0;
        return Ptr(reader, first + (nint)(index * 8));
    }

    private static nint Ptr(MemoryReader reader, nint addr)
    {
        if (!reader.TryReadStruct<nint>(addr, out var p)) return 0;
        var u = (ulong)p;
        return (u < 0x10000 || u > 0x7FFFFFFFFFFF) ? 0 : p;
    }

    private static void GetClientSize(ProcessHandle process, out float w, out float h)
    {
        w = 1920; h = 1080;
        _ = process;
    }

    public static nint ResolveGameStateSlot(ProcessHandle process, MemoryReader reader)
    {
        foreach (var pat in AobPatterns.GameStateRefs)
        {
            foreach (var s in AobScanner.ScanForResolvedAddresses(process, reader, pat).Distinct())
                if (new Poe2Live(reader, s).TryResolve(out _, out _, out _)) return s;
        }
        return 0;
    }
}
