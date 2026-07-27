using System.Diagnostics;
using System.Runtime.InteropServices;
using POE2Radar.Overlay.Diagnostics;

namespace POE2Radar.Overlay.Stealth;

/// <summary>
/// Release-only relaunch under a random-named hardlink next to the real exe.
/// Skip with <c>--no-stealth</c>; child processes carry <c>--stealth-hl</c>.
/// </summary>
internal static partial class StealthLaunch
{
    public const string RelaunchMarker = "--stealth-hl";
    public const string SkipMarker = "--no-stealth";

    private static string? _hardlinkPathToDelete;

    /// <summary>
    /// When true, the caller should exit with <paramref name="exitCode"/> (parent after spawning child).
    /// When false, continue running in this process (already relaunched, Debug, skipped, or fail-open).
    /// </summary>
    public static bool TryRelaunchAndExit(string[] args, out int exitCode)
    {
        exitCode = 0;

        if (HasFlag(args, SkipMarker))
            return false;

        if (HasFlag(args, RelaunchMarker))
        {
            RegisterHardlinkCleanup();
            return false;
        }

#if DEBUG
        return false;
#else
        return TrySpawnHardlinkChild(args, out exitCode);
#endif
    }

    /// <summary>Parse helpers exposed for unit tests.</summary>
    internal static bool HasFlag(string[] args, string flag)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Build child argv: drop prior markers, append relaunch marker.</summary>
    internal static string[] BuildChildArgs(string[] original)
    {
        var list = new List<string>(original.Length + 1);
        foreach (var a in original)
        {
            if (string.Equals(a, RelaunchMarker, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(a, SkipMarker, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(a);
        }
        list.Add(RelaunchMarker);
        return list.ToArray();
    }

    private static bool TrySpawnHardlinkChild(string[] args, out int exitCode)
    {
        exitCode = 0;
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            {
                CrashLog.Write("Stealth", "Hardlink relaunch skipped: ProcessPath missing.");
                return false;
            }

            var dir = Path.GetDirectoryName(processPath)!;
            PruneStaleHardlinks(dir, processPath);

            string linkPath;
            var attempts = 0;
            do
            {
                linkPath = Path.Combine(dir, StealthIdentity.GenerateHardlinkFileName());
                attempts++;
            } while (File.Exists(linkPath) && attempts < 8);

            if (File.Exists(linkPath))
            {
                CrashLog.Write("Stealth", "Hardlink relaunch skipped: could not pick a free name.");
                return false;
            }

            if (!CreateHardLinkW(linkPath, processPath, 0))
            {
                CrashLog.Write("Stealth", $"Hardlink relaunch skipped: CreateHardLink failed (win32={Marshal.GetLastPInvokeError()}).");
                return false;
            }

            var childArgs = BuildChildArgs(args);
            var psi = new ProcessStartInfo
            {
                FileName = linkPath,
                UseShellExecute = false,
                WorkingDirectory = Directory.GetCurrentDirectory(),
            };
            foreach (var a in childArgs)
                psi.ArgumentList.Add(a);

            Process.Start(psi);
            exitCode = 0;
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Stealth", $"Hardlink relaunch skipped: {ex.Message}");
            return false;
        }
    }

    private static void RegisterHardlinkCleanup()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            var name = Path.GetFileName(path);
            if (!StealthIdentity.IsHardlinkFileName(name)) return;
            _hardlinkPathToDelete = path;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDeleteHardlink();
        }
        catch
        {
            // Cleanup is best-effort.
        }
    }

    private static void TryDeleteHardlink()
    {
        var path = _hardlinkPathToDelete;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path) && StealthIdentity.IsHardlinkFileName(Path.GetFileName(path)))
                File.Delete(path);
        }
        catch
        {
            // File may still be locked briefly; prune on next start will catch it.
        }
    }

    /// <summary>Best-effort: delete orphaned hardlink names that are not our current process.</summary>
    internal static void PruneStaleHardlinks(string directory, string? keepPath)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*.exe"))
            {
                var name = Path.GetFileName(file);
                if (!StealthIdentity.IsHardlinkFileName(name)) continue;
                if (keepPath is not null &&
                    string.Equals(file, keepPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { File.Delete(file); }
                catch { /* locked / in use */ }
            }
        }
        catch
        {
            // Ignore prune failures.
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string lpFileName, string lpExistingFileName, nint lpSecurityAttributes);
}
