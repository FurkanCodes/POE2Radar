namespace POE2Radar.Overlay.StashUtility;

internal readonly record struct StashUtilityModDefinition(
    string Id,
    string Name,
    string Category,
    int ItemRarity = 0,
    int PackSize = 0,
    int MonsterRarity = 0,
    int MonsterEffectiveness = 0,
    int DropChance = 0,
    float MinRoll = 0,
    float MaxRoll = 0);

internal readonly record struct TabletRuleGroup(
    string Name,
    string Description,
    string[] ModifierCategories);

internal static class StashUtilityCatalog
{
    public static readonly TabletRuleGroup[] TabletGroups =
    [
        new(
            "Irradiated Tablet",
            "Universal rolls plus Expedition-useful rolls. Expedition Tablet is not a separate current drop.",
            ["General", "Expedition"]),
        new(
            "Overseer Tablet",
            "Universal and Map Boss-focused rolls for Tablets that empower the Map Boss.",
            ["General"]),
        new(
            "Abyss Tablet",
            "Universal rolls plus modifiers that improve Abysses and Abyssal rewards.",
            ["General", "Abyss"]),
        new(
            "Breach Tablet",
            "Universal rolls plus modifiers for Otherworldly Breaches, Wombgifts and Hiveblood.",
            ["General", "Breach"]),
        new(
            "Delirium Tablet",
            "Universal rolls plus modifiers for Delirium Fog, mirrors and splinters.",
            ["General", "Delirium"]),
        new(
            "Ritual Tablet",
            "Universal rolls plus modifiers for Ritual Favours, Tribute and rerolls.",
            ["General", "Ritual"]),
        new(
            "Temple Tablet",
            "Universal rolls plus modifiers for Vaal Beacons, Crystals and their encounters.",
            ["General", "Incursion"]),
    ];

    public static readonly StashUtilityModDefinition[] WaystoneMods =
    [
        W("MapMonsterDamageAsFire", "Extra fire damage", effect: 16, drop: 15),
        W("MapMonsterDamageAsCold", "Extra cold damage", rarity: 14, drop: 15),
        W("MapMonsterDamageAsLightning", "Extra lightning damage", pack: 8, drop: 15),
        W("MapMonsterDamageIncrease", "Increased monster damage", monsterRarity: 25, drop: 20),
        W("MapMonsterSpeedIncrease", "Monster attack, cast and movement speed", pack: 9, drop: 25),
        W("MapMonsterCritIncrease", "Monster critical chance and damage", pack: 9, drop: 15),
        W("MapMonsterLifeIncrease", "More monster life", monsterRarity: 23, drop: 15),
        W("MapMonsterElementalResistances", "Monster elemental resistances", rarity: 14, drop: 10),
        W("MapMonsterArmoured", "Monsters are armoured", monsterRarity: 18, drop: 15),
        W("MapMonsterEvasive", "Monsters are evasive", pack: 6, drop: 15),
        W("MapMonsterEnergyShield", "Monster extra energy shield", rarity: 13, drop: 15),
        W("MapMonsterPoisoning", "Monsters poison on hit", pack: 7, drop: 10),
        W("MapMonsterBleeding", "Monsters inflict bleeding", effect: 13, drop: 10),
        W("MapMonsterStunAilmentThreshold", "Monster ailment and stun threshold", effect: 13, drop: 10),
        W("MapMonsterArmourBreak", "Monsters break armour", monsterRarity: 19, drop: 10),
        W("MapMonsterAccuracy", "Monster accuracy", pack: 7, drop: 10),
        W("MapMonsterDamageAsChaos", "Extra chaos damage", rarity: 15, drop: 15),
        W("MapMonsterStunBuildup", "Monster stun buildup", effect: 13, drop: 15),
        W("MapMonsterElementAilmentChance", "Elemental ailment application", rarity: 11, drop: 15),
        W("MapMonsterAdditionalProjectiles", "Additional projectiles", pack: 9, drop: 25),
        W("MapMonsterIncreasedAreaOfEffect", "Monster area of effect", drop: 20),
        W("MapPlayerEnfeeble", "Players periodically cursed with Enfeeble", effect: 16, drop: 20),
        W("MapPlayerTemporalChains", "Players periodically cursed with Temporal Chains", pack: 8, drop: 20),
        W("MapPlayerElementalWeakness", "Players periodically cursed with Elemental Weakness", rarity: 13, drop: 20),
        W("MapSpreadBurningGround", "Patches of ignited ground", effect: 15, drop: 15),
        W("MapSpreadChilledGround", "Patches of chilled ground", rarity: 12, drop: 15),
        W("MapSpreadShockedGround", "Patches of shocked ground", pack: 7, drop: 15),
        W("MapMonstersElementalPenetration", "Monster elemental penetration", rarity: 16, drop: 20),
        W("MapPlayerMaximumResists", "Reduced maximum player resistances", pack: 10, drop: 25),
        W("MapPlayerFlaskChargeGain", "Reduced flask charge gain", pack: 7, drop: 15),
        W("MapPlayerRecoveryRate", "Less life and energy shield recovery", rarity: 15, drop: 20),
        W("MapPlayerCooldownRecovery", "Less cooldown recovery", rarity: 12, drop: 15),
        W("MapMonstersBaseSelfCriticalMultiplier", "Reduced extra critical damage taken", monsterRarity: 18, drop: 10),
        W("MapMonstersCurseEffectOnSelf", "Less curse effect on monsters", rarity: 10, drop: 10),
    ];

    public static readonly StashUtilityModDefinition[] TabletMods =
    [
        T("TowerDroppedItemRarityIncrease", "Item rarity", "General", 8, 12),
        T("TowerAdditionalStoneCircle", "Additional Summoning Circle", "General", 1, 1),
        T("TowerAdditionalExile", "Additional Rogue Exile", "General", 1, 1),
        T("TowerAdditionalAzmeriWisp", "Additional Azmeri Spirits", "General", 1, 2),
        T("TowerMonsterEffectiveness", "Monster effectiveness", "General", 10, 15),
        T("TowerRareChestCount", "Additional rare chests", "General", 2, 3),
        T("TowerExperienceGainIncrease", "Experience gain", "General", 12, 18),
        T("TowerDroppedGoldIncrease", "Gold found", "General", 25, 35),
        T("TowerMonsterRarityIncrease", "Monster rarity", "General", 15, 20),
        T("TowerRarePackIncrease", "Rare monsters", "General", 25, 35),
        T("TowerMagicPackIncrease", "Magic monsters", "General", 30, 40),
        T("TowerPackSizeIncrease", "Pack size", "General", 5, 15),
        T("TowerMapBossExperience", "Map boss experience", "General", 40, 80),
        T("TowerMapBossWaystoneChance", "Waystones from map bosses", "General", 18, 30),
        T("TowerMapBossAdditionalSpirit", "Map boss adds Azmeri Spirits", "General"),
        T("TowerMapBossAdditionalEssence", "Map boss adds Essences", "General"),
        T("TowerAdditionalEssence", "Additional Essence", "General", 1, 2),
        T("TowerMapBossAdditionalShrine", "Map boss adds Shrines", "General"),
        T("TowerAdditionalShrine", "Additional Shrine", "General", 1, 1),
        T("TowerMapBossAdditionalStrongbox", "Map boss adds Strongboxes", "General"),
        T("TowerAdditionalStrongbox", "Additional Strongbox", "General", 1, 1),
        T("TowerMapBossRarity", "Item rarity from map bosses", "General", 35, 60),
        T("TowerMapBossQuantity", "Item quantity from map bosses", "General", 13, 20),
        T("TowerMapAdditionalUniqueMonsterModifier", "Unique monsters gain a rare modifier", "General", 1, 1),
        T("TowerMapAdditionalModifier", "Additional random map modifiers", "General", 1, 2),
        T("TowerStoneCircleChance", "Summoning Circle chance", "General", 70, 100),
        T("TowerAdditionalSpiritChance", "Azmeri Spirit chance", "General", 70, 100),
        T("TowerAdditionalEssenceChance", "Essence chance", "General", 70, 100),
        T("TowerAdditionalStrongboxChance", "Strongbox chance", "General", 70, 100),
        T("TowerAdditionalShrineChance", "Shrine chance", "General", 70, 100),
        T("TowerRareAdditionalModChance", "Rare monsters gain an additional modifier", "General"),
        T("TowerMapDroppedMapsIncrease", "Waystones found", "General", 30, 40),
        T("TowerAdditionalExileChance", "Rogue Exile chance", "General", 70, 100),

        T("TowerRitualOmenChance", "Ritual Favours are Omens", "Ritual", 35, 70),
        T("TowerRitualMagicMonsters", "Ritual monsters become Rare", "Ritual", 25, 40),
        T("TowerRitualRareMonsters", "Ritual monsters become Magic", "Ritual", 35, 70),
        T("TowerRitualChanceForNoCost", "Free Ritual rerolls", "Ritual", 3, 6),
        T("TowerRitualAdditionalReroll", "Additional Ritual rerolls", "Ritual", 1, 3),
        T("TowerRitualDeferSpeed", "Deferred Favours return sooner", "Ritual", 25, 40),
        T("TowerRitualDeferCostIncrease", "Reduced deferral cost", "Ritual", 20, 30),
        T("TowerRitualRerollCostIncrease", "Reduced reroll cost", "Ritual", 20, 30),
        T("TowerRitualTributeIncrease", "Increased Ritual Tribute", "Ritual", 18, 30),

        T("TowerIncursionRareChestChance", "Rare Vaal Beacon chests", "Incursion", 30, 60),
        T("TowerIncursionBossChance", "Vaal Beacon unique monster", "Incursion", 10, 25),
        T("TowerIncursionTokenChance", "Additional Vaal Beacon Crystal", "Incursion", 5, 10),
        T("TowerIncursionSecondaryEncounters", "Vaal Beacons summon more monsters", "Incursion", 25, 50),
        T("TowerIncursionExtraPacksChance", "Extra packs around Vaal Beacons", "Incursion", 30, 60),
        T("TowerIncursionExtraPacks", "Extra Vaal Beacon monster packs", "Incursion", 1, 1),
        T("TowerIncursionPackSize", "Vaal Beacon pack size", "Incursion", 10, 30),

        T("TowerAbyss4AdditionalChance", "Four additional Abysses", "Abyss", 20, 40),
        T("TowerAbyssExtraTickets", "Desecrated Currency from Abysses", "Abyss", 20, 30),
        T("TowerAbyssExtraModifiers", "Abyssal modifiers", "Abyss", 20, 30),
        T("TowerAbyssIncreasedRewards", "Abyss Pits more likely to reward", "Abyss"),
        T("TowerAbyssAdditionalChance", "Additional Abyss", "Abyss", 1, 1),
        T("TowerAbyssDepthsChance", "Abyssal Depths chance", "Abyss", 10, 20),
        T("TowerAbyssEffectivenessPerChasm", "Abyssal monster effectiveness", "Abyss", 8, 12),
        T("TowerAbyssEnhancedMonstersPerChasm", "Enhanced Abyssal monsters", "Abyss"),
        T("TowerAbyssRareMonsterIncrease", "Rare Abyssal monsters", "Abyss", 1, 2),
        T("TowerAbyssMonsterIncrease", "More Abyssal monsters", "Abyss", 20, 30),

        T("TowerBreachAdditionalRares", "Rare monsters from Breaches", "Breach", 1, 3),
        T("TowerBreachBossChance", "Vruun chance", "Breach", 20, 50),
        T("TowerBreachWombgiftLevelChance", "Higher-level Wombgifts", "Breach", 10, 30),
        T("TowerBreachWombgiftQuantity", "Wombgift quantity", "Breach", 30, 60),
        T("TowerBreachHivebloodQuantity", "Hiveblood quantity", "Breach", 30, 60),
        T("TowerBreachRareMonsterPotency", "Rare Breach monster effectiveness", "Breach", 5, 20),
        T("TowerBreachMonsterQuantity", "Breach pack size", "Breach"),

        T("TowerDeliriumAdditionalShardsChance", "MirrorShards from Delirium", "Delirium", 12, 26),
        T("TowerDeliriumRareMonsterPause", "Rare kills pause Delirium timer", "Delirium", 3, 5),
        T("TowerDeliriumDoodadsIncrease", "Fracturing Mirrors", "Delirium", 15, 30),
        T("TowerDeliriumPackSizeIncrease", "Delirium pack size", "Delirium", 15, 30),
        T("TowerDeliriumDifficultyIncrease", "Deliriousness", "Delirium", 15, 30),
        T("TowerDeliriumFogPersistence", "Slower Delirium Fog dissipation", "Delirium", 20, 30),
        T("TowerDeliriumFogDissipationDelayNew", "Delirium Fog delay", "Delirium", 6, 12),
        T("TowerDeliriumMonsterSplinterIncrease", "Simulacrum Splinter stack size", "Delirium", 15, 30),
        T("TowerDeliriumBossChance", "Delirium boss chance", "Delirium", 15, 30),

        T("TowerExpeditionRelicModEffect", "Expedition Remnant effect", "Expedition"),
        T("TowerExpeditionRunicMonsters", "Runic Monster markers", "Expedition"),
        T("TowerExpeditionRareMonsters", "Rare Expedition monsters", "Expedition"),
        T("TowerExpeditionLogbookIncrease", "Expedition Logbooks", "Expedition"),
        T("TowerExpeditionExplosionRadius", "Explosive radius", "Expedition"),
        T("TowerExpeditionRelicIncrease", "Additional Remnants", "Expedition"),
        T("TowerExpeditionExplosionPlacement", "Explosive placement range", "Expedition"),
        T("TowerExpeditionArtifactIncrease", "Expedition Artifacts", "Expedition"),
    ];

    public static StashUtilityModDefinition? MatchWaystone(string rawId)
        => Match(rawId, WaystoneMods);

    public static StashUtilityModDefinition? MatchTablet(string rawId)
        => Match(rawId, TabletMods);

    public static IEnumerable<StashUtilityModDefinition> TabletModsFor(TabletRuleGroup group)
        => TabletMods.Where(definition => group.ModifierCategories.Contains(
            definition.Category,
            StringComparer.OrdinalIgnoreCase));

    public static string TabletCategoryHeading(TabletRuleGroup group, string category)
    {
        if (string.Equals(category, "General", StringComparison.OrdinalIgnoreCase))
            return "Universal rolls";
        if (string.Equals(group.Name, "Irradiated Tablet", StringComparison.Ordinal)
            && string.Equals(category, "Expedition", StringComparison.OrdinalIgnoreCase))
            return "Expedition-useful rolls";
        if (string.Equals(group.Name, "Temple Tablet", StringComparison.Ordinal)
            && string.Equals(category, "Incursion", StringComparison.OrdinalIgnoreCase))
            return "Temple / Vaal Beacon rolls";
        return $"{category} rolls";
    }

    private static StashUtilityModDefinition? Match(string rawId, StashUtilityModDefinition[] catalog)
    {
        if (string.IsNullOrWhiteSpace(rawId)) return null;
        StashUtilityModDefinition? best = null;
        foreach (var definition in catalog)
        {
            if (!rawId.Contains(definition.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (best is null || definition.Id.Length > best.Value.Id.Length)
                best = definition;
        }
        return best;
    }

    private static StashUtilityModDefinition W(
        string id, string name, int rarity = 0, int pack = 0, int monsterRarity = 0, int effect = 0, int drop = 0)
        => new(id, name, "Waystone", rarity, pack, monsterRarity, effect, drop);

    private static StashUtilityModDefinition T(string id, string name, string category, float min = 0, float max = 0)
        => new(id, name, category, MinRoll: min, MaxRoll: max);
}
