namespace POE2Radar.Overlay.Settings;

/// <summary>Plain-language hover hints shared by ImGui settings, web dashboard, and startup launcher.</summary>
internal static class SettingHints
{
    internal static class DisplayRules
    {
        public const string ColumnOrder = "Rule order. Top row is checked first.";
        public const string ColumnOn = "Rule is active. Off = skipped, next rule can match.";
        public const string ColumnHide = "Matched entities are hidden on the map.";
        public const string ColumnPath = "Draw a walking path to matching entities.";
        public const string ColumnIcon = "Icon shape or sprite for matching entities.";
        public const string SpritePicker = "PNG sprite from icons.png — click to pick a cell.";
        public const string SpritePickerLoading = "PNG sprite from icons.png (loads on first in-game frame).";
        public const string ColumnColor = "Dot color on the map.";
        public const string ColumnAlpha = "How see-through the dot is (0 = invisible).";
        public const string ColumnSize = "Dot size on the map.";
        public const string ColumnSprite = "Scale for PNG sprite icons from icons.png.";
        public const string ColumnName = "Rule name shown in this list.";
        public const string ColumnLabel = "Optional text chip beside the dot on the map (empty = none).";
        public const string HideLabel = "Icon only — hide the text chip on the map for matches.";
        public const string ColumnHideLabel = "Hide the map text chip for this rule (icon only).";
        public const string ColumnGroup = "Entity category this rule matches, or Any for all types.";
        public const string ColumnMatch = "Metadata token or substring — comma-separated.";
        public const string ColumnAdvanced = "Rarity, reaction, life, opened state, and POI filters.";
        public const string MatcherOpened = "Match opened or unopened chests, or any.";
        public const string GlobalIconScale = "Multiplier on PNG sprite size from icons.png (per-rule scale stacks on top).";
        public const string AddRule = "Append a blank rule at the lowest priority.";
        public const string DuplicateRule = "Duplicate the selected rule below it.";
        public const string DeleteRule = "Remove the selected rule.";
        public const string MoveUp = "Higher priority — evaluated earlier.";
        public const string MoveDown = "Lower priority — evaluated later.";
        public const string SearchRules = "Filter by rule name, match token, or category.";
        public const string RuleName = "Friendly name shown in lists and the dashboard.";
        public const string MatchContains = "Metadata substring or glob (* ?). Any term matching is enough.";
        public const string EntityType = "Only match entities in these categories. None checked = any type.";
        public const string Rarity = "Match monster/item rarity, or any.";
        public const string Reaction = "Match friendly or hostile entities, or any.";
        public const string Life = "Match alive or dead entities, or any.";
        public const string Chest = "Match opened or unopened chests, or any."; // legacy alias
        public const string Poi = "Match points of interest, or any.";
        public const string HideOnMap = "Do not draw matching entities on the radar.";
        public const string AutoPathTarget = "Continuously path to the nearest match (needs paths enabled).";
        public const string Shape = "Icon shape drawn on the map.";
        public const string MapLabel = "Optional text beside the dot on the map.";
    }

    internal static class Entities
    {
        public const string DetectionRadius = "Max grid distance from player for entity dots, nav targets, and API list. 0 = no limit.";
        public const string AutoPathNearest = "Continuously draw paths to the nearest navigation targets — endgame mechanics, rares, uniques, POIs by default.";
        public const string ShowAllMonsters = "Show normal/magic grey monsters and other map clutter on the radar.";
        public const string NeverShowPattern = "Substring or glob (* ?) — not on map, not in lists, not for paths.";
        public const string NeverShowAdd = "Add this pattern to the never-show list.";
        public const string TypeSearch = "Filter entity types in the current zone.";
        public const string TierShow = "Show or hide all types in this tier for this zone only.";
        public const string TierNav = "Enable or disable paths for all types in this tier for this zone only.";
        public const string TypeShow = "Show this entity type on the map in this zone.";
        public const string TypeNav = "Draw paths to this entity type in this zone.";
        public const string TypeShowGlobal = "Controlled by display rules below.";
        public const string TypeNavGlobal = "Controlled by display rules below.";
        public const string PromoteTypeToRule = "Add this type to display rules — edit icon and path in the table below.";
        public const string InDisplayRules = "Jump to this type's row in display rules below.";
    }

    internal static class Radar
    {
        public const string ShowMonsters = "Draw entity dots (monsters, NPCs, chests, POIs) on the map overlay.";
        public const string ShowTerrain = "Draw walkable terrain boundary edges on the map overlay.";
        public const string ShowPlayerBlip = "Show a cyan dot at your position on the map overlay.";
        public const string ShowPathWorld = "Route breadcrumbs on the game world when the Tab map is closed.";
        public const string ShowGroundWaypoints = "Included in Path on ground — world-screen breadcrumb dots along the route.";
        public const string ShowPathMap = "Draw route lines on the large Tab map.";
        public const string ShowPathMinimap = "Draw route lines on the corner minimap.";
        public const string HideJunk = "Hide cosmetic FX, daemons, and other noise dots on the map.";
        public const string CuratedLandmarks = "Use community-curated friendly names for landmarks instead of raw tile paths.";
        public const string LandmarkClusterGap = "Max tile distance for merging nearby landmarks into one marker (0 = disable clustering).";
        public const string DrawAllLandmarkPaths = "Draw path lines to every landmark tile (heavy; off by default).";
        public const string LargeMapScale = "Tab-map overlay scale — fine-tune pixel lock when the large map is open.";
        public const string MinimapScale = "Corner minimap overlay scale multiplier.";
        public const string OffsetX = "Shift the entire map overlay horizontally in pixels.";
        public const string OffsetY = "Shift the entire map overlay vertically in pixels.";
        public const string TerrainInterior = "Color and opacity for the interior of walkable terrain cells.";
        public const string TerrainEdge = "Color and opacity for walkable terrain boundary edges.";
        public const string TerrainEdgeDetail = "Terrain edge sampling detail — higher = smoother edges, more GPU work.";
        public const string TerrainEdgeThickness = "Line thickness for terrain boundary edges.";
    }

    internal static class Amanamu
    {
        public const string Enabled = "Detect Amanamu Lightless Well monsters and show their cloud-immunity state. Off adds no component, mod, or buff reads.";
        public const string RareOnly = "Restrict discovery to rare and unique hostile monsters, matching the important Amanamu targets.";
        public const string Distance = "Maximum grid distance for discovery. 0 removes the distance limit; a lower limit reduces work in dense Abyss encounters.";
    }

    internal static class Performance
    {
        public const string LowImpactMode = "Favor lower memory-read cadence when idle or unfocused.";
        public const string FpsCap = "Overlay present rate — lower = less GPU load.";
        public const string LiveRefreshHz = "How often player position, map UI, vitals, and camera update.";
        public const string WorldRefreshHz = "How often entities, terrain, landmarks, and routes update.";
        public const string InactiveRefreshHz = "World reads while PoE2 is unfocused and overlay is hidden.";
        public const string HpBarRefreshHz = "How often live HP bar positions and values update.";
        public const string MaxLiveHpBars = "Cap how many HP nameplates are read each tick (0 = unlimited).";
        public const string SmoothOverlayMotion = "Smooth paths, map transform, and label movement between reads.";
        public const string OverlaySmoothingMs = "Smoothing time for paths, player, and map transform (milliseconds).";
        public const string ChipSmoothingMs = "Smoothing time for label chip rectangles (milliseconds).";
        public const string PixelSnapLabels = "Round final text and chip positions to whole pixels.";
        public const string OverlayVSync = "Present overlay frames on the display refresh cadence.";
        public const string FpsResourceOverlay = "Tick + render FPS and app CPU/GPU/RAM under the POE2Radar nav button.";
        public const string ExtendedPerfStats = "Extra timing and memory-read lines under the nav menu.";
        public const string MetricsRefreshHz = "How often CPU/RAM metrics sample when the metrics HUD is on.";
        public const string GpuMetricsSeconds = "How often GPU/VRAM metrics sample when the metrics HUD is on.";
        public const string UiFontSize = "Text size in the settings panel and nav menu.";
        public const string NavMenuCorner = "Where the in-game POE2Radar nav dropdown is pinned.";
    }

    internal static class HpBars
    {
        public const string Normal = "Show HP bars on normal (white name) monsters.";
        public const string Magic = "Show HP bars on magic (blue name) monsters.";
        public const string Rare = "Show HP bars on rare (yellow name) monsters.";
        public const string Unique = "Show HP bars on unique (orange name) bosses and monsters.";
        public const string UseTextures = "Draw gradient HP/ES bars from Overlay/Textures PNG files.";
        public const string WidthNormal = "HP bar width in pixels for normal monsters.";
        public const string WidthMagic = "HP bar width in pixels for magic monsters.";
        public const string WidthRare = "HP bar width in pixels for rare monsters.";
        public const string WidthUnique = "HP bar width in pixels for unique monsters and bosses.";
        public const string BarHeight = "HP bar height in pixels — applies to all rarities.";
        public const string OffsetX = "Horizontal offset from the monster's position in pixels.";
        public const string OffsetY = "Vertical offset from the monster — negative = above, positive = below.";
        public const string ColumnOn = "Draw HP bars for this rarity.";
        public const string ColumnWidth = "Bar width in pixels for this rarity.";
        public const string ColumnBorder = "Border color around the bar.";
        public const string ColumnThick = "Border thickness in pixels (0 = no border).";
    }

    internal static class Flask
    {
        public const string TriggerPool = "Which resource pool triggers the life flask key — Health, Energy Shield, or Either.";
        public const string LifeThreshold = "Use life flask when HP falls below this percentage.";
        public const string EsThreshold = "Use life flask when energy shield falls below this percentage (ES or Either mode).";
        public const string LifeCooldown = "Minimum delay between life flask activations in milliseconds.";
        public const string LifeKey = "Keyboard key for the life flask (Win32 virtual-key code).";
        public const string ManaThreshold = "Use mana flask when mana falls below this percentage.";
        public const string ManaCooldown = "Minimum delay between mana flask activations in milliseconds.";
        public const string ManaKey = "Keyboard key for the mana flask (Win32 virtual-key code).";
    }

    internal static class Atlas
    {
        public const string Language = "Language for atlas map name matching.";
        public const string ShowAllNodes = "Draw every atlas node present in memory on the open atlas.";
        public const string ShowAllNodesTracked = "Track filters are active — leave on to show all nodes while still drawing tracked rings.";
        public const string ShowNames = "Label on-screen nodes with map name (and highlight tag when matched).";
        public const string RevealFog = "Show fogged or unrevealed nodes with a cool tint so the full atlas graph is visible.";
        public const string OffScreenArrows = "Edge arrows for arrow-tagged highlights only (e.g. Citadels off-screen).";
        public const string ShowRoute = "Draw the route through the atlas node connection graph.";
        public const string RouteFromCurrent = "When no F10 START is set, route from your current atlas position.";
        public const string RouteChevrons = "Draw chevron markers along the atlas route line.";
        public const string BiomeBorders = "Outline biome regions on the atlas map.";
        public const string ContentBadges = "Show content-type badges on atlas nodes.";
        public const string ContentCount = "Show small count pips for content on atlas nodes.";
        public const string IconScale = "Scale of atlas node icons on screen.";
        public const string LabelScale = "Scale of atlas node name labels.";
        public const string RouteThickness = "Thickness of the atlas route line.";
        public const string ChevronSpacing = "Distance between chevrons along the route.";
        public const string SearchQuery = "Type a map name (loose words ok — e.g. Moor skies). Matches highlight and get a path; others dim. Comma = OR.";
        public const string HideCompleted = "Hide maps you have already completed.";
        public const string HideNotAccessible = "Hide maps you cannot reach yet (optional; off by default).";
        public const string HideAvailable = "Hide maps that are available but not completed (optional; off by default).";
        public const string GroupEnabled = "Draw this map style group on the atlas.";
        public const string GroupColor = "Background tint for maps in this group.";
        public const string GroupMaps = "Comma-separated map names in this style group.";
        public const string RouteGroupDraw = "Draw farming routes for this content group.";
        public const string RouteGroupThickness = "Line thickness for this route group.";
        public const string RouteEntryDraw = "Draw a route to this content type.";
        public const string RouteMaxHops = "Max atlas hops for this route (0 = unlimited).";
        public const string RouteEntryColor = "Route line color for this content type.";
        public const string AddCitadelDefaults = "Track + arrow every Citadel map name (gold ring).";
        public const string AddEndgameDefaults = "Highlight endgame maps and boss content for repeat Arbiter runs.";
        public const string ClearFilters = "Remove all atlas track, arrow, and color filters.";
        public const string TagFilter = "Filter the tag and map name list below.";
        public const string ColName = "Map or content tag name.";
        public const string ColKind = "Whether this row is a map name or content tag.";
        public const string ColCount = "How many atlas nodes match this tag.";
        public const string ColTrack = "Draw a ring and route to matching nodes.";
        public const string ColArrow = "Show an edge arrow when the node is off-screen.";
        public const string ColColor = "Highlight ring color for tracked tags.";
        public const string ShowNodeSprites = "Draw extra icon sprites on atlas nodes (off matches GameHelper's clean look).";
        public const string AnchorNudgeY = "Vertical offset for map name pills below each node center.";
        public const string ScaleMultiplier = "Overall atlas label and chip scale for your resolution.";
        public const string BiomeBorderThickness = "Thickness of the colored border around map name pills.";
        public const string DrawLinesSearchQuery = "Draw route lines to maps matching the search box.";
        public const string DrawLinesToUniqueMaps = "Draw route lines to reachable unique maps.";
        public const string PathToLineageMaps = "Draw route lines to lineage maps.";
        public const string PathToArbiterMaps = "Draw route lines to arbiter maps.";
        public const string ShowContentIcons = "Show content icons above map names when PNGs exist in atlas-content-icons.";
        public const string ContentIconSize = "Height of content icons above map names.";
        public const string UseUniversalFont = "Scale atlas labels for non-English map and content names.";
        public const string ShowShipsInFog = "Draw ship markers on fogged ocean chunks (Uncharted Waters).";
        public const string ShowUnchartedLeylines = "When hovering a fog ship, highlight that chunk's atlas connections.";
        public const string ShipIconSize = "Size of Uncharted Waters ship icons on the atlas.";
        public const string ShowIslandRumours = "Show every assigned island, including rumours hidden by the game's three-line panel, plus community farming tiers. Adds no extra game-memory reads.";
        public const string ShowIslandRumourBadges = "Draw the total number of special islands on each ship marker.";
        public const string IslandRumourPriorityFilter = "Highlight ships containing any destination or rumour phrase in this | separated list.";
        public const string IslandRumourPriorityColor = "Border and count-badge color for priority Island Rumours.";
        public const string ShowRitualPrediction = "While ritual line mode is open, show predicted Rite mods on candidate maps.";
        public const string ShowRitualPlanner = "Show a window listing predicted ritual rewards for the active line.";
        public const string RitualRewardFilter = "Only list planner rows whose text contains these words (comma-separated).";
    }

    internal static class Hotkeys
    {
        public const string GamepadEnabled = "Xbox / XInput controllers on player slot 0–3.";
        public const string PadSlot = "Which controller slot to read (0 = first controller).";
        public const string Bind = "Click, then press a key or controller button.";
        public const string Clear = "Remove this hotkey binding.";
        public const string HideEntity = "Hover an entity and press to add its type to Never show.";
        public const string TrackEntity = "Add the entity under your cursor to display rules and open settings.";
        public const string ToggleRendering = "Show or hide every POE2Radar overlay surface until toggled again.";
        public const string AutoPathToggle = "Toggle continuous auto-pathing.";
        public const string AddNearestPath = "Add nearest navigation target to the path list.";
        public const string ClearPaths = "Clear all path targets.";
        public const string AutoFlaskToggle = "Master kill-switch for auto-flask.";
        public const string AtlasPick = "Pick atlas tile under cursor for routing.";
        public const string LootDetails = "Open the run-loot breakdown, advance its pages, then close it. Supports keyboard or Xbox/XInput buttons.";
        public const string ToggleSettings = "Open or close this settings panel.";
        public const string OpenDashboard = "Open the web dashboard in your browser.";
        public const string Quit = "Exit POE2Radar.";
    }

    internal static class Startup
    {
        public const string BrowseGame = "Pick PathOfExile.exe or PathOfExileSteam.exe on this PC.";
        public const string StartGame = "Launch Path of Exile 2, then load into a zone.";
        public const string StartRadar = "Attach the overlay to the running game and start the radar.";
        public const string Quit = "Close POE2Radar without starting the overlay.";
    }

    internal static class Dashboard
    {
        public const string LiveColName = "Entity display name in the zone.";
        public const string LiveColCategory = "Entity category (monster, chest, etc.).";
        public const string LiveColRarity = "Item or monster rarity.";
        public const string LiveColDist = "Grid distance from your position.";
        public const string LiveColHp = "Current and max hit points.";
        public const string LiveColRule = "First active display rule that matches this entity.";
        public const string DbColCategory = "Entity category in the full game catalog.";
        public const string DbColPath = "Full metadata path for this entity type.";
        public const string RulesPaused = "When paused, this rule is ignored and the next rule can match.";
        public const string RulesHide = "Matched entities are hidden on the radar.";
        public const string RulesPath = "Draw a walking path to matching entities.";
    }

    internal static class CraftingAssistant
    {
        public const string Enabled = "Show craft hints on inventory Waystones or Tablets and allow AUTO crafting.";
        public const string Target = "Craft Waystones or Precursor Tablets in your open inventory.";
        public const string ModeManual = "Highlight the next currency to use — you click yourself.";
        public const string ModeAuto = "Automatically right-click currency and apply it to each eligible item.";
        public const string AutoAck = "Required before AUTO can move your mouse and click in inventory.";
        public const string Recipe = "Which crafting sequence to run on the selected target type.";
        public const string TabletModTarget = "Stop when a tablet reaches this many explicit modifiers (3–4 need Atlas unlocks).";
        public const string MinimumTier = "Ignore Waystones below this tier.";
        public const string UseRegal = "Keep Magic Waystone mods with Regal. Off (default) uses Alchemy on blues too — same as whites.";
        public const string ApplyExalt = "Keep Exalting identified Rare Waystones until they reach the mod target.";
        public const string DesiredMods = "Stop Exalting when a Waystone reaches this many explicit mods.";
        public const string ActionDelay = "Pause between currency click and item click.";
        public const string Start = "Close settings, focus the game, and begin AUTO crafting eligible inventory items.";
        public const string Stop = "Stop AUTO crafting immediately.";
        public const string RunHotkey = "Optional keyboard or Xbox bind that toggles AUTO crafting.";
        public const string EmergencyStop = "Instant kill-switch for AUTO crafting (default F8).";
    }

    internal static class Ritual
    {
        public const string ShowOverlay = "Paint prices on each tribute row in-game while the Ritual shop is open.";
        public const string ShowPricesWindow = "List tribute rewards with names and prices in a side window (like Monolith Rewards).";
        public const string PriceSource = "Where to fetch public PoE2 price data from.";
        public const string League = "League name sent to the price API.";
        public const string RefreshIntervalMin = "How often to refresh cached prices in minutes.";
        public const string MinDisplayExalted = "Hide rewards priced below this Exalted value in the window and overlay.";
    }

    internal static class Runecraft
    {
        public const string ShowOverlay = "Paint Exalted prices on each reward row while the Runeshape Combinations panel is open.";
        public const string ColorMode = "Tint prices off, vs the median on screen, or vs fixed Exalted thresholds.";
        public const string OverlayXOffset = "Slide price text left or right after the row's rune icons.";
        public const string HighlightLockedRecipe = "Gold border on the sealed monolith's locked-in recipe row.";
        public const string PriceSource = "Where to fetch public PoE2 price data from.";
        public const string League = "League name sent to the price API.";
        public const string RefreshIntervalMin = "How often to refresh cached prices in minutes.";
        public const string ShowMapLabels = "Show each monolith's best reward value on the large map.";
        public const string HideMapValueWhenPanelOpen = "Hide map value labels while the combinations panel is open.";
        public const string MapLabelMinExalted = "Skip map labels below this Exalted value.";
        public const string MapValueScaleMultiplier = "Scale monolith map labels to match your map zoom.";
        public const string MapValueXOffset = "Move monolith value labels horizontally on the large map.";
        public const string MapValueYOffset = "Move monolith value labels vertically on the large map.";
        public const string ShowMonolithWindow = "Open a list of nearby monolith candidate recipes and prices.";
        public const string MonolithRewardsMinExalted = "Hide candidate rewards whose total value is below this threshold; zero shows everything.";
        public const string MonolithHighlightThreshold = "Tint high-value monolith headers when their best candidate reaches this value; zero disables threshold highlighting.";
        public const string AutoShowMonolithWithGamepad = "Open the monolith rewards window while a controller is connected. Uses a light background scan — turn off if you want minimum CPU.";
    }
}
