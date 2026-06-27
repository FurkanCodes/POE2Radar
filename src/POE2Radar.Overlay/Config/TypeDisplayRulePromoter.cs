using POE2Radar.Core.Game;
using POE2Radar.Overlay.Web;

namespace POE2Radar.Overlay.Config;

/// <summary>Promote a live zone entity type token into the global ordered display ruleset.</summary>
public static class TypeDisplayRulePromoter
{
    public static string RuleNameFor(string token) => token;

    public static int FindRuleIndex(IReadOnlyList<DisplayRule> rules, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return -1;
        var legacyName = $"Type override: {token}";
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (string.Equals(r.Name, token, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Name, legacyName, StringComparison.OrdinalIgnoreCase))
                return i;
            if (r.Match.Any(m => string.Equals(m, token, StringComparison.OrdinalIgnoreCase)))
                return i;
        }
        return -1;
    }

    public static bool RuleMatchesSearch(DisplayRule rule, string filter)
    {
        if (filter.Length == 0) return true;
        if (rule.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        if (rule.Match.Any(m => m.Contains(filter, StringComparison.OrdinalIgnoreCase))) return true;
        if (rule.Categories.Any(c => c.Contains(filter, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    /// <summary>Insert after state-hide rows (dead monsters, opened chests).</summary>
    public static int PromoteInsertIndex(IReadOnlyList<DisplayRule> rules)
    {
        var at = 0;
        for (var i = 0; i < rules.Count; i++)
        {
            if (EntityDisplayHelper.IsStateHideRule(rules[i]))
                at = i + 1;
        }
        return at;
    }

    /// <summary>Same shape as dashboard Live / database “+ Rule” rows.</summary>
    public static DisplayRule BuildRule(
        Poe2Live.EntityDot sample,
        string token,
        DisplayRule? effectiveRule,
        RadarStyles styles,
        string? displayLabel)
    {
        var rule = new DisplayRule
        {
            Name = token,
            Categories = new List<string> { sample.Category.ToString() },
            Match = new List<string> { token },
            Enabled = true,
            Hide = false,
        };

        if (effectiveRule is not null)
        {
            rule.Navigable = effectiveRule.Navigable;
            rule.Label = effectiveRule.Label;
            rule.Shape = effectiveRule.Shape;
            rule.Color = effectiveRule.Color;
            rule.Opacity = effectiveRule.Opacity;
            rule.Size = effectiveRule.Size;
            rule.Sprite = effectiveRule.Sprite?.Clone();
        }
        else
            ApplyCategoryDefaults(rule, sample, styles);

        return rule;
    }

    public static (int index, bool created) Promote(
        DisplayRules displayRules,
        ZoneEntityOverrides? zoneOverrides,
        string? areaCode,
        string token,
        Poe2Live.EntityDot sample,
        DisplayRule? effectiveRule,
        RadarStyles styles,
        string? displayLabel)
    {
        var all = displayRules.All.ToList();
        var existing = FindRuleIndex(all, token);
        if (existing >= 0)
        {
            if (!string.IsNullOrEmpty(areaCode))
                zoneOverrides?.ClearOverride(areaCode, token);
            return (existing, false);
        }

        var rule = BuildRule(sample, token, effectiveRule, styles, displayLabel);
        all.Insert(PromoteInsertIndex(all), rule);
        displayRules.Replace(all);
        if (!string.IsNullOrEmpty(areaCode))
            zoneOverrides?.ClearOverride(areaCode, token);
        return (FindRuleIndex(displayRules.All, token), true);
    }

    private static void ApplyCategoryDefaults(DisplayRule rule, Poe2Live.EntityDot e, RadarStyles styles)
    {
        IconStyle s;
        if (ChestDisplayPolicy.IsPlainChestEntity(e))
            s = e.Rarity == Poe2Live.Rarity.Unique ? styles.ChestUnique : styles.ChestRare;
        else
        {
            s = e.Category switch
            {
                Poe2Live.EntityCategory.Chest when e.Rarity == Poe2Live.Rarity.Unique => styles.ChestUnique,
                Poe2Live.EntityCategory.Chest => styles.ChestRare,
                Poe2Live.EntityCategory.Npc => styles.Npc,
                Poe2Live.EntityCategory.Transition => styles.Transition,
                Poe2Live.EntityCategory.Monster when e.Rarity == Poe2Live.Rarity.Unique => styles.MonsterUnique,
                Poe2Live.EntityCategory.Monster when e.Rarity == Poe2Live.Rarity.Rare => styles.MonsterRare,
                Poe2Live.EntityCategory.Monster when e.Rarity == Poe2Live.Rarity.Magic => styles.MonsterMagic,
                Poe2Live.EntityCategory.Monster => styles.MonsterNormal,
                _ => styles.Poi,
            };
        }

        rule.Shape = s.Shape;
        rule.Color = s.Color;
        rule.Opacity = s.Opacity;
        rule.Size = s.Size;
        rule.Sprite = s.Sprite?.Clone();
    }
}
