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
