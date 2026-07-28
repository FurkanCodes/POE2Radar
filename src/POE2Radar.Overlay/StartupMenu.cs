using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Diagnostics;
using POE2Radar.Overlay.UI;

namespace POE2Radar.Overlay;

/// <summary>Classic WinForms launcher shown before the in-game overlay starts.</summary>
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
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using var menu = new StartupForm(settings);
                Application.Run(menu);
                if (menu.Result is { } result)
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
            Name = "StartupMenu",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadError is not null)
            return null;

        return started;
    }
}
