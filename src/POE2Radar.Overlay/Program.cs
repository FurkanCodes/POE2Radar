using POE2Radar.Overlay;
using POE2Radar.Overlay.Diagnostics;
using POE2Radar.Overlay.Stealth;

CrashLog.InstallGlobalHandlers();

if (args is ["--export-release-assets", var releaseDir, ..])
{
    var target = Path.GetFullPath(releaseDir);
    IconLibrary.MaterializeTo(target);
    Console.WriteLine($"Exported built-in icons to {Path.Combine(target, "icons")}");
    return 0;
}

if (StealthLaunch.TryRelaunchAndExit(args, out var stealthExit))
    return stealthExit;

CrashLog.Write("Startup", $"Process started. BaseDirectory={AppContext.BaseDirectory}");

var attach = StartupMenu.Run();
if (attach is null)
    return 0;

var (process, reader, slot) = attach.Take();
attach.Dispose();

using (process)
{
    using var app = new RadarApp(process, reader, slot);
    app.Run();
}

return 0;
