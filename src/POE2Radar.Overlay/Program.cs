using POE2Radar.Core;
using POE2Radar.Overlay;
using POE2Radar.Overlay.Diagnostics;

CrashLog.InstallGlobalHandlers();

if (args is ["--export-release-assets", var releaseDir, ..])
{
    var target = Path.GetFullPath(releaseDir);
    IconLibrary.MaterializeTo(target);
    Console.WriteLine($"Exported built-in icons to {Path.Combine(target, "icons")}");
    return 0;
}

[System.Diagnostics.CodeAnalysis.DoesNotReturn]
static void Fail(int code, string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine();
    Console.Error.WriteLine("Press Enter to exit...");
    try { Console.ReadLine(); } catch { }
    Environment.Exit(code);
}

CrashLog.Write("Startup", $"POE2Radar process started. BaseDirectory={AppContext.BaseDirectory}");

Console.WriteLine("POE2Radar — map/radar overlay");
Console.WriteLine("=============================");

using var process = ProcessHandle.AttachToPoE();
if (process is null)
    Fail(1, "PoE2 not running (no matching process found).\nStart Path of Exile 2 first, then launch POE2Radar again.");
Console.WriteLine($"Attached to {process.ProcessName} (PID {process.ProcessId})");

var reader = new MemoryReader(process);

var slot = Bootstrap.ResolveGameStateSlot(process, reader);
if (slot == 0)
    Fail(2, "Could not attach to in-game state.\nMake sure you are loaded into a zone (not login / character select), then try again as Administrator.");

Console.WriteLine();
Console.WriteLine("Radar running. Open the in-game map to see terrain + entities.");
Console.WriteLine("Quit this overlay before rebuilding — a running exe locks bin\\ and stale builds won't apply.");
Console.WriteLine("F4 over an entity = inspect (console + on-screen HUD). F5 = hide type. F10 on atlas = tile pick.");
Console.WriteLine("Atlas: open it in-game; rings are auto-positioned.");
Console.WriteLine("Ctrl+C to exit.");

using var app = new RadarApp(process, reader, slot);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; app.RequestShutdown(); };
app.Run();

Console.WriteLine("Done.");
return 0;
