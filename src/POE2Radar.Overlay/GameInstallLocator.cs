using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using POE2Radar.Core;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay;

/// <summary>Finds the PoE2 client executable on disk (Steam, standalone, saved path, running process).</summary>
internal static class GameInstallLocator
{
    private const string SteamCommonFolder = @"Path of Exile 2";

    internal enum LocateSource
    {
        None,
        Saved,
        RunningProcess,
        Steam,
        Registry,
        CommonPath,
    }

    internal sealed record LocateResult(string? Path, LocateSource Source, string Message)
    {
        public bool Found => !string.IsNullOrWhiteSpace(Path);
    }

    internal static bool IsValidGameExe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        var name = Path.GetFileName(path);
        return name.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    internal static LocateResult Discover(string? savedPath)
    {
        if (IsValidGameExe(savedPath))
            return new(Normalize(savedPath!), LocateSource.Saved, "Using saved game path.");

        if (TryFromRunningProcess(out var running))
            return new(running, LocateSource.RunningProcess, "Found from the running game process.");

        foreach (var path in EnumerateSteamCandidates())
        {
            if (IsValidGameExe(path))
                return new(path, LocateSource.Steam, "Found via Steam library.");
        }

        if (TryFromRegistry(out var registry))
            return new(registry, LocateSource.Registry, "Found via Windows install record.");

        foreach (var path in EnumerateCommonCandidates())
        {
            if (IsValidGameExe(path))
                return new(path, LocateSource.CommonPath, "Found in a common install folder.");
        }

        return new(null, LocateSource.None, "Path of Exile 2 not found. Browse to PathOfExile.exe or PathOfExileSteam.exe.");
    }

    internal static bool TryLaunch(string path, out string? error)
    {
        error = null;
        if (!IsValidGameExe(path))
        {
            error = "Select a valid Path of Exile 2 executable first.";
            return false;
        }

        try
        {
            var full = Path.GetFullPath(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = full,
                WorkingDirectory = Path.GetDirectoryName(full)!,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static void SavePath(RadarSettings settings, string path)
    {
        settings.GameExePath = Normalize(path);
        settings.Save();
    }

    private static string Normalize(string path) => Path.GetFullPath(path);

    private static bool TryFromRunningProcess(out string path)
    {
        path = "";
        try
        {
            using var handle = ProcessHandle.AttachToPoE();
            if (handle is null) return false;
            if (!IsValidGameExe(handle.ModulePath)) return false;
            path = handle.ModulePath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateSteamCandidates()
    {
        var steamRoot = ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        if (string.IsNullOrWhiteSpace(steamRoot)) yield break;

        steamRoot = steamRoot.Replace('/', '\\');
        foreach (var library in EnumerateSteamLibraries(steamRoot))
        {
            yield return Path.Combine(library, "steamapps", "common", SteamCommonFolder, "PathOfExileSteam.exe");
            yield return Path.Combine(library, "steamapps", "common", SteamCommonFolder, "PathOfExile.exe");
        }
    }

    private static IEnumerable<string> EnumerateSteamLibraries(string steamRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (seen.Add(steamRoot))
            yield return steamRoot;

        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
        {
            var lib = match.Groups[1].Value.Replace(@"\\", @"\").Replace('/', '\\');
            if (!string.IsNullOrWhiteSpace(lib) && seen.Add(lib))
                yield return lib;
        }
    }

    private static bool TryFromRegistry(out string path)
    {
        path = "";
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var sub in new[]
                     {
                         @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                         @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                     })
            {
                using var key = root.OpenSubKey(sub);
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(name);
                    var display = app?.GetValue("DisplayName") as string;
                    if (display is null || !display.Contains("Path of Exile 2", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var installDir = app?.GetValue("InstallLocation") as string;
                    if (TryPickExeInFolder(installDir, out path)) return true;

                    var icon = app?.GetValue("DisplayIcon") as string;
                    if (TryNormalizeExe(icon, out path)) return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCommonCandidates()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return Path.Combine(programFilesX86, "Grinding Gear Games", "Path of Exile 2", "PathOfExile.exe");
        yield return Path.Combine(programFiles, "Grinding Gear Games", "Path of Exile 2", "PathOfExile.exe");
        yield return Path.Combine(localApp, "Programs", "Path of Exile 2", "PathOfExile.exe");

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            var root = drive.RootDirectory.FullName.TrimEnd('\\');
            yield return Path.Combine(root, "SteamLibrary", "steamapps", "common", SteamCommonFolder, "PathOfExileSteam.exe");
            yield return Path.Combine(root, "Program Files (x86)", "Steam", "steamapps", "common", SteamCommonFolder, "PathOfExileSteam.exe");
        }
    }

    private static bool TryPickExeInFolder(string? folder, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;

        foreach (var name in new[] { "PathOfExileSteam.exe", "PathOfExile.exe", "PathOfExileEGS.exe", "PathOfExile_x64.exe", "PathOfExile_KG.exe" })
        {
            var candidate = Path.Combine(folder, name);
            if (IsValidGameExe(candidate))
            {
                path = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryNormalizeExe(string? value, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim().Trim('"');
        var comma = trimmed.IndexOf(',');
        if (comma >= 0) trimmed = trimmed[..comma];
        return IsValidGameExe(trimmed) && (path = trimmed).Length > 0;
    }

    private static string? ReadRegistryString(RegistryKey root, string subKey, string valueName)
    {
        using var key = root.OpenSubKey(subKey);
        return key?.GetValue(valueName) as string;
    }
}
