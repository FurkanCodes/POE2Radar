using System.Text.Json;
using System.Text.Json.Serialization;

namespace POE2Radar.Overlay.Web;

/// <summary>Per-zone-type override for one entity metadata token (last path segment). Only stores
/// deltas from the global display rules — unset fields inherit the global rule.</summary>
public sealed class ZoneEntityOverride
{
    public bool? Hide { get; set; }
    public bool? Navigable { get; set; }
}

/// <summary>Zone-specific entity visibility/nav overrides keyed by area code (zone type), then metadata
/// token. Persisted to <c>config/zone_entity_overrides.json</c>. Toggles in "Types in this zone"
/// write here — never the global <see cref="DisplayRules"/> file.</summary>
public sealed class ZoneEntityOverrides
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private Dictionary<string, Dictionary<string, ZoneEntityOverride>> _byArea = new(StringComparer.OrdinalIgnoreCase);
    private volatile int _generation;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ZoneEntityOverrides(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public int Generation => _generation;

    /// <summary>Override for <paramref name="areaCode"/> + <paramref name="token"/>, or null if none.</summary>
    public ZoneEntityOverride? GetOverride(string areaCode, string token)
    {
        if (string.IsNullOrEmpty(areaCode) || string.IsNullOrEmpty(token)) return null;
        lock (_gate)
        {
            var area = _byArea.GetValueOrDefault(areaCode);
            return area?.GetValueOrDefault(token);
        }
    }

  /// <summary>Whether an override row exists (even if all fields are null — shouldn't happen).</summary>
    public bool HasOverride(string areaCode, string token)
    {
        if (string.IsNullOrEmpty(areaCode) || string.IsNullOrEmpty(token)) return false;
        lock (_gate)
        {
            var area = _byArea.GetValueOrDefault(areaCode);
            return area != null && area.ContainsKey(token);
        }
    }

    /// <summary>Set or clear override fields. Pass null for <paramref name="hide"/> / <paramref name="navigable"/>
    /// to leave that field unchanged. Removes the entry when both resolved fields match "no override".</summary>
    public void SetOverride(string areaCode, string token, bool? hide, bool? navigable)
    {
        if (string.IsNullOrEmpty(areaCode) || string.IsNullOrEmpty(token)) return;
        lock (_gate)
        {
            if (!_byArea.TryGetValue(areaCode, out var area))
            {
                area = new Dictionary<string, ZoneEntityOverride>(StringComparer.OrdinalIgnoreCase);
                _byArea[areaCode] = area;
            }

            if (!area.TryGetValue(token, out var ov))
            {
                ov = new ZoneEntityOverride();
                area[token] = ov;
            }

            if (hide.HasValue) ov.Hide = hide;
            if (navigable.HasValue) ov.Navigable = navigable;

            if (ov.Hide is null && ov.Navigable is null)
                area.Remove(token);
            if (area.Count == 0)
                _byArea.Remove(areaCode);

            _generation++;
            Save();
        }
    }

    public void ClearOverride(string areaCode, string token)
    {
        if (string.IsNullOrEmpty(areaCode) || string.IsNullOrEmpty(token)) return;
        lock (_gate)
        {
            var area = _byArea.GetValueOrDefault(areaCode);
            if (area == null || !area.Remove(token)) return;
            if (area.Count == 0) _byArea.Remove(areaCode);
            _generation++;
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var dict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ZoneEntityOverride>>>(
                File.ReadAllText(_filePath), Json);
            if (dict != null)
            {
                _byArea = new Dictionary<string, Dictionary<string, ZoneEntityOverride>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (area, tokens) in dict)
                    _byArea[area] = new Dictionary<string, ZoneEntityOverride>(tokens, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Zone entity overrides load failed: {ex.Message}"); }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_byArea, Json));
        }
        catch (Exception ex) { Console.Error.WriteLine($"Zone entity overrides save failed: {ex.Message}"); }
    }
}
