using POE2Radar.Core;
using POE2Radar.Core.Game;

namespace POE2Radar.Research;

internal static class RitualResearchProbes
{
    public static int Run(
        ProcessHandle process,
        MemoryReader reader,
        nint gameStateSlot,
        bool watch,
        int intervalMs,
        int clientWidth,
        int clientHeight)
    {
        var live = new Poe2Live(reader, gameStateSlot);
        var w = clientWidth > 0 ? clientWidth : 1920;
        var h = clientHeight > 0 ? clientHeight : 1080;

        do
        {
            if (!live.TryResolve(out var igs, out _, out _))
            {
                Console.WriteLine("Not in game — open PoE2 and stand in a zone.");
                if (!watch) return 1;
            }
            else
            {
                var snap = live.ProbeRitualUi(igs, w, h);
                PrintSnapshot(snap, w, h);
            }

            if (!watch) return 0;
            Console.WriteLine($"--- watch {intervalMs}ms (Ctrl+C to stop) ---");
            Thread.Sleep(Math.Clamp(intervalMs, 100, 5000));
        }
        while (watch);

        return 0;
    }

    private static void PrintSnapshot(Poe2Live.RitualUiProbeSnapshot snap, float w, float h)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] client {w:F0}x{h:F0}");
        Console.WriteLine($"  cache grid=0x{snap.CachedGrid:X} root=0x{snap.CachedRoot:X} hint={snap.ProbeHint} nextBfs={FormatUtc(snap.NextBfsUtc)}");
        foreach (var branch in snap.Branches)
        {
            Console.WriteLine(
                $"  [{branch.Branch}] root=0x{branch.Root:X} fast[76][13]=0x{branch.FastGrid:X} " +
                $"visible={branch.FastVisible} valid={branch.FastValid} tiles={branch.TileCount} items={branch.ItemCount}");
        }

        var read = snap.Read;
        Console.WriteLine(
            $"  ReadRitualRewards → open={read.IsOpen} source={read.Source} branch={read.Branch} " +
            $"grid=0x{read.GridAddress:X} slots={read.Slots.Length}");
        for (var i = 0; i < read.Slots.Length && i < 8; i++)
        {
            var slot = read.Slots[i];
            Console.WriteLine(
                $"    [{i}] {slot.InternalName} {slot.Rarity} rect=({slot.Rect.X:F0},{slot.Rect.Y:F0} {slot.Rect.W:F0}x{slot.Rect.H:F0})");
        }

        if (read.IsOpen && read.Slots.Length == 0)
            Console.WriteLine("  WARN OPEN but zero slots — shop is opening; prices appear once tiles populate.");
        if (!read.IsOpen && snap.Branches.Any(b => b.FastVisible && b.ItemCount > 0))
            Console.WriteLine("  WARN Visible fast grid with items but reader closed — likely ghost tile (panel closed) or missing shop signature.");
    }

    public static int RunDeep(
        ProcessHandle process,
        MemoryReader reader,
        nint gameStateSlot,
        int clientWidth,
        int clientHeight)
    {
        var live = new Poe2Live(reader, gameStateSlot);
        var w = clientWidth > 0 ? clientWidth : 1920;
        var h = clientHeight > 0 ? clientHeight : 1080;

        if (!live.TryResolve(out var igs, out _, out _))
        {
            Console.WriteLine("Not in game — open PoE2 and open the Ritual tribute shop.");
            return 1;
        }

        var forceRead = live.ReadRitualRewards(igs, w, h, forceBfsFallback: true);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] client {w:F0}x{h:F0}");
        Console.WriteLine(
            $"  ReadRitualRewards(forceBfs) → open={forceRead.IsOpen} source={forceRead.Source} " +
            $"grid=0x{forceRead.GridAddress:X} slots={forceRead.Slots.Length}");
        foreach (var slot in forceRead.Slots.Take(6))
            Console.WriteLine($"    slot {slot.InternalName} {slot.Rarity} base={slot.BaseItemName}");

        foreach (var branch in live.DeepProbeRitualUi(igs, w, h))
        {
            Console.WriteLine(
                $"  [{branch.Branch}] root=0x{branch.Root:X} children={branch.RootChildCount} " +
                $"fast76children={branch.Fast76ChildCount} fastGrid=0x{branch.FastGrid:X}");
            foreach (var hit in branch.TextHits.Take(8))
                Console.WriteLine($"    text{(hit.MatchesSignature ? "*" : "")}: \"{hit.Text}\" @0x{hit.Element:X}");
            foreach (var grid in branch.GridCandidates)
            {
                Console.WriteLine(
                    $"    grid@0x{grid.Grid:X} tiles={grid.TileCount} items@4F8={grid.ItemCountAt4F8} " +
                    $"best={grid.BestItemCount}@+0x{grid.BestItemOffset:X} visible={grid.Visible} " +
                    $"rect=({grid.Rect.X:F0},{grid.Rect.Y:F0} {grid.Rect.W:F0}x{grid.Rect.H:F0}) " +
                    $"shopCtx={grid.PassesShopContext} strict={grid.PassesStrictValidation}");
            }
        }

        return 0;
    }

    private static string FormatUtc(DateTime utc)
        => utc == DateTime.MinValue ? "ready" : utc.ToString("HH:mm:ss.fff");
}
