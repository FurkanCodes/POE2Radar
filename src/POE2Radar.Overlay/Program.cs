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

CrashLog.Write("Startup", $"POE2Radar process started. BaseDirectory={AppContext.BaseDirectory}");

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
