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
    /// <summary>Legacy master flag — read on first migrate only. Rendering uses per-layer toggles below.</summary>
    public bool ShowPath { get; set; } = false;
    public bool PathTogglesMigrated { get; set; }
    public bool ShowPathWorld { get; set; } = false;
    public bool ShowPathMap { get; set; } = true;
    /// <summary>When auto-path is on, draw routes to curated terrain POIs from CustomLandmarks.json.</summary>
    public bool ShowCuratedPaths { get; set; } = true;
    /// <summary>When auto-path is on, draw routes to entities whose display rule is Navigable.</summary>
    public bool ShowEntityPaths { get; set; } = true;
    /// <summary>Stop drawing (and replanning) a path once the player walks within <see cref="ReachedPathDistance"/> grid units.</summary>
    public bool HideReachedPaths { get; set; } = true;
    /// <summary>Grid-distance threshold for <see cref="HideReachedPaths"/>.</summary>
    public float ReachedPathDistance { get; set; } = 50f;
    /// <summary>True when any path draw layer is enabled (API backward compat for legacy <c>showPath</c>).</summary>
    [JsonIgnore]
    public bool ShowPathAnyLayer => ShowPathWorld || ShowPathMap;
    /// <summary>Ground-screen breadcrumbs when the Tab map is closed (sub-toggle of <see cref="ShowPathWorld"/>).</summary>
    public bool ShowGroundWaypoints { get; set; } = true;
    public bool UseCuratedLandmarks { get; set; } = true;
    public bool DrawAllLandmarkPaths { get; set; } = false;

    // ── Landmark clustering. A reusable tile (e.g. a "stairs up" wall piece) recurs in several
    //    disjoint spots — a multi-level dungeon has several stair-up/stair-down sections — so the
    //    scanner groups a tile path's cells into spatial clusters and emits one marker per cluster.
    //    This is the MAX GAP (in TILES; 1 tile ≈ 23 grid units) between cells still considered the
    //    same cluster: larger = merges nearby spots (fewer markers, less map spam), smaller = splits
    //    them (more markers). 0 disables bridging (only directly-touching tiles group). ──
    public int LandmarkClusterGap { get; set; } = 2;

    // ── Entity gather/draw radius (grid cells from player). 0 = unlimited (game network bubble only). ──
    public int EntityDrawRadiusGrid { get; set; } = 0;

    // ── Live auto-path: F3 fills entity-mechanic slots; curated terrain paths follow ShowCuratedPaths. ──
    public bool AutoPathNavigable { get; set; } = false;

    // ── In-game hotkey to toggle <see cref="AutoPathNavigable"/> (default F3 = 0x72). 0 = disabled. ──
    public int AutoPathToggleHotkey { get; set; } = 0x72;

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

    // One-time: tighten Navigable defaults to GameHelper-style conservative auto-path qualification.
    public bool ConservativeNavDefaultsMigrated { get; set; } = false;

    // One-time: fresh installs default ShowPathWorld=false; existing configs are not changed.
    public bool PathGroundDefaultMigrated { get; set; } = false;

    // One-time: expand ground-item category defaults (poe.ninja group keys).
    public bool GroundItemCategoriesMigrated { get; set; } = false;

    // ── Ground loot + league reward pricing (poe.ninja). ──
    public GroundItemSettings GroundItems { get; set; } = new();
    public MonolithSettings Monoliths { get; set; } = new();

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

    // ── Navigation-menu widget: which screen corner it is pinned to.
    //    One of "TopLeft", "TopRight", "BottomLeft", "BottomRight". ──
    public string NavMenuCorner { get; set; } = "TopLeft";
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
    public float LargeMapScaleMultiplier { get; set; } = 0.1738f;
    public float ScaleMul { get; set; } = 1.0f;
    public float OffX { get; set; } = 0f;
    public float OffY { get; set; } = 0f;

    // Draw the overlay even when PoE2 isn't the foreground window (e.g. while tweaking the dashboard).
    // Auto-flask stays foreground-gated regardless (safety). Default off (overlay hides when unfocused).
    public bool AlwaysShowOverlay { get; set; } = false;

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
    // Fogged/hidden nodes drawn at full opacity with a distinct tint instead of near-invisible.
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
    public bool AtlasHideCompletedMaps { get; set; } = false;
    public bool AtlasHideNotAccessibleMaps { get; set; } = false;
    public bool AtlasHideAvailableMaps { get; set; } = false;
    public bool AtlasShowBiomeBorders { get; set; } = true;
    public bool AtlasShowContentBadges { get; set; } = true;
    public bool AtlasShowContentCount { get; set; } = true;
    public bool AtlasShowContentTokens { get; set; } = false;
    public bool AtlasShowRouteChevrons { get; set; } = true;
    public float AtlasRouteLineThickness { get; set; } = 3.5f;
    public float AtlasRouteChevronSpacing { get; set; } = 28f;
    public float AtlasSearchRange { get; set; } = 1f;
    public float AtlasLabelOffsetX { get; set; } = 0f;
    public float AtlasLabelOffsetY { get; set; } = 0f;
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
    public int AddNearestPathHotkey { get; set; } = 0x75;
    public int ClearPathsHotkey { get; set; } = 0x76;
    public int AutoFlaskToggleHotkey { get; set; } = 0x77;
    public int QuitHotkey { get; set; } = 0x78;
    public int AtlasPickHotkey { get; set; } = 0x79;
    public int ToggleSettingsHotkey { get; set; } = 0x7A;
    public int OpenDashboardHotkey { get; set; } = 0x7B;

    // ── Xbox / XInput controller (player slot 0–3). ──
    public bool GamepadHotkeysEnabled { get; set; } = false;
    public int GamepadUserIndex { get; set; } = 0;

    // ── In-game ImGui chrome (GameHelper defaults: Microsoft YaHei 18px). ──
    public string UiFontPath { get; set; } = @"C:\Windows\Fonts\msyh.ttc";
    public int UiFontSize { get; set; } = 18;
    public UiFontGlyphRange UiFontGlyphRange { get; set; } = UiFontGlyphRange.ChineseSimplifiedCommon;
    public bool UiFontDefaultsMigrated { get; set; }

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

        if (!PathTogglesMigrated)
        {
            ShowPathWorld = ShowPathMap = ShowPath;
            PathTogglesMigrated = true;
            changed = true;
        }

        if (!PathGroundDefaultMigrated)
        {
            PathGroundDefaultMigrated = true;
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

        return changed;
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
        => new()
        {
            new AtlasRouteGroupSettings
            {
                Name = "Map Targets",
                Locked = true,
                DrawPaths = true,
                LineThickness = 3.5f,
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
            }
        };

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

public sealed class AtlasMapGroupSettings
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#1f2937";
    public string FontColor { get; set; } = "#ffffff";
    public bool Enabled { get; set; } = true;
    public List<string> Maps { get; set; } = new();
}

public sealed class AtlasRouteGroupSettings
{
    public string Name { get; set; } = "";
    public bool DrawPaths { get; set; } = true;
    public bool Locked { get; set; }
    public float LineThickness { get; set; } = 3.5f;
    public List<AtlasRouteEntrySettings> Entries { get; set; } = new();
}

public sealed class AtlasRouteEntrySettings
{
    public string Name { get; set; } = "";
    public string Match { get; set; } = "";
    public string Color { get; set; } = "#58A6FF";
    public bool DrawPath { get; set; } = true;
    public int MaxHops { get; set; } = 25;
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

/// <summary>Ground loot value overlay settings (poe.ninja-backed).</summary>
public sealed class GroundItemSettings
{
    public bool Enabled { get; set; } = true;
    public double HighlightMinEx { get; set; } = 10.0;
    public double UniqueMinEx { get; set; } = 5.0;
    public double CurrencyMinEx { get; set; } = 1.0;
    public double OtherMinEx { get; set; } = 1.0;
    public int MinQuantity { get; set; } = 2;
    public string League { get; set; } = "";
    public bool AnchorValuesToTags { get; set; } = true;
    public List<string> Categories { get; set; } = new()
    {
        "Uniques", "Currency", "Runes", "SoulCores", "Essences", "Fragments",
        "UncutGems", "Delirium", "Tablets", "Idols", "Abyss", "Ritual",
    };
}

/// <summary>Runeshape monolith reward preview settings.</summary>
public sealed class MonolithSettings
{
    public bool Enabled { get; set; } = true;
    public double HighlightMinEx { get; set; } = 30.0;
    public double MinRewardEx { get; set; } = 1.0;
    public double MinValueEx { get; set; } = 0.0;
    public bool HideCollected { get; set; } = true;
    public bool ShowPanel { get; set; } = true;
    public bool ShowMapLabel { get; set; } = true;
    public float PanelMaxDistance { get; set; } = 0f;
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
    public IconStyle ChestRare     { get; set; } = new("Square",  "#FFD926", 0.95f, 10.0f, SpriteIconRef.Cell(4, 48, 1.25f));
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
