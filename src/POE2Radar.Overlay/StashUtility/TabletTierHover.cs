using System.Globalization;
using POE2Radar.Core.Game;

namespace POE2Radar.Overlay.StashUtility;

internal readonly record struct StashTierHoverLine(
    string Tier,
    string Color,
    string Modifier,
    string Roll);

internal readonly record struct StashTierHoverMetric(
    string Label,
    string Value,
    string Tier,
    string Color);

internal readonly record struct StashTierHoverSummary(
    string ItemType,
    string OverallTier,
    string OverallColor,
    string TierNote,
    StashTierHoverMetric[] Metrics,
    StashTierHoverLine[] Modifiers);

internal static class TabletTierHover
{
    public static bool TryBuild(
        Poe2Live.StashValueSlot slot,
        out StashTierHoverSummary summary)
    {
        summary = default;
        if (!slot.Hovered || !IsTablet(slot))
            return false;

        var lines = new List<(int Sort, StashTierHoverLine Line)>();
        foreach (var mod in slot.Mods.Where(mod => mod.Explicit))
        {
            if (StashUtilityCatalog.MatchTablet(mod.Id) is not { } definition)
            {
                if (!string.IsNullOrWhiteSpace(mod.Id) && mod.Id != "_")
                {
                    lines.Add((
                        5,
                        new StashTierHoverLine(
                            "?",
                            "#AAB2BF",
                            $"Unranked modifier ({mod.Id})",
                            FormatRoll(mod, percentage: false))));
                }
                continue;
            }

            lines.Add((
                definition.TierSortOrder,
                new StashTierHoverLine(
                    definition.MarketTier,
                    definition.TierColor,
                    definition.Name,
                    FormatRoll(mod, definition.Name.Contains('%')))));
        }

        var ranked = lines
            .OrderBy(line => line.Sort)
            .ThenBy(line => line.Line.Modifier, StringComparer.OrdinalIgnoreCase)
            .Select(line => line.Line)
            .ToArray();
        var best = ranked.FirstOrDefault(line => line.Tier is "S" or "A" or "B" or "C" or "D");
        var overallTier = string.IsNullOrEmpty(best.Tier) ? "—" : best.Tier;
        var overallColor = string.IsNullOrEmpty(best.Color) ? "#AAB2BF" : best.Color;

        summary = new StashTierHoverSummary(
            DetectTabletType(slot),
            overallTier,
            overallColor,
            "Price-informed market tier · Runes of Aldur",
            [],
            ranked);
        return true;
    }

    private static bool IsTablet(Poe2Live.StashValueSlot slot)
    {
        var identity = $"{slot.BaseItemName}|{slot.InternalName}|{slot.FullItemPath}";
        return identity.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
               || identity.Contains("TowerAugment", StringComparison.OrdinalIgnoreCase);
    }

    internal static string DetectTabletType(Poe2Live.StashValueSlot slot)
    {
        var identity = $"{slot.BaseItemName}|{slot.InternalName}|{slot.FullItemPath}";
        foreach (var group in StashUtilityCatalog.TabletGroups)
        {
            var family = group.Name[..^" Tablet".Length];
            if (identity.Contains(group.Name, StringComparison.OrdinalIgnoreCase)
                || identity.Contains(family, StringComparison.OrdinalIgnoreCase))
            {
                return group.Name;
            }
        }

        if (identity.Contains("Incursion", StringComparison.OrdinalIgnoreCase))
            return "Temple Tablet";
        if (identity.Contains("TowerAugmentBoss", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("TabletBoss", StringComparison.OrdinalIgnoreCase))
            return "Overseer Tablet";
        if (identity.Contains("TowerAugmentGeneric", StringComparison.OrdinalIgnoreCase)
            || identity.Contains("TabletGeneric", StringComparison.OrdinalIgnoreCase))
            return "Irradiated Tablet";

        return string.IsNullOrWhiteSpace(slot.BaseItemName)
            ? "Tablet"
            : slot.BaseItemName.Trim();
    }

    private static string FormatRoll(Poe2Live.StashItemMod mod, bool percentage)
    {
        var value = !float.IsNaN(mod.Value0) && Math.Abs(mod.Value0) > float.Epsilon
            ? mod.Value0
            : !float.IsNaN(mod.Value1)
                ? mod.Value1
                : 0f;
        value = Math.Abs(value);
        if (value <= float.Epsilon)
            return "";

        return value.ToString("0.##", CultureInfo.InvariantCulture) + (percentage ? "%" : "");
    }
}
