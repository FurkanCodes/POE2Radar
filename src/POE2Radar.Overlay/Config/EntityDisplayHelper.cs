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
    {
        var resolved = EntityNameResolver.Shared.Resolve(e.Metadata);
        if (resolved is { Length: > 0 })
        {
            const string dnt = "[DNT-UNUSED] ";
            if (resolved.StartsWith(dnt, StringComparison.Ordinal)) resolved = resolved[dnt.Length..];
            return resolved;
        }

        var token = TypeToken(e.Metadata);
        if (token.Length > 0)
        {
            var humanized = HumanizeToken(token);
            if (humanized.Length > 0) return humanized;
        }

        return EntityNameResolver.Shared.ResolveOrShorten(e.Metadata);
    }

    /// <summary>Insert spaces before interior capitals / digit-letter edges (e.g. Waypoint_LongActivationRadius).</summary>
    public static string HumanizeToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return "";

        var sb = new System.Text.StringBuilder(token.Length + 8);
        for (var i = 0; i < token.Length; i++)
        {
            var ch = token[i];
            if (i > 0)
            {
                var prev = token[i - 1];
                var boundary = (char.IsUpper(ch) && (char.IsLower(prev) || char.IsDigit(prev)))
                               || (char.IsDigit(ch) && char.IsLetter(prev) && !char.IsDigit(prev));
                if (boundary && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            }
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Nav/path/zone label: bosses, mechanics, NPC (name), map-marker POIs as proper names.</summary>
    public static string FormatEntityLabel(Poe2Live.EntityDot e, DisplayRule? rule)
    {
        if (e.Rarity == Poe2Live.Rarity.Unique && IsBossMetadata(e.Metadata))
        {
            var name = EntityNameResolver.Shared.Resolve(e.Metadata);
            if (name is { Length: > 0 }) return $"{name} (Boss)";
        }
        if (EndgameMechanicCatalog.TryMatch(e, out var mechanic))
            return mechanic!.Name;

        var ruleName = rule?.Name ?? "";
        if (IsGenericPoiRule(ruleName))
        {
            var specific = SpecificEntityName(e);
            if (specific.Length > 0) return specific;
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

    private static bool IsGenericPoiRule(string ruleName)
        => string.Equals(ruleName, "Map marker", StringComparison.OrdinalIgnoreCase)
           || string.Equals(ruleName, "Point of Interest", StringComparison.OrdinalIgnoreCase);

    private static bool IsNpcRule(DisplayRule? rule, Poe2Live.EntityDot e)
        => e.Category == Poe2Live.EntityCategory.Npc
           || rule is { Name: { Length: > 0 } n }
              && string.Equals(n, "NPC", StringComparison.OrdinalIgnoreCase);

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
        "Expedition", "Ritual", "Breach", "Abyss", "Delirium", "Strongbox", "Essence", "Shrine",
        "Summoning Circle", "Wisp", "Rogue Exile",
    };
}
