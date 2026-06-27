using POE2Radar.Core.Game;
using POE2Radar.Overlay.Web;

namespace POE2Radar.Overlay.Config;

/// <summary>Plain loot chest defaults (not strongboxes): icon on the map, no text chips, no auto-path.</summary>
public static class ChestDisplayPolicy
{
    public static bool IsStrongboxMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return false;
        return metadata.Contains("StrongBoxes", StringComparison.OrdinalIgnoreCase)
            || metadata.Contains("Strongbox", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPlainChestMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return false;
        if (IsStrongboxMetadata(metadata)) return false;
        return metadata.Contains("/Chests/", StringComparison.OrdinalIgnoreCase)
            || metadata.Contains("Chests/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Any generic area chest entity (metadata/Chests/…), excluding strongboxes.</summary>
    public static bool IsPlainChestEntity(Poe2Live.EntityDot e)
    {
        if (IsStrongboxMetadata(e.Metadata)) return false;
        if (e.Category == Poe2Live.EntityCategory.Chest) return true;
        return IsPlainChestMetadata(e.Metadata);
    }

    public static (SpriteIconRef? Sprite, string? Shape, float Size, string Color, float Opacity) DrawStyle(
        Poe2Live.EntityDot e,
        RadarStyles styles)
    {
        var s = e.Rarity == Poe2Live.Rarity.Unique ? styles.ChestUnique : styles.ChestRare;
        return (s.Sprite, s.Shape, s.Size, s.Color, s.Opacity);
    }

    public static bool IsStrongboxRule(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.StartsWith("Strongbox", StringComparison.OrdinalIgnoreCase)) return true;
        return name.Equals("ServerIcon · Strongbox", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Generic area chests — not league strongboxes.</summary>
    public static bool IsPlainChestRule(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && (name.Equals("Chest · Unique", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Chest · Rare", StringComparison.OrdinalIgnoreCase)
               || name.Equals("ServerIcon · Chest", StringComparison.OrdinalIgnoreCase));

    public static bool IsChestLootRule(string? name) => IsPlainChestRule(name);

    public static bool IsRareChestRule(string? name)
        => string.Equals(name, "Chest · Rare", StringComparison.OrdinalIgnoreCase);

    /// <summary>Plain chests that are not rare (or unique) should not draw on the map overlay.</summary>
    public static bool ShouldHideNonRarePlainChest(Poe2Live.EntityDot e)
        => IsPlainChestEntity(e)
           && e.Rarity is not Poe2Live.Rarity.Rare
           && e.Rarity is not Poe2Live.Rarity.Unique;

    /// <summary>Rare loot chests show a yellow map chip; other plain chest rules stay icon-only.</summary>
    public static bool ShouldShowPlainChestLabel(Poe2Live.EntityDot e, DisplayRule? rule)
    {
        if (!IsPlainChestEntity(e) || e.Rarity != Poe2Live.Rarity.Rare) return false;
        if (rule is { HideLabel: true }) return false;
        if (IsRareChestRule(rule?.Name)) return true;
        return rule?.Label is { Length: > 0 };
    }

    /// <summary>Watched / legacy rules for generic chest metadata (not StrongBoxes).</summary>
    public static bool IsChestLootPatternRule(DisplayRule rule)
    {
        if (rule.Hide) return false;
        if (IsPlainChestRule(rule.Name)) return true;
        foreach (var term in rule.Match)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            if (term.Contains("StrongBoxes", StringComparison.OrdinalIgnoreCase)
                || term.Contains("Strongbox", StringComparison.OrdinalIgnoreCase))
                return false;
            if (term.Contains("/Chests/", StringComparison.OrdinalIgnoreCase)
                && !term.Contains("QuestChest", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsStrongboxPatternRule(DisplayRule rule)
    {
        if (rule.Hide) return false;
        if (IsStrongboxRule(rule.Name)) return true;
        return rule.Match.Any(m =>
            !string.IsNullOrWhiteSpace(m)
            && m.Contains("StrongBoxes", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Apply icon-only defaults to plain chest rules. Returns true when changed.</summary>
    public static bool ApplyIconOnlyDefaults(DisplayRule rule)
    {
        if (IsRareChestRule(rule.Name)) return false;
        if (!IsChestLootRule(rule.Name) && !IsChestLootPatternRule(rule)) return false;

        var changed = false;
        if (rule.Navigable) { rule.Navigable = false; changed = true; }
        if (rule.Label is { Length: > 0 }) { rule.Label = null; changed = true; }
        return changed;
    }

    /// <summary>Restore strongbox labels + auto-path after a chest-only pass.</summary>
    public static bool ApplyStrongboxDefaults(DisplayRule rule)
    {
        if (!IsStrongboxRule(rule.Name) && !IsStrongboxPatternRule(rule)) return false;

        var changed = false;
        if (!rule.Navigable) { rule.Navigable = true; changed = true; }
        var label = IsStrongboxPatternRule(rule) && !IsStrongboxRule(rule.Name) ? "Strongbox" : rule.Name;
        if (rule.Label is not { Length: > 0 }) { rule.Label = label; changed = true; }
        return changed;
    }
}
