using POE2Radar.Core;
using POE2Radar.Core.Game;

namespace POE2Radar.Research;

internal static class RunecraftResearchProbes
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
                Console.WriteLine("Not in game — open PoE2 and open Runeshape Combinations (controller or KBM).");
                if (!watch) return 1;
            }
            else
            {
                var snap = live.ProbeRunecraftUi(igs, w, h);
                PrintSnapshot(snap, w, h);
            }

            if (!watch) return 0;
            Console.WriteLine($"--- watch {intervalMs}ms (Ctrl+C to stop) ---");
            Thread.Sleep(Math.Clamp(intervalMs, 100, 5000));
        }
        while (watch);

        return 0;
    }

    private static void PrintSnapshot(Poe2Live.RunecraftUiProbeSnapshot snap, float w, float h)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] client {w:F0}x{h:F0}");
        Console.WriteLine(
            $"  cache panel=0x{snap.CachedPanel:X} branchRoot=0x{snap.CachedBranchRoot:X} " +
            $"viewport=0x{snap.CachedViewport:X} hint={snap.ProbeHint} nextScan={FormatUtc(snap.NextScanUtc)}");

        foreach (var branch in snap.Branches)
        {
            Console.WriteLine(
                $"  [{branch.Branch}] root=0x{branch.Root:X} fp=0x{branch.FpPanelAddress:X} bfs=0x{branch.BfsPanelAddress:X} " +
                $"panel=0x{branch.PanelAddress:X} via={branch.DiscoverSource} viewport=0x{branch.ViewportAddress:X} " +
                $"gate={branch.GateVisible} rawRows={branch.RawRowCount}");
        }

        var read = snap.Read;
        Console.WriteLine(
            $"  ReadRuneshapePanel → open={read.IsOpen} branch={read.Branch} rows={read.Rows.Length} " +
            $"scroll=({snap.Scroll.X:F1},{snap.Scroll.Y:F1})");
        Console.WriteLine(
            $"  viewportRect=({snap.ViewportRect.X:F0},{snap.ViewportRect.Y:F0} {snap.ViewportRect.W:F0}x{snap.ViewportRect.H:F0}) " +
            $"panelRect=({snap.PanelRect.X:F0},{snap.PanelRect.Y:F0} {snap.PanelRect.W:F0}x{snap.PanelRect.H:F0})");

        for (var i = 0; i < snap.SampleRows.Length; i++)
        {
            var row = snap.SampleRows[i];
            Console.WriteLine(
                $"    row[{i}] '{row.RawLabel}' rect=({row.RowRect.X:F0},{row.RowRect.Y:F0} {row.RowRect.W:F0}x{row.RowRect.H:F0}) " +
                $"contentRight={row.ContentRightX:F0} childRight={row.RightmostChildRight:F0} children={row.ChildCount}");
            foreach (var child in row.Children)
            {
                Console.WriteLine(
                    $"      child[{child.Index}] visible={child.Visible} " +
                    $"rect=({child.Rect.X:F0},{child.Rect.Y:F0} {child.Rect.W:F0}x{child.Rect.H:F0})");
            }
        }

        WarnIfNeeded(snap);
    }

    private static void WarnIfNeeded(Poe2Live.RunecraftUiProbeSnapshot snap)
    {
        var read = snap.Read;
        if (!read.IsOpen)
        {
            if (snap.Branches.Length > 0 && snap.Branches.All(b => !b.PanelFound))
                Console.WriteLine("  WARN panel not found on any branch — open Runeshape Combinations in-game.");
            else if (snap.Branches.Any(b => b.BfsPanelAddress != 0 && b.FpPanelAddress == 0))
                Console.WriteLine("  HINT BFS sees a recipes container but fingerprint walk missed it — overlay uses BFS fallback.");
            return;
        }

        var controllerBranch = snap.Branches.FirstOrDefault(b =>
            string.Equals(b.Branch, nameof(Poe2UiAnchors.BranchKind.Controller), StringComparison.Ordinal));
        var kbmBranch = snap.Branches.FirstOrDefault(b =>
            string.Equals(b.Branch, nameof(Poe2UiAnchors.BranchKind.KeyboardMouse), StringComparison.Ordinal));
        if (read.Branch == Poe2UiAnchors.BranchKind.KeyboardMouse && controllerBranch.PanelAddress == 0 && kbmBranch.PanelAddress != 0)
            Console.WriteLine("  NOTE panel is on GameUi (KBM tree) only — normal when using a controller; overlay still works.");

        if (snap.ViewportRect.W <= 1f || snap.ViewportRect.H <= 1f)
            Console.WriteLine("  WARN viewport rect invalid — scroll clip and anchors may be wrong.");

        var controllerOpen = snap.Branches.Any(b =>
            b.PanelFound && b.GateVisible &&
            string.Equals(b.Branch, nameof(Poe2UiAnchors.BranchKind.Controller), StringComparison.Ordinal));
        var kbmOpen = snap.Branches.Any(b =>
            b.PanelFound && b.GateVisible &&
            string.Equals(b.Branch, nameof(Poe2UiAnchors.BranchKind.KeyboardMouse), StringComparison.Ordinal));

        if (controllerOpen && read.Branch == Poe2UiAnchors.BranchKind.KeyboardMouse)
            Console.WriteLine("  WARN panel visible on Controller branch but reader reports KeyboardMouse.");
        if (kbmOpen && read.Branch == Poe2UiAnchors.BranchKind.Controller)
            Console.WriteLine("  WARN panel visible on KBM branch but reader reports Controller.");

        foreach (var row in snap.SampleRows)
        {
            if (row.ChildCount < 2)
                Console.WriteLine($"  WARN row '{row.RawLabel}' has <2 children — price may need viewport fallback.");
            if (row.RowRect.W > 1f && row.ContentRightX > 0f &&
                Math.Abs(row.ContentRightX - (row.RowRect.X + row.RowRect.W)) < 2f)
                Console.WriteLine($"  WARN row '{row.RawLabel}' contentRight ≈ row right — name/icon split may be missing.");
        }

        if (read.IsOpen && read.Rows.Length == 0)
            Console.WriteLine("  WARN OPEN but zero priced rows — scroll or geometry miss.");
    }

    private static string FormatUtc(DateTime utc)
        => utc == DateTime.MinValue ? "ready" : utc.ToString("HH:mm:ss.fff");
}
