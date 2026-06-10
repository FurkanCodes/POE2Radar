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

    /// <summary>Nav/path/zone label: zone bosses get curated name + (Boss); others use generic rule labels.</summary>
    public static string FormatEntityLabel(Poe2Live.EntityDot e, DisplayRule? rule)
    {
        if (e.Rarity == Poe2Live.Rarity.Unique && IsBossMetadata(e.Metadata))
        {
            var name = EntityNameResolver.Shared.Resolve(e.Metadata);
            if (name is { Length: > 0 }) return $"{name} (Boss)";
        }
        var label = RuleLabel(rule);
        return label.Length > 0 ? label : TypeToken(e.Metadata);
    }

    public static bool IsBossMetadata(string metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return false;
        if (metadata.Contains("Strongbox", StringComparison.OrdinalIgnoreCase)
            || metadata.Contains("StrongBoxes", StringComparison.OrdinalIgnoreCase))
            return false;
        return metadata.Contains("/boss/", StringComparison.OrdinalIgnoreCase)
            || metadata.Contains("Boss", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStateHideRule(DisplayRule r)
        => r.Hide && (r.Life == "Dead" || r.Chest == "Opened" || r.Encounter == "Complete");

    /// <summary>Per-token rules mistakenly written to the global file (legacy Types-in-zone bug).</summary>
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
        "Expedition", "Ritual", "Breach", "Strongbox", "Essence", "Shrine",
    };
}
