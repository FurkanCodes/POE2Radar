using System.Text.RegularExpressions;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay.StashUtility;

internal enum StashUtilityKind : byte { Waystone, Tablet }

internal readonly record struct StashUtilityEvaluation(
    StashUtilityKind Kind,
    bool Bad,
    bool Great,
    int Tier,
    int Revives,
    int ItemRarity,
    int PackSize,
    int MonsterRarity,
    int MonsterEffectiveness,
    int DropChance,
    int ExplicitMods,
    int GoodMods,
    string Summary);

internal static partial class StashUtilityRules
{
    [GeneratedRegex(@"(?:Tier\s*|Waystone)(\d{1,2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TierRegex();

    public static bool IsEnabled(StashUtilitySettings settings)
        => settings.EnableWaystones || settings.EnableTablets;

    public static bool TryEvaluate(
        Poe2Live.StashValueSlot slot,
        StashUtilitySettings settings,
        out StashUtilityEvaluation evaluation)
    {
        evaluation = default;
        if (slot.Panel == Poe2Live.StashValuePanel.Stash && !settings.IncludeStash) return false;
        if (slot.Panel == Poe2Live.StashValuePanel.Inventory && !settings.IncludeInventory) return false;

        var identity = $"{slot.BaseItemName}|{slot.InternalName}|{slot.FullItemPath}";
        var isWaystone = identity.Contains("Waystone", StringComparison.OrdinalIgnoreCase)
                         || identity.Contains("MapKey", StringComparison.OrdinalIgnoreCase);
        var isTablet = identity.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
                       || identity.Contains("TowerAugment", StringComparison.OrdinalIgnoreCase);

        if (isWaystone && settings.EnableWaystones)
            return TryEvaluateWaystone(slot, settings, identity, out evaluation);
        if (isTablet && settings.EnableTablets)
            return TryEvaluateTablet(slot, settings, out evaluation);
        return false;
    }

    private static bool TryEvaluateWaystone(
        Poe2Live.StashValueSlot slot,
        StashUtilitySettings s,
        string identity,
        out StashUtilityEvaluation evaluation)
    {
        evaluation = default;
        if (s.HideNormalWaystones && slot.Rarity == Poe2Live.Rarity.Normal) return false;

        var tier = ParseTier(identity);
        if (tier < Math.Clamp(s.MinTier, 1, 16)) return false;

        var rarity = slot.Stats.ItemRarity;
        var pack = slot.Stats.PackSize;
        var monsterRarity = slot.Stats.MonsterRarity;
        var effect = slot.Stats.MonsterEffectiveness;
        var drop = slot.Stats.WaystoneDropChance + slot.Stats.Quality;
        var explicitCount = slot.Mods.Count(m => m.Explicit);
        var revives = Math.Clamp(6 - explicitCount, 0, 5);
        var matchedRequired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedGreat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bad = false;

        foreach (var mod in slot.Mods)
        {
            if (mod.Id.Contains("revive", StringComparison.OrdinalIgnoreCase))
                revives = Math.Clamp((int)MathF.Round(PrimaryValue(mod)), 0, 5);

            var definition = StashUtilityCatalog.MatchWaystone(mod.Id);
            if (definition is not { } def) continue;
            if (!slot.Stats.HasMemoryStats)
            {
                rarity += def.ItemRarity;
                pack += def.PackSize;
                monsterRarity += def.MonsterRarity;
                effect += def.MonsterEffectiveness;
                drop += def.DropChance;
            }
            if (Contains(s.GoodWaystoneMods, def.Id)) matchedRequired.Add(def.Id);
            if (Contains(s.GreatWaystoneMods, def.Id)) matchedGreat.Add(def.Id);
            if (Contains(s.BadWaystoneMods, def.Id)) bad = true;
        }

        var selectedRequired = s.GoodWaystoneMods ?? [];
        var selectedGreat = s.GreatWaystoneMods ?? [];
        var selectedBad = s.BadWaystoneMods ?? [];
        var required = selectedRequired.Count > 0 && (s.RequireAllGoodWaystoneMods
            ? selectedRequired.All(matchedRequired.Contains)
            : matchedRequired.Count > 0);
        var greatMod = selectedGreat.Count > 0 && (s.RequireAllGreatWaystoneMods
            ? selectedGreat.All(matchedGreat.Contains)
            : matchedGreat.Count > 0);

        var numerical = (!s.FilterMaxRevives || revives <= Math.Clamp(s.MaxRevives, 0, 5))
            && (!s.FilterMinItemRarity || rarity >= s.MinItemRarity)
            && (!s.FilterMinPackSize || pack >= s.MinPackSize)
            && (!s.FilterMinMonsterRarity || monsterRarity >= s.MinMonsterRarity)
            && (!s.FilterMinMonsterEffectiveness || effect >= s.MinMonsterEffectiveness)
            && (!s.FilterMinDropChance || drop >= s.MinDropChance)
            && (!s.FilterMinExplicitMods || explicitCount >= s.MinExplicitMods)
            && (!s.FilterMaxExplicitMods || explicitCount <= s.MaxExplicitMods);

        var hasModRules = selectedRequired.Count > 0 || selectedGreat.Count > 0 || selectedBad.Count > 0;
        var matchesSelectedRule = required || greatMod || bad;
        var qualifies = numerical && (!hasModRules || matchesSelectedRule);
        if (bad && !s.BadOnlyWhenNumericalFiltersPass)
            qualifies = true;
        if (!qualifies) return false;
        if (!s.RedTakesPriority && (required || greatMod)) bad = false;

        var greatChecks = 0;
        var great = true;
        Check(selectedGreat.Count > 0, greatMod);
        Check(s.GreatByItemRarity, rarity >= s.GreatItemRarity);
        Check(s.GreatByPackSize, pack >= s.GreatPackSize);
        Check(s.GreatByDropChance, drop >= s.GreatDropChance);
        Check(s.GreatByExplicitMods, explicitCount >= s.GreatExplicitMods);
        great &= greatChecks > 0;

        var summary = $"T{tier} · {revives} rev · {rarity}% rarity · {pack}% pack · {drop}% drops";
        evaluation = new StashUtilityEvaluation(
            StashUtilityKind.Waystone, bad, great, tier, revives, rarity, pack, monsterRarity,
            effect, drop, explicitCount, matchedRequired.Count, summary);
        return true;

        void Check(bool enabled, bool passes)
        {
            if (!enabled) return;
            greatChecks++;
            great &= passes;
        }
    }

    private static bool TryEvaluateTablet(
        Poe2Live.StashValueSlot slot,
        StashUtilitySettings s,
        out StashUtilityEvaluation evaluation)
    {
        evaluation = default;
        var matchedRequired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedGreat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bad = false;

        foreach (var mod in slot.Mods)
        {
            var definition = StashUtilityCatalog.MatchTablet(mod.Id);
            if (definition is not { } def) continue;
            var passesRoll = PassesMinimumRoll(mod, def, s.TabletMinimumRolls);
            if (Contains(s.GodTabletMods, def.Id) && passesRoll) matchedGreat.Add(def.Id);
            if (Contains(s.BadTabletMods, def.Id)) bad = true;
            if (Contains(s.GoodTabletMods, def.Id) && passesRoll) matchedRequired.Add(def.Id);
        }

        var selectedRequired = s.GoodTabletMods ?? [];
        var selectedGreat = s.GodTabletMods ?? [];
        var selectedBad = s.BadTabletMods ?? [];
        var required = selectedRequired.Count > 0 && (s.RequireAllGoodTabletMods
            ? selectedRequired.All(matchedRequired.Contains)
            : matchedRequired.Count > 0);
        var great = selectedGreat.Count > 0 && (s.RequireAllGreatTabletMods
            ? selectedGreat.All(matchedGreat.Contains)
            : matchedGreat.Count > 0);
        var otherRulesSelected = selectedRequired.Count > 0 || selectedGreat.Count > 0;
        var badCanInclude = !s.BadTabletOnlyWhenOtherRulesPass || !otherRulesSelected || required || great;
        var hasModRules = otherRulesSelected || selectedBad.Count > 0;

        if (!hasModRules || (!required && !great && !(bad && badCanInclude))) return false;
        if (!s.RedTakesPriority && (required || great)) bad = false;
        if (bad && s.HideBadTablets) return false;

        var explicitCount = slot.Mods.Count(m => m.Explicit);
        var summary = great
            ? $"GREAT · {matchedRequired.Count} required · {matchedGreat.Count} great"
            : $"{matchedRequired.Count} required mods";
        evaluation = new StashUtilityEvaluation(
            StashUtilityKind.Tablet, bad, great, 0, 0, 0, 0, 0, 0, 0,
            explicitCount, matchedRequired.Count, summary);
        return true;
    }

    internal static int ParseTier(string text)
    {
        var match = TierRegex().Match(text ?? "");
        return match.Success && int.TryParse(match.Groups[1].Value, out var tier)
            ? Math.Clamp(tier, 0, 16)
            : 0;
    }

    internal static bool PassesMinimumRoll(
        Poe2Live.StashItemMod mod,
        StashUtilityModDefinition definition,
        IReadOnlyDictionary<string, float>? required)
    {
        if (definition.MinRoll == definition.MaxRoll || required is null ||
            !required.TryGetValue(definition.Id, out var minimum)) return true;
        var value = Math.Abs(PrimaryValue(mod));
        return value <= 0f || value >= minimum;
    }

    private static float PrimaryValue(Poe2Live.StashItemMod mod)
    {
        if (!float.IsNaN(mod.Value0) && Math.Abs(mod.Value0) > float.Epsilon) return mod.Value0;
        if (!float.IsNaN(mod.Value1)) return mod.Value1;
        return 0f;
    }

    private static bool Contains(IReadOnlyList<string>? list, string value)
    {
        if (list is null) return false;
        for (var i = 0; i < list.Count; i++)
            if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
