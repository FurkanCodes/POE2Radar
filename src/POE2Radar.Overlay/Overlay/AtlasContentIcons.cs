namespace POE2Radar.Overlay;

/// <summary>Resolves PNG content icons from <c>atlas-content-icons/</c> next to the exe.</summary>
internal static class AtlasContentIcons
{
    private static readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private static bool _dirChecked;

    public static bool TryGetPath(string? basename, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(basename)) return false;
        EnsureIndexed();
        if (_paths.TryGetValue(basename, out path!)) return true;
        // Allow basename without extension / with .png
        var key = Path.GetFileNameWithoutExtension(basename);
        return _paths.TryGetValue(key, out path!);
    }

    /// <summary>Legacy no-op GPU stub — overlay draws via <see cref="TryGetPath"/> + texture cache.</summary>
    public static bool TryGet(string? basename, out IntPtr textureId)
    {
        textureId = IntPtr.Zero;
        return false;
    }

    private static void EnsureIndexed()
    {
        if (_dirChecked) return;
        _dirChecked = true;
        var dir = Path.Combine(AppContext.BaseDirectory, "atlas-content-icons");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
            _paths[Path.GetFileNameWithoutExtension(file)] = file;
    }
}
