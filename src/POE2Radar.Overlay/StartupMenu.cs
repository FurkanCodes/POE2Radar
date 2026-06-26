using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Diagnostics;

namespace POE2Radar.Overlay;

/// <summary>GameHelper-style launcher: ImGui menu before the in-game overlay starts.</summary>
internal static class StartupMenu
{
    /// <summary>Blocks until the user starts the radar or quits. Returns null on quit.</summary>
    public static AttachResult? Run()
    {
        AttachResult? started = null;
        Exception? threadError = null;

        var thread = new Thread(() =>
        {
            try
            {
                var settings = RadarSettings.Load();
                using var menu = new StartupMenuOverlay(settings);
                menu.Run().GetAwaiter().GetResult();
                if (menu.Started && menu.Result is { } result)
                    started = result;
            }
            catch (Exception ex)
            {
                threadError = ex;
                CrashLog.Write("Startup menu failed", ex);
            }
        })
        {
            IsBackground = false,
            Name = "POE2Radar StartupMenu",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadError is not null)
            return null;

        return started;
    }
}
