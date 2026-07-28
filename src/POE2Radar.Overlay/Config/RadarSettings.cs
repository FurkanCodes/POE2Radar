using System.Text.Json;
using System.Text.Json.Serialization;
using POE2Radar.Core.Game;

namespace POE2Radar.Overlay.Config;

/// <summary>
/// User-tweakable overlay settings, persisted as JSON next to the executable
/// (<c>config/radar_settings.json</c>). Defaults reproduce the original hardcoded behavior exactly,
/// so a missing/partial file changes nothing. Calibration is saved live as hotkeys adjust it.
/// </summary>
public sealed class RadarSettings
{
    // ── Feature flags (reserved for later phases; no behavior wired yet). ──
    public bool HideJunk { get; set; } = false;
    public bool ShowPath { get; set; } = false;
    public bool PathTogglesMigrated { get; set; }
    public bool ShowPathWorld { get; set; } = true;
    public bool ShowPathMap { get; set; } = true;
    public bool ShowPathMinimap { get; set; } = true;
    /// <summary>Ground-screen breadcrumbs when the Tab map is closed (sub-toggle of <see cref="ShowPathWorld"/>).</summary>
    public bool ShowGroundWaypoints { get; set; } = true;
    public bool UseCuratedLandmarks { get; set; } = true;
    public bool DrawAllLandmarkPaths { get; set; } = false;

    /// <summary>True when any path display layer is enabled (ground needs world + waypoints).</summary>
    public bool AnyPathLayerEnabled =>
        (ShowPathWorld && ShowGroundWaypoints) || ShowPathMap || ShowPathMinimap;

    /// <summary>Enable or disable all path display layers together (legacy <see cref="ShowPath"/> compat).</summary>
    public void SetAllPathLayers(bool enabled)
    {
        ShowPath = enabled;
        ShowPathWorld = enabled;
        ShowGroundWaypoints = enabled;
        ShowPathMap = enabled;
        ShowPathMinimap = enabled;
    }

    /// <summary>Ground path draw gate — sets both world polyline and breadcrumb toggles.</summary>
    public void SetPathGroundEnabled(bool enabled)
    {
        ShowPathWorld = enabled;
        ShowGroundWaypoints = enabled;
    }

    // ── Landmark clustering. A reusable tile (e.g. a "stairs up" wall piece) recurs in several
    //    disjoint spots — a multi-level dungeon has several stair-up/stair-down sections — so the
    //    scanner groups a tile path's cells into spatial clusters and emits one marker per cluster.
    //    This is the MAX GAP (in TILES; 1 tile ≈ 23 grid units) between cells still considered the
    //    same cluster: larger = merges nearby spots (fewer markers, less map spam), smaller = splits
    //    them (more markers). 0 disables bridging (only directly-touching tiles group). ──
    public int LandmarkClusterGap { get; set; } = 2;

    // ── Entity gather/draw radius (grid cells from player). 0 = unlimited (game network bubble only). ──
    public int EntityDrawRadiusGrid { get; set; } = 0;

    // ── Live auto-path: each world tick, fill remaining path slots with nearest targets whose display
    //    rule has Auto-path (Navigable) enabled. Manual F6/legend selections are never overridden. ──
    public bool AutoPathNavigable { get; set; } = false;

    // ── In-game hotkey to toggle <see cref="AutoPathNavigable"/> (default F2 = 0x71). 0 = disabled. ──
    public int AutoPathToggleHotkey { get; set; } = 0x71;

    // ── One-time guard: the nav model moved to "display rules are the single source of truth" (an entity
    //    auto-paths only via its rule's Navigable flag, not a hardcoded POI/unique clause). On the first
    //    run after the upgrade we flip Navigable on the default POI/Transition/Unique rules so behavior is
    //    preserved; this stays true thereafter so we never re-stomp the user's edits. ──
    public bool NavRuleModelMigrated { get; set; } = false;

    // ── Curated radar: when true, normal/magic monster trash and miscellaneous clutter are not drawn
    //    on the map overlay (nav is still governed by display-rule Navigable flags). Toggle off to
    //    reveal everything. ──
    public bool ImportantOnly { get; set; } = true;

    // ── One-time: flip Navigable on mechanic rules + Monster · Rare after the endgame-nav defaults pass. ──
    public bool EndgameNavMigrated { get; set; } = false;

    // ── One-time: append quest/waypoint/portal/bridge semantic rules + Boss labels to display_rules.json. ──
    public bool GlobalRulesExpandedMigrated { get; set; } = false;

    // ── One-time: move per-token global rules into zone_entity_overrides for the current area code. ──
    public bool PerTypeRulesMigrated { get; set; } = false;

    // ── One-time: Checkpoint/Map marker split, stash/town portal rules, sprite defaults on rules. ──
    public bool SemanticNamesMigrated { get; set; } = false;

    // ── One-time: bump icon sizes + assign PNG sprites on existing display rules. ──
    public bool IconDefaultsMigrated { get; set; } = false;

    // ── One-time: endgame mechanic catalog (Abyss, Delirium, dedupe, before Map marker). ──
    public bool EndgameMechanicsMigrated { get; set; } = false;

    // ── One-time: refresh Abyss/Object matchers + catalog-first resolve (gates, troves). ──
    public bool EndgameMechanicsV2Migrated { get; set; } = false;

    // ── One-time: Essence frozen-mob matchers + mechanics ordered before Monster Rare. ──
    public bool EndgameMechanicsV3Migrated { get; set; } = false;

    // ── One-time: Essence rare+POI cluster + any-category Essence rule matchers. ──
    public bool EndgameMechanicsV7Migrated { get; set; } = false;

    // ── One-time: split Strongbox into Unique / Landmark / Cartographer / Researcher / Abyss + generic. ──
    public bool EndgameMechanicsV8Migrated { get; set; } = false;

    // ── One-time: strongbox family completion, Ultimatum/Corruption, opened-chest exemptions. ──
    public bool EndgameMechanicsV9Migrated { get; set; } = false;

    // ── One-time: atlas display settings upgrade (AtlasDrawAll → AtlasShowOnScreenNodes). ──
    public bool AtlasDisplayMigrated { get; set; } = false;

    // One-time: GameHelper-style atlas visuals (hide filters, thin routes, label pills).
    public bool AtlasGhStyleMigrated { get; set; } = false;

    // One-time: clean atlas MVP — show all memory nodes; hide filters off by default.
    public bool AtlasCleanMvpMigrated { get; set; } = false;

    // One-time: Atlas2 QoL — MapGroups categories + visuals defaults.
    public bool Atlas2QolMigrated { get; set; } = false;

    // One-time: low-impact cadence defaults (lower read rate by default without removing features).
    public bool PerformanceDefaultsMigrated { get; set; } = false;

    // One-time: add server-authoritative minimap icon display rules (waypoint/entrance/checkpoint default on).
    public bool ServerIconsMigrated { get; set; } = false;

    // One-time: add server-icon mechanic rules (Abyss/Strongbox/Breach/Ritual) + catch-all.
    public bool ServerIconMechanicsMigrated { get; set; } = false;

    // One-time: add server-icon chest rule.
    public bool ServerIconChestMigrated { get; set; } = false;

    // One-time: add server-icon portal rule.
    public bool ServerIconPortalMigrated { get; set; } = false;

    // One-time: F3 is now the global render kill-switch; move auto-path off the old F3 default.
    public bool RenderingHotkeyMigrated { get; set; } = false;

    public RitualSettings Ritual { get; set; } = new();
    public AmanamuSettings Amanamu { get; set; } = new();
    public RunecraftSettings Runecraft { get; set; } = new();
    public SekhemaSettings Sekhema { get; set; } = new();
    public StashValueSettings StashValue { get; set; } = new();
    public StashUtilitySettings StashUtility { get; set; } = new();
    public WaystoneAlchemySettings WaystoneAlchemy { get; set; } = new();
    public PickupHelperSettings PickupHelper { get; set; } = new();
    public LootTrackerSettings LootTracker { get; set; } = new();
    public CampaignSettings Campaign { get; set; } = new();

    // ── Global multiplier on map icon sprite scale (PNG from icons.png). ──
    public float GlobalIconScale { get; set; } = 1.25f;

    // ── Radar display toggles. ──
    public bool ShowMonsters { get; set; } = true;
    public bool ShowTerrain { get; set; } = true;
    // The player position blip at map-center. Default on (prior behavior); some prefer it off.
    public bool ShowPlayerBlip { get; set; } = true;

    // ── Overlay render/present rate (Hz). Lower = less CPU/GPU tax on the game.
    //    60 is plenty smooth for a radar; raise toward your monitor's refresh if you prefer. The
    //    heavier entity/terrain walk stays fixed at ~30 Hz regardless. ──
    public bool LowImpactMode { get; set; } = true;
    public int FpsCap { get; set; } = 45;
    public int LiveRefreshHz { get; set; } = 30;
    public int WorldRefreshHz { get; set; } = 12;
    public int InactiveRefreshHz { get; set; } = 1;
    public int HpBarRefreshHz { get; set; } = 8;
    public int MaxLiveHpBars { get; set; } = 32;
    public int MetricsRefreshHz { get; set; } = 1;
    public int GpuMetricsRefreshSeconds { get; set; } = 5;
    public bool SmoothOverlayMotion { get; set; } = true;
    public int OverlaySmoothingMs { get; set; } = 45;
    public int ChipSmoothingMs { get; set; } = 70;
    public bool PixelSnapLabels { get; set; } = true;
    public bool OverlayVSync { get; set; } = true;

    /// <summary>
    /// When true, the overlay HWND is excluded from screenshots, OBS, and Windows share-screen
    /// (<c>WDA_EXCLUDEFROMCAPTURE</c>). Turn off only when you need to capture the overlay itself.
    /// </summary>
    public bool HideFromScreenCapture { get; set; } = true;

    // ── Navigation-menu widget: which screen corner it is pinned to.
    //    One of "TopLeft", "TopRight", "BottomLeft", "BottomRight". ──
    public string NavMenuCorner { get; set; } = "TopLeft";
    /// <summary>Free-drag taskbar position in overlay-client pixels. Negative uses NavMenuCorner.</summary>
    public float NavTaskbarX { get; set; } = -1f;
    public float NavTaskbarY { get; set; } = -1f;
    // Developer/performance tuning aid: extended timing/read counters in the on-screen perf HUD.
    public bool ShowPerfStats { get; set; } = false;
    // FPS + App CPU/GPU/RAM under the POE2Radar nav-menu button.
    public bool ShowFpsOverlay { get; set; } = false;

    // ── Persistent auto-nav: substrings matched (case-insensitive Contains) against a navigation
    //    target's MatchKey (tile path / entity metadata). On every zone change, every target whose
    //    MatchKey matches ANY pattern is auto-selected (up to the 8-color cap), so entering a new
    //    zone auto-draws a path to e.g. the expedition encounter. Seeded with one example so the
    //    feature is visible out of the box; clear the list to disable. ──
    // Dir-qualified so it matches the real marker ("Expedition2/Expedition2Encounter") and not the
    // transient ".../Objects/Expedition2EncounterCrack" effects. (Plain "ExpeditionEncounter" matched
    // nothing — the live path is "Expedition2Encounter" with a digit.)
    public List<string> AutoNavPatterns { get; set; } = new() { "Expedition2/Expedition2Encounter" };

    // ── Monster HP bars (world-space nameplates) by rarity.
    //    Defaults preserve prior behavior: Magic/Rare/Unique shown, Normal hidden. ──
    public bool HpBarNormal { get; set; } = false;
    public bool HpBarMagic { get; set; } = false;
    public bool HpBarRare { get; set; } = true;
    public bool HpBarUnique { get; set; } = true;

    // ── Projection calibration (PageUp/Down = scale, arrows = offset, Home = reset). ──
    public float LargeMapScaleMultiplier { get; set; } = 1.0f;
    public float ScaleMul { get; set; } = 1.0f;
    public float OffX { get; set; } = 0f;
    public float OffY { get; set; } = 0f;

    // NOTE: the atlas canvas→screen projection has NO stored settings — it's derived live from the game
    // window height (UIscale = winH/1600 × live zoom) in RadarApp.AtlasProjection, so it's resolution-
    // correct everywhere with no calibration. (The old F10/F11 homography calibration + its AtlasScale/
    // Off/Shear/Pers/CalibZoom settings were removed; F10 now inspects the tile under the cursor.)

    // Atlas highlight rules: optional accent layer (ring colour, route tracking). Matched case-insensitively
    // against each node's content tags (e.g. "Breach") or map name. Edited in overlay Settings → Atlas.
    public List<string> AtlasHighlightTags { get; set; } = new();
    // Tags with the off-screen ARROW enabled: when a matching map is outside render distance, an edge
    // arrow points toward it (for hunting high-value maps you can't zoom out to). Independent of tracking.
    public List<string> AtlasArrowTags { get; set; } = new();
    // Per-rule ring colour (tag → "#RRGGBB"), so each highlighted map draws in its filter's category
    // colour in-game (Citadel gold, Boss red, …).
    public Dictionary<string, string> AtlasHighlightColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Seeded-defaults guard: false until the atlas rules have been initialized once (either by seeding
    // the Citadel defaults when nodes are first read, or by any overlay/dashboard edit). Stops re-seeding.
    public bool AtlasRulesInitialized { get; set; }
    // Legacy JSON key — migrated to AtlasShowOnScreenNodes on load; not shown in UI.
    public bool AtlasDrawAll { get; set; } = false;
    // Draw every atlas node on-screen. When any Track highlight is active, only tracked nodes are shown instead.
    public bool AtlasShowOnScreenNodes { get; set; } = true;
    // Label on-screen nodes with map name (+ highlight tag when matched).
    public bool AtlasShowNames { get; set; } = true;
    // Fogged/hidden nodes drawn with a cool tint so every memory node is visible.
    public bool AtlasRevealFog { get; set; } = true;
    // Off-screen edge arrows for arrow-tagged highlights only (e.g. Citadels).
    public bool AtlasOffScreenArrows { get; set; } = true;
    // Atlas-only icon sprite scale (does not affect zone map icons).
    public float AtlasIconScale { get; set; } = 1.0f;
    // Atlas map-name label chip scale (text + chip body).
    public float AtlasLabelScale { get; set; } = 1.0f;
    // Atlas routing: F10 over a tile sets it as the route destination; the overlay draws the shortest path
    // (through the node connection graph) from the player's current node to it. On by default.
    public bool AtlasShowRoute { get; set; } = true;
    // When no manual F10 START is set, route from the player's current atlas tile (live marker read).
    public bool AtlasUseCurrentStart { get; set; } = true;
    public string AtlasLanguage { get; set; } = "english";
    public string AtlasSearchQuery { get; set; } = "";
    // Atlas2-aligned hide filters (completed on; inaccessible off; available unused).
    public bool AtlasHideCompletedMaps { get; set; } = true;
    public bool AtlasHideNotAccessibleMaps { get; set; } = false;
    public bool AtlasHideAvailableMaps { get; set; } = false;
    public bool AtlasShowBiomeBorders { get; set; } = true;
    public bool AtlasShowContentBadges { get; set; } = true;
    public bool AtlasShowContentCount { get; set; } = false;
    public bool AtlasShowContentTokens { get; set; } = false;
    // Uncharted Waters (Atlas2).
    public bool AtlasShowShipsInFog { get; set; } = false;
    public float AtlasShipIconSize { get; set; } = 46f;
    public bool AtlasShowUnchartedLeylines { get; set; } = false;
    public string AtlasUnchartedLeylineColor { get; set; } = "#33D9E6";
    public float AtlasUnchartedLeylineThickness { get; set; } = 10f;
    // Complete Island Rumours are opt-in so the existing atlas render path is byte-for-byte idle
    // unless requested. When enabled, manifests are built once from the already-read node snapshot.
    public bool AtlasShowIslandRumours { get; set; } = false;
    public bool AtlasShowIslandRumourBadges { get; set; } = true;
    public string AtlasIslandRumourPriorityFilter { get; set; } =
        "Fallen stars|Unknown ruins|All that glitters|Almost paradise|Reflective waters|Stardrinker|Origin of the fall|Crazed Chieftain";
    public string AtlasIslandRumourPriorityColor { get; set; } = "#FFD166";
    // Ritual atlas line (Atlas2 RitualFeatures) — separate from Ritual shop pricing.
    public bool AtlasShowRitualPrediction { get; set; } = false;
    public bool AtlasShowRitualPlanner { get; set; } = true;
    public string AtlasRitualRewardFilter { get; set; } = "";
    public float AtlasRitualPlannerFontScale { get; set; } = 1f;
    public Dictionary<string, int> AtlasRitualRewardWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AtlasShowRouteChevrons { get; set; } = false;
    public float AtlasRouteLineThickness { get; set; } = 6f;
    public float AtlasRouteChevronSpacing { get; set; } = 8f;
    public string AtlasManualRouteColor { get; set; } = "#3BDBFF";
    public string AtlasSearchRouteColor { get; set; } = "#FFFFFF";
    public float AtlasRouteOpacity { get; set; } = 0.95f;
    public float AtlasSearchRange { get; set; } = 1f;
    public float AtlasLabelOffsetX { get; set; } = 0f;
    public float AtlasLabelOffsetY { get; set; } = 0f;
    public float AtlasAnchorNudgeY { get; set; } = 28f;
    public float AtlasBaseWidth { get; set; } = 1920f;
    public float AtlasBaseHeight { get; set; } = 1080f;
    public float AtlasScaleMultiplier { get; set; } = 1f;
    public float AtlasBiomeBorderThickness { get; set; } = 2f;
    public bool AtlasShowNodeSprites { get; set; } = false;
    public bool AtlasDrawLinesSearchQuery { get; set; } = true;
    public bool AtlasDrawLinesToUniqueMaps { get; set; } = false;
    public bool AtlasPathToLineageMaps { get; set; } = false;
    public bool AtlasPathToArbiterMaps { get; set; } = false;
    public bool AtlasShowContentIcons { get; set; } = false;
    public float AtlasContentIconSize { get; set; } = 32f;
    public bool AtlasUseUniversalFont { get; set; } = true;
    public string AtlasDefaultBackgroundColor { get; set; } = "#000000";
    public string AtlasDefaultFontColor { get; set; } = "#FFFFFF";
    public List<AtlasContentGroupSettings> AtlasContentGroups { get; set; } = new();
    public List<AtlasMapGroupSettings> AtlasMapGroups { get; set; } = BuildDefaultAtlasMapGroups();
    public List<AtlasRouteGroupSettings> AtlasRouteGroups { get; set; } = BuildDefaultAtlasRouteGroups();

    // ── Auto-flask thresholds + per-flask cooldowns (milliseconds). ──
    // What the (single) life-flask key triggers on: "Health" watches HP% only (default — unchanged
    // behavior), "EnergyShield" watches ES% only (for CI / ES-stacking builds), "Either" fires when
    // EITHER pool drops below its own threshold. ES is ignored when the build has no ES pool.
    public string LifeFlaskMode { get; set; } = "Health";
    public float LifeThresholdPct { get; set; } = 65f;
    public float EsThresholdPct { get; set; } = 50f;
    public float ManaThresholdPct { get; set; } = 30f;
    public int LifeCooldownMs { get; set; } = 2500;
    public int ManaCooldownMs { get; set; } = 2000;

    // ── Flask key codes (Win32 virtual-key). Defaults: '1' = life, '2' = mana. ──
    public int LifeKey { get; set; } = 0x31;
    public int ManaKey { get; set; } = 0x32;

    // ── Hide entity under cursor: adds TypeToken to hidden_entities.json (F5 default). ──
    public int HideEntityHotkey { get; set; } = 0x74;

    // ── Inspect entity under cursor: print identity to console (F4 default). JSON key kept for compat. ──
    public int TrackEntityHotkey { get; set; } = 0x73;

    // ── Path / overlay / atlas hotkeys (0 = disabled; gamepad uses 0x10000 | XINPUT button mask). ──
    public int ToggleRenderingHotkey { get; set; } = 0x72;
    public int AddNearestPathHotkey { get; set; } = 0x75;
    public int ClearPathsHotkey { get; set; } = 0x76;
    public int AutoFlaskToggleHotkey { get; set; } = 0x77;
    public int QuitHotkey { get; set; } = 0x78;
    public int AtlasPickHotkey { get; set; } = 0x79;
    public int ToggleSettingsHotkey { get; set; } = 0x7A;
    public int OpenDashboardHotkey { get; set; } = 0x7B;

    /// <summary>Settings/taskbar appearance: "Modern" keeps ImGui; "Old" uses classic WinForms chrome.</summary>
    public string InterfaceStyle { get; set; } = "Old";

    /// <summary>Last settings sidebar page (e.g. "Radar", "Crafting Assistant"). Restored when reopening settings.</summary>
    public string LastSettingsTab { get; set; } = "Radar";

    // ── Xbox / XInput controller (player slot 0–3). ──
    public bool GamepadHotkeysEnabled { get; set; } = false;
    public int GamepadUserIndex { get; set; } = 0;

    // ── In-game ImGui chrome (GameHelper defaults: Microsoft YaHei 18px). ──
    public string UiFontPath { get; set; } = @"C:\Windows\Fonts\msyh.ttc";
    public int UiFontSize { get; set; } = 18;
    public UiFontGlyphRange UiFontGlyphRange { get; set; } = UiFontGlyphRange.ChineseSimplifiedCommon;
    public bool UiFontDefaultsMigrated { get; set; }

    /// <summary>One-time: large-map scale knob was unwired with a GameHelper-style 0.1738 stub default.</summary>
    public bool LargeMapScaleWiredMigrated { get; set; }

    /// <summary>One-time: plain loot chests icon-only (no map chips, no auto-path).</summary>
    public bool ChestIconOnlyMigrated { get; set; }

    /// <summary>One-time: restore strongbox labels + auto-path after chest-only icon pass.</summary>
    public bool StrongboxDefaultsRestoredMigrated { get; set; }

    /// <summary>One-time: rare chests use triangle icon + yellow Rare label; hide normal/magic chests.</summary>
    public bool RareChestDisplayMigrated { get; set; }

    /// <summary>One-time: enable monolith rewards window (controller-friendly recipe list).</summary>
    public bool RunecraftMonolithWindowMigrated { get; set; }

    /// <summary>One-time: disable pad auto-open monolith window default (CPU).</summary>
    public bool RunecraftAutoMonolithCpuMigrated { get; set; }

    /// <summary>One-time: enable Ritual Prices side window.</summary>
    public bool RitualPricesWindowMigrated { get; set; }

    /// <summary>One-time: reconcile Atlas built-in route targets with the GameHelper target list.</summary>
    public bool AtlasRouteTargetsGhParityMigrated { get; set; }

    /// <summary>One-time: restore Atlas2 route visibility defaults and solid-line presentation.</summary>
    public bool AtlasRoutePresentationGhParityMigrated { get; set; }

    /// <summary>
    /// One-time: prefer Alchemy on Magic Waystones (PoE2 0.3.1+). Prior default Regal'd blues and
    /// never used Alchemy orbs on them.
    /// </summary>
    public bool WaystoneAlchemyPreferAlchemyMigrated { get; set; }

    // ── HTTP API. ──
    public int ApiPort { get; set; } = 7777;

    /// <summary>Path to the PoE2 client executable (PathOfExile.exe / PathOfExileSteam.exe). Set via startup menu browse.</summary>
    public string GameExePath { get; set; } = "";

    // ── Per-item icon styling (shape / color / opacity / size) + metadata-matched "mechanic"
    //    overrides. Defaults reproduce the original hardcoded look exactly. ──
    public RadarStyles Styles { get; set; } = new();

    // ── Monster HP-bar geometry (the per-rarity ENABLE flags above stay the source of truth;
    //    this adds per-rarity sizing, border thickness, and border color). ──
    public HpBarSettings HpBars { get; set; } = new();

    // ── Walkable-terrain bitmap colors/transparency. Defaults reproduce the old hardcoded wash. ──
    public TerrainSettings Terrain { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Config file path: a "config" directory next to the executable.</summary>
    public static string FilePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "config", "radar_settings.json");

    /// <summary>
    /// Load settings from disk. Returns defaults if the file is missing (and writes a default file),
    /// and is tolerant of partial/missing keys. Never throws on IO/parse errors — logs and falls back.
    /// </summary>
    public static RadarSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var fresh = new RadarSettings();
                fresh.Save();
                return fresh;
            }

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<RadarSettings>(json, Json) ?? new RadarSettings();
            loaded.Amanamu ??= new AmanamuSettings();
            loaded.Campaign ??= new CampaignSettings();
            // Existing configs are loaded verbatim (never re-seeded from defaults), so repair stale
            // patterns shipped by older builds in place, then persist the upgrade.
            if (loaded.Migrate())
            {
                loaded.Save();
                Console.WriteLine("Settings: migrated stale mechanic rules (Expedition/Strongbox category gating).");
            }
            return loaded;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Settings load failed ({ex.Message}); using defaults.");
            return new RadarSettings();
        }
    }

    /// <summary>
    /// One-time, idempotent repair of mechanic rules from older builds (loaded verbatim, so they'd
    /// otherwise keep the bug forever). Both fixes address ungated rules that tagged a mechanic's
    /// spawned monsters, not just the object:
    /// <list type="bullet">
    /// <item>Expedition: bare "Expedition" / dead "ExpeditionEncounter" → precise, Other-gated
    ///   "Expedition2/Expedition2Encounter".</item>
    /// <item>Strongbox: add a Chest category gate (the box's Vaal guards carry "...Strongbox").</item>
    /// </list>
    /// Returns true if anything changed.
    /// </summary>
    public bool Migrate()
    {
        const string precise = "Expedition2/Expedition2Encounter";
        static bool IsStaleExp(string p) =>
            string.Equals(p, "Expedition", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p, "ExpeditionEncounter", StringComparison.OrdinalIgnoreCase);

        var changed = false;

        static bool IsBroadStrongbox(string p) => string.Equals(p, "Strongbox", StringComparison.OrdinalIgnoreCase);

        changed |= RepairSekhemaSettings();

        if (Styles?.Mechanics is { } mechanics)
            foreach (var m in mechanics)
            {
                if (m.Match is null) continue;
                // Expedition: drop the stale/over-broad keys → the precise key + an Other category gate
                // (so it can't hijack the Monster-category expedition mobs).
                if (m.Match.RemoveAll(IsStaleExp) > 0)
                {
                    if (!m.Match.Exists(p => string.Equals(p, precise, StringComparison.OrdinalIgnoreCase)))
                        m.Match.Add(precise);
                    m.Categories ??= new List<string>();
                    if (m.Categories.Count == 0) m.Categories.Add("Other");
                    changed = true;
                }
                // Strongbox: the default's bare "Strongbox" term over-matched twice — the box's spawned
                // Vaal guards (…Strongbox monsters) and ordinary area chests named "...Strongbox". Drop
                // it down to the "StrongBoxes" directory term and gate to Chest (the box is a /Chests/
                // entity). Triggers whenever the broad term is still present, regardless of category.
                else if (m.Match.Exists(IsBroadStrongbox))
                {
                    m.Match.RemoveAll(IsBroadStrongbox);
                    if (!m.Match.Exists(p => string.Equals(p, "StrongBoxes", StringComparison.OrdinalIgnoreCase)))
                        m.Match.Add("StrongBoxes");
                    m.Categories ??= new List<string>();
                    if (m.Categories.Count == 0) m.Categories.Add("Chest");
                    changed = true;
                }
            }

        // Auto-nav: the seeded "ExpeditionEncounter" matched nothing (digit in the real path).
        if (AutoNavPatterns is not null)
            for (var i = 0; i < AutoNavPatterns.Count; i++)
                if (IsStaleExp(AutoNavPatterns[i])) { AutoNavPatterns[i] = precise; changed = true; }

        // Atlas display: legacy AtlasDrawAll → show-on-screen; first upgrade enables the new defaults.
        if (!AtlasDisplayMigrated)
        {
            if (AtlasDrawAll) AtlasShowOnScreenNodes = true;
            AtlasDisplayMigrated = true;
            changed = true;
        }

        if (!AtlasGhStyleMigrated)
        {
            AtlasHideCompletedMaps = true;
            AtlasHideNotAccessibleMaps = true;
            AtlasHideAvailableMaps = true;
            AtlasShowNodeSprites = false;
            AtlasAnchorNudgeY = 28f;
            AtlasRouteLineThickness = 1f;
            AtlasRouteChevronSpacing = 8f;
            AtlasGhStyleMigrated = true;
            changed = true;
        }

        if (!AtlasCleanMvpMigrated)
        {
            AtlasShowOnScreenNodes = true;
            AtlasRevealFog = true;
            AtlasHideCompletedMaps = false;
            AtlasHideNotAccessibleMaps = false;
            AtlasHideAvailableMaps = false;
            AtlasDrawLinesSearchQuery = true;
            AtlasCleanMvpMigrated = true;
            changed = true;
        }

        if (!Atlas2QolMigrated)
        {
            AtlasShowOnScreenNodes = true;
            AtlasRevealFog = true;
            AtlasHideCompletedMaps = true;
            AtlasHideNotAccessibleMaps = false;
            AtlasHideAvailableMaps = false;
            AtlasShowBiomeBorders = true;
            AtlasShowContentBadges = true;
            AtlasShowContentCount = false;
            AtlasDrawLinesSearchQuery = true;
            AtlasRouteGroups = BuildDefaultAtlas2RouteGroups();
            Atlas2QolMigrated = true;
            changed = true;
        }

        if (!PathTogglesMigrated)
        {
            ShowPathWorld = ShowPathMap = ShowPathMinimap = ShowPath;
            if (ShowPath) ShowGroundWaypoints = true;
            PathTogglesMigrated = true;
            changed = true;
        }

        if (!RenderingHotkeyMigrated)
        {
            ToggleRenderingHotkey = 0x72; // F3
            if (AutoPathToggleHotkey == 0x72)
                AutoPathToggleHotkey = 0x71; // F2
            RenderingHotkeyMigrated = true;
            changed = true;
        }

        if (!PerformanceDefaultsMigrated)
        {
            LowImpactMode = true;
            FpsCap = Math.Min(FpsCap, 45);
            LiveRefreshHz = 30;
            WorldRefreshHz = 12;
            InactiveRefreshHz = 1;
            HpBarRefreshHz = 8;
            MaxLiveHpBars = 32;
            MetricsRefreshHz = 1;
            GpuMetricsRefreshSeconds = 5;
            ShowFpsOverlay = false;
            PerformanceDefaultsMigrated = true;
            changed = true;
        }

        if (!UiFontDefaultsMigrated)
        {
            UiFontPath = @"C:\Windows\Fonts\msyh.ttc";
            UiFontSize = 18;
            UiFontGlyphRange = UiFontGlyphRange.ChineseSimplifiedCommon;
            UiFontDefaultsMigrated = true;
            changed = true;
        }

        if (!LargeMapScaleWiredMigrated)
        {
            // Large map used ScaleMul before the dedicated knob was wired; 0.1738 was never applied live.
            if (MathF.Abs(LargeMapScaleMultiplier - 0.1738f) < 0.0001f)
                LargeMapScaleMultiplier = ScaleMul;
            LargeMapScaleWiredMigrated = true;
            changed = true;
        }

        if (!RunecraftMonolithWindowMigrated)
        {
            Runecraft.ShowMonolithWindow = true;
            Runecraft.AutoShowMonolithWithGamepad = true;
            RunecraftMonolithWindowMigrated = true;
            changed = true;
        }

        if (!RunecraftAutoMonolithCpuMigrated)
        {
            Runecraft.AutoShowMonolithWithGamepad = false;
            RunecraftAutoMonolithCpuMigrated = true;
            changed = true;
        }

        if (!RitualPricesWindowMigrated)
        {
            Ritual.ShowPricesWindow = true;
            RitualPricesWindowMigrated = true;
            changed = true;
        }

        if (AtlasMapGroups.Count == 0)
        {
            AtlasMapGroups = BuildDefaultAtlasMapGroups();
            changed = true;
        }
        if (AtlasRouteGroups.Count == 0)
        {
            AtlasRouteGroups = BuildDefaultAtlasRouteGroups();
            changed = true;
        }

        if (!AtlasRouteTargetsGhParityMigrated)
        {
            // Legacy single "Map Targets" list — skip when Atlas2 BuiltInKey categories are present.
            if (!AtlasRouteGroups.Any(g => !string.IsNullOrEmpty(g.BuiltInKey)))
                ReconcileBuiltInAtlasRouteTargets();
            AtlasRouteTargetsGhParityMigrated = true;
            changed = true;
        }

        if (!AtlasRoutePresentationGhParityMigrated)
        {
            AtlasShowRouteChevrons = false;
            AtlasRouteLineThickness = 6f;
            ReconcileAtlas2RoutePresentation();
            AtlasRoutePresentationGhParityMigrated = true;
            changed = true;
        }

        if (!WaystoneAlchemyPreferAlchemyMigrated)
        {
            WaystoneAlchemy.UseRegalOnMagic = false;
            WaystoneAlchemyPreferAlchemyMigrated = true;
            changed = true;
        }

        return changed;
    }

    private bool RepairSekhemaSettings()
    {
        var changed = false;
        if (Sekhema is null)
        {
            Sekhema = new SekhemaSettings();
            changed = true;
        }

        var settings = Sekhema;
        if (settings.Profiles is null)
        {
            settings.Profiles = new Dictionary<string, SekhemaProfileSettings>(StringComparer.OrdinalIgnoreCase);
            changed = true;
        }

        changed |= RepairProfile("Default", SekhemaProfileSettings.CreateDefault());
        changed |= RepairProfile("No-Hit", SekhemaProfileSettings.CreateNoHit());

        if (string.IsNullOrWhiteSpace(settings.CurrentProfile) ||
            !settings.Profiles.ContainsKey(settings.CurrentProfile))
        {
            settings.CurrentProfile = "Default";
            changed = true;
        }

        string[] defaultOrder =
        [
            "GrandSpectrum", "RadiusJewels", "LargeRelic", "Jewels", "Currency",
            "MediumRelic", "SmallRelic", "Maps", "Generic",
        ];
        if (settings.ChestPriorityOrder is null || settings.ChestPriorityOrder.Count == 0)
        {
            settings.ChestPriorityOrder = [.. defaultOrder];
            changed = true;
        }

        if (settings.ChestDisabledContent is null ||
            settings.ChestDisabledContent.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            settings.ChestDisabledContent = settings.ChestDisabledContent is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(settings.ChestDisabledContent, StringComparer.OrdinalIgnoreCase);
            changed = true;
        }

        return changed;

        bool RepairProfile(string name, SekhemaProfileSettings defaults)
        {
            if (!settings.Profiles.TryGetValue(name, out var profile) || profile is null)
            {
                settings.Profiles[name] = defaults;
                return true;
            }

            var repaired = false;
            if (profile.RoomTypeWeights is null || profile.RoomTypeWeights.Count == 0)
            {
                profile.RoomTypeWeights = new Dictionary<string, float>(defaults.RoomTypeWeights);
                repaired = true;
            }
            if (profile.AfflictionWeights is null || profile.AfflictionWeights.Count == 0)
            {
                profile.AfflictionWeights = new Dictionary<string, float>(defaults.AfflictionWeights);
                repaired = true;
            }
            if (profile.RewardWeights is null || profile.RewardWeights.Count == 0)
            {
                profile.RewardWeights = new Dictionary<string, float>(defaults.RewardWeights);
                repaired = true;
            }
            return repaired;
        }
    }

    private static List<AtlasMapGroupSettings> BuildDefaultAtlasMapGroups()
        => AtlasCatalog.Shared.DefaultMapGroups
            .Select(g => new AtlasMapGroupSettings
            {
                Name = g.Name,
                Color = g.Color,
                FontColor = g.FontColor,
                Maps = g.Maps.ToList(),
            })
            .ToList();

    private static List<AtlasRouteGroupSettings> BuildDefaultAtlasRouteGroups()
        => BuildDefaultAtlas2RouteGroups();

    private static List<AtlasRouteGroupSettings> BuildDefaultAtlas2RouteGroups()
        => Atlas2Defaults.Categories.Select(c => new AtlasRouteGroupSettings
        {
            Name = c.Name,
            BuiltInKey = c.BuiltInKey,
            Locked = true,
            DrawPaths = c.DrawPath,
            LineThickness = 6f,
            Color = c.Color,
            BackgroundColor = c.BackgroundColor,
            ContentRule = c.ContentRule ?? "",
            MaxHops = c.MaxHops,
            Entries = c.Targets.Select(t => new AtlasRouteEntrySettings
            {
                Name = t.Name,
                Match = string.IsNullOrEmpty(c.ContentRule) ? $"name:{t.Name}" : $"content:{c.ContentRule}",
                Color = c.Color,
                MaxHops = c.MaxHops,
                DrawPath = t.Enabled,
            }).ToList(),
        }).ToList();

    private void ReconcileAtlas2RoutePresentation()
    {
        foreach (var seed in Atlas2Defaults.Categories)
        {
            var group = AtlasRouteGroups.FirstOrDefault(g =>
                string.Equals(g.BuiltInKey, seed.BuiltInKey, StringComparison.OrdinalIgnoreCase));
            if (group is null) continue;

            group.DrawPaths = seed.DrawPath;
            group.LineThickness = 6f;
            group.MaxHops = seed.MaxHops;

            foreach (var target in seed.Targets)
            {
                var entry = group.Entries.FirstOrDefault(e =>
                    string.Equals(e.Name, target.Name, StringComparison.OrdinalIgnoreCase));
                if (entry is not null)
                    entry.DrawPath = target.Enabled;
            }
        }
    }

    private void ReconcileBuiltInAtlasRouteTargets()
    {
        // Pre-Atlas2 locked "Map Targets" group from catalog route targets (legacy configs only).
        var seeded = new AtlasRouteGroupSettings
        {
            Name = "Map Targets",
            Locked = true,
            DrawPaths = true,
            LineThickness = 1.5f,
            Entries = AtlasCatalog.Shared.DefaultRouteTargets
                .Select(t => new AtlasRouteEntrySettings
                {
                    Name = t.Name,
                    Match = t.Match,
                    Color = t.Color,
                    MaxHops = t.MaxHops,
                    DrawPath = t.Enabled,
                })
                .ToList(),
        };
        var existing = AtlasRouteGroups.FirstOrDefault(g => g.Locked)
            ?? AtlasRouteGroups.FirstOrDefault(g => string.Equals(g.Name, seeded.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            AtlasRouteGroups.Insert(0, seeded);
            return;
        }

        existing.Name = seeded.Name;
        existing.Locked = true;
        existing.DrawPaths = true;
        existing.LineThickness = 1.5f;

        var previous = existing.Entries
            .GroupBy(e => e.Match, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        existing.Entries = seeded.Entries.Select(seed =>
        {
            if (!previous.TryGetValue(seed.Match, out var old)) return seed;
            seed.DrawPath = old.DrawPath;
            seed.Color = string.IsNullOrWhiteSpace(old.Color) ? seed.Color : old.Color;
            seed.MaxHops = old.MaxHops;
            return seed;
        }).ToList();
    }

    /// <summary>Persist current settings to disk. Never throws on IO error — logs and continues.</summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Settings save failed: {ex.Message}");
        }
    }
}

/// <summary>Campaign helper behavior and compact widget presentation.</summary>
public enum CampaignGuideMode
{
    Required,
    FullClear,
}

public sealed class CampaignSettings
{
    public bool Enabled { get; set; } = true;
    public bool AutoActivate { get; set; } = true;
    public bool AutoRoute { get; set; } = true;
    public bool SafeAutoCheck { get; set; } = true;
    public CampaignGuideMode GuideMode { get; set; } = CampaignGuideMode.FullClear;
    public bool ShowCompletedObjectives { get; set; } = true;
    public bool WidgetCollapsed { get; set; }
    public float WidgetX { get; set; } = -1f;
    public float WidgetY { get; set; } = -1f;
    public float WidgetScale { get; set; } = 1f;
    public float WidgetOpacity { get; set; } = 0.94f;
    public bool ShowDiagnosticTargetStatus { get; set; } = true;
}

public sealed class AtlasMapGroupSettings
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#1f2937";
    public string FontColor { get; set; } = "#ffffff";
    public bool Enabled { get; set; } = true;
    public List<string> Maps { get; set; } = new();
}

public sealed class RitualSettings
{
    public int PriceSource { get; set; } = 1; // 0 = poe.ninja, 1 = poe2scout
    public string League { get; set; } = "Runes of Aldur";
    public int RefreshIntervalMin { get; set; } = 5;
    public int DisplayCurrency { get; set; } = 1; // 0 = Divine, 1 = Exalted, 2 = Chaos
    public bool ShowOverlay { get; set; } = true;
    /// <summary>ImGui side window listing ritual shop rewards with names and prices.</summary>
    public bool ShowPricesWindow { get; set; } = true;
    public bool PlayValueAlert { get; set; } = true;
    public float AlertMinDivine { get; set; } = 1f;
    public int AlertSound { get; set; } = 0;
    public bool DebugMode { get; set; } = false;
    public bool DiagnosePricing { get; set; } = false;
    public bool ForceBfsFallback { get; set; } = false;
    public float PriceFontScale { get; set; } = 1.025f;
    public float PriceOffsetX { get; set; } = 5f;
    public float PriceOffsetY { get; set; } = -5f;
    public string PriceTextColor { get; set; } = "#FFEB8C";
    public float MinDisplayExalted { get; set; } = 50f;
}

public sealed class AmanamuSettings
{
    /// <summary>Master gate. Off performs no Amanamu component/mod/buff reads.</summary>
    public bool Enabled { get; set; } = true;
    public bool OnlyRareOrUnique { get; set; } = true;
    public bool ShowWorldOverlay { get; set; } = true;
    public bool ShowMapMarkers { get; set; } = true;
    public bool DrawLabels { get; set; } = true;
    public bool DrawOffscreenArrows { get; set; } = true;
    public bool DrawCircle { get; set; } = true;
    /// <summary>Maximum grid-cell distance to inspect. 0 = unlimited.</summary>
    public int MaxDistanceGrid { get; set; } = 368;
    public float CircleRadius { get; set; } = 36f;
    public float LabelYOffset { get; set; } = 70f;
    public float ArrowEdgeMargin { get; set; } = 60f;
    public string InsideCloudColor { get; set; } = "#B450FF";
    public string OutsideCloudColor { get; set; } = "#50FF78";
}

public sealed class RunecraftSettings
{
    public bool ShowOverlay { get; set; } = true;
    public int PriceSource { get; set; } = 0;
    public string League { get; set; } = "Runes of Aldur";
    public int RefreshIntervalMin { get; set; } = 60;
    public int ColorMode { get; set; } = 1; // 0 off, 1 relative, 2 absolute
    public float OverlayXOffset { get; set; } = 0f;
    public bool ShowMapLabels { get; set; } = false;
    public float MapLabelMinExalted { get; set; } = 0f;
    public bool HideMapValueWhenPanelOpen { get; set; } = true;
    public bool ShowMonolithWindow { get; set; } = false;
    public bool ShowMonolithDebugWindow { get; set; } = false;
    /// <summary>When off, still open the monolith window while a gamepad is connected.</summary>
    public bool AutoShowMonolithWithGamepad { get; set; } = false;
    public float MonolithRewardsMinExalted { get; set; } = 0f;
    public float MonolithHighlightThreshold { get; set; } = 0f;
    public float MapValueScaleMultiplier { get; set; } = 1f;
    public float MapValueXOffset { get; set; } = 0f;
    public float MapValueYOffset { get; set; } = 0f;
    public bool HighlightBestRecipe { get; set; } = true;
    public bool HighlightLockedRecipe { get; set; } = true;
    public bool DiagnosePricing { get; set; } = false;

    // Expedition placement planner. It is guidance-only and reads encounter state directly, so it
    // does not need a mouse cursor or a particular keyboard/controller HUD branch.
    public bool ShowExpeditionPlanner { get; set; } = true;
    public bool ShowExpeditionRouteOnMap { get; set; } = true;
    public bool ShowExpeditionNextPlacementWorld { get; set; } = true;
    public int ExpeditionManualCharges { get; set; } = 5;
    public float ExpeditionMonolithMinExalted { get; set; } = 0f;
    public int ExpeditionMinMarkersPerSpareCharge { get; set; } = 2;
    public float ExpeditionTinyMarkerWeight { get; set; } = 0f;
    public float ExpeditionWhiteMarkerWeight { get; set; } = 10f;
    public float ExpeditionMagicMarkerWeight { get; set; } = 30f;
    public float ExpeditionGoldMarkerWeight { get; set; } = 60f;
    public float ExpeditionLogbookMarkerWeight { get; set; } = 100f;
    public Dictionary<string, float> ExpeditionRewardWeights { get; set; } = new(StringComparer.Ordinal);
    public float ExpeditionPreferredRelicWeight { get; set; } = 40f;
    public float ExpeditionDangerousRelicPenalty { get; set; } = 100f;
    public List<string> ExpeditionPreferredRelicMods { get; set; } =
    [
        "ExpeditionRelicUpsideItemQuantityChest",
        "ExpeditionRelicUpsideItemQuantityMonster",
        "ExpeditionRelicUpsideIncreasedArtifactsChest",
        "ExpeditionRelicUpsideIncreasedArtifactsMonster",
        "ExpeditionRelicUpsideExpeditionLogbookQuantityMonster",
        "ExpeditionRelicUpsidePackSize",
        "ExpeditionRelicUpsideRareMonsterChance",
        "ExpeditionRelicUpsideElitesDuplicated",
    ];
    public List<string> ExpeditionDangerousRelicMods { get; set; } = [];
}

public sealed class SekhemaSettings
{
    public bool Enabled { get; set; } = true;
    public string CurrentProfile { get; set; } = "Default";
    public Dictionary<string, SekhemaProfileSettings> Profiles { get; set; } = new()
    {
        ["Default"] = SekhemaProfileSettings.CreateDefault(),
        ["No-Hit"] = SekhemaProfileSettings.CreateNoHit(),
    };

    public bool DrawBestPath { get; set; } = true;
    public bool Debug { get; set; }
    public string BestPathColor { get; set; } = "#33FF33";
    public string DebugTextColor { get; set; } = "#FFFFFF";
    public string DebugBackgroundColor { get; set; } = "#000000";
    public float FrameThickness { get; set; } = 4f;

    public bool SuppressMerchantLowWater { get; set; } = true;
    public int MerchantWaterThreshold { get; set; } = 250;
    public bool SuppressHonourRestoreHighPct { get; set; } = true;
    public int HonourRestoreThresholdPct { get; set; } = 80;

    public bool DrawHazardRoute { get; set; } = true;
    public bool HazardWalkableRoute { get; set; } = true;
    public string HazardRouteColor { get; set; } = "#FFD933";
    public string HazardMarkerColor { get; set; } = "#FF4D4D";
    public float HazardRouteThickness { get; set; } = 1.5f;
    public float HazardMarkerRadius { get; set; } = 9f;
    public int HazardIdGroupGap { get; set; } = 10;
    public float HazardRoomMargin { get; set; } = 30f;
    public string HazardDebugCrystalIds { get; set; } = "";
    public bool HazardDebugDrawWalkable { get; set; }
    public float HazardDebugWalkableRadius { get; set; } = 150f;

    public bool DrawChestPriority { get; set; } = true;
    public string ChestMarkerColor { get; set; } = "#4DFF73";
    public float ChestMarkerRadius { get; set; } = 6f;
    public List<string> ChestPriorityOrder { get; set; } =
    [
        "GrandSpectrum", "RadiusJewels", "LargeRelic", "Jewels", "Currency",
        "MediumRelic", "SmallRelic", "Maps", "Generic",
    ];
    public HashSet<string> ChestDisabledContent { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Currency", "MediumRelic", "SmallRelic", "Maps", "Generic",
    };

    public bool ShowPortals { get; set; } = true;
    public bool ShowLevers { get; set; } = true;
    public string PortalColor { get; set; } = "#FF4D4D";
    public string LeverColor { get; set; } = "#4DB3FF";
    public float RoomObjectMarkerRadius { get; set; } = 8f;
}

public sealed class SekhemaProfileSettings
{
    public Dictionary<string, float> RoomTypeWeights { get; set; } = new();
    public Dictionary<string, float> AfflictionWeights { get; set; } = new();
    public Dictionary<string, float> RewardWeights { get; set; } = new();

    public static SekhemaProfileSettings CreateDefault() => CreateBase();

    public static SekhemaProfileSettings CreateNoHit()
    {
        var profile = CreateBase();
        profile.RoomTypeWeights["Gauntlet"] = -200;
        profile.RoomTypeWeights["Hourglass"] = -1000;
        profile.AfflictionWeights["Death Toll"] = -500000;
        profile.AfflictionWeights["Spiked Exit"] = -600000;
        profile.AfflictionWeights["Deceptive Mirror"] = -400000;
        profile.AfflictionWeights["Glass Shard"] = -50000;
        profile.AfflictionWeights["Myriad Aspersions"] = -50000;
        foreach (var name in new[]
                 {
                     "Ghastly Scythe", "Deadly Snare", "Branded Balbalakh", "Chiselled Stone",
                     "Weakened Flesh", "Costly Aid", "Suspected Sympathiser", "Haemorrhage",
                     "Leaking Waterskin", "Rusted Mallet", "Chains of Binding", "Dishonoured Tattoo",
                     "Dark Pit", "Honed Claws", "Hungry Fangs",
                 })
            profile.AfflictionWeights[name] = 0;
        return profile;
    }

    private static SekhemaProfileSettings CreateBase() => new()
    {
        RoomTypeWeights = new Dictionary<string, float>
        {
            ["Gauntlet"] = -1000, ["Hourglass"] = -200, ["Chalice"] = 0,
            ["Ritual"] = 0, ["Escape"] = 100, ["Boss"] = 0,
        },
        AfflictionWeights = new Dictionary<string, float>
        {
            ["Orbala's Leathers"] = 0,
            ["Glass Shard"] = -4000, ["Ghastly Scythe"] = -4000, ["Veiled Sight"] = -4000,
            ["Myriad Aspersions"] = -4000, ["Deceptive Mirror"] = -4000, ["Purple Smoke"] = -4000,
            ["Golden Smoke"] = -400, ["Red Smoke"] = -4000, ["Black Smoke"] = -4000,
            ["Rapid Quicksand"] = -1000, ["Deadly Snare"] = -1000, ["Forgotten Traditions"] = -1000,
            ["Season of Famine"] = -1000, ["Orb of Negation"] = -1000, ["Winter Drought"] = -1000,
            ["Branded Balbalakh"] = -1000, ["Chiselled Stone"] = -1000, ["Weakened Flesh"] = -100,
            ["Untouchable"] = -1000, ["Costly Aid"] = -900, ["Blunt Sword"] = -1000,
            ["Spiked Shell"] = -1000, ["Suspected Sympathiser"] = -200, ["Haemorrhage"] = -100,
            ["Corrosive Concoction"] = 0, ["Iron Manacles"] = 0, ["Shattered Shield"] = 0,
            ["Unquenched Thirst"] = -200, ["Unassuming Brick"] = -1000, ["Tradition's Demand"] = -800,
            ["Fiendish Wings"] = -400, ["Hungry Fangs"] = -600, ["Worn Sandals"] = -400,
            ["Trade Tariff"] = -300, ["Death Toll"] = -400, ["Spiked Exit"] = -300,
            ["Exhausted Wells"] = 0, ["Gate Toll"] = -100, ["Leaking Waterskin"] = -100,
            ["Low Rivers"] = -100, ["Sharpened Arrowhead"] = 0, ["Rusted Mallet"] = 0,
            ["Chains of Binding"] = 0, ["Dishonoured Tattoo"] = 0, ["Tattered Blindfold"] = 0,
            ["Dark Pit"] = 0, ["Honed Claws"] = 0,
        },
        RewardWeights = new Dictionary<string, float>
        {
            ["Gold Key"] = 0, ["Silver Key"] = 0, ["Bronze Key"] = 0,
            ["Golden Cache"] = 0, ["Silver Cache"] = 0, ["Bronze Cache"] = 0,
            ["Large Fountain"] = 100, ["Fountain"] = 50, ["Pledge to Kochai"] = 20,
            ["Honour Halani"] = 8, ["Honour Ahkeli"] = -1, ["Honour Orbala"] = 50,
            ["Honour Galai"] = 300, ["Honour Tabana"] = 0, ["Merchant"] = 20,
            ["Honour"] = 50, ["Boon"] = 200, ["Curse"] = 0, ["Random"] = 0,
        },
    };
}

public sealed class StashValueSettings
{
    public bool ShowOverlay { get; set; } = true;
    public bool ShowInventoryOverlay { get; set; } = false;
    public bool ShowDebugInfo { get; set; } = false;
    public float MinValueEx { get; set; } = 0f;
    public bool HidePriceOnHover { get; set; } = true;
    public int PriceSource { get; set; } = 1;
    public string League { get; set; } = "Runes of Aldur";
    public int RefreshIntervalMin { get; set; } = 5;
    public int DisplayCurrency { get; set; } = 1;
    public float PriceFontScale { get; set; } = 1f;
    public float PriceOffsetX { get; set; } = 5f;
    public float PriceOffsetY { get; set; } = -5f;
    public string PriceTextColor { get; set; } = "#FFEB8C";
}

public sealed class StashUtilitySettings
{
    public bool EnableWaystones { get; set; } = true;
    public bool EnableTablets { get; set; } = true;
    public bool ShowWaystoneTiersOnHover { get; set; } = true;
    public bool ShowTabletTiersOnHover { get; set; } = true;
    public bool IncludeStash { get; set; } = true;
    public bool IncludeInventory { get; set; } = true;
    public bool HideNormalWaystones { get; set; }

    public int MinTier { get; set; } = 1;
    public bool FilterMaxRevives { get; set; }
    public int MaxRevives { get; set; }
    public bool FilterMinItemRarity { get; set; }
    public int MinItemRarity { get; set; }
    public bool FilterMinPackSize { get; set; }
    public int MinPackSize { get; set; }
    public bool FilterMinMonsterRarity { get; set; }
    public int MinMonsterRarity { get; set; }
    public bool FilterMinMonsterEffectiveness { get; set; }
    public int MinMonsterEffectiveness { get; set; }
    public bool FilterMinDropChance { get; set; }
    public int MinDropChance { get; set; }
    public bool FilterMinExplicitMods { get; set; }
    public int MinExplicitMods { get; set; }
    public bool FilterMaxExplicitMods { get; set; }
    public int MaxExplicitMods { get; set; } = 10;

    public bool GreatByItemRarity { get; set; }
    public int GreatItemRarity { get; set; } = 30;
    public bool GreatByPackSize { get; set; }
    public int GreatPackSize { get; set; } = 20;
    public bool GreatByDropChance { get; set; }
    public int GreatDropChance { get; set; } = 120;
    public bool GreatByExplicitMods { get; set; }
    public int GreatExplicitMods { get; set; } = 5;

    public bool RequireAllGoodWaystoneMods { get; set; }
    public bool RequireAllGreatWaystoneMods { get; set; }
    public bool BadOnlyWhenNumericalFiltersPass { get; set; }
    public bool RedTakesPriority { get; set; } = true;
    // "Good" is retained in serialized property names for compatibility; the UI presents it as Required.
    public List<string> GoodWaystoneMods { get; set; } = new();
    public List<string> GreatWaystoneMods { get; set; } = new();
    public List<string> BadWaystoneMods { get; set; } = new();

    public bool RequireAllGoodTabletMods { get; set; }
    public bool RequireAllGreatTabletMods { get; set; }
    public bool BadTabletOnlyWhenOtherRulesPass { get; set; }
    public bool HideBadTablets { get; set; }
    // "Good"/"God" are retained in serialized property names for compatibility; the UI uses Required/GREAT.
    public List<string> GoodTabletMods { get; set; } = new();
    public List<string> BadTabletMods { get; set; } = new();
    public List<string> GodTabletMods { get; set; } = new();
    public Dictionary<string, float> TabletMinimumRolls { get; set; } = new();

    public float BorderThickness { get; set; } = 3f;
    public float BorderMargin { get; set; } = 4f;
    public int GoodBorderStyle { get; set; }
    public int BadBorderStyle { get; set; }
    public bool ShowRarityCorner { get; set; } = true;
    public float RarityCornerSize { get; set; } = 10f;
    public bool ShowGreatArrow { get; set; } = true;
    public float GreatArrowSize { get; set; } = 20f;
    public int GreatArrowCorner { get; set; }
    public string WaystoneGoodColor { get; set; } = "#00D9FF";
    public string WaystoneBadColor { get; set; } = "#FF4000";
    public string WaystoneGreatColor { get; set; } = "#0AD407";
    public string TabletGoodColor { get; set; } = "#D933FF";
    public string TabletBadColor { get; set; } = "#BF0026";
    public string TabletGreatColor { get; set; } = "#FFC800";
}

public sealed class WaystoneAlchemySettings
{
    public bool Enabled { get; set; }
    /// <summary>0 = guided/manual, 1 = automatic clicks.</summary>
    public int Mode { get; set; }
    /// <summary>0 = Waystones, 1 = Tablets.</summary>
    public int TargetType { get; set; }
    /// <summary>
    /// Waystones: 0 = upgrade, 1 = corrupt, 2 = Distilled Paranoia guidance.
    /// Tablets: 0 = upgrade (Transmute/Augment/Regal/Exalt), 1 = Ancient Infuser, 2 = Alchemy (needs Partial Translation).
    /// </summary>
    public int Recipe { get; set; }
    public int RunHotkey { get; set; }
    public int EmergencyStopHotkey { get; set; } = 0x77; // F8
    public int MinimumTier { get; set; } = 1;
    /// <summary>
    /// When true, Magic Waystones use Regal (keep mods). When false (default), Magic Waystones use
    /// Alchemy like Normal ones — PoE2 Alchemy works on Normal and Magic.
    /// </summary>
    public bool UseRegalOnMagic { get; set; }
    public bool ApplyExaltedToRare { get; set; } = true;
    public int DesiredExplicitMods { get; set; } = 6;
    /// <summary>2 is always available; 3-4 require the corresponding Tablet Atlas unlocks.</summary>
    public int DesiredTabletExplicitMods { get; set; } = 2;
    public int ActionDelayMs { get; set; } = 350;
    public bool AutoModeAcknowledged { get; set; }
    /// <summary>
    /// Confirms Partial Translation (4-mod tablet unlock) so Orb of Alchemy may be used on Normal tablets.
    /// </summary>
    public bool TabletAlchemyUnlocked { get; set; }
}

public sealed class PickupHelperSettings
{
    public bool Enabled { get; set; }
    /// <summary>Uses the bounded low-latency pickup cadence while preserving all safety gates.</summary>
    public bool HumanSpeed { get; set; } = true;
    /// <summary>0 = assist, 1 = hovered/held, 2 = nearby/held, 3 = nearby automatic toggle.</summary>
    public int Mode { get; set; }
    /// <summary>Keyboard VK or encoded XInput button. The action only runs while this binding is held.</summary>
    public int ActivationHotkey { get; set; }
    public int EmergencyStopHotkey { get; set; } = 0x77; // F8
    public int MaxPickupDistance { get; set; } = 45;
    public int MinPickupDelayMs { get; set; } = 35;
    public int MaxPickupDelayMs { get; set; } = 110;
    public int ClickCooldownMs { get; set; } = 180;
    public int ConfirmationTimeoutMs { get; set; } = 1600;
    public int MissRetryDelayMs { get; set; } = 300;
    public int MaxMissesBeforeCooldown { get; set; } = 3;
    public int MissedItemCooldownMs { get; set; } = 2000;
    public bool AutoModeAcknowledged { get; set; }
    public bool ShowTargetHighlight { get; set; } = true;
    public bool PauseWhileShowHiddenHeld { get; set; } = true;
    public int ShowHiddenItemsHotkey { get; set; } = 0x12; // Alt by default
    public PickupPolicySettings Policy { get; set; } = new();
}

public sealed class PickupPolicySettings
{
    /// <summary>Off by default: weapons, armour, jewellery, flasks, and charms remain excluded.</summary>
    public bool AllowEquipment { get; set; }
    /// <summary>Optional comma/newline-separated name or metadata fragments. Empty allows all non-gear.</summary>
    public string AllowPatterns { get; set; } = "";
    /// <summary>Comma/newline-separated name or metadata fragments. Deny always wins.</summary>
    public string DenyPatterns { get; set; } = "";
    /// <summary>Ordered comma/newline-separated fragments; earlier matches are selected first.</summary>
    public string PriorityPatterns { get; set; } = "";
}

public sealed class LootTrackerSettings
{
    public const int CurrencyAuto = 0;
    public const int CurrencyDivine = 1;
    public const int CurrencyExalted = 2;
    public const int CurrencyChaos = 3;

    public bool Enabled { get; set; } = true;
    public bool KeepVisibleAfterRun { get; set; } = true;
    public int PriceSource { get; set; } = 1; // 0 = poe.ninja, 1 = poe2scout
    public string League { get; set; } = "Runes of Aldur";
    public int RefreshIntervalMin { get; set; } = 5;
    public int HistorySize { get; set; } = 50;
    public int MaxSessions { get; set; } = 30;
    public float BarBottomOffset { get; set; } = 5f;
    public bool BarOnRight { get; set; } = true;
    public float BarOpacity { get; set; } = 0.55f;
    public bool ShowKills { get; set; } = true;
    public float CompactHeight { get; set; } = 115f;
    public float CompactWidth { get; set; } = 730f;
    public float UiScale { get; set; } = 1.2f;
    public bool ShowPickupToasts { get; set; } = false;
    public float NotifyMinEx { get; set; } = 20f;
    public float NotifyDurationSec { get; set; } = 2.5f;
    public bool ShowPricesInDivineOnly { get; set; } = false;
    public int DisplayCurrency { get; set; } = CurrencyExalted;
    public int DetailsHotkey { get; set; }
}

public sealed class AtlasRouteGroupSettings
{
    public string Name { get; set; } = "";
    public string BuiltInKey { get; set; } = "";
    public string Color { get; set; } = "#58A6FF";
    public string BackgroundColor { get; set; } = "#000000D9";
    public string ContentRule { get; set; } = "";
    public int MaxHops { get; set; } = 100;
    public bool DrawPaths { get; set; }
    public bool Locked { get; set; }
    public float LineThickness { get; set; } = 1.5f;
    public List<AtlasRouteEntrySettings> Entries { get; set; } = new();
}

public sealed class AtlasRouteEntrySettings
{
    public string Name { get; set; } = "";
    public string Match { get; set; } = "";
    public string Color { get; set; } = "#58A6FF";
    public bool DrawPath { get; set; }
    public int MaxHops { get; set; } = 25;
}

public sealed class AtlasContentGroupSettings
{
    public string Name { get; set; } = "";
    public bool DrawPaths { get; set; } = true;
    public float LineThickness { get; set; } = 1f;
    public List<AtlasContentRouteEntrySettings> Contents { get; set; } = new();
}

public sealed class AtlasContentRouteEntrySettings
{
    public string ContentKey { get; set; } = "";
    public string Color { get; set; } = "#FFD933";
    public bool DrawPath { get; set; } = true;
    public int MaxHops { get; set; } = 0;
}

/// <summary>
/// A single drawable radar icon: shape, RGB color, opacity, pixel size, and an enable toggle.
/// <see cref="Shape"/> is one of Circle/Triangle/Star/Diamond/Plus/Square (anything else falls back
/// to Circle when rendered); <see cref="Color"/> is <c>#RRGGBB</c>; <see cref="Opacity"/> is 0..1.
/// </summary>
public sealed class SpriteIconRef
{
    public string Sheet { get; set; } = "icons.png";
    public int Col { get; set; }
    public int Row { get; set; }
    public int CellSize { get; set; } = 64;
    public float Scale { get; set; } = 1f;

    public SpriteIconRef Clone() => new()
    {
        Sheet = Sheet,
        Col = Col,
        Row = Row,
        CellSize = CellSize,
        Scale = Scale,
    };

    public static SpriteIconRef Cell(int col, int row, float scale = 1f, int cellSize = 64) => new()
    {
        Col = col,
        Row = row,
        Scale = scale,
        CellSize = cellSize,
    };
}

public sealed class IconStyle
{
    public bool Enabled { get; set; } = true;
    public string Shape { get; set; } = "Circle";
    public string Color { get; set; } = "#FFFFFF";
    public float Opacity { get; set; } = 1.0f;
    public float Size { get; set; } = 3.0f;
    public SpriteIconRef? Sprite { get; set; }

    public IconStyle() { }
    public IconStyle(string shape, string color, float opacity, float size, SpriteIconRef? sprite = null)
    {
        Shape = shape; Color = color; Opacity = opacity; Size = size; Sprite = sprite;
    }
}

/// <summary>
/// A user-defined "mechanic" highlight: when an entity's metadata contains ANY of <see cref="Match"/>
/// (case-insensitive) AND its category is in <see cref="Categories"/> (if any are listed), it draws
/// this icon instead of its generic category dot — so e.g. an Expedition marker or a Strongbox stands
/// out. The first enabled matching rule wins.
/// </summary>
public sealed class MechanicStyle
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public List<string> Match { get; set; } = new();
    /// <summary>Entity-category gate by <c>Poe2Live.EntityCategory</c> name (e.g. "Monster", "Chest",
    /// "Other"). A rule applies only to these categories; empty = all categories. This stops a broad
    /// match term (e.g. "Expedition") from hijacking the wrong entities — the league POI marker
    /// (category Other) vs. the monsters that spawn during the event (category Monster).</summary>
    public List<string> Categories { get; set; } = new();
    public string Shape { get; set; } = "Star";
    public string Color { get; set; } = "#FFFFFF";
    public float Opacity { get; set; } = 1.0f;
    public float Size { get; set; } = 6.0f;
    public SpriteIconRef? Sprite { get; set; }
}

/// <summary>
/// Monster HP-bar geometry. Width, border thickness, and border color are per-rarity; height + X/Y
/// offset are shared. The per-rarity enable flags live on <see cref="RadarSettings"/>
/// (HpBarNormal/Magic/Rare/Unique). The bar *fill* color is taken from the matching monster icon
/// color (so "rare = gold" stays one setting); the border is configured independently below. Border
/// defaults reproduce the old weight-by-rarity cue (Normal undecorated, Magic 1px, Rare/Unique 2px)
/// with borders tinted to match each rarity's icon color.
/// </summary>
public sealed class HpBarSettings
{
    public bool UseTextures { get; set; } = true;
    public float Height { get; set; } = 5f;
    public float OffsetX { get; set; } = 0f;
    public float OffsetY { get; set; } = -30f; // px relative to the mob's screen position (neg = up)
    public float WidthNormal { get; set; } = 30f;
    public float WidthMagic { get; set; } = 38f;
    public float WidthRare { get; set; } = 50f;
    public float WidthUnique { get; set; } = 64f;
    // Border thickness in px (0 = no border).
    public float BorderNormal { get; set; } = 0f;
    public float BorderMagic { get; set; } = 1f;
    public float BorderRare { get; set; } = 2f;
    public float BorderUnique { get; set; } = 2f;
    // Border color (#RRGGBB); defaults mirror the per-rarity monster icon colors.
    public string BorderColorNormal { get; set; } = "#FF3333";
    public string BorderColorMagic { get; set; } = "#73A6FF";
    public string BorderColorRare { get; set; } = "#FFD926";
    public string BorderColorUnique { get; set; } = "#FF7300";
    public string EnergyShieldColor { get; set; } = "#73E6FF";
}

/// <summary>
/// Walkable-terrain bitmap styling: the interior "wash" over walkable cells and the brighter
/// outline drawn on walkable cells bordering a wall/edge. Color is <c>#RRGGBB</c>; opacity is 0..1
/// (baked into the per-pixel alpha). Defaults reproduce the formerly hardcoded look exactly:
/// interior <c>#506482</c> @ ~30/255, edge <c>#3CDCFF</c> @ ~180/255. The per-area terrain bitmap
/// is rebuilt when any of these change.
/// </summary>
public sealed class TerrainSettings
{
    public string InteriorColor { get; set; } = "#506482";
    public float InteriorOpacity { get; set; } = 0.118f; // → 30/255
    public string EdgeColor { get; set; } = "#3CDCFF";
    public float EdgeOpacity { get; set; } = 0.706f;      // → 180/255
    // ImGuiDx backend only: higher detail samples more terrain border cells (smoother, heavier).
    public int ImGuiEdgeDetail { get; set; } = 8;
    // ImGuiDx backend only: radius of each anti-aliased terrain edge point.
    public float ImGuiEdgeThickness { get; set; } = 1.8f;
}

/// <summary>
/// The full radar icon style table. Every default mirrors the formerly hardcoded values,
/// so a missing/partial config renders identically to before.
/// </summary>
public sealed class RadarStyles
{
    // Monster dots by rarity.
    public IconStyle MonsterNormal { get; set; } = new("Circle",   "#FF3333", 0.95f, 9.0f, SpriteIconRef.Cell(0, 14, 1.25f));
    public IconStyle MonsterMagic  { get; set; } = new("Diamond",  "#73A6FF", 0.97f, 9.0f, SpriteIconRef.Cell(4, 57, 1.25f));
    public IconStyle MonsterRare   { get; set; } = new("Triangle", "#FFD926", 1.00f, 10.0f, SpriteIconRef.Cell(4, 57, 1.25f));
    public IconStyle MonsterUnique { get; set; } = new("Star",     "#FF7300", 1.00f, 12.0f, SpriteIconRef.Cell(6, 57, 1.25f));

    // Other entity categories.
    public IconStyle Player        { get; set; } = new("Circle",  "#4DF2FF", 1.00f, 9.0f, SpriteIconRef.Cell(2, 0, 1.25f));
    public IconStyle Npc           { get; set; } = new("Plus",    "#FFD933", 0.95f, 9.0f, SpriteIconRef.Cell(3, 0, 1.25f));
    public IconStyle ChestRare     { get; set; } = new("Triangle", "#FFFF77", 1.00f, 10.0f, SpriteIconRef.Cell(4, 57, 1.25f));
    public IconStyle ChestUnique   { get; set; } = new("Square",  "#FF7300", 0.95f, 10.0f, SpriteIconRef.Cell(8, 38, 1.25f));
    public IconStyle Transition    { get; set; } = new("Diamond", "#66FF99", 0.95f, 10.0f, SpriteIconRef.Cell(1, 37, 1.25f));
    public IconStyle Poi           { get; set; } = new("Circle",  "#8CBFFF", 0.70f, 10.0f, SpriteIconRef.Cell(12, 44, 1.25f));

    // Tile landmarks (shape marker + text label at the group centroid).
    public IconStyle Landmark      { get; set; } = new("Diamond", "#F259F2", 1.00f, 10.0f, SpriteIconRef.Cell(1, 37, 1.25f));

    // Server-authoritative minimap icons (waypoints, entrances, party members, league mechanics).
    public IconStyle ServerIcon    { get; set; } = new("Star",    "#FFD700", 0.95f, 10.0f, SpriteIconRef.Cell(6, 57, 1.25f));

    // Metadata-matched overrides — seeded from <see cref="EndgameMechanicCatalog"/>.
    public List<MechanicStyle> Mechanics { get; set; } = EndgameMechanicCatalog.DefaultMechanicStyles();
}
