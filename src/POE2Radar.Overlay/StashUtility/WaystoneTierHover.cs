using System.Globalization;
using POE2Radar.Core.Game;

namespace POE2Radar.Overlay.StashUtility;

internal static class WaystoneTierHover
{
    internal enum RewardMetric
    {
        ItemRarity,
        PackSize,
        MonsterRarity,
        MonsterEffectiveness,
        WaystoneDropChance,
        Revives,
    }

    public static bool TryBuild(
        Poe2Live.StashValueSlot slot,
        out StashTierHoverSummary summary)
    {
        summary = default;
        if (!slot.Hovered || !IsWaystone(slot))
            return false;

        var lines = new List<(int Sort, StashTierHoverLine Line)>();
        for (var index = 0; index < slot.Mods.Length; index++)
        {
            var mod = slot.Mods[index];
            if (!mod.Explicit)
                continue;

            if (StashUtilityCatalog.MatchWaystone(mod.Id) is not { } definition)
            {
                if (!string.IsNullOrWhiteSpace(mod.Id) && mod.Id != "_")
                {
                    lines.Add((
                        5,
                        new StashTierHoverLine(
                            "?",
                            "#AAB2BF",
                            $"Unranked modifier ({mod.Id})",
                            "")));
                }
                continue;
            }

            var exactText = index < slot.ModLines.Length
                ? slot.ModLines[index].Trim()
                : "";
            var displayText = exactText.Length > 3
                              && !string.Equals(exactText, mod.Id, StringComparison.OrdinalIgnoreCase)
                ? exactText
                : definition.Name;
            var alreadyContainsRoll = displayText.Any(char.IsDigit);
            lines.Add((
                definition.TierSortOrder,
                new StashTierHoverLine(
                    definition.MarketTier,
                    definition.TierColor,
                    displayText,
                    alreadyContainsRoll ? "" : FormatRoll(mod))));
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
            ItemTitle(slot),
            overallTier,
            overallColor,
            "Reward tier · per-modifier Waystone Drop Chance · danger is build-specific",
            BuildMetrics(slot),
            ranked);
        return true;
    }

    private static StashTierHoverMetric[] BuildMetrics(Poe2Live.StashValueSlot slot)
    {
        var itemRarity = slot.Stats.ItemRarity;
        var packSize = slot.Stats.PackSize;
        var monsterRarity = slot.Stats.MonsterRarity;
        var monsterEffectiveness = slot.Stats.MonsterEffectiveness;
        var dropChance = slot.Stats.WaystoneDropChance + slot.Stats.Quality;

        if (!slot.Stats.HasMemoryStats)
        {
            foreach (var mod in slot.Mods.Where(mod => mod.Explicit))
            {
                if (StashUtilityCatalog.MatchWaystone(mod.Id) is not { } definition)
                    continue;
                itemRarity += definition.ItemRarity;
                packSize += definition.PackSize;
                monsterRarity += definition.MonsterRarity;
                monsterEffectiveness += definition.MonsterEffectiveness;
                dropChance += definition.DropChance;
            }
        }

        var explicitMods = slot.Mods.Count(mod => mod.Explicit);
        var revives = Math.Clamp(6 - explicitMods, 0, 5);
        return
        [
            Metric("Item rarity", Percent(itemRarity), RewardMetric.ItemRarity, itemRarity),
            Metric("Pack size", Percent(packSize), RewardMetric.PackSize, packSize),
            Metric("Monster rarity", Percent(monsterRarity), RewardMetric.MonsterRarity, monsterRarity),
            Metric(
                "Monster effectiveness",
                Percent(monsterEffectiveness),
                RewardMetric.MonsterEffectiveness,
                monsterEffectiveness),
            Metric(
                "Waystone drop chance",
                Percent(dropChance),
                RewardMetric.WaystoneDropChance,
                dropChance),
            Metric(
                "Revives available",
                revives.ToString(CultureInfo.InvariantCulture),
                RewardMetric.Revives,
                revives),
        ];
    }

    private static StashTierHoverMetric Metric(
        string label,
        string value,
        RewardMetric metric,
        int amount)
    {
        var tier = AggregateRewardTier(metric, amount);
        return new StashTierHoverMetric(
            label,
            value,
            tier,
            StashUtilityCatalog.TierColorFor(tier));
    }

    internal static string AggregateRewardTier(RewardMetric metric, int value)
        => metric switch
        {
            // Current 0.5 market/filter breakpoints. These deliberately grade each
            // aggregate independently; they are not the tier of an individual affix.
            RewardMetric.ItemRarity => value switch
            {
                >= 65 => "S",
                >= 50 => "A",
                >= 40 => "B",
                >= 20 => "C",
                _ => "D",
            },
            RewardMetric.PackSize => value switch
            {
                >= 42 => "S",
                >= 30 => "A",
                >= 20 => "B",
                >= 10 => "C",
                _ => "D",
            },
            RewardMetric.MonsterRarity => value switch
            {
                >= 50 => "S",
                >= 40 => "A",
                >= 30 => "B",
                >= 20 => "C",
                _ => "D",
            },
            RewardMetric.MonsterEffectiveness => value switch
            {
                >= 40 => "S",
                >= 30 => "A",
                >= 20 => "B",
                >= 10 => "C",
                _ => "D",
            },
            RewardMetric.WaystoneDropChance => value switch
            {
                >= 145 => "S",
                >= 120 => "A",
                >= 100 => "B",
                >= 75 => "C",
                _ => "D",
            },
            RewardMetric.Revives => value switch
            {
                <= 0 => "S",
                1 => "A",
                2 => "B",
                3 => "C",
                _ => "D",
            },
            _ => "D",
        };

    private static bool IsWaystone(Poe2Live.StashValueSlot slot)
    {
        var identity = $"{slot.BaseItemName}|{slot.InternalName}|{slot.FullItemPath}";
        return identity.Contains("Waystone", StringComparison.OrdinalIgnoreCase)
               || identity.Contains("MapKey", StringComparison.OrdinalIgnoreCase);
    }

    private static string ItemTitle(Poe2Live.StashValueSlot slot)
    {
        if (!string.IsNullOrWhiteSpace(slot.BaseItemName))
            return slot.BaseItemName.Trim();
        if (!string.IsNullOrWhiteSpace(slot.InternalName))
            return slot.InternalName.Trim();
        return "Waystone";
    }

    private static string FormatRoll(Poe2Live.StashItemMod mod)
    {
        var value = !float.IsNaN(mod.Value0) && Math.Abs(mod.Value0) > float.Epsilon
            ? mod.Value0
            : !float.IsNaN(mod.Value1)
                ? mod.Value1
                : 0f;
        value = Math.Abs(value);
        if (value <= float.Epsilon)
            return "";

        var suffix = mod.Id.Contains("AdditionalProjectiles", StringComparison.OrdinalIgnoreCase)
            ? ""
            : "%";
        return value.ToString("0.##", CultureInfo.InvariantCulture) + suffix;
    }

    private static string Percent(int value)
        => $"{(value >= 0 ? "+" : "")}{value.ToString(CultureInfo.InvariantCulture)}%";
}
