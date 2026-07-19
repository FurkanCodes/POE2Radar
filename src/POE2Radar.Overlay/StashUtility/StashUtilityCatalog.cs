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
    float MaxRoll = 0,
    string MarketTier = "C")
{
    public int TierSortOrder => MarketTier switch
    {
        "S" => 0,
        "A" => 1,
        "B" => 2,
        "C" => 3,
        _ => 4,
    };

    public string TierColor => StashUtilityCatalog.TierColorFor(MarketTier);
}

internal readonly record struct TabletRuleGroup(
    string Name,
    string Description,
    string[] ModifierCategories);

internal static class StashUtilityCatalog
{
    public static string TierColorFor(string tier) => tier switch
    {
        "S" => "#FFD166",
        "A" => "#6EEB87",
        "B" => "#63B3ED",
        "D" => "#FF6B6B",
        _ => "#AAB2BF",
    };

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
        T("TowerDroppedItemRarityIncrease", "(8-12)% increased Rarity of Items found in Map", "General", 8, 12),
        T("TowerAdditionalStoneCircle", "Map contains an additional Summoning Circle", "General", 1, 1, tier: "D"),
        T("TowerAdditionalExile", "Map is inhabited by 1 additional Rogue Exile", "General", 1, 1),
        T("TowerAdditionalAzmeriWisp", "Map contains 1 additional Azmeri Spirit", "General", 1, 1),
        T("TowerMonsterEffectiveness", "Monsters have (10-15)% increased Effectiveness", "General", 10, 15, tier: "B"),
        T("TowerRareChestCount", "Map contains (2-3) additional Rare Chests", "General", 2, 3, tier: "D"),
        T("TowerExperienceGainIncrease", "(12-18)% increased Experience gain in Map", "General", 12, 18),
        T("TowerDroppedGoldIncrease", "(25-35)% increased Gold found in Map", "General", 25, 35),
        T("TowerMonsterRarityIncrease", "Map has (15-20)% increased Monster Rarity", "General", 15, 20, tier: "B"),
        T("TowerRarePackIncrease", "Map has (25-35)% increased number of Rare Monsters", "General", 25, 35, tier: "B"),
        T("TowerMagicPackIncrease", "Map has (30-40)% increased Magic Monsters", "General", 30, 40),
        T("TowerPackSizeIncrease", "(5-7)% increased Pack Size in Map", "General", 5, 7),
        T("TowerMapBossExperience", "Map Bosses grant (40-80)% increased Experience", "General", 40, 80, tier: "D"),
        T("TowerMapBossWaystoneChance", "(18-30)% increased Quantity of Waystones dropped by Map Bosses", "General", 18, 30, tier: "A"),
        T("TowerMapBossAdditionalSpirit", "Map contains (1-2) additional Azmeri Spirits", "General", 1, 2),
        T("TowerMapBossAdditionalEssence", "Map contains (1-2) additional Essences", "General", 1, 2),
        T("TowerAdditionalEssence", "Map contains an additional Essence", "General", 1, 1),
        T("TowerMapBossAdditionalShrine", "Map contains (1-2) additional Shrines", "General", 1, 2, tier: "D"),
        T("TowerAdditionalShrine", "Map contains an additional Shrine", "General", 1, 1, tier: "D"),
        T("TowerMapBossAdditionalStrongbox", "Map contains (1-2) additional Strongboxes", "General", 1, 2, tier: "D"),
        T("TowerAdditionalStrongbox", "Map contains an additional Strongbox", "General", 1, 1, tier: "D"),
        T("TowerMapBossRarity", "(35-60)% increased Rarity of Items dropped by Map Bosses", "General", 35, 60, tier: "B"),
        T("TowerMapBossQuantity", "(13-20)% increased Quantity of Items dropped by Map Bosses", "General", 13, 20, tier: "B"),
        T("TowerMapAdditionalUniqueMonsterModifier", "Unique Monsters have 1 additional Rare Modifier", "General", 1, 1),
        T("TowerMapAdditionalModifier", "Map has (1-2) additional random Modifiers", "General", 1, 2, tier: "A"),
        T("TowerStoneCircleChance", "Map has (70-100)% increased chance to contain a Summoning Circle", "General", 70, 100, tier: "D"),
        T("TowerAdditionalSpiritChance", "Map has (70-100)% increased chance to contain Azmeri Spirits", "General", 70, 100),
        T("TowerAdditionalEssenceChance", "Map has (70-100)% increased chance to contain Essences", "General", 70, 100),
        T("TowerAdditionalStrongboxChance", "Map has (70-100)% increased chance to contain Strongboxes", "General", 70, 100, tier: "D"),
        T("TowerAdditionalShrineChance", "Map has (70-100)% increased chance to contain Shrines", "General", 70, 100, tier: "D"),
        T("TowerRareAdditionalModChance", "Rare Monsters in Map have a (50-80)% Surpassing chance to have an additional Modifier", "General", 50, 80, tier: "B"),
        T("TowerMapDroppedMapsIncrease", "(30-40)% increased Quantity of Waystones found in Map", "General", 30, 40, tier: "A"),
        T("TowerAdditionalExileChance", "Map has (70-100)% increased chance to contain Rogue Exiles", "General", 70, 100),

        T("TowerRitualOmenChance", "Ritual Favours in Map have (35-70)% increased chance to be Omens", "Ritual", 35, 70, tier: "A"),
        T("TowerRitualMagicMonsters", "Revived Monsters from Ritual Altars in Map have (25-40)% increased chance to be Rare", "Ritual", 25, 40, tier: "B"),
        T("TowerRitualRareMonsters", "Revived Monsters from Ritual Altars in Map have (35-70)% increased chance to be Magic", "Ritual", 35, 70),
        T("TowerRitualChanceForNoCost", "Favours Rerolled at Ritual Altars in Map have (3-6)% chance to cost no Tribute", "Ritual", 3, 6, tier: "B"),
        T("TowerRitualAdditionalReroll", "Ritual Altars in Map allow rerolling Favours (1-3) additional times", "Ritual", 1, 3, tier: "S"),
        T("TowerRitualDeferSpeed", "Favours Deferred at Ritual Altars in Map reappear (25-40)% sooner", "Ritual", 25, 40, tier: "B"),
        T("TowerRitualDeferCostIncrease", "Deferring Favours at Ritual Altars in Map costs (20-30)% reduced Tribute", "Ritual", 20, 30, tier: "A"),
        T("TowerRitualRerollCostIncrease", "Rerolling Favours at Ritual Altars in Map costs (20-30)% reduced Tribute", "Ritual", 20, 30, tier: "A"),
        T("TowerRitualTributeIncrease", "Monsters Sacrificed at Ritual Altars in Map grant (18-30)% increased Tribute", "Ritual", 18, 30, tier: "A"),

        T("TowerIncursionRareChestChance", "(30-60)% increased chance Vaal Beacon Chests are Rare in Map", "Incursion", 30, 60),
        T("TowerIncursionBossChance", "(10-25)% chance to add a Vaal Beacon Unique Monster to the Map", "Incursion", 10, 25, tier: "B"),
        T("TowerIncursionTokenChance", "(5-10)% chance to gain an additional Crystal from Vaal Beacons in Map", "Incursion", 5, 10, tier: "A"),
        T("TowerIncursionSecondaryEncounters", "(25-50)% increased chance Vaal Beacons summon additional Monsters in Map", "Incursion", 25, 50),
        T("TowerIncursionExtraPacksChance", "(30-60)% chance for an extra packs of Monsters around Vaal Beacons in Map", "Incursion", 30, 60),
        T("TowerIncursionExtraPacks", "1 extra pack of Monsters around Vaal Beacons in Map", "Incursion", 1, 1),
        T("TowerIncursionPackSize", "(10-30)% increased Pack Size for Monsters around Vaal Beacons in Map", "Incursion", 10, 30, tier: "B"),

        T("TowerAbyss4AdditionalChance", "Map has (20-40)% chance to contain four additional Abysses", "Abyss", 20, 40, tier: "A"),
        T("TowerAbyssExtraTickets", "(20-30)% increased chance for Desecrated Currency from Abysses in Map", "Abyss", 20, 30, tier: "B"),
        T("TowerAbyssExtraModifiers", "(20-30)% increased chance for Abyssal monsters in Map to have Abyssal Modifiers", "Abyss", 20, 30, tier: "B"),
        T("TowerAbyssIncreasedRewards", "Abyss Pits in Map are twice as likely to have Rewards", "Abyss", tier: "B"),
        T("TowerAbyssAdditionalChance", "Map contains an additional Abyss", "Abyss", 1, 1, tier: "B"),
        T("TowerAbyssDepthsChance", "Abysses in Map have (10-20)% increased chance to lead to an Abyssal Depths", "Abyss", 10, 20, tier: "B"),
        T("TowerAbyssEffectivenessPerChasm", "Abyssal Monsters have (8-12)% increased Effectiveness for each closed Pit, up to 100%", "Abyss", 8, 12, tier: "A"),
        T("TowerAbyssEnhancedMonstersPerChasm", "Abyssal Monsters in Map have increased Difficulty and Reward for each closed Pit", "Abyss", tier: "B"),
        T("TowerAbyssRareMonsterIncrease", "(1-2) additional Rare Monsters are spawned from Abysses in Map", "Abyss", 1, 2, tier: "B"),
        T("TowerAbyssMonsterIncrease", "Abysses in Map spawn (20-30)% increased Monsters", "Abyss", 20, 30),

        T("TowerBreachAdditionalRares", "Unstable Breaches in Map spawn (1-3) additional Rare Monsters when Stabilised", "Breach", 1, 3, tier: "B"),
        T("TowerBreachBossChance", "Unstable Breaches in Map have (20-50)% increased chance to contain Vruun, Marshal of Xesht", "Breach", 20, 50),
        T("TowerBreachWombgiftLevelChance", "Wombgifts have (10-30)% chance to drop one Level higher in Map", "Breach", 10, 30),
        T("TowerBreachWombgiftQuantity", "(30-60)% increased Quantity of Wombgifts found in Map", "Breach", 30, 60, tier: "B"),
        T("TowerBreachHivebloodQuantity", "(30-60)% increased Quantity of Hiveblood found in Map", "Breach", 30, 60, tier: "B"),
        T("TowerBreachRareMonsterPotency", "(5-20)% increased Effectiveness of Rare Breach Monsters in Map", "Breach", 5, 20, tier: "B"),
        T("TowerBreachMonsterQuantity", "Breaches in Map have (5-15)% increased Pack Size", "Breach", 5, 15, tier: "B"),

        T("TowerDeliriumAdditionalShardsChance", "Delirium Fog in Map spawns (12-26)% increased MirrorShards", "Delirium", 12, 26, tier: "A"),
        T("TowerDeliriumRareMonsterPause", "Slaying Rare Monsters in Map pauses the Delirium Mirror Timer for (3-5) seconds", "Delirium", 3, 5),
        T("TowerDeliriumDoodadsIncrease", "Delirium Fog in Map spawns (15-30)% increased Fracturing Mirrors", "Delirium", 15, 30, tier: "B"),
        T("TowerDeliriumPackSizeIncrease", "Delirium Monsters in Map have (15-30)% increased Pack Size", "Delirium", 15, 30, tier: "B"),
        T("TowerDeliriumDifficultyIncrease", "Delirium Fog in Map applies (15-30)% increased Deliriousness to Players", "Delirium", 15, 30, tier: "B"),
        T("TowerDeliriumFogPersistence", "Delirium Fog in Map dissipates (20-30)% slower", "Delirium", 20, 30),
        T("TowerDeliriumFogDissipationDelayNew", "Delirium Fog in Map lasts (6-12) additional seconds before dissipating", "Delirium", 6, 12),
        T("TowerDeliriumMonsterSplinterIncrease", "(15-30)% increased Stack size of Simulacrum Splinters found in Map", "Delirium", 15, 30, tier: "B"),
        T("TowerDeliriumBossChance", "Delirium Encounters in Map are (15-30)% more likely to spawn Unique Bosses", "Delirium", 15, 30, tier: "B"),

        T("TowerExpeditionRelicModEffect", "(12-18)% increased Effect of Expedition Remnants in Map", "Expedition", 12, 18, tier: "B"),
        T("TowerExpeditionRunicMonsters", "Map contains (15-30)% increased number of Runic Monster Markers", "Expedition", 15, 30, tier: "B"),
        T("TowerExpeditionRareMonsters", "(25-40)% increased number of Rare Expedition Monsters in Map", "Expedition", 25, 40, tier: "B"),
        T("TowerExpeditionLogbookIncrease", "(15-30)% increased Quantity of Expedition Logbooks dropped by Runic Monsters in Map", "Expedition", 15, 30, tier: "B"),
        T("TowerExpeditionExplosionRadius", "(15-30)% increased Expedition Explosive Radius in Map", "Expedition", 15, 30),
        T("TowerExpeditionRelicIncrease", "Expeditions in Map have +(1-2) Remnants", "Expedition", 1, 2, tier: "B"),
        T("TowerExpeditionExplosionPlacement", "(15-30)% increased Expedition Explosive Placement Range in Map", "Expedition", 15, 30),
        T("TowerExpeditionArtifactIncrease", "(15-30)% increased quantity of Expedition Artifacts dropped by Monsters in Map", "Expedition", 15, 30, tier: "B"),
    ];

    public static StashUtilityModDefinition? MatchWaystone(string rawId)
        => Match(rawId, WaystoneMods);

    public static StashUtilityModDefinition? MatchTablet(string rawId)
        => Match(rawId, TabletMods);

    public static IEnumerable<StashUtilityModDefinition> TabletModsFor(TabletRuleGroup group)
        => TabletMods.Where(definition => group.ModifierCategories.Contains(
            definition.Category,
            StringComparer.OrdinalIgnoreCase));

    public static bool MatchesTabletSearch(StashUtilityModDefinition definition, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var haystack = $"{definition.Name} {definition.Id} {definition.Category} Tier {definition.MarketTier}";
        var tokens = search.Split(
            [' ', '\t', '\r', '\n', '-', '_', '/', '\\', '(', ')', '[', ']', '%', '+'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0
               && tokens.All(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

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
        => new(
            id,
            name,
            "Waystone",
            rarity,
            pack,
            monsterRarity,
            effect,
            drop,
            MarketTier: WaystoneRewardTier(drop));

    internal static string WaystoneRewardTier(int dropChance)
        => dropChance switch
        {
            >= 25 => "S",
            >= 20 => "A",
            >= 15 => "B",
            >= 10 => "C",
            _ => "D",
        };

    private static StashUtilityModDefinition T(
        string id,
        string name,
        string category,
        float min = 0,
        float max = 0,
        string tier = "C")
        => new(id, name, category, MinRoll: min, MaxRoll: max, MarketTier: tier);
}
