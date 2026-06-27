using System.Runtime.InteropServices;
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
        if (!live.TryResolve(out var igs, out var area, out _)) { Console.Error.WriteLine("Not in game."); return 1; }
        GetClientSize(process, out var w, out var h);

        Console.WriteLine("=== Ritual Helper probe ===");
        Console.WriteLine("Open the Ritual Favours / tribute shop in-game BEFORE running this probe.");
        Poe2UiAnchors.InvalidateDiscoverCache();
        Console.WriteLine($"Fast chain: GameUi.children[{Poe2.Ritual.FastChainChildA}].children[{Poe2.Ritual.FastChainChildB}]");
        Console.WriteLine($"Tile item ptr: UiElement + 0x{Poe2.Ritual.TileItemEntityPtr:X}");
        Console.WriteLine($"Signatures: {string.Join(", ", Poe2.Ritual.SignatureTexts.Select(s => $"\"{s}\""))}");
        Console.WriteLine($"League: \"{live.LeagueName(area)}\"");

        if (!Poe2UiAnchors.TryDiscover(reader, igs, out var gameUi, out var controllerUi))
            Console.WriteLine("WARN: Could not discover GameUi / GameUiController anchors.");
        else
        {
            Poe2UiAnchors.SanitizeBranches(reader, ref gameUi, ref controllerUi);
            Console.WriteLine(
                $"Anchors: GameUi=0x{gameUi:X} ({FormatChildCount(reader, gameUi)}) " +
                $"Controller=0x{controllerUi:X} ({FormatChildCount(reader, controllerUi)})");
            if (gameUi != 0) DumpBranchDiagnostics(reader, "KBM", gameUi);
            if (controllerUi != 0 && controllerUi != gameUi)
                DumpBranchDiagnostics(reader, "Controller", controllerUi);
        }

        var branches = live.GetUiBranches(igs);
        Console.WriteLine($"Ui branches ({branches.Length}): {string.Join(", ", branches.ToArray().Select(b => $"0x{b:X}"))}");
        foreach (var root in branches)
            RitualSignatureScan(reader, root, maxNodes: 1200);

        if (gameUi != 0 || controllerUi != 0)
        {
            Console.WriteLine("Fast-chain tile scoring by probe hint (O(1) only, no BFS):");
            foreach (var hint in new[] { Poe2UiAnchors.BranchKind.None, Poe2UiAnchors.BranchKind.Controller })
            {
                var scratch = new Poe2RitualRewards(reader, live);
                var st = scratch.ReadWindowState(igs, w, h, allowFullLocate: false, hint, forceBfsFallback: false);
                var bypass = scratch.LastBranchRoot != 0 && scratch.LastBranchRoot == controllerUi;
                Console.WriteLine(
                    $"  hint={hint} open={st.PanelOpen} tiles={st.InBoundsTiles} branch={scratch.LastBranchKind} " +
                    $"root=0x{scratch.LastBranchRoot:X} controllerVisBypass={bypass}");
            }
        }

        var ritual = new Poe2RitualRewards(reader, live);
        var window = ritual.ReadWindowState(igs, w, h, allowFullLocate: true, Poe2UiAnchors.BranchKind.None, forceBfsFallback: true);
        var rewards = window.PanelOpen
            ? ritual.ReadRewardsFromCachedWindow(w, h, forceRefresh: true)
            : Array.Empty<Poe2RitualRewards.RitualRewardTile>();
        var ctrlBypass = ritual.LastBranchRoot != 0 && ritual.LastBranchRoot == controllerUi;
        Console.WriteLine(
            $"Panel open={window.PanelOpen} path={window.PathKind} branch={ritual.LastBranchKind} " +
            $"root=0x{ritual.LastBranchRoot:X} fastChain=[{ritual.LastFastChainChildA},{ritual.LastFastChainChildB}] " +
            $"controllerVisBypass={ctrlBypass} tiles={rewards.Count} sig=0x{window.ItemSignature:X}");
        Console.WriteLine($"Perf: fast={ritual.Perf.FastChainHits} bfs={ritual.Perf.BfsHits} full={ritual.Perf.FullReads} cache={ritual.Perf.CacheHits} probeMs={ritual.Perf.LastProbeMs:F2}");

        foreach (var (i, r) in rewards.Select((t, idx) => (idx, t.Reward)))
        {
            Console.WriteLine($"  [{i}] rect=({r.X:F0},{r.Y:F0}) {r.W:F0}x{r.H:F0}");
            Console.WriteLine($"       rarity={r.Rarity} name=\"{r.Name ?? "-"}\" art=\"{r.Art ?? "-"}\"");
        }

        if (!window.PanelOpen)
        {
            Console.WriteLine();
            Console.WriteLine("Panel not detected. If the shop IS open:");
            Console.WriteLine("  - Check signature hits above (text may have changed after a patch).");
            Console.WriteLine("  - Brute-scanning fast-chain indices on plausible branches...");
            foreach (var root in branches)
            {
                if (!Poe2UiAnchors.IsPlausibleBranch(reader, root)) continue;
                BruteFastChain(reader, root);
            }
        }

        Console.WriteLine();
        Console.WriteLine("Paste-ready offset block:");
        Console.WriteLine("    public static class Ritual");
        Console.WriteLine("    {");
        var chainA = window.PanelOpen ? ritual.LastFastChainChildA : Poe2.Ritual.FastChainChildA;
        var chainB = window.PanelOpen ? ritual.LastFastChainChildB : Poe2.Ritual.FastChainChildB;
        Console.WriteLine($"        public const int FastChainChildA = {chainA};");
        Console.WriteLine($"        public const int FastChainChildB = {chainB};");
        Console.WriteLine($"        public const int TileItemEntityPtr = 0x{Poe2.Ritual.TileItemEntityPtr:X};");
        Console.WriteLine("    }");
        return 0;
    }

    private static void DumpBranchDiagnostics(MemoryReader reader, string label, nint root)
    {
        var a = ChildAt(reader, root, Poe2.Ritual.FastChainChildA);
        var b = a != 0 ? ChildAt(reader, a, Poe2.Ritual.FastChainChildB) : 0;
        Console.WriteLine($"{label} fast chain: child[{Poe2.Ritual.FastChainChildA}]=0x{a:X} child[{Poe2.Ritual.FastChainChildB}]=0x{b:X}");
    }

    private static string FormatChildCount(MemoryReader reader, nint parent)
    {
        var n = ChildCount(reader, parent);
        return n < 0 ? "invalid" : $"{n} children";
    }

    private static long ChildCount(MemoryReader reader, nint parent)
    {
        if (parent == 0) return 0;
        var first = Ptr(reader, parent + Poe2.UiElement.Children);
        if (first == 0) return 0;
        if (!reader.TryReadStruct<nint>(parent + Poe2.UiElement.ChildrenEnd, out var last)) return -1;
        var n = ((long)last - (long)first) / 8;
        return n is >= 0 and <= 4096 ? n : -1;
    }

    private static void RitualSignatureScan(MemoryReader reader, nint root, int maxNodes)
    {
        if (root == 0) return;
        var queue = new Queue<nint>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited < maxNodes)
        {
            var el = queue.Dequeue();
            if (el == 0) continue;
            visited++;
            var text = ReadUiText(reader, el);
            foreach (var sig in Poe2.Ritual.SignatureTexts)
            {
                if (text.Contains(sig, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  SIG \"{sig}\" @ 0x{el:X} text=\"{TrimProbe(text, 80)}\"");
                    break;
                }
            }
            if (!TryChildren(reader, el, out var first, out var n)) continue;
            for (long i = 0; i < n; i++) queue.Enqueue(Ptr(reader, first + (nint)(i * 8)));
        }
    }

    private static bool RitualHasSignature(MemoryReader reader, nint root, int maxNodes)
    {
        if (root == 0) return false;
        var queue = new Queue<nint>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited < maxNodes)
        {
            var el = queue.Dequeue();
            if (el == 0) continue;
            visited++;
            var text = ReadUiText(reader, el);
            foreach (var sig in Poe2.Ritual.SignatureTexts)
                if (text.Contains(sig, StringComparison.OrdinalIgnoreCase)) return true;
            if (!TryChildren(reader, el, out var first, out var n)) continue;
            for (long i = 0; i < n; i++) queue.Enqueue(Ptr(reader, first + (nint)(i * 8)));
        }
        return false;
    }

    private static void BruteFastChain(MemoryReader reader, nint root)
    {
        var childCount = ChildCount(reader, root);
        var hits = 0;
        for (var a = 0; a < childCount && hits < 8; a++)
        {
            var ritualRoot = ChildAt(reader, root, a);
            if (ritualRoot == 0) continue;
            var bCount = ChildCount(reader, ritualRoot);
            for (var b = 0; b < bCount && hits < 8; b++)
            {
                var window = ChildAt(reader, ritualRoot, b);
                if (window == 0) continue;
                if (!RitualHasSignature(reader, window, 48)) continue;
                Console.WriteLine($"  candidate fast chain [{a}][{b}] window=0x{window:X}");
                hits++;
            }
        }
        if (hits == 0)
            Console.WriteLine($"  no signature-bearing window found under 0x{root:X} (childCount={childCount})");
    }

    private static bool TryChildren(MemoryReader reader, nint el, out nint first, out long n)
    {
        first = Ptr(reader, el + Poe2.UiElement.Children);
        n = 0;
        if (first == 0) return false;
        if (!reader.TryReadStruct<nint>(el + Poe2.UiElement.ChildrenEnd, out var last)) return false;
        n = ((long)last - (long)first) / 8;
        return n is > 0 and <= 4000;
    }

    private static string ReadUiText(MemoryReader reader, nint el)
    {
        if (!reader.TryReadStruct<long>(el + Poe2.UiElement.Text + 0x10, out var len) || len <= 0 || len > 1024) return "";
        if (len < 8) return reader.ReadStringUtf16(el + Poe2.UiElement.Text, (int)len);
        var ptr = Ptr(reader, el + Poe2.UiElement.Text);
        return ptr == 0 ? "" : reader.ReadStringUtf16(ptr, (int)len);
    }

    private static string TrimProbe(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

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
        w = 1920;
        h = 1080;
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(process.ProcessId);
            if (p.MainWindowHandle != 0 && GetClientRect(p.MainWindowHandle, out var rc))
            {
                w = rc.Right - rc.Left;
                h = rc.Bottom - rc.Top;
            }
        }
        catch
        {
            // fallback resolution
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
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
