using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace POE2Radar.Core.Game;

/// <summary>
/// Campaign zone code → boss name, parsed from embedded <c>zone_notes.json</c> act tables and zone notes.
/// Not exhaustive (Interludes are sparse); live unique monsters still win via <see cref="EntityNameResolver"/>.
/// </summary>
public sealed class ZoneBossCatalog
{
    private static readonly Regex ActLineRegex = new(
        @"^\s*(?<zone>.+?)\s+-\s+(?<boss>.+?)\s+-",
        RegexOptions.Compiled);

    private static readonly Regex DropsRegex = new(
        @"\b([A-Za-z][A-Za-z'-]+)\s+drops\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex KillRegex = new(
        @"\bKill\s+(?:the\s+)?([A-Za-z][A-Za-z'-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ListEntryRegex = new(
        @"^\d+\)\s*([A-Za-z][A-Za-z'-]+)\s+-",
        RegexOptions.Compiled);

    private static readonly Regex BossLabelRegex = new(
        @"\bBoss:\s*([A-Za-z][A-Za-z' -]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly Dictionary<string, string> _byZoneCode = new(StringComparer.OrdinalIgnoreCase);

    public static ZoneBossCatalog Shared { get; } = LoadEmbedded();

    public string? BossName(string? zoneCode)
    {
        if (string.IsNullOrWhiteSpace(zoneCode)) return null;
        return _byZoneCode.TryGetValue(zoneCode.Trim(), out var name) ? name : null;
    }

    public int Count => _byZoneCode.Count;

    private static ZoneBossCatalog LoadEmbedded()
    {
        var catalog = new ZoneBossCatalog();
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.Contains("zone_notes"));
            if (resName == null) return catalog;

            using var stream = asm.GetManifestResourceStream(resName);
            if (stream is null) return catalog;
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var byZoneName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("actNotes", out var actArr) && actArr.ValueKind == JsonValueKind.Array)
                foreach (var act in actArr.EnumerateArray())
                {
                    if (!act.TryGetProperty("notes", out var notesEl)) continue;
                    ParseActTable(notesEl.GetString() ?? "", byZoneName);
                }

            if (root.TryGetProperty("zoneNotes", out var zoneArr) && zoneArr.ValueKind == JsonValueKind.Array)
                foreach (var zn in zoneArr.EnumerateArray())
                {
                    var code = Str(zn, "zoneCode");
                    if (code.Length == 0) continue;

                    var zoneName = Norm(Str(zn, "zoneName"));
                    if (zoneName.Length > 0 && byZoneName.TryGetValue(zoneName, out var fromAct))
                        catalog._byZoneCode[code] = fromAct;

                    if (!catalog._byZoneCode.ContainsKey(code))
                    {
                        var fromNotes = ExtractBossFromZoneNotes(Str(zn, "notes"));
                        if (fromNotes is { Length: > 0 }) catalog._byZoneCode[code] = fromNotes;
                    }
                }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ZoneBossCatalog load failed: {ex.Message}");
        }
        return catalog;
    }

    private static void ParseActTable(string notes, Dictionary<string, string> byZoneName)
    {
        foreach (var line in notes.Split('\n'))
        {
            var m = ActLineRegex.Match(line);
            if (!m.Success) continue;
            var zone = Norm(m.Groups["zone"].Value);
            var boss = CleanupBossName(m.Groups["boss"].Value);
            if (zone.Length > 0 && boss.Length > 0) byZoneName[zone] = boss;
        }
    }

    private static string? ExtractBossFromZoneNotes(string notes)
    {
        if (notes.Length == 0) return null;

        foreach (var line in notes.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var drops = DropsRegex.Match(trimmed);
            if (drops.Success) return CleanupBossName(drops.Groups[1].Value);

            var kill = KillRegex.Match(trimmed);
            if (kill.Success) return CleanupBossName(kill.Groups[1].Value);

            var list = ListEntryRegex.Match(trimmed);
            if (list.Success) return CleanupBossName(list.Groups[1].Value);

            var bossLabel = BossLabelRegex.Match(trimmed);
            if (bossLabel.Success) return CleanupBossName(bossLabel.Groups[1].Value);
        }
        return null;
    }

    private static string CleanupBossName(string raw)
    {
        var s = raw.Trim();
        if (s.EndsWith(" Boss", StringComparison.OrdinalIgnoreCase))
            s = s[..^5].Trim();
        if (s.StartsWith("Boss ", StringComparison.OrdinalIgnoreCase))
            s = s[5..].Trim();
        return s;
    }

    private static string Norm(string s) => WhitespaceRegex.Replace(s.Trim(), " ");

    private static string Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
