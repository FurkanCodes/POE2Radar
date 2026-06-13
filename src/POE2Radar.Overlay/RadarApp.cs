using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NumVec2 = System.Numerics.Vector2;
using POE2Radar.Core;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Diagnostics;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Native;
using POE2Radar.Overlay.Navigation;
using POE2Radar.Overlay.Web;
using PathCell = POE2Radar.Core.Pathfinding.PathCell;
using RoutePlanStatus = POE2Radar.Core.Pathfinding.RoutePlanStatus;

namespace POE2Radar.Overlay;

/// <summary>
/// Drives the PoE2 radar: per-tick resolve chain → read player/entities/terrain/map → render.
/// Read-only. Render, live-player, world-scan, HP-bar, and metrics work all use separate configurable
/// cadences so the overlay can draw smoothly without multiplying memory reads.
/// </summary>
public sealed partial class RadarApp : IDisposable
{
    private readonly ProcessHandle _process;
    private readonly MemoryReader _reader;
    private readonly Poe2Live _live;
    private readonly Poe2Atlas _atlas;
    private ImGuiRadarOverlay? _imguiOverlay;
    private Thread? _imguiThread;
    private readonly ApiServer _api;
    private readonly RadarSettings _settings;
    private readonly HiddenEntities _hidden;
    private readonly WatchedEntities _watched;
    private readonly LandmarkPatterns _landmarkPatterns;
    private readonly DisplayRules _displayRules;
    private readonly ZoneEntityOverrides _zoneOverrides;
    private readonly DisplayRuleEngine _ruleEngine;
    private readonly LandmarkStore _landmarkStore;
    private readonly CompletedContentSuppressor _completedSuppressor = new();
    private uint _suppressorAreaHash;
    private int _landmarkGen;
    private int _displayRulesGen;
    private int _landmarkStoreGen;
    private int _appliedClusterGap;
    private nint _areaInstanceForApi;   // current AreaInstance, for the /api/tiles tile-path lookup
    private nint _inGameStateForApi;    // current InGameState, for the /api/atlas node read
    private volatile RadarState _state = RadarState.Empty;
    private volatile WorldSnapshot _snapshot = WorldSnapshot.Empty;
    private readonly MemoryReader _worldReader;
    private readonly Poe2Live _worldLive;
    private readonly Thread _worldThread;
    private readonly object _trackerGate = new();
    private readonly PerfAccumulator _perf = new();
    private readonly ProcessMetricsSampler _processMetrics;
    private PerfSnapshot _perfSnapshot = PerfSnapshot.Empty;
    private volatile float _worldTickMs;
    private readonly PerformanceCadence _liveCadence = new();
    private readonly PerformanceCadence _hpBarCadence = new();
    private readonly VisualMotionSmoother _visualSmoother = new();
    private LiveFrameState _liveFrame = LiveFrameState.Empty;
    private volatile bool _mainLandmarksInvalid;

    // ── Atlas overlay: live node highlights (takes precedence over the radar when the atlas is open). ──
    private readonly object _atlasLock = new();
    private readonly HashSet<nint> _atlasSel = new();   // selected node element addresses (from the dashboard)
    private bool _atlasOpen;
    private List<AtlasMark> _atlasMarks = new();         // built each world tick (tick thread only)
    private IReadOnlyList<AtlasMark> _atlasMarksPublish = Array.Empty<AtlasMark>(); // snapshot for render thread
    private IReadOnlyList<NumVec2> _atlasRoutePublish = Array.Empty<NumVec2>();
    private List<AtlasTagCatalogEntry> _atlasTagCatalog = new(); // distinct tags/maps for overlay Settings → Atlas
    private DateTime _nextInspectAt = DateTime.MinValue; // F10 hotkey debounce
    // F10 route workflow (manual, no memory-marker dependency): 1st F10 sets START tile, 2nd sets END tile
    // (and routes between them through the connection graph), 3rd resets. Stored by GRID coord so they
    // survive pan/zoom and the tiles going off-screen.
    private (int X, int Y)? _atlasStartGrid;
    private (int X, int Y)? _atlasGoalGrid;
    private NumVec2? _atlasStartPt, _atlasEndPt; // start/end in canvas relPos (for the markers), per tick
    private List<NumVec2> _atlasRoute = new();   // graph path start→end in canvas relPos (empty if none)
    private DateTime _atlasGoodAt = DateTime.MinValue; // last tick we read nodes — debounces transient misses
    private long _lastAtlasContentSig;   // highlight/tag/catalog signature — skips tag-catalog rebuild only (not positions)
    private bool _builtAtlasOnce;        // marks built at least once this atlas session
    // Live atlas zoom (= canvas/node scale @ +0x130; 0.85 max-out … larger zoomed in). relPos is read
    // live (pan baked in) and the projection scales by this zoom, so rings track pan AND zoom.
    private volatile float _atlasZoom = 0.85f;
    private List<Poe2Atlas.AtlasNodeLive> _atlasNodesCache = new();
    private readonly object _atlasNodesCacheLock = new();
    private Poe2Live.MapViews _cachedMaps;
    private float _atlasUpdateMs;
    private volatile UpdateChecker.Result? _update;   // GitHub version check (best-effort, set async at startup)
    // Atlas projection is derived live from the game window height (UIscale = winH/1600 × live zoom) in
    // AtlasProjection — resolution-correct, no calibration. (The 1080p reference: scale = (1080/1600)×0.85
    // ≈ 0.574 at max zoom-out, offset 0.)

    /// <summary>Directory holding the user config files (shared with <see cref="RadarSettings"/>).</summary>
    private static string ConfigDir => Path.Combine(AppContext.BaseDirectory, "config");

    private List<Poe2Live.EntityDot> _entities = new();
    // Monster HP-bar pipeline. _hpSpecs (style + which mobs get a bar) is rebuilt at WORLD rate from the
    // resolved rules; _hpFrame (live position + HP) is rebuilt every RENDER frame from cheap per-mob reads
    // so bars track moving monsters smoothly without re-enumerating/re-resolving thousands of entities.
    // Published into the RenderContext and enumerated on the ImGui render thread, so it must never be
    // mutated in place — each frame we swap in a FRESH list (volatile publish) to avoid a cross-thread
    // "collection modified" crash in DrawNameplates.
    private volatile HpBarTarget[] _hpFrame = Array.Empty<HpBarTarget>();
    private IReadOnlyList<Poe2Live.Landmark> _landmarks = Array.Empty<Poe2Live.Landmark>();
    private Poe2Live.TerrainData? _worldTerrain;
    private uint _areaHash;
    private nint _lastAreaInstance;
    private nint _gameHwnd;
    private string _mapDiag = "";
    private volatile bool _shutdown;

    // ── Auto-flask (opt-in input). Foreground + in-game gated; F8 master kill-switch.
    //    Flask keys are configurable in RadarSettings (LifeKey/ManaKey). ──
    private bool _autoFlask = true;                        // auto-on; toggle with F8
    private DateTime _lifeFiredAt = DateTime.MinValue, _manaFiredAt = DateTime.MinValue;
    private DateTime _nextToggleAt = DateTime.MinValue;
    private DateTime _nextQuitAt = DateTime.MinValue;
    private DateTime _nextPathKeyAt = DateTime.MinValue;
    private DateTime _nextAutoPathToggleAt = DateTime.MinValue;
    private DateTime _nextBrowserAt = DateTime.MinValue;
    private DateTime _nextSettingsAt = DateTime.MinValue;
    private bool _hideKeyWasDown;
    private bool _trackKeyWasDown;
    private bool _settingsWereOpen;
    private MapFrame _lastMapFrame;
    private MapFrame _lastMiniMapFrame;
    private NumVec2 _lastPlayerGrid;
    private string? _cursorInspectTitle;
    private string? _cursorInspectMeta;
    private float _hpPct = 100f, _manaPct = 100f, _esPct = 100f;
    private string _flaskNote = "";
    private string _areaCode = "", _charName = "";
    private nint _charNameFor;   // local-player ptr the cached _charName was read for (re-read only on change)
    private int _charLevel;
    private float[]? _cameraMatrix;

    // Render inputs rebuilt at world rate (30 Hz), not per render frame.

    // ── Phase 1: exploration fog + draw-only path guidance (all gated by RadarSettings flags). ──
    // Unified navigation targets: a single list built each world tick from BOTH terrain-tile
    // landmarks AND entity POIs (bosses, expedition, waypoints…), each addressed by a STABLE STRING
    // id ("t:<path>" / "e:<entityId>"). Multi-select: each selected target draws its OWN full A*
    // route in its OWN color (by selection-order slot). F6 adds the nearest not-yet-selected target;
    // F7 clears the whole selection; clicking a legend row toggles that target. Selection is capped
    // at the palette size so colors stay distinct (and per-tick planning stays bounded). On a zone
    // change the selection is cleared, then the persistent auto-nav patterns re-select matching
    // targets in the new zone.
    private const int MaxSelectedTargets = 8;
    // Background A* replanner (single reused PathPlanner on a worker thread) + one RouteTracker per
    // selected id. The tick thread does only CHEAP per-tick maintenance (cursor advance) and rebuilds
    // _selectedPaths from the trackers; the worker owns all A*. See BackgroundReplanner / RouteTracker.
    private readonly ConcurrentQueue<Action> _commandQueue = new();
    private readonly BackgroundReplanner _replanner = new();
    private readonly Dictionary<string, RouteTracker> _trackers = new(); // one per selected id; OWNED by the tick thread
    private List<NavTarget> _navTargets = new();                         // unified targets, rebuilt each world tick
    private PathCell[] _worldDoorOverrides = Array.Empty<PathCell>();
    private readonly record struct TargetSnapshot(
        string Label,
        NumVec2 Grid,
        bool IsEntity,
        DateTime LastSeenUtc,
        int TileCount,
        int GoalSearchRadius,
        PathCell[] RouteAnchors);
    private readonly record struct TargetSnapshotKey(uint AreaHash, string TargetId);
    private readonly record struct TargetRenderInfo(
        string Id,
        string Label,
        NumVec2 Grid,
        bool IsEntity,
        NavTargetStatus Status,
        int TileCount,
        int GoalSearchRadius,
        PathCell[] RouteAnchors);
    private readonly object _targetSnapshotLock = new();
    private readonly Dictionary<TargetSnapshotKey, TargetSnapshot> _targetSnapshots = new();
    // The ONLY state shared with the HTTP/API thread. Every read/iterate/mutate of _selectedIds is
    // done under _navLock (snapshot to a local, then work outside the lock). Trackers are reconciled
    // from this list on the tick thread only — mutators (in-game + API) just edit _selectedIds.
    private readonly object _navLock = new();
    private readonly List<string> _selectedIds = new();                  // selected target ids (order drives the color slot)
    private readonly HashSet<string> _autoSelectedIds = new();           // ids added by live auto-path (not manual F6/legend)
    private readonly List<SelectedPath> _renderPaths = new();                   // one route per selected target (from trackers)
    private volatile SelectedPath[] _renderPathSnapshot = Array.Empty<SelectedPath>();
    private bool _selectionCapWarned;                                    // log the "cap reached" notice once
    private nint _navTargetsArea = -1;                                   // AreaInstance the auto-nav was applied for
    // Per-instance nav memory: the nav selection for each AreaInstance hash, so returning to a zone
    // (e.g. after a town trip, which re-resolves a fresh AreaInstance) RESTORES what was selected
    // instead of clearing it. AreaHash is the stable per-instance id (same instance → same hash;
    // a re-rolled map → new hash → fresh auto-nav). In-session only and capped (LRU) so a long
    // session can't grow it unbounded. _selectionAreaHash is the hash _selectedIds belong to now.
    private readonly Dictionary<uint, List<string>> _zoneSelections = new();
    private readonly List<uint> _zoneOrder = new();                      // insertion order, for LRU eviction
    private uint _selectionAreaHash;
    private const int MaxRememberedZones = 64;
    private const int MaxTargetSnapshots = 512;
    private int OverlayWidth => _imguiOverlay?.OverlayWidth ?? 0;
    private int OverlayHeight => _imguiOverlay?.OverlayHeight ?? 0;

    private readonly record struct LiveFrameState(
        bool InGame,
        nint InGameState,
        nint AreaInstance,
        nint LocalPlayer,
        NumVec2 PlayerGrid,
        System.Numerics.Vector3 PlayerWorld,
        float PlayerTerrainHeight,
        Poe2Live.MapViews Maps,
        uint AreaHash,
        float[]? CameraMatrix)
    {
        public static readonly LiveFrameState Empty = new(
            false, 0, 0, 0, NumVec2.Zero, System.Numerics.Vector3.Zero, 0f, default, 0, null);
    }

    public void RequestShutdown() => _shutdown = true;

    public RadarApp(ProcessHandle process, MemoryReader reader, nint gameStateSlot)
    {
        _process = process;
        _reader = reader;
        _processMetrics = ProcessMetricsSampler.ForOverlay();
        _settings = RadarSettings.Load();
        Console.WriteLine($"Settings: {RadarSettings.FilePath}");
        Console.WriteLine($"Entity names: {EntityNameResolver.Shared.Count} mappings; zones: {ZoneGuide.Shared.Count}; zone bosses: {ZoneBossCatalog.Shared.Count}");
        _live = new Poe2Live(reader, gameStateSlot);
        _worldReader = new MemoryReader(process);
        _worldLive = new Poe2Live(_worldReader, gameStateSlot);
        _atlas = new Poe2Atlas(reader);
        CrashLog.Write("Backend selected", "Starting ImGuiDx backend.");
        try
        {
            _imguiOverlay = CreateImGuiOverlay();
            _imguiThread = new Thread(RunImGuiOverlayThread)
            {
                IsBackground = true,
                Name = "POE2Radar ImGuiDx",
            };
            _imguiThread.SetApartmentState(ApartmentState.STA);
            _imguiThread.Start();
            Console.WriteLine("Overlay backend: ImGuiDx (GPU draw lists).");
        }
        catch (Exception ex)
        {
            CrashLog.Write("ImGuiDx backend startup failed", ex);
            Console.Error.WriteLine("FATAL: ImGuiDx backend failed to start. Exiting.");
            Environment.Exit(1);
        }
        _hidden = new HiddenEntities(Path.Combine(ConfigDir, "hidden_entities.json"));
        _watched = new WatchedEntities(Path.Combine(ConfigDir, "watched_entities.json"));
        _landmarkPatterns = new LandmarkPatterns(Path.Combine(ConfigDir, "landmark_patterns.json"));
        _live.CustomLandmarkMatch = TileLandmarkMatch;
        _worldLive.CustomLandmarkMatch = TileLandmarkMatch; // surface tiles via landmark patterns + Tile rules
        _landmarkGen = _landmarkPatterns.Generation;
        _live.LandmarkClusterGap = _settings.LandmarkClusterGap;
        _appliedClusterGap = _settings.LandmarkClusterGap;
        // Unified display ruleset — single source of truth for the entity dot decision. On first run
        // (no display_rules.json) seed it from the legacy category styles + mechanics + watched rules
        // so behavior is identical; thereafter it's the authoritative, editable, ordered ruleset.
        _displayRules = new DisplayRules(Path.Combine(ConfigDir, "display_rules.json"));
        _zoneOverrides = new ZoneEntityOverrides(Path.Combine(ConfigDir, "zone_entity_overrides.json"));
        _ruleEngine = new DisplayRuleEngine(_displayRules, _zoneOverrides, () => _settings.Styles);
        if (_displayRules.Count == 0)
        {
            _displayRules.Replace(DisplayRules.BuildDefault(
                _settings.Styles, _settings.ShowMonsters, _watched.All));
            Console.WriteLine($"Display rules: seeded {_displayRules.Count} from legacy config (first run).");
        }
        // One-time: fold any user landmark-tile patterns into Tile display rules (the unified system),
        // then clear the old config so it's retired and won't double-apply or re-migrate.
        if (_landmarkPatterns.All.Count > 0)
        {
            var rules = _displayRules.All.ToList();
            var seen = new HashSet<string>(
                rules.Where(r => r.Categories.Contains("Tile")).SelectMany(r => r.Match), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var lp in _landmarkPatterns.All)
            {
                if (!seen.Add(lp.Pattern)) continue;
                rules.Add(new DisplayRule
                {
                    Enabled = lp.Enabled, Name = string.IsNullOrWhiteSpace(lp.Label) ? lp.Pattern : lp.Label,
                    Categories = new() { "Tile" }, Match = new() { lp.Pattern },
                    Shape = "Diamond", Color = "#F259F2", Opacity = 1f, Size = 5f, Navigable = true,
                    Label = string.IsNullOrWhiteSpace(lp.Label) ? null : lp.Label,
                });
                added++;
            }
            if (added > 0) _displayRules.Replace(rules);
            foreach (var lp in _landmarkPatterns.All.ToList()) _landmarkPatterns.Remove(lp.Pattern);
            Console.WriteLine($"Migrated {added} landmark-tile pattern(s) into Tile display rules.");
        }
        // One-time: fold the old AutoNavPatterns list onto matching rules' Auto-path flag (a rule auto-
        // paths when one of its match terms overlaps a pattern), then retire the list. Preserves the
        // "auto-path to the expedition encounter on zone entry" default.
        if (_settings.AutoNavPatterns.Count > 0)
        {
            var rules = _displayRules.All.ToList();
            var pats = _settings.AutoNavPatterns;
            var changed = false;
            foreach (var r in rules)
            {
                if (r.Navigable) continue;
                if (r.Match.Any(m => pats.Any(p =>
                        m.Contains(p, StringComparison.OrdinalIgnoreCase) || p.Contains(m, StringComparison.OrdinalIgnoreCase))))
                { r.Navigable = true; changed = true; }
            }
            if (changed) _displayRules.Replace(rules);
            _settings.AutoNavPatterns = new(); _settings.Save();
            Console.WriteLine("Migrated auto-path patterns onto display rules' Auto-path flag.");
        }
        // One-time: nav qualification is now rule-driven (rule.Navigable), not a hardcoded POI/unique
        // clause. Flip Navigable=true on the default POI/Transition/Unique rules of an existing config so
        // the prior "POIs/transitions/uniques auto-path" behavior is preserved. Name-based (a renamed
        // rule is skipped — acceptable; the user can re-check Nav). Guarded so we never re-stomp edits.
        if (!_settings.NavRuleModelMigrated)
        {
            var navDefaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Point of Interest", "Transition", "Monster · Unique" };
            var rules = _displayRules.All.ToList();
            var changed = false;
            foreach (var r in rules)
                if (!r.Navigable && navDefaults.Contains(r.Name)) { r.Navigable = true; changed = true; }
            if (changed) _displayRules.Replace(rules);
            _settings.NavRuleModelMigrated = true; _settings.Save();
            if (changed) Console.WriteLine("Migrated default POI/Transition/Unique rules to Navigable (rule-driven nav).");
        }
        if (!_settings.EndgameNavMigrated)
        {
            var endgameNav = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Monster · Rare", "Expedition", "Ritual", "Breach", "Strongbox", "Essence", "Shrine",
            };
            var rules = _displayRules.All.ToList();
            var changed = false;
            foreach (var r in rules)
                if (!r.Navigable && endgameNav.Contains(r.Name)) { r.Navigable = true; changed = true; }
            if (changed) _displayRules.Replace(rules);
            _settings.EndgameNavMigrated = true; _settings.Save();
            if (changed) Console.WriteLine("Migrated mechanic + rare monster rules to Navigable (endgame defaults).");
        }
        if (!_settings.GlobalRulesExpandedMigrated)
        {
            var rules = _displayRules.All.ToList();
            foreach (var r in rules)
            {
                if (string.Equals(r.Name, "Monster · Unique", StringComparison.OrdinalIgnoreCase))
                {
                    r.Name = "Boss";
                    r.Label = "Boss";
                }
                else if (string.IsNullOrEmpty(r.Label))
                {
                    if (string.Equals(r.Name, "Monster · Rare", StringComparison.OrdinalIgnoreCase)) r.Label = "Rare";
                    else if (string.Equals(r.Name, "NPC", StringComparison.OrdinalIgnoreCase)) r.Label = "NPC";
                    else if (EntityDisplayHelper.KnownSemanticRuleNames.Contains(r.Name)) r.Label = r.Name;
                }
            }
            DisplayRules.AppendMissingSemanticRules(rules, _settings.Styles, _settings.ShowMonsters);
            _displayRules.Replace(rules);
            _settings.GlobalRulesExpandedMigrated = true;
            _settings.Save();
            Console.WriteLine("Expanded global display rules (quest, waypoint, portal, bridge, Boss labels).");
        }
        if (!_settings.SemanticNamesMigrated)
        {
            var rules = _displayRules.All.ToList();
            foreach (var r in rules)
            {
                if (string.Equals(r.Name, "Point of Interest", StringComparison.OrdinalIgnoreCase))
                {
                    r.Name = "Map marker";
                    r.Label = "Map marker";
                }
            }
            DisplayRules.AppendMissingSemanticRules(rules, _settings.Styles, _settings.ShowMonsters);
            _displayRules.Replace(rules);
            _settings.SemanticNamesMigrated = true;
            _settings.Save();
            Console.WriteLine("Semantic display rule names (Checkpoint, Map marker, Stash, Town portal).");
        }
        if (!_settings.IconDefaultsMigrated)
        {
            var rules = _displayRules.All.ToList();
            DisplayRules.ApplyIconDefaults(rules);
            _displayRules.Replace(rules);
            _settings.IconDefaultsMigrated = true;
            _settings.Save();
            Console.WriteLine("Applied default PNG sprites and icon sizes to display rules.");
        }
        if (!_settings.EndgameMechanicsMigrated)
        {
            var rules = _displayRules.All.ToList();
            var changed = DisplayRules.MigrateEndgameMechanics(rules);
            if (changed) _displayRules.Replace(rules);
            _settings.Styles.Mechanics = EndgameMechanicCatalog.DefaultMechanicStyles();
            _settings.EndgameMechanicsMigrated = true;
            _settings.Save();
            Console.WriteLine("Endgame mechanics migrated (Abyss, Delirium, catalog labels before Map marker).");
        }
        if (!_settings.EndgameMechanicsV2Migrated)
        {
            var rules = _displayRules.All.ToList();
            if (DisplayRules.MigrateEndgameMechanics(rules))
                _displayRules.Replace(rules);
            _settings.Styles.Mechanics = EndgameMechanicCatalog.DefaultMechanicStyles();
            _settings.EndgameMechanicsV2Migrated = true;
            _settings.Save();
            Console.WriteLine("Endgame mechanics v2: Abyss gates/troves (Object+Other), catalog resolves before Map marker.");
        }
        if (!_settings.EndgameMechanicsV3Migrated)
        {
            var rules = _displayRules.All.ToList();
            if (DisplayRules.MigrateEndgameMechanics(rules))
                _displayRules.Replace(rules);
            _settings.Styles.Mechanics = EndgameMechanicCatalog.DefaultMechanicStyles();
            _settings.EndgameMechanicsV3Migrated = true;
            _settings.Save();
            Console.WriteLine("Endgame mechanics v3: Essence frozen-mob matchers; mechanics before Monster Rare; display_rules authoritative.");
        }
        if (!_settings.EndgameMechanicsV7Migrated)
        {
            var rules = _displayRules.All.ToList();
            if (DisplayRules.MigrateEndgameMechanics(rules))
                _displayRules.Replace(rules);
            _settings.Styles.Mechanics = EndgameMechanicCatalog.DefaultMechanicStyles();
            _settings.EndgameMechanicsV7Migrated = true;
            _settings.Save();
            Console.WriteLine("Endgame mechanics v7: Essence rare+POI cluster; Essence rule any-category + monolith matchers.");
        }
        if (!_settings.EndgameMechanicsV8Migrated)
        {
            var rules = _displayRules.All.ToList();
            if (DisplayRules.MigrateEndgameMechanics(rules))
                _displayRules.Replace(rules);
            foreach (var r in rules)
            {
                if (r.Name.StartsWith("Strongbox", StringComparison.OrdinalIgnoreCase))
                    r.Navigable = true;
            }
            _displayRules.Replace(rules);
            _settings.Styles.Mechanics = EndgameMechanicCatalog.DefaultMechanicStyles();
            _settings.EndgameMechanicsV8Migrated = true;
            _settings.Save();
            Console.WriteLine("Endgame mechanics v8: Strongbox split (Unique, Landmark, Cartographer, Researcher, Abyss, generic).");
        }
        if (!_settings.EndgameMechanicsV9Migrated)
        {
            var rules = _displayRules.All.ToList();
            if (DisplayRules.MigrateEndgameMechanics(rules))
                _displayRules.Replace(rules);
            foreach (var r in rules)
            {
                if (r.Name.StartsWith("Strongbox", StringComparison.OrdinalIgnoreCase))
                    r.Navigable = true;
            }
            _displayRules.Replace(rules);
            _settings.Styles.Mechanics = EndgameMechanicCatalog.DefaultMechanicStyles();
            _settings.EndgameMechanicsV9Migrated = true;
            _settings.Save();
            Console.WriteLine("Endgame mechanics v9: strongbox family complete, Ultimatum/Corruption, opened Unique/Landmark exempt.");
        }
        LogMissingHpBarTextures();
        _displayRulesGen = _ruleEngine.Generation;
        // User-editable overlay on the baked curated landmark table (the "Landmarks" tab). Inject its
        // lookup so the landmark scan honors user edits on top of the shipped community data.
        _landmarkStore = new LandmarkStore(Path.Combine(ConfigDir, "landmarks.json"));
        _live.CuratedLookup = _landmarkStore.Lookup;
        _worldLive.CuratedLookup = _landmarkStore.Lookup;
        _landmarkStoreGen = _landmarkStore.Generation;
        Console.WriteLine($"Hidden entities: {_hidden.Count} pattern(s); display rules: {_displayRules.Count}");
        _imguiOverlay?.AttachEntityStores(_displayRules, _zoneOverrides, _ruleEngine, _hidden);
        _worldThread = new Thread(WorldReaderLoop) { IsBackground = true, Name = "POE2Radar.WorldReader" };
        _worldThread.Start();
        _api = new ApiServer(() => _state, _settings, GetNavSelection, ToggleNavTarget, ClearNavSelection,
                             _hidden, _displayRules, _zoneOverrides, _ruleEngine, _landmarkStore, CurrentTilePaths,
                             AtlasJson, SetAtlasSelection, SetAtlasHighlight, VersionJson, _settings.ApiPort);
        try { _api.Start(); Console.WriteLine($"API on http://localhost:{_settings.ApiPort} (dashboard at /)"); }
        catch (Exception ex) { Console.Error.WriteLine($"API server disabled: {ex.Message}"); }
        Console.WriteLine("Hotkeys: configurable in dashboard (Settings → Hotkeys) or overlay settings. "
                          + "Xbox pad supported when Gamepad hotkeys is enabled.");
        // Best-effort version check against GitHub (non-blocking; never fails startup).
        _ = Task.Run(async () =>
        {
            var u = await UpdateChecker.CheckAsync();
            _update = u;
            if (u.UpdateAvailable)
                Console.WriteLine($"\n*** UPDATE AVAILABLE: {u.Latest} — you have v{u.Current}. Download: {u.Url} ***\n");
            else
                Console.WriteLine($"POE2Radar v{u.Current}" + (u.Latest != null ? " (up to date)." : " (update check unavailable)."));
        });
    }

    private void RunImGuiOverlayThread()
    {
        while (!_shutdown)
        {
            try
            {
                if (_imguiOverlay is null) return;
                _imguiOverlay.Run().GetAwaiter().GetResult();
                if (_shutdown) return;
                CrashLog.Write("ImGuiDx backend stopped", "Overlay loop exited unexpectedly; restarting in 1s.");
                Thread.Sleep(1000);
                RestartImGuiOverlay();
            }
            catch (Exception ex)
            {
                if (_shutdown) return;
                CrashLog.Write("ImGuiDx backend crashed; restarting in 1s", ex);
                Thread.Sleep(1000);
                try { RestartImGuiOverlay(); }
                catch (Exception restartEx)
                {
                    CrashLog.Write("ImGuiDx backend restart failed", restartEx);
                    RequestShutdown();
                    return;
                }
            }
        }
    }

    private ImGuiRadarOverlay CreateImGuiOverlay()
        => new ImGuiRadarOverlay(
            cmd => _commandQueue.Enqueue(cmd),
            id => TogglePathTarget(id),
            corner =>
            {
                _settings.NavMenuCorner = corner;
                _settings.Save();
            },
            () => AddNearestPathTarget(),
            () => ClearPathTargets(),
            _settings);

    private void RestartImGuiOverlay()
    {
        _imguiOverlay = CreateImGuiOverlay();
        _imguiOverlay.AttachEntityStores(_displayRules, _zoneOverrides, _ruleEngine, _hidden);
        if (_gameHwnd != 0)
            TrackImGuiGameWindow(_gameHwnd);
    }

    /// <summary>API (/api/version): this build's version + the latest known on GitHub + a download URL.
    /// Lets the dashboard show an "update available" banner. Null-ish until the async check completes.</summary>
    private object VersionJson()
    {
        var u = _update;
        return new
        {
            current = u?.Current ?? UpdateChecker.Current,
            latest = u?.Latest,
            updateAvailable = u?.UpdateAvailable ?? false,
            url = u?.Url ?? UpdateChecker.ReleasesPage,
        };
    }

    public void Run()
    {
        _gameHwnd = OverlayNative.FindWindowForProcess(_process.ProcessId);
        while (!_shutdown)
        {
            var frameStart = Stopwatch.GetTimestamp();
            if (_gameHwnd == 0) _gameHwnd = OverlayNative.FindWindowForProcess(_process.ProcessId);
            if (_gameHwnd != 0)
                TrackImGuiGameWindow(_gameHwnd);
            Tick();
            // Configurable frame budget (read live so dashboard edits apply immediately). World,
            // live, HP, and metrics reads are independently throttled by their own cadences.
            var hz = Math.Clamp(_settings.FpsCap, 15, 360);
            var budgetMs = 1000.0 / hz;
            var elapsedMs = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
            var sleepMs = budgetMs - elapsedMs;
            if (sleepMs >= 1)
                Thread.Sleep((int)sleepMs);
        }
    }

    private void TrackImGuiGameWindow(nint gameHwnd)
    {
        if (_imguiOverlay is null) return;
        // GameHelper2 parity: anchor to the game's CLIENT area, not the outer window rect.
        // GetWindowRect includes title bar + borders in windowed mode, which shifts every UI
        // coordinate and inflates the winH/1600 UI scale — the radar then never lines up with
        // the game's own minimap.
        if (!OverlayNative.GetClientRect(gameHwnd, out var client)) return;
        var w = client.Right - client.Left;
        var h = client.Bottom - client.Top;
        if (w <= 0 || h <= 0) return;
        var origin = new OverlayNative.POINT { X = 0, Y = 0 };
        if (!OverlayNative.ClientToScreen(gameHwnd, ref origin)) return;
        _imguiOverlay.SetGameBounds(origin.X, origin.Y, w, h);
    }

    private void Tick()
    {
        var tickStart = Stopwatch.GetTimestamp();
        double hpBarsMs = 0;

        while (_commandQueue.TryDequeue(out var action)) action();

        HandleHotkeys();

        var snap = _snapshot;
        var windowWidth = OverlayWidth;
        var windowHeight = OverlayHeight;
        if (windowWidth <= 0 || windowHeight <= 0)
            ResolveGameClientSize(out windowWidth, out windowHeight);
        var realActive = IsGameFocused();
        var drawActive = realActive || _settings.AlwaysShowOverlay;

        if (_liveCadence.IsDue(PerformanceCadence.ClampHz(_settings.LiveRefreshHz, 5, 120)))
            _liveFrame = RefreshLiveFrame(snap, windowWidth, windowHeight, drawActive, realActive);

        var live = _liveFrame;
        if (live.InGame)
            live = RefreshMapLock(live, windowWidth, windowHeight);

        if (live.InGame)
        {
            BuildRenderPaths(live.PlayerGrid, snap);
            if (!_atlasOpen && _hpBarCadence.IsDue(PerformanceCadence.ClampHz(_settings.HpBarRefreshHz, 1, 30)))
            {
                var hpBarsStart = Stopwatch.GetTimestamp();
                RefreshHpBars(snap);
                hpBarsMs = ElapsedMs(hpBarsStart);
            }
            else if (_atlasOpen && _hpFrame.Length > 0)
                _hpFrame = Array.Empty<HpBarTarget>();
        }
        else
        {
            _renderPaths.Clear();
            _renderPathSnapshot = Array.Empty<SelectedPath>();
            if (_atlasOpen) CloseAtlasSession();
            if (_hpFrame.Length > 0) _hpFrame = Array.Empty<HpBarTarget>();
        }

        var largeMap = live.Maps.LargeMap;
        var miniMap = live.Maps.MiniMap;
        _state = new RadarState(live.InGame, snap.AreaHash, snap.AreaLevel, largeMap.IsVisible, largeMap.Zoom, live.PlayerGrid, snap.Entities, snap.Landmarks,
            _hpPct, _manaPct, _esPct, _autoFlask, _flaskNote, _areaCode, _charName, snap.CharLevel, _perfSnapshot,
            MapDiag: _mapDiag,
            MiniMapVisible: miniMap.IsVisible, MiniMapRect: miniMap.HasScreenRect,
            MiniMapW: miniMap.Width, MiniMapH: miniMap.Height,
            GameFocused: realActive, OverlayActive: drawActive,
            OverlayW: windowWidth, OverlayH: windowHeight, GameHwnd: _gameHwnd);

        var atlasProj = AtlasProjection();
        var mapFrame = BuildLargeMapFrame(largeMap, windowWidth, windowHeight, live.PlayerTerrainHeight);
        var miniMapFrame = BuildMiniMapFrame(miniMap, windowWidth, windowHeight, live.PlayerTerrainHeight);
        var visual = _visualSmoother.Update(
            Stopwatch.GetTimestamp(),
            _settings.SmoothOverlayMotion,
            Math.Clamp(_settings.OverlaySmoothingMs, 0, 150),
            new LiveVisualSample(
                live.InGame,
                snap.AreaHash,
                windowWidth,
                windowHeight,
                live.PlayerGrid,
                live.PlayerWorld,
                live.PlayerTerrainHeight,
                largeMap,
                miniMap,
                mapFrame,
                miniMapFrame,
                _atlasOpen,
                live.CameraMatrix));
        _lastMapFrame = mapFrame;
        _lastMiniMapFrame = miniMapFrame;
        _lastPlayerGrid = live.PlayerGrid;
        if (live.InGame)
        {
            UpdateCursorInspect(live);
            HandleCursorPickHotkeys();
        }
        else
        {
            _cursorInspectTitle = null;
            _cursorInspectMeta = null;
        }

        var ctx = new RenderContext(
            InGame: live.InGame,
            Active: drawActive,
            WindowWidth: windowWidth,
            WindowHeight: windowHeight,
            PlayerGrid: visual.PlayerGrid,
            PlayerWorld: visual.PlayerWorld,
            RawPlayerGrid: live.PlayerGrid,
            RawPlayerWorld: live.PlayerWorld,
            Map: largeMap,
            MiniMap: miniMap,
            MapFrame: mapFrame,
            MiniMapFrame: miniMapFrame,
            Entities: snap.Entities,
            Landmarks: snap.Landmarks,
            MapEntities: snap.MapEntities,
            MapLandmarks: snap.MapLandmarks,
            AreaHash: snap.AreaHash,
            Terrain: snap.Terrain,
            ScaleMul: _settings.ScaleMul,
            OffsetX: _settings.OffX,
            OffsetY: _settings.OffY,
            HpPct: _hpPct,
            ManaPct: _manaPct,
            EsPct: _esPct,
            FlaskNote: _flaskNote,
            AreaCode: snap.AreaCode,
            CharLevel: snap.CharLevel,
            CameraMatrix: live.CameraMatrix,
            HideJunk: _settings.HideJunk,
            ImportantOnly: _settings.ImportantOnly,
            GlobalIconScale: _settings.GlobalIconScale,
            ShowPathWorld: _settings.ShowPathWorld,
            ShowGroundWaypoints: _settings.ShowGroundWaypoints,
            ShowPathMap: _settings.ShowPathMap,
            ShowPathMinimap: _settings.ShowPathMinimap,
            SimplePathReplan: _settings.SimplePathReplan,
            WorldPathProjectionZ: _settings.WorldPathProjectionZ,
            UseCuratedLandmarks: _settings.UseCuratedLandmarks,
            ShowMonsters: _settings.ShowMonsters,
            ShowTerrain: _settings.ShowTerrain,
            ShowPlayerBlip: _settings.ShowPlayerBlip,
            HpBarNormal: _settings.HpBarNormal,
            HpBarMagic: _settings.HpBarMagic,
            HpBarRare: _settings.HpBarRare,
            HpBarUnique: _settings.HpBarUnique,
            SelectedPaths: _renderPathSnapshot,
            SelectedIds: snap.SelectedIds,
            Legend: snap.Legend,
            NavMenuExpanded: false,
            NavMenuCorner: _settings.NavMenuCorner,
            ShowPerfStats: _settings.ShowPerfStats,
            ShowFpsOverlay: _settings.ShowFpsOverlay,
            SmoothOverlayMotion: _settings.SmoothOverlayMotion,
            OverlaySmoothingMs: Math.Clamp(_settings.OverlaySmoothingMs, 0, 150),
            ChipSmoothingMs: Math.Clamp(_settings.ChipSmoothingMs, 0, 250),
            PixelSnapLabels: _settings.PixelSnapLabels,
            Perf: _perfSnapshot,
            Styles: _settings.Styles,
            HpBars: _settings.HpBars,
            HpBarTargets: _hpFrame,
            TerrainStyle: _settings.Terrain,
            AtlasOpen: _atlasOpen,
            AtlasNodes: _atlasMarksPublish,
            AtlasShowOnScreenNodes: _settings.AtlasShowOnScreenNodes,
            AtlasTrackedOnly: (_settings.AtlasHighlightTags?.Count ?? 0) > 0,
            AtlasShowNames: _settings.AtlasShowNames,
            AtlasRevealFog: _settings.AtlasRevealFog,
            AtlasOffScreenArrows: _settings.AtlasOffScreenArrows,
            AtlasIconScale: _settings.AtlasIconScale,
            AtlasLabelScale: _settings.AtlasLabelScale,
            AtlasTagCatalog: _atlasOpen ? _atlasTagCatalog.ToArray() : null,
            AtlasScale: (float)atlasProj[0],
            AtlasScaleY: (float)atlasProj[4],
            AtlasOffX: (float)atlasProj[2],
            AtlasOffY: (float)atlasProj[5],
            AtlasShearX: (float)atlasProj[1],
            AtlasShearY: (float)atlasProj[3],
            AtlasPersX: (float)atlasProj[6],
            AtlasPersY: (float)atlasProj[7],
            AtlasStart: (_atlasOpen && _settings.AtlasShowRoute) ? _atlasStartPt : null,
            AtlasEnd: (_atlasOpen && _settings.AtlasShowRoute) ? _atlasEndPt : null,
            AtlasRoute: (_atlasOpen && _settings.AtlasShowRoute && _atlasRoutePublish.Count >= 2) ? _atlasRoutePublish : null,
            CursorInspectTitle: _cursorInspectTitle,
            CursorInspectMeta: _cursorInspectMeta);
        _imguiOverlay?.UpdateContext(ctx);

        var overlayMetrics = _imguiOverlay?.GetRenderMetrics().Snapshot() ?? default;
        _perf.RecordRender(
            overlayMetrics.RenderMs,
            0,
            overlayMetrics.NameplatesMs,
            overlayMetrics.MapMs,
            overlayMetrics.PathsMs,
            overlayMetrics.NavMenuMs,
            overlayMetrics.AtlasMs);
        _processMetrics.Sample(
            _settings.ShowFpsOverlay || _settings.ShowPerfStats,
            PerformanceCadence.ClampHz(_settings.MetricsRefreshHz, 1, 10),
            Math.Clamp(_settings.GpuMetricsRefreshSeconds, 1, 30));

        _perfSnapshot = _perf.RecordFrame(
            tickMs: ElapsedMs(tickStart),
            worldMs: _worldTickMs,
            entitiesMs: 0,
            hpBarsMs: hpBarsMs,
            mainReadCount: _reader.ReadCount,
            mainReadBytes: _reader.BytesRead,
            worldReadCount: _worldReader.ReadCount,
            worldReadBytes: _worldReader.BytesRead,
            failedReads: _reader.FailedReads + _worldReader.FailedReads,
            entityCount: _entities.Count,
            hpBarCount: _hpFrame.Length,
            selectedPathCount: _renderPathSnapshot.Length,
            renderFps: overlayMetrics.RenderFps,
            renderMs: overlayMetrics.RenderMs,
            processCpuPct: _processMetrics.CpuPercent,
            workingSetMb: _processMetrics.WorkingSetMb,
            gpuPercent: _processMetrics.GpuPercent,
            gpuMemoryMb: _processMetrics.GpuMemoryMb);
    }
    private static double ElapsedMs(long start)
        => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private LiveFrameState RefreshLiveFrame(WorldSnapshot snap, int windowWidth, int windowHeight, bool drawActive, bool realActive)
    {
        if (_mainLandmarksInvalid)
        {
            _mainLandmarksInvalid = false;
            _live.LandmarkClusterGap = _appliedClusterGap;
            _live.InvalidateLandmarks();
        }

        var inGame = _live.TryResolve(out var inGameState, out var areaInstance, out var localPlayer);
        if (!inGame)
            return LiveFrameState.Empty;

        _areaInstanceForApi = areaInstance;
        _inGameStateForApi = inGameState;
        _areaHash = _live.AreaHash(areaInstance);

        var player = _live.PlayerGrid(localPlayer) ?? NumVec2.Zero;
        var playerWorld = _live.PlayerWorld(localPlayer) is { } pw
            ? new System.Numerics.Vector3(pw.X, pw.Y, pw.Z)
            : new System.Numerics.Vector3(
                player.X * POE2Radar.Core.Pathfinding.GridConstants.GridToWorld,
                player.Y * POE2Radar.Core.Pathfinding.GridConstants.GridToWorld,
                0f);
        var playerTerrainHeight = _live.PlayerTerrainHeight(localPlayer);

        if (_atlasOpen && !_atlas.IsAtlasOpen(inGameState))
            CloseAtlasSession();

        var maps = _cachedMaps;
        if (!_atlasOpen && drawActive)
        {
            maps = _live.ReadMaps(inGameState, areaInstance, windowWidth, windowHeight);
            _cachedMaps = maps;
            _mapDiag = _live.MapDiagnostics(inGameState, windowWidth, windowHeight);
        }

        MaybeMigratePerTypeRules();
        if (localPlayer != _charNameFor)
        {
            _charNameFor = localPlayer;
            _charName = _live.PlayerName(localPlayer);
        }

        var needsCamera =
            _settings.ShowPathWorld ||
            _renderPathSnapshot.Length > 0 ||
            _hpFrame.Length > 0 ||
            _settings.HpBarNormal || _settings.HpBarMagic || _settings.HpBarRare || _settings.HpBarUnique ||
            _settings.TrackEntityHotkey > 0 || _settings.HideEntityHotkey > 0;
        _cameraMatrix = needsCamera ? CopyCameraMatrix(_live.CameraMatrix(inGameState)) : null;

        if (realActive)
            TickAutoFlask(localPlayer);
        else
            _flaskNote = _autoFlask ? "paused (PoE2 not focused)" : "OFF (F8)";

        return new LiveFrameState(
            true, inGameState, areaInstance, localPlayer, player, playerWorld, playerTerrainHeight,
            maps, _areaHash, _cameraMatrix);
    }

    /// <summary>Fast player lock between live refreshes. Map UI/controller branch reads stay on
    /// LiveRefreshHz via <see cref="RefreshLiveFrame"/> so render FPS does not multiply map reads.</summary>
    private LiveFrameState RefreshMapLock(LiveFrameState live, int windowWidth, int windowHeight)
    {
        if (!live.InGame || live.LocalPlayer == 0 || live.InGameState == 0 || live.AreaInstance == 0)
            return live;

        var player = _live.PlayerGrid(live.LocalPlayer) ?? live.PlayerGrid;
        var playerTerrainHeight = _live.PlayerTerrainHeight(live.LocalPlayer);
        var maps = live.Maps;

        if (player == live.PlayerGrid && maps.Equals(live.Maps) && playerTerrainHeight.Equals(live.PlayerTerrainHeight))
            return live;

        return live with
        {
            PlayerGrid = player,
            PlayerTerrainHeight = playerTerrainHeight,
            Maps = maps,
        };
    }

    private void RefreshHpBars(WorldSnapshot snap)
    {
        if (snap.HpSpecs.Length == 0)
        {
            if (_hpFrame.Length > 0) _hpFrame = Array.Empty<HpBarTarget>();
            return;
        }

        var cap = Math.Clamp(_settings.MaxLiveHpBars, 0, 256);
        if (cap == 0)
        {
            _hpFrame = Array.Empty<HpBarTarget>();
            return;
        }

        var hpFrame = new List<HpBarTarget>(Math.Min(cap, snap.HpSpecs.Length));
        foreach (var spec in snap.HpSpecs.OrderBy(HpBarPriority).Take(cap))
        {
            if (!_live.TryLiveBar(spec.Entity, out var w, out var cur, out var max, out var esCur, out var esMax) || max <= 0 || cur <= 0)
                continue;
            var esFrac = esMax > 0 && esCur > 0 ? Math.Clamp((float)esCur / esMax, 0f, 1f) : 0f;
            hpFrame.Add(new HpBarTarget(w, Math.Clamp((float)cur / max, 0f, 1f), esFrac, spec.Width, spec.Fill, spec.BorderWidth, spec.Border));
        }
        _hpFrame = hpFrame.Count > 0 ? hpFrame.ToArray() : Array.Empty<HpBarTarget>();
    }

    private static int HpBarPriority(HpBarSpec spec)
        => spec.Rarity switch
        {
            Poe2Live.Rarity.Unique => 0,
            Poe2Live.Rarity.Rare => 1,
            Poe2Live.Rarity.Magic => 2,
            Poe2Live.Rarity.Normal => 3,
            _ => 4,
        };

    private static float[]? CopyCameraMatrix(float[]? source)
    {
        if (source is not { Length: >= 16 }) return null;
        var copy = new float[16];
        Array.Copy(source, copy, 16);
        return copy;
    }

    private const float MapScaleDivisor = 677f;

    private MapFrame BuildLargeMapFrame(Poe2Live.MapUi map, int windowWidth, int windowHeight, float playerTerrainHeight)
    {
        // Sikaka/v1.3.0 parity — fullscreen Tab overlay only (draw gated on Map.IsVisible):
        //   center = window center + Shift + DefaultShiftY(-20) + manual offset
        //   scale  = Zoom × (WindowHeight / 677) × ScaleMul
        var w = MathF.Max(1f, windowWidth);
        var h = MathF.Max(1f, windowHeight);
        var (cx, cy) = MapViewportLogic.MapProjectionCenter(
            windowWidth,
            windowHeight,
            map.ShiftX,
            map.ShiftY,
            _settings.OffX,
            _settings.OffY,
            minimapClip: false,
            clipLeft: 0,
            clipTop: 0,
            clipRight: 0,
            clipBottom: 0);
        var center = new NumVec2(cx, cy);
        var scale = (map.Zoom > 0f ? map.Zoom : 1f) * (h / MapScaleDivisor) * _settings.ScaleMul;
        return new MapFrame(center, scale, w, h, map.Element, playerTerrainHeight, NumVec2.Zero, IsMinimap: false);
    }

    private MapFrame BuildMiniMapFrame(Poe2Live.MapUi map, int windowWidth, int windowHeight, float playerTerrainHeight)
    {
        var fallbackSide = MathF.Max(1f, MathF.Min(windowWidth, windowHeight) * 0.28f);
        var width = map.Width;
        var height = map.Height;
        var x = map.PositionX;
        var y = map.PositionY;
        var hasUiFrame =
            map.Element != 0 &&
            map.HasScreenRect &&    // raw unscaled values must never place the on-screen frame
            float.IsFinite(width) && float.IsFinite(height) &&
            float.IsFinite(x) && float.IsFinite(y) &&
            width >= 32f && height >= 32f &&
            width <= MathF.Max(1f, windowWidth) && height <= MathF.Max(1f, windowHeight);

        if (!hasUiFrame)
        {
            width = fallbackSide;
            height = fallbackSide;
            x = MathF.Max(0f, windowWidth - width - 18f);
            y = 18f;
        }

        var (cx, cy) = MapViewportLogic.MapProjectionCenter(
            windowWidth,
            windowHeight,
            map.ShiftX,
            map.ShiftY,
            offsetX: 0f,
            offsetY: 0f,
            minimapClip: true,
            clipLeft: x,
            clipTop: y,
            clipRight: x + width,
            clipBottom: y + height);
        var center = new NumVec2(cx, cy);
        // v1.3.0 parity: minimap scale = Zoom × (clipSide / 677) × ScaleMul — NOT diagonal/240.
        var referenceSide = MathF.Max(1f, MathF.Min(width, height));
        var scale = (map.Zoom > 0f ? map.Zoom : 1f) * (referenceSide / MapScaleDivisor) * _settings.ScaleMul;
        return new MapFrame(center, scale, width, height, map.Element, playerTerrainHeight, new NumVec2(x, y), IsMinimap: true);
    }

    /// <summary>Decide which monsters get an HP bar and precompute each bar's style (width + packed
    /// fill/border colours) at WORLD rate. This is the work that used to run per entity per render frame in
    /// the renderer (rarity gate + rule resolve + colour parse); doing it once per world tick — only for
    /// mobs with a live HP pool — leaves the render-frame path to just re-read position/HP and draw, which
    /// is what keeps 50–100 bars smooth without re-resolving thousands of entities every frame.</summary>
    private List<HpBarSpec> BuildHpSpecsFrom(List<Poe2Live.EntityDot> entities)
    {
        var specs = new List<HpBarSpec>();
        var hb = _settings.HpBars;
        var st = _settings.Styles;
        foreach (var e in entities)
        {
            if (e.Category != Poe2Live.EntityCategory.Monster) continue;
            if (!e.IsAlive || e.HpMax <= 0) continue;
            var on = e.Rarity switch
            {
                Poe2Live.Rarity.Normal => _settings.HpBarNormal,
                Poe2Live.Rarity.Magic  => _settings.HpBarMagic,
                Poe2Live.Rarity.Rare   => _settings.HpBarRare,
                Poe2Live.Rarity.Unique => _settings.HpBarUnique,
                _                      => false,
            };
            if (!on) continue;
            // Match map dots: only skip explicit state hides (dead/opened). Do NOT apply ImportantOnly
            // trash here — magic/rare bars should show even when trash dots are curated off the map.
            var rule = _ruleEngine.ResolveGlobal(e, _entities);
            if (rule is { Hide: true }) continue;
            var (bw, fillHex, borderW, borderHex) = HpBarStyleFor(e, rule, hb, st);
            if (bw <= 0f) continue;
            specs.Add(new HpBarSpec(e.Address, e.Rarity, bw, PackColor(fillHex), borderW, PackColor(borderHex)));
        }
        return specs;
    }
    private static (float Width, string Fill, float BorderW, string BorderHex) HpBarStyleFor(
        Poe2Live.EntityDot e, DisplayRule? rule, HpBarSettings hb, RadarStyles st)
    {
        var fill = rule?.Color ?? MonsterStyleColor(e, st);
        switch (e.Rarity)
        {
            case Poe2Live.Rarity.Normal:
                return (hb.WidthNormal, fill, hb.BorderNormal, hb.BorderColorNormal);
            case Poe2Live.Rarity.Magic:
                return (hb.WidthMagic, fill, hb.BorderMagic, hb.BorderColorMagic);
            case Poe2Live.Rarity.Rare:
                return (hb.WidthRare, fill, hb.BorderRare, hb.BorderColorRare);
            case Poe2Live.Rarity.Unique:
                return (hb.WidthUnique, fill, hb.BorderUnique, hb.BorderColorUnique);
            default:
                return (0f, fill, 0f, hb.BorderColorNormal);
        }
    }

    private static string MonsterStyleColor(Poe2Live.EntityDot e, RadarStyles st)
    {
        switch (e.Rarity)
        {
            case Poe2Live.Rarity.Unique: return st.MonsterUnique.Color;
            case Poe2Live.Rarity.Rare: return st.MonsterRare.Color;
            case Poe2Live.Rarity.Magic: return st.MonsterMagic.Color;
            default: return st.MonsterNormal.Color;
        }
    }

    /// <summary>Parse a "#RRGGBB" hex colour to packed 0xFFRRGGBB once (opacity = 1, matching the old
    /// per-frame ParseColor(hex, 1f) for HP bars). Falls back to opaque white on a malformed string.</summary>
    private static uint PackColor(string hex)
    {
        if (hex is { Length: >= 7 } && hex[0] == '#'
            && byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        return 0xFFFFFFFFu;
    }

    /// <summary>
    /// Auto-flask: press the life/mana flask key when the corresponding pool drops below its
    /// threshold. Hard-gated: enabled + PoE2 is the foreground window + per-flask cooldown.
    /// The life flask's trigger pool is selectable (LifeFlaskMode): Health%, Energy Shield%, or
    /// Either — ES is ignored on builds with no ES pool, so "Either" is safe for a pure-life build.
    /// </summary>
    private void TickAutoFlask(nint localPlayer)
    {
        // No plausible vitals read (Life component missing, or vital offsets drifted past the auto-
        // relocation's reach): DON'T fire — firing on unknown HP would either spam or never trigger.
        // Surface it so a post-patch break is visible instead of silently "armed but never fires".
        if (_live.PlayerVitals(localPlayer) is not { } v)
        {
            _flaskNote = "paused (vitals unreadable — offsets may have drifted)";
            return;
        }
        _hpPct = v.HpPct; _manaPct = v.ManaPct; _esPct = v.EsPct;

        if (!_autoFlask) { _flaskNote = "OFF (F8)"; return; }
        if (!IsGameFocused()) { _flaskNote = "paused (PoE2 not focused)"; return; }
        _flaskNote = "armed";

        // Which pool(s) the single life-flask key watches. ES only participates when a real ES pool is
        // present (HasEs) — a build with no shield never trips the ES branch even in "Either" mode.
        var hpLow = v.HpPct < _settings.LifeThresholdPct;
        var esLow = v.HasEs && v.EsPct < _settings.EsThresholdPct;
        var (lifeTrigger, lifeReason) = _settings.LifeFlaskMode switch
        {
            "EnergyShield" => (esLow, $"es@{v.EsPct:F0}%"),
            "Either"       => (hpLow || esLow, hpLow ? $"life@{v.HpPct:F0}%" : $"es@{v.EsPct:F0}%"),
            _              => (hpLow, $"life@{v.HpPct:F0}%"), // "Health" (default)
        };

        var now = DateTime.UtcNow;
        if (lifeTrigger && now - _lifeFiredAt >= TimeSpan.FromMilliseconds(_settings.LifeCooldownMs))
        {
            SendInputNative.Tap((ushort)_settings.LifeKey); _lifeFiredAt = now; _flaskNote = lifeReason;
        }
        if (v.ManaPct < _settings.ManaThresholdPct &&
            now - _manaFiredAt >= TimeSpan.FromMilliseconds(_settings.ManaCooldownMs))
        {
            SendInputNative.Tap((ushort)_settings.ManaKey); _manaFiredAt = now; _flaskNote = $"mana@{v.ManaPct:F0}%";
        }
    }

    /// <summary>Poll overlay hotkeys (keyboard, mouse VK, or Xbox pad). Map calibration is web-only.</summary>
    private void HandleHotkeys()
    {
        HotkeyPoll.BeginTick(_settings);

        if (HotkeyPressed(_settings.AutoFlaskToggleHotkey, ref _nextToggleAt))
        {
            _autoFlask = !_autoFlask;
            Console.WriteLine($"\nAuto-flask: {(_autoFlask ? "ON" : "OFF")}");
        }

        if (HotkeyPressed(_settings.QuitHotkey, ref _nextQuitAt, debounceMs: 0))
            { Console.WriteLine("\nQuit hotkey — exiting."); RequestShutdown(); }

        if (HotkeyPressed(_settings.OpenDashboardHotkey, ref _nextBrowserAt, debounceMs: 800, requireGameFocus: true))
            OpenDashboard();

        if (HotkeyPressed(_settings.ToggleSettingsHotkey, ref _nextSettingsAt, requireGameFocus: true))
            _imguiOverlay?.ToggleSettings();

        if (DateTime.UtcNow >= _nextPathKeyAt)
        {
            if (HotkeyPressed(_settings.AddNearestPathHotkey, ref _nextPathKeyAt))
                AddNearestPathTarget();
            else if (HotkeyPressed(_settings.ClearPathsHotkey, ref _nextPathKeyAt))
                ClearPathTargets();
        }

        if (HotkeyPressed(_settings.AutoPathToggleHotkey, ref _nextAutoPathToggleAt, requireGameFocus: true))
        {
            _settings.AutoPathNavigable = !_settings.AutoPathNavigable;
            if (_settings.AutoPathNavigable) _settings.ShowPath = true;
            _settings.Save();
            Console.WriteLine($"\nAuto-path: {(_settings.AutoPathNavigable ? "ON" : "OFF")}");
        }

        if (HotkeyPressed(_settings.AtlasPickHotkey, ref _nextInspectAt, debounceMs: 250))
            AtlasRoutePick();
    }

    private bool HotkeyPressed(int binding, ref DateTime nextAt, int debounceMs = 300, bool requireGameFocus = false)
    {
        if (binding <= 0 || !HotkeyPoll.IsDown(binding)) return false;
        if (requireGameFocus && !IsGameFocused()) return false;
        if (debounceMs > 0 && DateTime.UtcNow < nextAt) return false;
        if (debounceMs > 0) nextAt = DateTime.UtcNow.AddMilliseconds(debounceMs);
        return true;
    }

    /// <summary>F4/F5 cursor picks — run after map frames + entities are refreshed this tick.</summary>
    private void HandleCursorPickHotkeys()
    {
        HotkeyPoll.BeginTick(_settings);
        if (!IsGameFocused() || _areaInstanceForApi == 0) return;

        var settingsOpen = _imguiOverlay?.IsSettingsOpen == true;
        if (_settingsWereOpen && !settingsOpen)
        {
            _hideKeyWasDown = false;
            _trackKeyWasDown = false;
        }
        _settingsWereOpen = settingsOpen;
        if (settingsOpen) return;

        var hideDown = _settings.HideEntityHotkey > 0 && HotkeyPoll.IsDown(_settings.HideEntityHotkey);
        if (hideDown && !_hideKeyWasDown)
            HideEntityUnderCursor();
        _hideKeyWasDown = hideDown;

        var inspectDown = _settings.TrackEntityHotkey > 0 && HotkeyPoll.IsDown(_settings.TrackEntityHotkey);
        if (inspectDown && !_trackKeyWasDown)
            InspectEntityUnderCursor();
        _trackKeyWasDown = inspectDown;
    }

    private bool TryGetCursorClient(out NumVec2 cursor, string? logPrefix = null)
    {
        if (!GetCursorPos(out var screenPt))
        {
            if (logPrefix != null)
                Console.WriteLine($"\n[{logPrefix}] could not read cursor position.");
            cursor = default;
            return false;
        }

        var client = new OverlayNative.POINT { X = screenPt.X, Y = screenPt.Y };
        if (_gameHwnd == 0 || !OverlayNative.ScreenToClient(_gameHwnd, ref client))
        {
            if (logPrefix != null)
                Console.WriteLine($"\n[{logPrefix}] could not map cursor to game client area.");
            cursor = default;
            return false;
        }

        cursor = new NumVec2(client.X, client.Y);
        return true;
    }

    private float[]? ResolveCameraMatrixForPick()
    {
        var live = _liveFrame;
        if (live.CameraMatrix is { Length: >= 16 }) return live.CameraMatrix;
        if (_cameraMatrix is { Length: >= 16 }) return _cameraMatrix;
        if (!live.InGame || live.InGameState == 0) return null;
        var matrix = CopyCameraMatrix(_live.CameraMatrix(live.InGameState));
        if (matrix != null) _cameraMatrix = matrix;
        return matrix;
    }

    private void UpdateCursorInspect(LiveFrameState live)
    {
        _cursorInspectTitle = null;
        _cursorInspectMeta = null;
        if (_areaInstanceForApi == 0 || _atlasOpen) return;
        if (_imguiOverlay?.IsSettingsOpen == true) return;
        if (!TryGetCursorClient(out var cursor)) return;
        if (!TryPickEntityAt(cursor, live.PlayerGrid, out var entity, out _)) return;

        var rule = _ruleEngine.Resolve(entity, _areaCode, _settings.ImportantOnly, _entities);
        _cursorInspectTitle = EntityLabel(entity, rule);
        _cursorInspectMeta = entity.Metadata;
    }

    private bool TryPickEntityAt(
        NumVec2 cursor,
        NumVec2 playerGrid,
        out Poe2Live.EntityDot entity,
        out string? failReason)
    {
        entity = default;
        failReason = null;

        ResolveGameClientSize(out var windowWidth, out var windowHeight);
        if (windowWidth <= 0 || windowHeight <= 0)
        {
            failReason = "game client size unavailable";
            return false;
        }

        IReadOnlyList<Poe2Live.EntityDot> entities =
            _snapshot.Entities.Length > 0 ? _snapshot.Entities : _entities;
        var (largeFrame, miniFrame) = ActiveMapFrames(_liveFrame.Maps);
        if (!EntityUnderCursorPicker.TryPick(
                cursor,
                windowWidth,
                windowHeight,
                largeFrame,
                miniFrame,
                playerGrid,
                entities,
                _settings.ImportantOnly,
                _settings.Styles,
                e => _ruleEngine.Resolve(e, _areaCode, _settings.ImportantOnly, _entities),
                _settings.GlobalIconScale,
                ResolveCameraMatrixForPick(),
                out entity))
        {
            failReason = "no entity under cursor";
            return false;
        }

        return true;
    }

    private (MapFrame Large, MapFrame Mini) ActiveMapFrames(Poe2Live.MapViews maps)
    {
        var large = MapOverlayDrawPolicy.ShouldDrawLargeMap(maps.LargeMap) ? _lastMapFrame : default;
        var mini = MapOverlayDrawPolicy.ShouldDrawMinimap(maps.LargeMap, maps.MiniMap) ? _lastMiniMapFrame : default;
        return (large, mini);
    }

    private bool TryPickEntityUnderCursor(string failPrefix, out Poe2Live.EntityDot entity)
    {
        entity = default;
        if (!TryGetCursorClient(out var cursor, failPrefix)) return false;

        if (!TryPickEntityAt(cursor, _lastPlayerGrid, out entity, out var failReason))
        {
            Console.WriteLine($"\n[{failPrefix}] {failReason}.");
            return false;
        }

        return true;
    }

    /// <summary>Hotkey: pick the map icon under the cursor and add its TypeToken to the hidden-metadata cull list.</summary>
    private void HideEntityUnderCursor()
    {
        if (!TryPickEntityUnderCursor("hide", out var entity)) return;

        var pattern = EntityDisplayHelper.TypeToken(entity.Metadata);
        if (pattern.Length == 0)
        {
            var slash = entity.Metadata.LastIndexOf('/');
            pattern = slash >= 0 ? entity.Metadata[(slash + 1)..] : entity.Metadata;
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            Console.WriteLine("\n[hide] entity has no metadata token.");
            return;
        }

        if (_hidden.Add(pattern))
        {
            Console.WriteLine($"\n[hide] Added pattern '{pattern}' — metadata: {entity.Metadata}");
            _entities = _entities.Where(ent => !_hidden.IsHidden(ent.Metadata)).ToList();
        }
        else
            Console.WriteLine($"\n[hide] Pattern '{pattern}' already hidden.");
    }

    /// <summary>Hotkey: print entity identity to the console (no path selection).</summary>
    private void InspectEntityUnderCursor()
    {
        if (!TryPickEntityUnderCursor("inspect", out var entity)) return;

        var rule = _ruleEngine.Resolve(entity, _areaCode, _settings.ImportantOnly, _entities);
        var label = EntityLabel(entity, rule);
        var token = EntityDisplayHelper.TypeToken(entity.Metadata);
        var ruleName = rule?.Name ?? "(none)";
        var hideFlag = rule is { Hide: true } ? "yes" : "no";
        var navFlag = rule is { Navigable: true } ? "yes" : "no";

        Console.WriteLine($"\n[inspect] Label: {label}");
        Console.WriteLine($"          Category: {entity.Category}");
        Console.WriteLine($"          TypeToken: {token}");
        Console.WriteLine($"          Metadata: {entity.Metadata}");
        Console.WriteLine($"          Id: e:{entity.Id}  Grid: {entity.Grid.X:F0},{entity.Grid.Y:F0}");
        Console.WriteLine($"          Rule: {ruleName} (hide={hideFlag} nav={navFlag})");
    }

    /// <summary>F10: pick the atlas tile under the cursor and advance the route workflow (START → END → reset).
    /// Inverts the same projection the renderer draws with (relPos = screen / scale) to map the cursor into
    /// canvas space, then picks the tile whose box CONTAINS it (fallback: nearest centre). Stores the pick by
    /// GRID coord so the route survives pan/zoom and tiles going off-screen. (No on-screen tile-details
    /// tooltip — that interfered with the point-to-point selection; the pick is just echoed to the console.)</summary>
    private void AtlasRoutePick()
    {
        if (_inGameStateForApi == 0 || !GetCursorPos(out var pt)) { Console.WriteLine("\n[atlas route] not in game."); return; }
        // Invert the shared projection: for screen = relPos × scale (offset/shear/persp = 0), relPos = screen/scale.
        var proj = AtlasProjection();
        double scaleX = Math.Abs(proj[0]) > 1e-6 ? proj[0] : 1, scaleY = Math.Abs(proj[4]) > 1e-6 ? proj[4] : 1;
        double curX = pt.X / scaleX, curY = pt.Y / scaleY; // cursor in canvas/relPos units

        Poe2Atlas.AtlasNodeLive? bestIn = null, bestAny = null; double bdIn = 1e18, bdAny = 1e18;
        foreach (var n in _atlas.ReadNodes(_inGameStateForApi))
        {
            // Consider EVERY node (not just the local-Visible ones): the game leaves the visible bit OFF for
            // undiscovered / fog-of-war tiles even though it draws them at a valid relPos, so filtering it made
            // F10 skip fogged tiles and snap to the nearest visible neighbour. Routing must reach those tiles.
            if (!float.IsFinite(n.X) || !float.IsFinite(n.Y)) continue;
            double dx = curX - n.X, dy = curY - n.Y, d = dx * dx + dy * dy;
            if (d < bdAny) { bdAny = d; bestAny = n; }     // nearest centre (fallback)
            double hw = (n.W > 1 ? n.W : 40) * 0.5, hh = (n.H > 1 ? n.H : 40) * 0.5; // tile half-extents (canvas units)
            if (Math.Abs(dx) <= hw && Math.Abs(dy) <= hh && d < bdIn) { bdIn = d; bestIn = n; } // cursor inside the tile box
        }
        if ((bestIn ?? bestAny) is not { } b) { Console.WriteLine("\n[atlas route] no tile under cursor (is the Atlas open?)."); return; }

        // 1st press → set START · 2nd press → set END (route computed each tick) · 3rd → reset.
        string stage;
        if (_atlasStartGrid is null) { _atlasStartGrid = b.Grid; _atlasGoalGrid = null; stage = $"START = {b.Grid} '{b.MapName}'  (F10 another tile to set END)"; }
        else if (_atlasGoalGrid is null) { _atlasGoalGrid = b.Grid; stage = $"END = {b.Grid} '{b.MapName}'  (routing from {_atlasStartGrid}; F10 again to reset)"; }
        else { _atlasStartGrid = null; _atlasGoalGrid = null; stage = "route RESET (F10 a tile to set a new START)"; }
        Console.WriteLine($"\n[atlas route] {stage}");
    }

    /// <summary>The atlas projection, derived LIVE from the game window height and live atlas zoom:
    /// screen = relPos × (UIscale×zoom), UIscale = winH/1600. Pure uniform scale, NO offset — relPos
    /// already has pan baked in and the canvas origin sits at screen (0,0) (the long-proven 1080p default
    /// was scale≈0.572 / offset 0). This is what lines up at any resolution with no hand-calibration.
    /// Returned in the 8-coeff homography layout (shear + perspective + offset = 0).</summary>
    private double[] AtlasProjection()
    {
        var h = OverlayHeight;
        float uiScale = h > 0 ? h / 1600f : 1080f / 1600f;
        float scale = uiScale * (_atlasZoom > 0.01f ? _atlasZoom : 0.85f);
        return new double[] { scale, 0, 0, 0, scale, 0, 0, 0 };
    }

    // ── Unified navigation-target selection (draw-only guidance, multi-select). ──────────────
    // Model: _navTargets is one list built each world tick from BOTH tile landmarks AND entity POIs,
    // each addressed by a STABLE STRING id ("t:<path>" / "e:<entityId>"). _selectedIds is the ordered
    // set of selected ids; an id's position in that list is its color SLOT (0..7), so each selected
    // target draws its own A* route + legend swatch in its own color. F6 adds the nearest not-yet-
    // selected target; F7 clears all; clicking a legend row toggles that target. The selection is
    // capped at MaxSelectedTargets (palette size) so colors stay distinct and per-tick planning is
    // bounded. On a zone change the selection is cleared and the persistent auto-nav patterns re-
    // select matching targets.

    /// <summary>
    /// Build the unified navigation-target list for this world tick: every tile landmark first, then
    /// qualifying entity targets nearest-first. Nav qualification is RULE-DRIVEN — the single source of
    /// truth — so what auto-paths is exactly what the display rules say: an entity qualifies iff its
    /// resolved rule is matched, not <see cref="DisplayRule.Hide"/>, and has <see cref="DisplayRule.Navigable"/>.
    /// (No hardcoded POI/unique clauses — those bypassed the rules and made waypoints un-excludable.)
    /// Each target carries <see cref="NavTarget.AutoPath"/> mirroring that flag. Deduped by id.
    /// </summary>
    private List<NavTarget> BuildNavTargets(NumVec2 player)
    {
        var targets = new List<NavTarget>(_landmarks.Count + 16);
        var seen = new HashSet<string>();

        // (a) Tile landmarks — id "t:<key>" (per-cluster). Auto-path when a Tile rule opts in.
        foreach (var lm in _landmarks)
        {
            var id = "t:" + lm.Key;
            if (!seen.Add(id)) continue;
            var autoPath = _displayRules.ResolveTile(lm.Path, requireMatch: false)?.Navigable ?? false;
            var radius = LandmarkGoalSearchRadius(lm.TileCount);
            targets.Add(new NavTarget(
                id,
                LandmarkLabel(lm),
                lm.Center,
                lm.Path,
                IsEntity: false,
                AutoPath: autoPath,
                TileCount: lm.TileCount,
                GoalSearchRadius: radius,
                RouteAnchors: lm.RouteAnchors ?? Array.Empty<PathCell>()));
        }

        // (b) Entity targets — id "e:<entityId>", nearest-first. An entity qualifies only when its
        // resolved display rule says so (visible + navigable); this is what lets the Entities-tab
        // Nav/Hide toggles (and the web dashboard) actually include/exclude a type from auto-path.
        var pois = _entities
            .Where(e => e.IsAlive && !e.IconComplete)
            .Select(e => (e, rule: _ruleEngine.Resolve(e, _areaCode, _settings.ImportantOnly, _entities)))
            .Where(x => x.rule is { Hide: false, Navigable: true })
            .OrderBy(x => NumVec2.DistanceSquared(x.e.Grid, player));
        foreach (var (e, rule) in pois)
        {
            var id = "e:" + e.Id;
            if (!seen.Add(id)) continue;
            targets.Add(new NavTarget(
                id,
                EntityLabel(e, rule),
                e.Grid,
                e.Metadata,
                IsEntity: true,
                AutoPath: true,
                TileCount: 1,
                GoalSearchRadius: 24,
                RouteAnchors: Array.Empty<PathCell>()));
        }

        return targets;
    }

    private static int LandmarkGoalSearchRadius(int tileCount)
        => Math.Clamp((int)MathF.Round(23f + MathF.Sqrt(Math.Max(1, tileCount)) * 23f), 32, 96);

    /// <summary>Remember the friendly label + last known grid for currently visible nav targets, so a
    /// selected entity keeps a readable label and route after it leaves the live entity set.</summary>
    private void RefreshTargetSnapshots(IReadOnlyList<NavTarget> targets)
    {
        if (targets.Count == 0) return;

        var now = DateTime.UtcNow;
        var selected = SnapshotSelection();
        var selectedSet = selected.Count == 0
            ? null
            : new HashSet<TargetSnapshotKey>(selected.Select(TargetSnapshotKeyFor));

        lock (_targetSnapshotLock)
        {
            foreach (var t in targets)
                _targetSnapshots[TargetSnapshotKeyFor(t.Id)] = new TargetSnapshot(
                    t.Name,
                    t.Grid,
                    t.IsEntity,
                    now,
                    t.TileCount,
                    t.GoalSearchRadius,
                    t.RouteAnchors ?? Array.Empty<PathCell>());
            PruneTargetSnapshotsLocked(selectedSet);
        }
    }

    private bool TryGetTargetSnapshot(string id, out TargetSnapshot snapshot)
    {
        lock (_targetSnapshotLock)
            return _targetSnapshots.TryGetValue(TargetSnapshotKeyFor(id), out snapshot);
    }

    private TargetSnapshotKey TargetSnapshotKeyFor(string id) => new(_areaHash, id);

    private void PruneTargetSnapshotsLocked(HashSet<TargetSnapshotKey>? selected)
    {
        if (_targetSnapshots.Count <= MaxTargetSnapshots) return;

        var over = _targetSnapshots.Count - MaxTargetSnapshots;
        var removable = _targetSnapshots
            .Where(kv => selected is null || !selected.Contains(kv.Key))
            .OrderBy(kv => kv.Value.LastSeenUtc)
            .Take(over)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in removable)
            _targetSnapshots.Remove(id);
    }

    /// <summary>Zone change: remember the leaving zone's selection (by its instance hash), then either
    /// RESTORE the selection we previously had for the zone we're entering (so a town round-trip keeps
    /// your pathing) or — on a first visit — seed it from the persistent auto-nav patterns. Trackers are
    /// NOT touched here — the per-tick reconciliation (ReconcileTrackers) syncs them to _selectedIds.</summary>
    private void OnAreaChanged(NumVec2 player)
    {
        int count; bool restored;
        lock (_navLock)
        {
            // Save what was selected in the zone we're leaving, keyed by ITS instance hash.
            if (_selectionAreaHash != 0) RememberZoneSelection(_selectionAreaHash, _selectedIds);

            _selectedIds.Clear();
            _autoSelectedIds.Clear();
            _selectionCapWarned = false;
            _selectionAreaHash = _areaHash;

            // Returning to a remembered instance → restore its selection verbatim (the user's explicit
            // choices win, including an intentionally-empty one, so a zone they cleared stays cleared).
            List<string>? remembered = null;
            restored = _areaHash != 0 && _zoneSelections.TryGetValue(_areaHash, out remembered);
            if (restored)
            {
                foreach (var id in remembered!)
                {
                    if (_selectedIds.Count >= MaxSelectedTargets) break;
                    if (!_selectedIds.Contains(id)) _selectedIds.Add(id);
                }
            }
            else if (_settings.AutoPathNavigable)
            {
                // First visit to this instance with auto-path on: seed the nearest navigation targets so
                // routes appear immediately (the per-tick AutoSelectNavigable keeps them reconciled after).
                foreach (var id in _navTargets
                             .OrderBy(t => NumVec2.DistanceSquared(t.Grid, player))
                             .Select(t => t.Id))
                {
                    if (_selectedIds.Count >= MaxSelectedTargets) break;
                    if (!_selectedIds.Contains(id))
                    {
                        _selectedIds.Add(id);
                        _autoSelectedIds.Add(id);
                    }
                }
            }
            count = _selectedIds.Count;
        }
        _renderPaths.Clear();

        if (count > 0)
            Console.WriteLine($"\nNav: {(restored ? "restored" : "auto-selected")} {count} target(s) on zone change.");
    }

    /// <summary>
    /// Drop selected ENTITY targets the game has marked complete (IconComplete — e.g. a claimed
    /// expedition / used incursion device). Such an entity is hidden from the map and excluded from
    /// the nav-target list, but it lingers (faded) in the live entity set, so <see cref="TryResolveTargetGrid"/>
    /// would still resolve it and the route would keep pathing there. Pruning the id stops the route
    /// (its tracker is removed by the next ReconcileTrackers) and "sticks" via the per-zone memory.
    /// <para>Only prunes targets whose entity is PRESENT-and-complete — an entity merely out of network
    /// range (temporarily absent) is left selected so it resumes when you return to it.</para>
    /// </summary>
    private void PruneCompletedTargets()
    {
        lock (_navLock)
        {
            if (_selectedIds.Count == 0) return;
            _selectedIds.RemoveAll(id =>
            {
                if (!id.StartsWith("e:", StringComparison.Ordinal) || !uint.TryParse(id.AsSpan(2), out var eid))
                    return false;
                foreach (var e in _entities)
                    if (e.Id == eid)
                    {
                        if (e.IconComplete) _autoSelectedIds.Remove(id);
                        return e.IconComplete; // present → prune iff completed; else keep
                    }
                return false; // absent (out of range) → keep; it may return
            });
        }
    }

    /// <summary>When <see cref="RadarSettings.AutoPathNavigable"/> is on, keep the selection filled with
    /// the NEAREST navigation targets (up to the cap). The candidate set is every nav target — which by
    /// construction is already the "navigation-worthy" set: game POIs, tile landmarks/transitions, unique
    /// monsters, and any entity type a display rule flags Auto-path (which is what lets those extra types
    /// enter <see cref="BuildNavTargets"/> at all). Manual selections (F6/legend/API) are preserved; ids
    /// the user removed stay out until they leave and re-enter the navigable set.</summary>
    private void AutoSelectNavigable(NumVec2 player)
    {
        if (!_settings.AutoPathNavigable)
        {
            if (_autoSelectedIds.Count == 0) return;
            lock (_navLock)
            {
                foreach (var id in _autoSelectedIds)
                    _selectedIds.Remove(id);
                _autoSelectedIds.Clear();
            }
            return;
        }

        var candidates = _navTargets
            .OrderBy(t => NumVec2.DistanceSquared(t.Grid, player))
            .Select(t => t.Id)
            .ToList();

        lock (_navLock)
        {
            var manual = new HashSet<string>(_selectedIds.Where(id => !_autoSelectedIds.Contains(id)));
            var desiredAuto = new List<string>();
            foreach (var id in candidates)
            {
                if (manual.Contains(id)) continue;
                if (manual.Count + desiredAuto.Count >= MaxSelectedTargets) break;
                desiredAuto.Add(id);
            }

            var desiredAutoSet = new HashSet<string>(desiredAuto);
            foreach (var id in _autoSelectedIds.ToList())
            {
                if (!desiredAutoSet.Contains(id))
                {
                    _selectedIds.Remove(id);
                    _autoSelectedIds.Remove(id);
                }
            }

            foreach (var id in desiredAuto)
            {
                if (_selectedIds.Contains(id))
                {
                    if (!manual.Contains(id)) _autoSelectedIds.Add(id);
                    continue;
                }
                if (_selectedIds.Count >= MaxSelectedTargets) break;
                _selectedIds.Add(id);
                _autoSelectedIds.Add(id);
            }
        }
    }

    /// <summary>Store a copy of <paramref name="ids"/> under <paramref name="hash"/>, evicting the
    /// oldest remembered zone when the table is full. Call under <see cref="_navLock"/>.</summary>
    private void RememberZoneSelection(uint hash, List<string> ids)
    {
        if (!_zoneSelections.ContainsKey(hash))
        {
            if (_zoneOrder.Count >= MaxRememberedZones)
            {
                _zoneSelections.Remove(_zoneOrder[0]);
                _zoneOrder.RemoveAt(0);
            }
            _zoneOrder.Add(hash);
        }
        _zoneSelections[hash] = new List<string>(ids);
    }

    /// <summary>Surfacing matcher fed to Poe2Live: a terrain tile surfaces as a landmark when a user
    /// landmark pattern matches OR a (non-hide) "Tile" display rule with explicit match terms matches.
    /// Returns the label to show (empty string = use the tile's derived name), or null to not surface.</summary>
    private string? TileLandmarkMatch(string tilePath)
    {
        var tr = _displayRules.ResolveTile(tilePath, requireMatch: true);
        return tr is { Hide: false } ? (tr.Label ?? "") : null;
    }

    /// <summary>Distinct terrain-tile paths for the current area (served by /api/tiles for the add-rule
    /// picker). Empty when not in game. Cached per area inside Poe2Live.</summary>
    private IReadOnlyList<string> CurrentTilePaths()
        => _areaInstanceForApi != 0 ? _live.TilePaths(_areaInstanceForApi) : Array.Empty<string>();


    /// <summary>F6: add the nearest navigation target not already selected into the selection.</summary>
    private void AddNearestPathTarget()
    {
        if (_navTargets.Count == 0) return;
        var player = _state.Player;

        // _navTargets isn't fully distance-sorted (tiles come first), so scan for the nearest
        // unselected target by grid distance. Snapshot the selection to test membership.
        var selected = SnapshotSelection();
        var bestId = (string?)null;
        var bestD = float.MaxValue;
        foreach (var t in _navTargets)
        {
            if (selected.Contains(t.Id)) continue;
            var d = NumVec2.DistanceSquared(t.Grid, player);
            if (d < bestD) { bestD = d; bestId = t.Id; }
        }
        if (bestId is not null) ToggleSelectionCore(bestId); // shares the cap check + locked mutate + log
    }

    /// <summary>F7: clear the entire path selection. Only edits _selectedIds (under the lock); the
    /// per-tick reconciliation removes the now-orphaned trackers.</summary>
    private void ClearPathTargets()
    {
        bool wasEmpty;
        lock (_navLock)
        {
            wasEmpty = _selectedIds.Count == 0;
            _selectedIds.Clear();
            _autoSelectedIds.Clear();
            _selectionCapWarned = false;
        }
        if (!wasEmpty) Console.WriteLine("\nPath targets: cleared");
    }

    /// <summary>
    /// Toggle a navigation target by its stable id (legend-row click / F6 / API). Delegates to the
    /// single locked toggle core so in-game and API mutations share identical semantics.
    /// </summary>
    private void TogglePathTarget(string id) => ToggleSelectionCore(id);

    /// <summary>Add a target id if absent and under the cap. Returns false if already selected or full.</summary>
    private bool AddSelectionCore(string id, bool logSelection = true)
    {
        if (string.IsNullOrEmpty(id)) return false;

        bool added;
        string labels;
        lock (_navLock)
        {
            if (_selectedIds.Contains(id)) return false;
            if (_selectedIds.Count >= MaxSelectedTargets)
            {
                if (!_selectionCapWarned)
                {
                    Console.WriteLine($"\nPath targets: selection full ({MaxSelectedTargets}); ignoring add.");
                    _selectionCapWarned = true;
                }
                return false;
            }

            _selectedIds.Add(id);
            _selectionCapWarned = false;
            added = true;
            labels = string.Join(", ", _selectedIds.Select(TargetLabel));
        }

        if (added && logSelection) Console.WriteLine($"\nPath targets: {labels}");
        return added;
    }

    /// <summary>
    /// THE one place the selection set is mutated. Adds the id if absent (unless at the cap), removes
    /// it if present — all under <see cref="_navLock"/>. Does NOT touch trackers (those are created/
    /// removed by the tick-thread reconciliation from _selectedIds), so it is safe to call from the
    /// HTTP thread. Returns the new selection labels for logging.
    /// </summary>
    private void ToggleSelectionCore(string id, bool logSelection = true)
    {
        if (string.IsNullOrEmpty(id)) return;

        bool changed;
        string labels;
        lock (_navLock)
        {
            if (_selectedIds.Remove(id))
            {
                _autoSelectedIds.Remove(id);
                _selectionCapWarned = false;
                changed = true;
            }
            else if (_selectedIds.Count >= MaxSelectedTargets)
            {
                if (!_selectionCapWarned)
                {
                    Console.WriteLine($"\nPath targets: selection full ({MaxSelectedTargets}); ignoring add.");
                    _selectionCapWarned = true;
                }
                return; // over cap — ignore the add
            }
            else
            {
                _selectedIds.Add(id);
                changed = true;
            }

            labels = _selectedIds.Count == 0 ? "none" : string.Join(", ", _selectedIds.Select(TargetLabel));
        }

        if (changed && logSelection) Console.WriteLine($"\nPath targets: {labels}");
    }

    /// <summary>Snapshot the current selection ids (under the lock) into a fresh list — the standard
    /// way every reader observes the selection without holding the lock during its work.</summary>
    private List<string> SnapshotSelection()
    {
        lock (_navLock) return new List<string>(_selectedIds);
    }

    /// <summary>
    /// Tick-thread tracker reconciliation: bring the (tick-thread-owned) <see cref="_trackers"/> map in
    /// line with the selection. Creates a <see cref="RouteTracker"/> (and enqueues its initial replan)
    /// for any selected id lacking one, and removes trackers whose id is no longer selected (their
    /// in-flight results are ignored on drain). This is the ONLY code that adds/removes trackers, so
    /// API-thread selection edits never race the tracker map. Takes a selection snapshot.
    /// </summary>


    /// <summary>
    /// Resolve ANY selected id to display/planning info. Live targets win; cached last-known targets
    /// keep selected entity routes readable/drawable after they leave read range.
    /// </summary>
    private bool TryResolveTargetInfo(
        string id,
        IReadOnlyList<NavTarget> navTargets,
        IReadOnlyList<Poe2Live.Landmark> landmarks,
        IReadOnlyList<Poe2Live.EntityDot> entities,
        uint areaHash,
        out TargetRenderInfo info)
    {
        info = default;
        if (string.IsNullOrEmpty(id) || id.Length < 2) return false;

        foreach (var t in navTargets)
        {
            if (t.Id != id) continue;
            info = new TargetRenderInfo(
                id,
                t.Name,
                t.Grid,
                t.IsEntity,
                NavTargetStatus.Live,
                t.TileCount,
                t.GoalSearchRadius,
                t.RouteAnchors ?? Array.Empty<PathCell>());
            return true;
        }

        if (id.StartsWith("t:", StringComparison.Ordinal))
        {
            var key = id[2..];
            foreach (var lm in landmarks)
            {
                if (lm.Key != key) continue;
                var label = LandmarkLabel(lm);
                var radius = LandmarkGoalSearchRadius(lm.TileCount);
                var anchors = lm.RouteAnchors ?? Array.Empty<PathCell>();
                RememberTargetSnapshot(id, label, lm.Center, isEntity: false, lm.TileCount, radius, anchors);
                info = new TargetRenderInfo(
                    id,
                    label,
                    lm.Center,
                    IsEntity: false,
                    NavTargetStatus.Live,
                    lm.TileCount,
                    radius,
                    anchors);
                return true;
            }
        }
        else if (id.StartsWith("e:", StringComparison.Ordinal) && uint.TryParse(id[2..], out var entityId))
        {
            foreach (var e in entities)
            {
                if (e.Id != entityId) continue;
                var label = EntityLabel(e, _ruleEngine.Resolve(e, _areaCode, _settings.ImportantOnly, entities));
                RememberTargetSnapshot(id, label, e.Grid, isEntity: true, 1, 24, Array.Empty<PathCell>());
                info = new TargetRenderInfo(
                    id,
                    label,
                    e.Grid,
                    IsEntity: true,
                    NavTargetStatus.Live,
                    1,
                    24,
                    Array.Empty<PathCell>());
                return true;
            }
        }

        if (TryGetTargetSnapshot(id, out var cached))
        {
            info = new TargetRenderInfo(
                id,
                cached.Label,
                cached.Grid,
                cached.IsEntity,
                NavTargetStatus.Cached,
                cached.TileCount,
                cached.GoalSearchRadius,
                cached.RouteAnchors);
            return true;
        }

        return false;
    }

    private bool TryResolveTargetGrid(
        string id,
        IReadOnlyList<Poe2Live.Landmark> landmarks,
        IReadOnlyList<Poe2Live.EntityDot> entities,
        out NumVec2 grid)
    {
        if (TryResolveTargetInfo(id, _navTargets, landmarks, entities, _areaHash, out var info))
        {
            grid = info.Grid;
            return true;
        }

        grid = default;
        return false;
    }

    /// <summary>Display label for a selected id: live nav target first, cached last-known label second,
    /// raw id only when the target was never observed.</summary>

    private void RememberTargetSnapshot(
        string id,
        string label,
        NumVec2 grid,
        bool isEntity,
        int tileCount,
        int goalSearchRadius,
        PathCell[] routeAnchors)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (_targetSnapshotLock)
            _targetSnapshots[TargetSnapshotKeyFor(id)] = new TargetSnapshot(
                label,
                grid,
                isEntity,
                DateTime.UtcNow,
                tileCount,
                goalSearchRadius,
                routeAnchors);
    }

    private void EnqueueReplan(string id, RouteTracker tracker, TargetRenderInfo info, NumVec2 player)
    {
        if (_worldTerrain is not { } terrain)
        {
            tracker.MarkWaitingForTerrain();
            return;
        }
        tracker.MarkReplanRequested();
        _replanner.Enqueue(new BackgroundReplanner.Request(
            id,
            terrain,
            ((int)MathF.Round(player.X), (int)MathF.Round(player.Y)),
            ((int)MathF.Round(info.Grid.X), (int)MathF.Round(info.Grid.Y)),
            info.GoalSearchRadius,
            info.RouteAnchors,
            _worldDoorOverrides));
    }

    private string TargetLabel(string id)
    {
        return TryResolveTargetInfo(id, _navTargets, _landmarks, _entities, _areaHash, out var info) ? info.Label : id;
    }

    /// <summary>Friendly display label for a tile landmark (curated if enabled + present, else derived).</summary>
    private string LandmarkLabel(Poe2Live.Landmark lm)
        => EntityDisplayHelper.FormatLandmarkLabel(
            lm.Path,
            _settings.UseCuratedLandmarks ? lm.CuratedName : null,
            lm.Name,
            _entities,
            _areaCode);

    /// <summary>One-time: relocate per-token rows from display_rules.json into zone overrides for the
    /// current area code (legacy Types-in-zone wrote globals).</summary>
    private void MaybeMigratePerTypeRules()
    {
        if (_settings.PerTypeRulesMigrated || string.IsNullOrEmpty(_areaCode)) return;

        var rules = _displayRules.All.ToList();
        var removed = 0;
        for (var i = rules.Count - 1; i >= 0; i--)
        {
            if (!EntityDisplayHelper.IsPerTypeEntityRule(rules[i])) continue;
            var token = rules[i].Match[0];
            _zoneOverrides.SetOverride(_areaCode, token, rules[i].Hide, rules[i].Navigable);
            rules.RemoveAt(i);
            removed++;
        }
        if (removed > 0) _displayRules.Replace(rules);
        _settings.PerTypeRulesMigrated = true;
        _settings.Save();
        if (removed > 0)
            Console.WriteLine($"Migrated {removed} per-type global rule(s) to zone overrides for '{_areaCode}'.");
    }

    private static void LogMissingHpBarTextures()
    {
        foreach (var name in new[] { "full_bar.png", "hollow_bar.png" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Overlay", "Textures", name);
            if (!File.Exists(path))
                Console.Error.WriteLine($"HP bar texture not found: Overlay/Textures/{name}");
        }
    }

    /// <summary>
    /// Turn an entity metadata path into a readable label: take the last '/'-segment, strip a trailing
    /// "_NN"/digit run, and insert spaces before interior capitals
    /// (e.g. ".../Expedition2/Expedition2Encounter" → "Expedition Encounter";
    /// "Waypoint_LongActivationRadius" → "Waypoint Long Activation Radius").
    /// </summary>
    private string EntityLabel(Poe2Live.EntityDot e, DisplayRule? rule)
        => EntityDisplayHelper.FormatEntityLabel(e, rule, _entities, _areaCode);

    /// <summary>Build the legend rows (one per unified navigation target), marking the selected targets
    /// and their selection-order color slot (-1 when unselected). Takes a selection snapshot so it
    /// doesn't touch _selectedIds while the API thread may be mutating it.</summary>
    private List<LegendEntry> BuildLegend(List<string> selected, NumVec2 player)
    {
        var legend = new List<LegendEntry>(_navTargets.Count + selected.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in _navTargets)
        {
            var slot = selected.IndexOf(t.Id);
            var dist = NumVec2.Distance(t.Grid, player);
            var pathDist = TryGetPathDistance(t.Id, out var pd) ? pd : -1f;
            var routeStatus = slot >= 0 ? GetRouteStatus(t.Id) : RoutePlanStatus.Unplanned;
            var status = slot >= 0 && !HasSelectedPath(t.Id) ? NavTargetStatus.NoPath : NavTargetStatus.Live;
            legend.Add(new LegendEntry(t, slot, slot >= 0, status, dist, pathDist, routeStatus, GetRouteFailure(t.Id)));
            seen.Add(t.Id);
        }

        for (var i = 0; i < selected.Count; i++)
        {
            var id = selected[i];
            if (seen.Contains(id)) continue;

            var slot = Math.Min(i, MaxSelectedTargets - 1);
            if (TryResolveTargetInfo(id, _navTargets, _landmarks, _entities, _areaHash, out var info))
            {
                var target = new NavTarget(
                    id,
                    info.Label,
                    info.Grid,
                    "",
                    info.IsEntity,
                    TileCount: info.TileCount,
                    GoalSearchRadius: info.GoalSearchRadius,
                    RouteAnchors: info.RouteAnchors);
                var status = HasSelectedPath(id) ? info.Status : NavTargetStatus.NoPath;
                var pathDist = TryGetPathDistance(id, out var pd) ? pd : -1f;
                legend.Add(new LegendEntry(
                    target,
                    slot,
                    true,
                    status,
                    NumVec2.Distance(info.Grid, player),
                    pathDist,
                    GetRouteStatus(id),
                    GetRouteFailure(id)));
            }
            else
            {
                var target = new NavTarget(id, id, player, "", id.StartsWith("e:", StringComparison.Ordinal));
                legend.Add(new LegendEntry(
                    target,
                    slot,
                    true,
                    NavTargetStatus.NoPath,
                    -1f,
                    RouteStatus: RoutePlanStatus.TargetUnavailable,
                    RouteFailureReason: "target unavailable"));
            }
        }
        return legend;
    }

    private bool HasSelectedPath(string id)
    {
        foreach (var p in _renderPaths)
            if (p.TargetId == id && p.RouteStatus == RoutePlanStatus.Planned && p.Points.Length > 0) return true;
        return false;
    }

    private RoutePlanStatus GetRouteStatus(string id)
    {
        lock (_trackerGate)
            return _trackers.TryGetValue(id, out var tracker) ? tracker.Status : RoutePlanStatus.Unplanned;
    }

    private string GetRouteFailure(string id)
    {
        lock (_trackerGate)
            return _trackers.TryGetValue(id, out var tracker) ? tracker.FailureReason : "";
    }

    private bool TryGetPathDistance(string id, out float pathDistance)
    {
        foreach (var p in _renderPaths)
            if (p.TargetId == id)
            {
                pathDistance = p.PathDistance;
                return true;
            }
        pathDistance = -1f;
        return false;
    }

    private static float SumPathGridDistance(IReadOnlyList<(int x, int y)> points)
    {
        if (points.Count < 2) return 0f;
        float sum = 0f;
        for (var i = 1; i < points.Count; i++)
        {
            var dx = points[i].x - points[i - 1].x;
            var dy = points[i].y - points[i - 1].y;
            sum += MathF.Sqrt(dx * dx + dy * dy);
        }
        return sum;
    }

    // ── Public navigation accessors (callable from the API/HTTP thread; all _navLock-guarded). ──

    /// <summary>API: a snapshot of the selected ids with their slot (index in selection order).
    /// Safe to call concurrently with the tick loop.</summary>
    public IReadOnlyList<NavSelectionInfo> GetNavSelection()
    {
        List<string> selected;
        lock (_navLock)
        {
            selected = new List<string>(_selectedIds);
        }

        lock (_trackerGate)
        {
            var list = new List<NavSelectionInfo>(selected.Count);
            for (var i = 0; i < selected.Count; i++)
            {
                var id = selected[i];
                if (_trackers.TryGetValue(id, out var tracker))
                {
                    list.Add(new NavSelectionInfo(
                        id,
                        i,
                        tracker.Status,
                        tracker.CurrentPoints.Count,
                        tracker.ResolvedGoal,
                        tracker.FailureReason,
                        tracker.LastPlanMilliseconds));
                }
                else
                {
                    list.Add(new NavSelectionInfo(
                        id,
                        i,
                        RoutePlanStatus.Unplanned,
                        0,
                        null,
                        "",
                        0));
                }
            }
            return list;
        }
    }

    /// <summary>API: toggle a nav target by id — add if absent (respecting the cap), remove if present.
    /// Shares the exact locked core the in-game toggle uses; only edits _selectedIds (trackers are
    /// reconciled on the tick thread). Safe to call concurrently with the tick loop.</summary>
    public void ToggleNavTarget(string id) => ToggleSelectionCore(id);

    /// <summary>API: clear the whole nav selection. Safe to call concurrently with the tick loop.</summary>
    public void ClearNavSelection() => ClearPathTargets();

    /// <summary>API (/api/atlas): a JSON-ready snapshot of the atlas map-data we can read — the full
    /// map-archetype catalog and the set of map types present in the current atlas region. Inspection /
    /// validation only (no spatial graph yet — see resources/atlas-research-notes.md). The reader scans
    /// + caches, so the first call after entering the atlas may take a moment; called on the API thread.</summary>
    private object AtlasJson()
    {
        // Anchor the scan to the live game-heap slab (the catalog shares the arena with AreaInstance).
        var d = _atlas.Read(_lastAreaInstance);
        // Live node graph (atlas nodes are UiElements) — summary + the locally-visible highlight set.
        List<Poe2Atlas.AtlasNodeLive> nodes;
        lock (_atlasNodesCacheLock)
            nodes = _atlasNodesCache.Count > 0
                ? new List<Poe2Atlas.AtlasNodeLive>(_atlasNodesCache)
                : _inGameStateForApi != 0
                    ? _atlas.ReadNodes(_inGameStateForApi, AtlasNodeReadMode.Light)
                    : new List<Poe2Atlas.AtlasNodeLive>();
        var vis = nodes.Where(n => n.Visible).ToList();
        return new
        {
            located = d.Located,
            note = d.Note,
            catalogAddr = $"0x{d.CatalogAddr:X}",
            catalogCount = d.CatalogCount,
            regionCount = d.Region.Count,
            catalog = d.Catalog.Select(m => new { id = m.Id, code = m.Code, name = m.Name, kind = m.Kind, parsedObj = $"0x{m.ParsedObj:X}" }),
            region = d.Region.Select(r => new { code = r.Code, name = r.Name, kind = r.Kind }),
            nodes = new
            {
                total = nodes.Count,
                visible = vis.Count,
                hasContent = nodes.Count(n => n.HasContent),
                unvisited = nodes.Count(n => !n.Visited),
                unlocked = nodes.Count(n => n.Unlocked),
                biomes = nodes.GroupBy(n => (int)n.Biome).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            },
            // Every distinct content tag currently on the atlas (+ count), for the dashboard's filter /
            // highlight-rule pickers. These are the readable content/mechanic names (Powerful Map Boss,
            // Breach, Delirium, …) resolved from each node's EndgameMapAtlas row.
            allTags = nodes.SelectMany(n => n.Tags).GroupBy(t => t).OrderByDescending(g => g.Count())
                .Select(g => new { tag = g.Key, count = g.Count() }),
            // Distinct MAP NAMES (Sun Temple, Precursor Tower, Vaal City, …) — the separate "Map" filter
            // group, so towers/temples/specific maps are highlightable independently of rolled content.
            allMaps = nodes.Where(n => !string.IsNullOrEmpty(n.MapName)).GroupBy(n => n.MapName)
                .OrderBy(g => g.Key).Select(g => new { tag = g.Key, count = g.Count() }),
            // The currently active rules (persisted): tracked tags (rings) + arrow tags (off-screen
            // direction). Match against BOTH content tags and map names.
            highlightTags = _settings.AtlasHighlightTags,
            arrowTags = _settings.AtlasArrowTags,
            // The individual live nodes for the dashboard's grid. On-screen first, then content/unvisited.
            nodeList = nodes
                .OrderByDescending(n => n.Visible).ThenByDescending(n => n.HasContent).ThenByDescending(n => !n.Visited)
                .Take(2000)
                .Select(n => new
                {
                    el = ((long)n.Element).ToString(), // unique stable key (element address) for selection
                    id = n.Id, biome = (int)n.Biome, type = n.IconType, hasContent = n.HasContent,
                    unlocked = n.Unlocked, visited = n.Visited, visible = n.Visible,
                    x = (int)n.X, y = (int)n.Y, map = n.MapName, tags = n.Tags,
                }),
        };
    }

    /// <summary>Read the live atlas nodes and update the F10 route. Cheap when the atlas is closed (ReadNodes
    /// returns empty via its visibility gate). When open, tracks the live zoom (for projection) and rebuilds
    /// the START/END markers + route path. Rides over transient empty reads so the route doesn't flicker.</summary>
    /// <summary>Signature for highlight-rule / tag-catalog work only. Node relPos must be re-read every tick
    /// (pan rewrites +0x118 live) — never use view position to skip mark/route rebuild.</summary>
    private long ComputeAtlasContentSig(int nodeCount)
    {
        int selCnt; lock (_atlasLock) selCnt = _atlasSel.Count;
        unchecked
        {
            long h = nodeCount;
            h = (h * 31) ^ (_atlas.AllTagsResolved ? 1 : 0);
            h = (h * 31) ^ (_settings.AtlasHighlightTags?.Count ?? 0);
            h = (h * 31) ^ (_settings.AtlasArrowTags?.Count ?? 0);
            h = (h * 31) ^ selCnt;
            h = (h * 31) ^ (_settings.AtlasShowOnScreenNodes ? 1 : 0);
            h = (h * 31) ^ (_settings.AtlasShowNames ? 1 : 0);
            h = (h * 31) ^ (_settings.AtlasRevealFog ? 1 : 0);
            h = (h * 31) ^ (_settings.AtlasOffScreenArrows ? 1 : 0);
            return h;
        }
    }

    /// <summary>Atlas UI closed — hide overlay, free node snapshot, reset route state.</summary>
    private void CloseAtlasSession()
    {
        _atlasOpen = false;
        _builtAtlasOnce = false;
        _lastAtlasContentSig = 0;
        _atlas.ClearSession();
        lock (_atlasNodesCacheLock) _atlasNodesCache = new();
        _atlasMarks.Clear();
        _atlasMarksPublish = Array.Empty<AtlasMark>();
        _atlasRoutePublish = Array.Empty<NumVec2>();
        if (_atlasTagCatalog.Count > 0) _atlasTagCatalog = new();
        _atlasRoute = new();
        _atlasStartPt = null;
        _atlasEndPt = null;
        _atlasStartGrid = null;
        _atlasGoalGrid = null;
        _loggedRoute = null;
    }

    private void UpdateAtlas(nint inGameState)
    {
        if (!_atlas.IsAtlasOpen(inGameState))
        {
            if (_atlasOpen || _atlasMarksPublish.Count > 0) CloseAtlasSession();
            return;
        }

        var readMode = _builtAtlasOnce && _atlas.AllTagsResolved
            ? AtlasNodeReadMode.Positions
            : AtlasNodeReadMode.Full;
        var nodes = _atlas.ReadNodes(inGameState, readMode);
        if (nodes.Count == 0)
        {
            var panelOpen = _atlas.IsAtlasOpen(inGameState);
            var transient = panelOpen && (DateTime.UtcNow - _atlasGoodAt).TotalSeconds < 0.4;
            if (_atlasOpen && transient) return;
            if (_atlasOpen || _atlasMarksPublish.Count > 0) CloseAtlasSession();
            return;
        }
        _atlasGoodAt = DateTime.UtcNow;
        _atlasOpen = true;
        lock (_atlasNodesCacheLock)
        {
            if (readMode == AtlasNodeReadMode.Full)
                _atlasNodesCache = new List<Poe2Atlas.AtlasNodeLive>(nodes);
            else if (_atlasNodesCache.Count == 0)
                _atlasNodesCache = new List<Poe2Atlas.AtlasNodeLive>(nodes);
        }

        var scales = nodes.Where(n => n.Scale > 0.01f).Select(n => n.Scale).OrderBy(s => s).ToList();
        if (scales.Count > 0) _atlasZoom = scales[scales.Count / 2];

        var contentSig = ComputeAtlasContentSig(nodes.Count);
        var catalogStale = !_builtAtlasOnce || contentSig != _lastAtlasContentSig;
        _lastAtlasContentSig = contentSig;
        _builtAtlasOnce = true;

        HashSet<nint> sel; lock (_atlasLock) sel = new HashSet<nint>(_atlasSel);

        if (catalogStale)
        {
            // One-time default: track + arrow every Citadel until the user edits rules. Wait until tag
            // resolution has caught up so we seed ALL citadels, not just the first batch resolved.
            if (!_settings.AtlasRulesInitialized && _atlas.AllTagsResolved)
            {
                var cit = nodes.Where(n => !string.IsNullOrEmpty(n.MapName) && n.MapName.Contains("Citadel", StringComparison.OrdinalIgnoreCase))
                               .Select(n => n.MapName).Distinct().ToList();
                if (cit.Count > 0)
                {
                    _settings.AtlasHighlightTags = new List<string>(cit);
                    _settings.AtlasArrowTags = new List<string>(cit);
                    foreach (var c in cit) _settings.AtlasHighlightColors[c] = "#e0b341"; // Citadel gold
                    _settings.AtlasRulesInitialized = true;
                    _settings.Save();
                }
            }

            var tagCounts = new Dictionary<string, (string Kind, int Count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodes)
            {
                if (!string.IsNullOrEmpty(n.MapName))
                {
                    if (!tagCounts.TryGetValue(n.MapName, out var e)) tagCounts[n.MapName] = ("map", 1);
                    else tagCounts[n.MapName] = (e.Kind, e.Count + 1);
                }
                if (n.Tags is { Count: > 0 })
                    foreach (var t in n.Tags)
                        if (!string.IsNullOrEmpty(t))
                        {
                            if (!tagCounts.TryGetValue(t, out var e)) tagCounts[t] = ("tag", 1);
                            else tagCounts[t] = (e.Kind, e.Count + 1);
                        }
            }
            _atlasTagCatalog = tagCounts
                .Select(kv => new AtlasTagCatalogEntry(kv.Key, kv.Value.Kind, kv.Value.Count))
                .OrderByDescending(e => e.Count).ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // A node matches a rule set if its map name or one of its content tags is in the set; returns the
        // matched tag (drives label + colour). Track set ⇒ draw a ring; Arrow set ⇒ off-screen edge arrow.
        var hlTrack = new HashSet<string>(_settings.AtlasHighlightTags ?? new(), StringComparer.OrdinalIgnoreCase);
        var hlArrow = new HashSet<string>(_settings.AtlasArrowTags ?? new(), StringComparer.OrdinalIgnoreCase);
        static string? Match(HashSet<string> set, in Poe2Atlas.AtlasNodeLive nd)
        {
            if (set.Count == 0) return null;
            if (!string.IsNullOrEmpty(nd.MapName) && set.Contains(nd.MapName)) return nd.MapName;
            if (nd.Tags is { Count: > 0 }) foreach (var t in nd.Tags) if (set.Contains(t)) return t;
            return null;
        }

        var trackedOnly = hlTrack.Count > 0;

        if (_atlasMarks.Capacity < nodes.Count) _atlasMarks.Capacity = nodes.Count;
        _atlasMarks.Clear();
        foreach (var n in nodes)
        {
            var selected = sel.Contains(n.Element);
            var mTrack = Match(hlTrack, n);
            var mArrow = Match(hlArrow, n);
            var isTracked = selected || mTrack != null;
            var isArrow = mArrow != null;
            if (trackedOnly)
            {
                if (!isTracked) continue;
            }
            else if (!_settings.AtlasShowOnScreenNodes && !isTracked && !isArrow) continue;
            var matched = mTrack ?? mArrow;
            string? color = matched != null && _settings.AtlasHighlightColors.TryGetValue(matched, out var c) ? c : null;
            var mapName = string.IsNullOrEmpty(n.MapName) ? null : n.MapName;
            var tier = AtlasEndgameCatalog.Classify(n.MapName, n.Tags);
            _atlasMarks.Add(new AtlasMark(n.X, n.Y, isTracked, n.HasContent, n.Visited, n.Unlocked, n.Visible,
                n.Biome, n.IconType, mapName, matched, color, isArrow, tier));
        }
        BuildAtlasRoute(nodes);
        _atlasMarksPublish = _atlasMarks.Count > 0 ? _atlasMarks.ToArray() : Array.Empty<AtlasMark>();
        _atlasRoutePublish = _atlasRoute.Count > 0 ? _atlasRoute.ToArray() : Array.Empty<NumVec2>();
    }

    /// <summary>Resolve the F10 START/END grid coords to canvas-space (relPos) points for the markers, and —
    /// when both are set — A* through the connection graph for the route polyline. All keyed by grid coord,
    /// so the markers + route survive pan/zoom and tiles going off-screen (every canvas child is in
    /// <paramref name="nodes"/>, so its relPos is available even when off-screen). Sets <see cref="_atlasStartPt"/>,
    /// <see cref="_atlasEndPt"/>, <see cref="_atlasRoute"/>. Logs once when a freshly-set END produces (or
    /// fails to produce) a path, so we can see whether the graph connected the two.</summary>
    private void BuildAtlasRoute(IReadOnlyList<Poe2Atlas.AtlasNodeLive> nodes)
    {
        _atlasRoute = new(); _atlasStartPt = null; _atlasEndPt = null;
        if (nodes.Count == 0) return;

        var gridToRel = new Dictionary<(int, int), NumVec2>(nodes.Count);
        foreach (var n in nodes) gridToRel[n.Grid] = new NumVec2(n.X, n.Y);

        var startGrid = _atlasStartGrid;
        if (startGrid is null && _settings.AtlasUseCurrentStart)
            startGrid = _atlas.CurrentNodeGrid();

        if (startGrid is { } s && gridToRel.TryGetValue(s, out var sp)) _atlasStartPt = sp;
        if (_atlasGoalGrid is { } g && gridToRel.TryGetValue(g, out var gp)) _atlasEndPt = gp;

        if (startGrid is { } start && _atlasGoalGrid is { } goal)
        {
            var path = _atlas.FindPath(start, goal);
            if (path != null) foreach (var p in path) if (gridToRel.TryGetValue(p, out var rp)) _atlasRoute.Add(rp);
            // Log once per (start,goal) pair so we can see graph connectivity (or the lack of it).
            if (_loggedRoute != (start, goal))
            {
                _loggedRoute = (start, goal);
                Console.WriteLine($"[atlas route] {start}→{goal}: {(path == null ? $"NO graph path (graph has {_atlas.GraphNodeCount} nodes; start in graph={_atlas.GraphHas(start)}, goal in graph={_atlas.GraphHas(goal)})" : $"{path.Count} hops")}");
            }
        }
        else _loggedRoute = null;
    }
    private (( int, int) s, (int, int) g)? _loggedRoute;

    /// <summary>API: set the dashboard-selected atlas nodes (by element address) to highlight in-game.
    /// Draw-only — never sends input to the game. Safe to call from the API thread.</summary>
    public void SetAtlasSelection(IReadOnlyList<long> els)
    {
        lock (_atlasLock) { _atlasSel.Clear(); foreach (var e in els) _atlasSel.Add((nint)e); }
    }

    /// <summary>API: set the active atlas highlight rules (tag + ring colour). Only nodes whose content
    /// tags or map name match one of these are drawn in-game, in the rule's colour. Persisted; applied on
    /// the next world tick. Draw-only.</summary>
    public void SetAtlasHighlight(IReadOnlyList<(string tag, string color, bool track, bool arrow)> rules)
    {
        var tags = new List<string>(); var arrows = new List<string>();
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tag, color, track, arrow) in rules)
        {
            if (string.IsNullOrWhiteSpace(tag) || !seen.Add(tag)) continue;
            if (track) tags.Add(tag);
            if (arrow) arrows.Add(tag);
            if (!string.IsNullOrWhiteSpace(color)) colors[tag] = color;
        }
        _settings.AtlasHighlightTags = tags;
        _settings.AtlasArrowTags = arrows;
        _settings.AtlasHighlightColors = colors;
        _settings.AtlasRulesInitialized = true;   // any explicit edit locks out the Citadel default-seed
        _settings.Save();
    }

    /// <summary>Open the web dashboard in the user's default browser (F12). Launches a browser only —
    /// nothing is sent to the game.</summary>
    private void OpenDashboard()
    {
        var url = $"http://localhost:{_settings.ApiPort}/";
        try
        {
            Console.WriteLine($"Opening dashboard — {url}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { Console.Error.WriteLine($"Open dashboard failed: {ex.Message}"); }
    }

    private bool IsGameFocused()
        => OverlayNative.IsGameFocused(_gameHwnd, _process.ProcessId);

    private void ResolveGameClientSize(out int width, out int height)
    {
        width = OverlayWidth;
        height = OverlayHeight;
        if (width > 0 && height > 0) return;
        if (_gameHwnd == 0 || !OverlayNative.GetClientRect(_gameHwnd, out var client)) return;
        width = client.Right - client.Left;
        height = client.Bottom - client.Top;
    }

    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint p);

    private sealed class PerfAccumulator
    {
        private const double Alpha = 0.08;
        private long _lastStamp = Stopwatch.GetTimestamp();
        private long _lastMainReadCount;
        private long _lastMainReadBytes;
        private long _lastWorldReadCount;
        private long _lastWorldReadBytes;
        private long _lastFailedReads;
        private bool _initialized;
        private double _fps;
        private double _tickMs;
        private double _worldMs;
        private double _entitiesMs;
        private double _hpBarsMs;
        private double _drawMs;
        private double _presentMs;
        private double _nameplatesMs;
        private double _mapMs;
        private double _pathsMs;
        private double _navMenuMs;
        private double _atlasMs;
        private double _readsPerSec;
        private double _mibPerSec;
        private double _failedReadsPerSec;
        private double _mainReadsPerSec;
        private double _worldReadsPerSec;
        private double _mainMibPerSec;
        private double _worldMibPerSec;

        public void RecordRender(
            double drawMs,
            double presentMs,
            double nameplatesMs,
            double mapMs,
            double pathsMs,
            double navMenuMs,
            double atlasMs)
        {
            _drawMs = Smooth(_drawMs, drawMs);
            _presentMs = Smooth(_presentMs, presentMs);
            _nameplatesMs = Smooth(_nameplatesMs, nameplatesMs);
            _mapMs = Smooth(_mapMs, mapMs);
            _pathsMs = Smooth(_pathsMs, pathsMs);
            _navMenuMs = Smooth(_navMenuMs, navMenuMs);
            _atlasMs = Smooth(_atlasMs, atlasMs);
        }

        public PerfSnapshot RecordFrame(
            double tickMs,
            double worldMs,
            double entitiesMs,
            double hpBarsMs,
            long mainReadCount,
            long mainReadBytes,
            long worldReadCount,
            long worldReadBytes,
            long failedReads,
            int entityCount,
            int hpBarCount,
            int selectedPathCount,
            float renderFps,
            float renderMs,
            float processCpuPct,
            float workingSetMb,
            float gpuPercent,
            float gpuMemoryMb)
        {
            var now = Stopwatch.GetTimestamp();
            var seconds = Math.Max(0.001, (now - _lastStamp) / (double)Stopwatch.Frequency);

            if (!_initialized)
            {
                _lastMainReadCount = mainReadCount;
                _lastMainReadBytes = mainReadBytes;
                _lastWorldReadCount = worldReadCount;
                _lastWorldReadBytes = worldReadBytes;
                _lastFailedReads = failedReads;
                _initialized = true;
            }

            _fps = Smooth(_fps, 1.0 / seconds);
            _tickMs = Smooth(_tickMs, tickMs);
            if (worldMs > 0) _worldMs = Smooth(_worldMs, worldMs);
            if (entitiesMs > 0) _entitiesMs = Smooth(_entitiesMs, entitiesMs);
            _hpBarsMs = Smooth(_hpBarsMs, hpBarsMs);
            var mainReadsPerSec = Math.Max(0, mainReadCount - _lastMainReadCount) / seconds;
            var worldReadsPerSec = Math.Max(0, worldReadCount - _lastWorldReadCount) / seconds;
            var mainMibPerSec = Math.Max(0, mainReadBytes - _lastMainReadBytes) / seconds / (1024.0 * 1024.0);
            var worldMibPerSec = Math.Max(0, worldReadBytes - _lastWorldReadBytes) / seconds / (1024.0 * 1024.0);
            _mainReadsPerSec = Smooth(_mainReadsPerSec, mainReadsPerSec);
            _worldReadsPerSec = Smooth(_worldReadsPerSec, worldReadsPerSec);
            _mainMibPerSec = Smooth(_mainMibPerSec, mainMibPerSec);
            _worldMibPerSec = Smooth(_worldMibPerSec, worldMibPerSec);
            _readsPerSec = _mainReadsPerSec + _worldReadsPerSec;
            _mibPerSec = _mainMibPerSec + _worldMibPerSec;
            _failedReadsPerSec = Smooth(_failedReadsPerSec, Math.Max(0, failedReads - _lastFailedReads) / seconds);

            _lastStamp = now;
            _lastMainReadCount = mainReadCount;
            _lastMainReadBytes = mainReadBytes;
            _lastWorldReadCount = worldReadCount;
            _lastWorldReadBytes = worldReadBytes;
            _lastFailedReads = failedReads;

            return new PerfSnapshot(
                (float)_fps,
                (float)_tickMs,
                (float)_worldMs,
                (float)_entitiesMs,
                (float)_hpBarsMs,
                (float)_drawMs,
                (float)_presentMs,
                (float)_nameplatesMs,
                (float)_mapMs,
                (float)_pathsMs,
                (float)_navMenuMs,
                (float)_atlasMs,
                (float)_readsPerSec,
                (float)_mibPerSec,
                (float)_failedReadsPerSec,
                (float)_mainReadsPerSec,
                (float)_worldReadsPerSec,
                (float)_readsPerSec,
                (float)_mainMibPerSec,
                (float)_worldMibPerSec,
                (float)_mibPerSec,
                entityCount,
                hpBarCount,
                selectedPathCount,
                renderFps,
                renderMs,
                processCpuPct,
                workingSetMb,
                gpuPercent,
                gpuMemoryMb);
        }

        private static double Smooth(double current, double sample)
            => current <= 0 ? sample : current + (sample - current) * Alpha;
    }

    public void Dispose()
    {
        _shutdown = true;
        try { _worldThread?.Join(2000); } catch { }
        _replanner.Dispose();
        _api.Dispose();
        _processMetrics.Dispose();
        _imguiOverlay?.RequestClose();
        try { _imguiThread?.Join(1000); } catch { }
    }
}

