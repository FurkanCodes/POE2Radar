using System.Reflection;
using System.Text.Json;

namespace POE2Radar.Core.Game;

/// <summary>Metadata path → in-game boss name when the path token is a type family (e.g. FireBeastBoss → Vornas).
/// Keys are metadata paths; prefix walk matches spawn variants like EntityNameResolver.</summary>
public sealed class BossDisplayNameCatalog
{
    private readonly Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public static BossDisplayNameCatalog Shared { get; } = LoadEmbedded();

    public string? Resolve(string? metadataPath)
    {
        if (string.IsNullOrEmpty(metadataPath)) return null;

        var at = metadataPath.IndexOf('@');
        var path = at >= 0 ? metadataPath[..at] : metadataPath;

        if (_byPath.TryGetValue(path, out var name) && name.Length > 0) return name;

        var probe = path;
        int slash;
        while ((slash = probe.LastIndexOf('/')) > 0)
        {
            probe = probe[..slash];
            if (_byPath.TryGetValue(probe, out name) && name.Length > 0) return name;
        }
        return null;
    }

    public int Count => _byPath.Count;

    private static BossDisplayNameCatalog LoadEmbedded()
    {
        var catalog = new BossDisplayNameCatalog();
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.Contains("boss_display_names"));
            if (resName == null) return catalog;

            using var stream = asm.GetManifestResourceStream(resName)!;
            using var doc = JsonDocument.Parse(stream);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var v = prop.Value.GetString();
                if (v is { Length: > 0 }) catalog._byPath[prop.Name] = v;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BossDisplayNameCatalog load failed: {ex.Message}");
        }
        return catalog;
    }
}
