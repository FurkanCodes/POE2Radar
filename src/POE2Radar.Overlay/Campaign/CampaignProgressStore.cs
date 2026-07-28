using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace POE2Radar.Overlay.Campaign;

/// <summary>
/// Versioned per-character progress. The only profile key written to disk is SHA-256(league + NUL +
/// character name); raw identity strings never enter the serialized document.
/// </summary>
public sealed class CampaignProgressStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private ProgressDocument _document;

    public CampaignProgressStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _document = Load(filePath);
    }

    public static string HashIdentity(string league, string characterName)
    {
        var normalizedLeague = (league ?? "").Trim();
        var normalizedCharacter = (characterName ?? "").Trim();
        if (normalizedLeague.Length == 0 || normalizedCharacter.Length == 0) return "";
        var bytes = Encoding.UTF8.GetBytes(normalizedLeague + "\0" + normalizedCharacter);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public CampaignProfileSnapshot Snapshot(string profileHash)
    {
        lock (_gate)
        {
            var profile = GetOrCreate(profileHash);
            return new CampaignProfileSnapshot(
                profile.Completed.ToHashSet(StringComparer.Ordinal),
                profile.Dismissed);
        }
    }

    public bool IsComplete(string profileHash, string objectiveId)
    {
        lock (_gate)
            return GetOrCreate(profileHash).Completed.Contains(objectiveId);
    }

    public void SetComplete(string profileHash, string objectiveId, bool complete)
    {
        SetCompleted(profileHash, [objectiveId], complete);
    }

    public void SetCompleted(string profileHash, IEnumerable<string> objectiveIds, bool complete)
    {
        if (profileHash.Length == 0) return;
        ArgumentNullException.ThrowIfNull(objectiveIds);
        lock (_gate)
        {
            var profile = GetOrCreate(profileHash);
            var changed = false;
            foreach (var objectiveId in objectiveIds.Where(x => !string.IsNullOrWhiteSpace(x)))
                changed |= complete
                    ? profile.Completed.Add(objectiveId)
                    : profile.Completed.Remove(objectiveId);
            if (changed) SaveLocked();
        }
    }

    public void SetDismissed(string profileHash, bool dismissed)
    {
        if (profileHash.Length == 0) return;
        lock (_gate)
        {
            var profile = GetOrCreate(profileHash);
            if (profile.Dismissed == dismissed) return;
            profile.Dismissed = dismissed;
            SaveLocked();
        }
    }

    public void ReplaceCompleted(string profileHash, IEnumerable<string> objectiveIds)
    {
        if (profileHash.Length == 0) return;
        ArgumentNullException.ThrowIfNull(objectiveIds);
        lock (_gate)
        {
            var profile = GetOrCreate(profileHash);
            profile.Completed = objectiveIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.Ordinal);
            SaveLocked();
        }
    }

    public void Reset(string profileHash)
    {
        if (profileHash.Length == 0) return;
        lock (_gate)
        {
            if (_document.Profiles.Remove(profileHash))
                SaveLocked();
        }
    }

    private ProfileProgress GetOrCreate(string profileHash)
    {
        if (!_document.Profiles.TryGetValue(profileHash, out var profile))
        {
            profile = new ProfileProgress();
            _document.Profiles[profileHash] = profile;
        }
        return profile;
    }

    private static ProgressDocument Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return new ProgressDocument();
            var parsed = JsonSerializer.Deserialize<ProgressDocument>(File.ReadAllText(filePath), Json);
            if (parsed is null || parsed.SchemaVersion > CurrentSchemaVersion || parsed.SchemaVersion <= 0)
                return new ProgressDocument();
            parsed.Profiles ??= new Dictionary<string, ProfileProgress>(StringComparer.Ordinal);
            foreach (var profile in parsed.Profiles.Values)
                profile.Completed ??= new HashSet<string>(StringComparer.Ordinal);
            return parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Campaign progress load failed ({ex.Message}); starting with empty progress.");
            return new ProgressDocument();
        }
    }

    private void SaveLocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_document, Json));
            File.Move(temporary, _filePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Campaign progress save failed: {ex.Message}");
        }
    }

    private sealed class ProgressDocument
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public Dictionary<string, ProfileProgress> Profiles { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ProfileProgress
    {
        public HashSet<string> Completed { get; set; } = new(StringComparer.Ordinal);
        public bool Dismissed { get; set; }
    }
}

public readonly record struct CampaignProfileSnapshot(
    IReadOnlySet<string> Completed,
    bool Dismissed);
