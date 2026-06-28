using ImGuiNET;

namespace POE2Radar.Overlay;

/// <summary>Lazy-loads optional PNG content icons from <c>atlas-content-icons/</c> next to the exe.</summary>
internal static class AtlasContentIcons
{
    private static readonly Dictionary<string, IntPtr> _textures = new(StringComparer.OrdinalIgnoreCase);
    private static bool _dirChecked;

    public static bool TryGet(string? basename, out IntPtr textureId)
    {
        textureId = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(basename)) return false;
        if (_textures.TryGetValue(basename, out textureId))
            return textureId != IntPtr.Zero;
        if (!_dirChecked)
        {
            _dirChecked = true;
            var dir = Path.Combine(AppContext.BaseDirectory, "atlas-content-icons");
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    _textures[name] = IntPtr.Zero;
                }
            }
        }
        if (!_textures.ContainsKey(basename))
            _textures[basename] = IntPtr.Zero;
        return false;
    }
}
