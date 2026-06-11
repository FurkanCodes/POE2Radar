using System.Text.RegularExpressions;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Web;

namespace POE2Radar.Overlay.Config;

/// <summary>Shared metadata-token helpers for display rules, zone overrides, and the Entities tab.</summary>
public static class EntityDisplayHelper
{
    /// <summary>Stable per-type match token: last path segment with trailing "_NN"/digits stripped.</summary>
    public static string TypeToken(string metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return "";
        var slash = metadata.LastIndexOf('/');
        var seg = slash >= 0 ? metadata[(slash + 1)..] : metadata;
        var end = seg.Length;
        while (end > 0 && char.IsDigit(seg[end - 1])) end--;
        if (end > 0 && seg[end - 1] == '_') end--;
        return end > 0 ? seg[..end] : seg;
    }

    public static string RuleLabel(DisplayRule? rule)
    {
        if (rule is null) return "";
        if (rule.Label is { Length: > 0 } lbl) return lbl;
        return rule.Name;
    }

    /// <summary>Best-effort specific name from curated table, humanized token, or path segment.</summary>
    public static string SpecificEntityName(Poe2Live.EntityDot e)
        => SpecificNameFromMetadata(e.Metadata);

    public static string SpecificNameFromMetadata(string metadata)
    {
        var resolved = EntityNameResolver.Shared.Resolve(metadata);
        if (resolved is { Length: > 0 } && !MetadataLabelHelper.IsTokenEquivalent(metadata, resolved))
        {
            const string dnt = "[DNT-UNUSED] ";
            if (resolved.StartsWith(dnt, StringComparison.Ordinal)) resolved = resolved[dnt.Length..];
            return resolved;
        }

        var token = TypeToken(metadata);
        if (token.Length > 0)
        {
            var humanized = HumanizeToken(token);
            if (humanized.Length > 0) return humanized;
        }

        return EntityNameResolver.Shared.ResolveOrShorten(metadata);
    }

    /// <summary>Insert spaces before interior capitals / digit-letter edges (e.g. Waypoint_LongActivationRadius).</summary>
    public static string HumanizeToken(string token)
        => MetadataLabelHelper.HumanizeSegment(token);

    /// <summary>Nav/path/zone label: bosses, mechanics, NPC (name), map-marker POIs as proper names.</summary>
    public static string FormatEntityLabel(
        Poe2Live.EntityDot e,
        DisplayRule? rule,
        IReadOnlyList<Poe2Live.EntityDot>? peers = null,
        string? areaCode = null)
    {
        if (IsBossMonster(e, rule) || IsBossMinimapIcon(e.Metadata))
            return FormatBossDisplayName(e, peers, areaCode);

        if (EndgameMechanicCatalog.TryMatch(e, out var mechanic))
            return mechanic!.Name;

        if (IsBossRoomMetadata(e.Metadata))
            return FormatBossRoomLabel(e.Metadata, peers, areaCode);

        var ruleName = rule?.Name ?? "";
        if (IsGenericPoiRule(ruleName))
        {
            var specific = SpecificEntityName(e);
            if (specific.Length > 0 && IsPlausibleBossDisplayName(specific)) return specific;
        }

        if (IsNpcRule(rule, e))
        {
            var specific = SpecificEntityName(e);
            if (specific.Length > 0 && !string.Equals(specific, "NPC", StringComparison.OrdinalIgnoreCase))
                return $"NPC ({specific})";
            return "NPC";
        }

        var label = RuleLabel(rule);
        return label.Length > 0 ? label : TypeToken(e.Metadata);
    }

    /// <summary>Boss arena / room terrain tiles (not monster entities).</summary>
    public static bool IsBossRoomLandmarkPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (IsBossRoomMetadata(path)) return true;
        return path.Contains("bossroom", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bossarena", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bosswall", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Skip boss-room landmark text when a boss monster is nearby — the entity label is authoritative
    /// and avoids duplicate lines ("Boss room" + "Name (Boss)").
    /// </summary>
    public static bool ShouldDrawBossLandmarkLabel(
        string path,
        string label,
        System.Numerics.Vector2 landmarkCenter,
        IReadOnlyList<Poe2Live.EntityDot> entities,
        Func<Poe2Live.EntityDot, DisplayRule?>? resolve,
        string? areaCode)
    {
        if (label.Length == 0) return false;
        if (!IsBossRoomLandmarkPath(path) && !label.EndsWith(" (Boss)", StringComparison.Ordinal))
            return true;
        if (entities.Count == 0) return true;

        foreach (var e in entities)
        {
            if (!IsBossLikeEntity(e)) continue;
            if (System.Numerics.Vector2.Distance(e.Grid, landmarkCenter) > BossLandmarkSuppressRadiusGrid)
                continue;

            if (label.EndsWith(" (Boss)", StringComparison.Ordinal))
            {
                var entityLabel = FormatEntityLabel(e, resolve?.Invoke(e), entities, areaCode);
                if (string.Equals(entityLabel, label, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Generic room marker or zone-catalog name — nearby spawned boss owns the label.
            return false;
        }
        return true;
    }

    public static bool IsBossLikeEntity(Poe2Live.EntityDot e)
    {
        if (IsBossRoomMetadata(e.Metadata) || IsBossMinimapIcon(e.Metadata)) return false;
        if (e.Category == Poe2Live.EntityCategory.Monster && e.Rarity == Poe2Live.Rarity.Unique) return true;
        return e.Category == Poe2Live.EntityCategory.Monster && IsBossMetadata(e.Metadata);
    }

    private const float BossLandmarkSuppressRadiusGrid = 220f;

    /// <summary>Landmark / tile label — upgrades generic curated "Boss" when a real name is known.</summary>
    public static string FormatLandmarkLabel(
        string path,
        string? curatedName,
        string defaultName,
        IReadOnlyList<Poe2Live.EntityDot>? peers = null,
        string? areaCode = null)
    {
        var raw = curatedName is { Length: > 0 } ? curatedName
            : (defaultName is { Length: > 0 } ? defaultName : "");
        if (!IsGenericBossLabel(raw)) return raw.Length > 0 ? raw : defaultName;
        return FormatBossRoomLabel(path, peers, areaCode, raw);
    }

    private static string FormatBossDisplayName(
        Poe2Live.EntityDot e,
        IReadOnlyList<Poe2Live.EntityDot>? peers,
        string? areaCode)
    {
        var name = SpecificEntityName(e);
        if (!IsPlausibleBossDisplayName(name)) name = "";
        if (name.Length == 0 && e.Metadata.Contains("/Monsters/", StringComparison.OrdinalIgnoreCase))
            name = DeriveMonsterBossName(e.Metadata);
        if (name.Length == 0 && peers != null) name = TryInferZoneBossName(peers) ?? "";
        if (name.Length == 0) name = ZoneBossCatalog.Shared.BossName(areaCode) ?? "";
        if (name.Length == 0) return "Boss";
        return $"{name} (Boss)";
    }

    private static string FormatBossRoomLabel(
        string tileOrMetadataPath,
        IReadOnlyList<Poe2Live.EntityDot>? peers,
        string? areaCode,
        string? genericFallback = null)
    {
        var name = ZoneBossCatalog.Shared.BossName(areaCode) ?? "";
        if (name.Length == 0 && peers != null) name = TryInferZoneBossName(peers) ?? "";
        if (name.Length == 0)
        {
            var fromTile = DeriveNameFromBossTilePath(tileOrMetadataPath);
            if (IsPlausibleBossDisplayName(fromTile)) name = fromTile;
        }
        if (name.Length > 0) return $"{name} (Boss)";
        if (genericFallback is { Length: > 0 }
            && string.Equals(genericFallback, "Bosses", StringComparison.OrdinalIgnoreCase))
            return genericFallback;
        return "Boss room";
    }

    /// <summary>Best-effort zone boss name from spawned unique monsters.</summary>
    public static string? TryInferZoneBossName(IReadOnlyList<Poe2Live.EntityDot> entities)
    {
        foreach (var e in entities)
        {
            if (e.Category != Poe2Live.EntityCategory.Monster) continue;
            if (e.Rarity != Poe2Live.Rarity.Unique) continue;
            if (IsBossRoomMetadata(e.Metadata) || IsBossMinimapIcon(e.Metadata)) continue;
            var name = SpecificEntityName(e);
            if (IsPlausibleBossDisplayName(name) && !IsGenericBossLabel(name)) return name;
        }
        return null;
    }

    private static bool IsGenericPoiRule(string ruleName)
        => string.Equals(ruleName, "Map marker", StringComparison.OrdinalIgnoreCase)
           || string.Equals(ruleName, "Point of Interest", StringComparison.OrdinalIgnoreCase);

    private static bool IsNpcRule(DisplayRule? rule, Poe2Live.EntityDot e)
        => e.Category == Poe2Live.EntityCategory.Npc
           || rule is { Name: { Length: > 0 } n }
              && string.Equals(n, "NPC", StringComparison.OrdinalIgnoreCase);

    private static bool IsGenericBossLabel(string label)
        => string.Equals(label, "Boss", StringComparison.OrdinalIgnoreCase)
           || string.Equals(label, "Bosses", StringComparison.OrdinalIgnoreCase)
           || string.Equals(label, "Boss room", StringComparison.OrdinalIgnoreCase);

    private static bool IsBossMinimapIcon(string metadata)
        => metadata.Contains("bossroomminimapicon", StringComparison.OrdinalIgnoreCase);

    /// <summary>Terrain/POI boss-arena markers — not the boss monster itself.</summary>
    public static bool IsBossRoomMetadata(string metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return false;
        if (IsBossMinimapIcon(metadata)) return false;
        if (metadata.Contains("BossRoom", StringComparison.OrdinalIgnoreCase)) return true;
        if (metadata.Contains("Boss_Room", StringComparison.OrdinalIgnoreCase)) return true;
        if (metadata.Contains("BossArena", StringComparison.OrdinalIgnoreCase)
            && !metadata.Contains("/Monsters/", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool IsBossMonster(Poe2Live.EntityDot e, DisplayRule? rule)
    {
        if (IsBossRoomMetadata(e.Metadata) || IsBossMinimapIcon(e.Metadata)) return false;
        if (rule is { Name: { Length: > 0 } n }
            && (string.Equals(n, "Boss", StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, "Monster · Unique", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (e.Category != Poe2Live.EntityCategory.Monster) return false;
        if (e.Rarity != Poe2Live.Rarity.Unique) return false;
        return IsBossMetadata(e.Metadata);
    }

    public static bool IsBossMetadata(string metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return false;
        if (IsBossRoomMetadata(metadata) || IsBossMinimapIcon(metadata)) return false;
        if (metadata.Contains("Strongbox", StringComparison.OrdinalIgnoreCase)
            || metadata.Contains("StrongBoxes", StringComparison.OrdinalIgnoreCase))
            return false;
        if (metadata.Contains("/boss/", StringComparison.OrdinalIgnoreCase)) return true;
        if (!metadata.Contains("/Monsters/", StringComparison.OrdinalIgnoreCase)) return false;
        return metadata.Contains("Boss", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parse boss name prefix from terrain tile paths (e.g. Qimah_bossroom.tdtx).</summary>
    public static string DeriveNameFromBossTilePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";

        var file = path;
        var colon = file.LastIndexOf(':');
        if (colon > 0) file = file[..colon];
        var dot = file.LastIndexOf('.');
        if (dot > 0) file = file[..dot];

        var bossroomIdx = file.IndexOf("bossroom", StringComparison.OrdinalIgnoreCase);
        if (bossroomIdx <= 0) return "";

        var prefix = file[..bossroomIdx].Trim('_', '-');
        if (prefix.Length == 0
            || string.Equals(prefix, "boss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(prefix, "bossroom", StringComparison.OrdinalIgnoreCase))
            return "";

        return HumanizeUnderscoreSegments(prefix);
    }

    /// <summary>Monster metadata only — never terrain paths.</summary>
    private static string DeriveMonsterBossName(string metadata)
    {
        if (!metadata.Contains("/Monsters/", StringComparison.OrdinalIgnoreCase)) return "";

        var at = metadata.IndexOf('@');
        var path = at >= 0 ? metadata[..at] : metadata;
        var segments = path.Split('/');
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var seg = segments[i];
            if (seg.Length == 0) continue;
            var token = TypeToken(seg);
            if (token.Length == 0 || MonsterSegmentSkip.Contains(token)) continue;
            var humanized = HumanizeToken(token);
            if (humanized.Length > 0 && IsPlausibleBossDisplayName(humanized)) return humanized;
        }
        return "";
    }

    private static bool IsPlausibleBossDisplayName(string name)
    {
        if (name.Length < 2) return false;
        if (name.Contains('/') || name.Contains('\\')) return false;
        if (IsGenericBossLabel(name)) return false;
        if (ZoneCodeLike.IsMatch(name)) return false;

        var lower = name.ToLowerInvariant();
        foreach (var deny in PathSegmentDeny)
            if (lower.Contains(deny, StringComparison.Ordinal)) return false;
        return true;
    }

    private static string HumanizeUnderscoreSegments(string text)
    {
        var parts = text.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        var sb = new System.Text.StringBuilder(text.Length + 4);
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            if (part.Length == 1) sb.Append(part);
            else sb.Append(char.ToUpperInvariant(part[0])).Append(part.AsSpan(1));
        }
        return sb.ToString().Trim();
    }

    private static readonly HashSet<string> MonsterSegmentSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "boss", "monster", "monsters", "metadata",
    };

    private static readonly string[] PathSegmentDeny =
    [
        "metadata", "terrain", "interlude", "dungeon", "machinarium", "islands", "maps",
        "miscellaneous", "tdtx", "feature", "features", "tiles", "arena", "building",
    ];

    private static readonly Regex ZoneCodeLike = new(@"^P?\d+[_\d]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsStateHideRule(DisplayRule r)
        => r.Hide && (r.Life == "Dead" || r.Chest == "Opened" || r.Encounter == "Complete");

    public static bool IsPerTypeEntityRule(DisplayRule r)
    {
        if (IsStateHideRule(r)) return false;
        if (r.Categories.Count > 0) return false;
        if (r.Match is not { Count: 1 }) return false;
        if (r.Name.StartsWith("Type override:", StringComparison.Ordinal)) return true;
        return !KnownSemanticRuleNames.Contains(r.Name);
    }

    public static readonly HashSet<string> KnownSemanticRuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Boss", "Monster · Unique", "Monster · Rare", "Monster · Magic", "Monster · Normal",
        "Player", "NPC", "Chest · Unique", "Chest · Rare", "Transition",
        "Quest object", "Quest marker", "Waypoint", "Bridge", "Portal",
        "Checkpoint", "Map marker", "Point of Interest", "Stash", "Town portal",
        "Expedition", "Ritual", "Breach", "Abyss", "Delirium", "Strongbox", "Essence", "Shrine",
        "Summoning Circle", "Wisp", "Rogue Exile",
    };
}
