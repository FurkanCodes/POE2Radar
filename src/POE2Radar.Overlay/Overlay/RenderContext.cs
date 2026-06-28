using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Config;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

/// <summary>
/// A unified navigation target — either a static terrain-tile landmark or an entity POI — addressed
/// by a STABLE STRING id so a selection survives world ticks and (where it matches) re-applies across
/// zones. <see cref="Id"/> is "t:&lt;path&gt;" for tiles, "e:&lt;entityId&gt;" for entities;
/// <see cref="MatchKey"/> (landmark path or entity metadata) is what auto-nav patterns match against;
/// <see cref="Grid"/> is the A* goal cell.
/// </summary>
public readonly record struct NavTarget(
    string Id,
    string Name,
    NumVec2 Grid,
    string MatchKey,
    bool IsEntity,
    bool AutoPath = false,
    int TileCount = 1,
    int GoalSearchRadius = 24,
    PathCell[]? RouteAnchors = null);

/// <summary>How fresh a selected navigation target is for display purposes.</summary>
public enum NavTargetStatus { Live, Cached, NoPath }

/// <summary>One legend row: a navigation target, the selection-order color slot it draws in (0..7, or
/// -1 when unselected), whether it is currently selected, its route freshness, and current grid
/// distance from the player (-1 when unknown).</summary>
public readonly record struct LegendEntry(
    NavTarget Target,
    int ColorSlot,
    bool IsSelected,
    NavTargetStatus Status,
    float Distance,
    float PathDistance = -1f,
    RoutePlanStatus RouteStatus = RoutePlanStatus.Unplanned,
    string RouteFailureReason = "");

/// <summary>One selected target's smoothed A* route: the selection-order color slot (0..7) used to pick
/// its draw/legend color, target identity/status metadata, and the smoothed grid-cell waypoints.
/// Empty <see cref="Points"/> = no path.</summary>
public readonly record struct SelectedPath(
    int ColorSlot, string TargetId, string Label, bool IsEntity, NavTargetStatus Status, float Distance,
    float PathDistance, (int x, int y)[] Points,
    (int x, int y)[] FullPoints,
    (int x, int y)? LiveGoal = null,
    RoutePlanStatus RouteStatus = RoutePlanStatus.Unplanned,
    (int x, int y)? ResolvedGoal = null,
    string RouteFailureReason = "",
    double LastPlanMilliseconds = 0);

public readonly record struct NavSelectionInfo(
    string Id,
    int Slot,
    RoutePlanStatus RouteStatus,
    int WaypointCount,
    (int x, int y)? ResolvedGoal,
    string FailureReason,
    double LastPlanMilliseconds);

/// <summary>Low-overhead runtime timings used to compare read/update/render costs while tuning the overlay.</summary>
public readonly record struct PerfSnapshot(
    float Fps,
    float TickMs,
    float WorldMs,
    float EntitiesMs,
    float HpBarsMs,
    float DrawMs,
    float PresentMs,
    float NameplatesMs,
    float MapMs,
    float PathsMs,
    float NavMenuMs,
    float AtlasMs,
    float ReadsPerSec,
    float MibPerSec,
    float FailedReadsPerSec,
    float MainReadsPerSec,
    float WorldReadsPerSec,
    float TotalReadsPerSec,
    float MainMibPerSec,
    float WorldMibPerSec,
    float TotalMibPerSec,
    int EntityCount,
    int HpBarCount,
    int SelectedPathCount,
    float RenderFps = 0,
    float RenderMs = 0,
    float ProcessCpuPct = 0,
    float WorkingSetMb = 0,
    float GpuPercent = -1,
    float GpuMemoryMb = -1)
{
    public static readonly PerfSnapshot Empty = new();
}

/// <summary>Per-frame map projection state for a concrete game map UI rectangle.</summary>
public readonly record struct MapFrame(
    NumVec2 Center,
    float Scale,
    float Width,
    float Height,
    nint MapElement,
    float PlayerTerrainHeight,
    NumVec2 Position,
    bool IsMinimap);

/// <summary>A monster HP bar to draw, with everything expensive already decided at world rate: the style
/// (width + packed 0xAARRGGBB fill/border colors) was resolved once when the entity set was built; only
/// <see cref="World"/> + <see cref="Frac"/> are refreshed live every render frame (cheap per-entity reads)
/// so the bar tracks the moving monster smoothly. The renderer just projects + fills.</summary>
public readonly record struct HpBarTarget(Vector3 World, float Frac, float EsFrac, float Width, uint Fill, float BorderWidth, uint Border);

public readonly record struct RitualPriceLabel(
    NumVec2 Pos,
    string IconFile,
    float IconWidth,
    float IconHeight,
    string ValueText,
    uint TextColor,
    float FontSize,
    float TextWidth,
    string? DebugText = null,
    NumVec2 DebugPos = default,
    float DebugFontSize = 0f);

public readonly record struct RitualPanelRow(
    string ItemName,
    string PriceText,
    string IconFile,
    uint TextColor,
    double SortDivine,
    bool HasPrice,
    string Rarity);

public readonly record struct RunecraftPriceLabel(
    NumVec2 Pos,
    NumVec2 Size,
    string ValueText,
    uint TextColor,
    float FontPx,
    bool Locked,
    float ClipTop,
    float ClipBottom,
    bool HasClip,
    float PriceLeftX,
    float OffsetX);

public readonly record struct RunecraftMapLabel(
    NumVec2 ScreenPos,
    string ValueText,
    uint TextColor);

public readonly record struct RunecraftMonolithCandidate(
    string Reward,
    int Count,
    double UnitEx,
    double TotalEx,
    bool Priced,
    string RunesTooltip,
    int Size,
    uint TotalColor);

public readonly record struct RunecraftMonolithPanelRow(
    long MonolithKey,
    string Header,
    double BestEx,
    uint HeaderColor,
    bool ShowAnchorWarning,
    RunecraftMonolithCandidate[] Candidates);

/// <summary>Endgame atlas tier for icon/label accent (Return of the Ancients / 0.5).</summary>
public enum AtlasEndgameTier : byte
{
    None = 0,
    BossContent = 1,
    Fortress = 2,
    Enigma = 3,
    Citadel = 4,
    KeyHalls = 5,
    Pinnacle = 6,
}

/// <summary>Colored content chip for atlas overlay (GameHelper DrawSquares style).</summary>
public readonly record struct AtlasContentChip(
    string Abbrev,
    byte BgR, byte BgG, byte BgB, float BgA,
    byte FgR, byte FgG, byte FgB, float FgA)
{
    public static AtlasContentChip FromInfo(AtlasCatalog.ContentInfo c)
        => new(c.Abbrev, c.BgR, c.BgG, c.BgB, c.BgA, c.FgR, c.FgG, c.FgB, c.FgA);
}

/// <summary>One atlas node to draw. <see cref="X"/>/<see cref="Y"/> keep canvas-space RelativePos for API
/// compatibility; <see cref="ScreenX"/>/<see cref="ScreenY"/> are live game UI screen-space centers.</summary>
public readonly record struct AtlasMark(
    float X, float Y, bool Selected, bool HasContent, bool Visited, bool Unlocked, bool Visible,
    int Biome, int IconType,
    float ScreenX = 0f,
    float ScreenY = 0f,
    float ScreenW = 0f,
    float ScreenH = 0f,
    string? MapName = null,
    string? HighlightLabel = null,
    string? Color = null,
    bool Arrow = false,
    AtlasEndgameTier EndgameTier = AtlasEndgameTier.None,
    string? BiomeColor = null,
    float BiomeAlpha = 0.9f,
    string? LabelBg = null,
    string? LabelFg = null,
    IReadOnlyList<string>? Badges = null,
    int ContentCount = 0,
    string? Tooltip = null,
    bool Completed = false,
    IReadOnlyList<AtlasContentChip>? FlagChips = null,
    IReadOnlyList<AtlasContentChip>? ContentChips = null,
    IReadOnlyList<string>? ContentNames = null,
    int RouteHops = 0);

/// <summary>One atlas route polyline with display metadata for multi-target search/content routes.</summary>
public readonly record struct AtlasRouteLine(
    IReadOnlyList<NumVec2> Points,
    string Label,
    string Color,
    int Hops,
    float Thickness = 1f,
    int PhaseIndex = 0);

/// <summary>Distinct atlas tag or map name for the overlay Settings → Atlas filter picker.</summary>
public readonly record struct AtlasTagCatalogEntry(string Key, string Kind, int Count);

/// <summary>What the PoE2 renderer needs each frame. Built fresh by <see cref="RadarApp"/>.</summary>
public sealed record RenderContext(
    bool InGame,
    bool Active,            // PoE2 is the foreground window — draw nothing when false
    int WindowWidth,
    int WindowHeight,
    NumVec2 PlayerGrid,
    System.Numerics.Vector3 PlayerWorld,
    NumVec2 RawPlayerGrid,
    System.Numerics.Vector3 RawPlayerWorld,
    Poe2Live.MapUi Map,
    Poe2Live.MapUi MiniMap,
    MapFrame MapFrame,
    MapFrame MiniMapFrame,
    Poe2Live.EntityDot[] Entities,
    Poe2Live.Landmark[] Landmarks,
    Poe2Live.ServerMinimapIcon[] ServerIcons,
    MapEntityRenderItem[] MapEntities,
    MapLandmarkRenderItem[] MapLandmarks,
    MapEntityRenderItem[] MapServerIcons,
    uint AreaHash,
    Poe2Live.TerrainData? Terrain,
    // Live projection calibration (adjustable at runtime).
    float ScaleMul,
    float OffsetX,
    float OffsetY,
    // Auto-flask status.
    float HpPct,
    float ManaPct,
    float EsPct,
    string FlaskNote,
    // Area / character HUD.
    string AreaCode,
    int CharLevel,
    // WorldToScreen matrix (16 floats, row-major) for world-space nameplates; null if unavailable.
    float[]? CameraMatrix,
    // ── Phase 1 features (all gated by their settings flag below). ──
    // Feature flags mirrored from RadarSettings.
    bool HideJunk,
    bool ImportantOnly,
    float GlobalIconScale,
    bool ShowPathWorld,
    bool ShowGroundWaypoints,
    bool ShowPathMap,
    bool ShowPathMinimap,
    bool UseCuratedLandmarks,
    // Radar display toggles.
    bool ShowMonsters,
    bool ShowTerrain,
    bool ShowPlayerBlip,
    // Monster HP-bar (nameplate) toggles by rarity.
    bool HpBarNormal,
    bool HpBarMagic,
    bool HpBarRare,
    bool HpBarUnique,
    // Smoothed guidance route per selected target, each carrying its selection-order color slot.
    SelectedPath[] SelectedPaths,
    string[] SelectedIds,
    // Legend rows (one per unified navigation target) for the HUD panel; never null.
    IReadOnlyList<LegendEntry> Legend,
    // ── Collapsible "POE2Radar" navigation-menu widget (always drawn when Active+InGame). ──
    bool NavMenuExpanded,         // dropdown open?
    string NavMenuCorner,         // pinned corner: TopLeft/TopRight/BottomLeft/BottomRight
    bool ShowPerfStats,           // extended timing lines in the perf HUD + nav menu
    bool ShowFpsOverlay,          // on-screen FPS / CPU / memory HUD (top-left)
    bool SmoothOverlayMotion,
    int OverlaySmoothingMs,
    int ChipSmoothingMs,
    bool PixelSnapLabels,
    PerfSnapshot Perf,
    // ── User-tweakable icon style table + HP-bar geometry (mirrored from RadarSettings). ──
    RadarStyles Styles,
    HpBarSettings HpBars,
    // Monster HP bars: style decided at world rate, position/HP refreshed live each render frame so bars
    // track moving mobs smoothly. Null/empty → none. Replaces the old per-frame resolve over all entities.
    HpBarTarget[] HpBarTargets,
    // Walkable-terrain bitmap colors/transparency (mirrored from RadarSettings).
    TerrainSettings TerrainStyle,
    RitualPriceLabel[] RitualLabels,
    bool RitualShowPricesWindow,
    bool RitualShopOpen,
    RitualPanelRow[] RitualPanelRows,
    RunecraftPriceLabel[] RunecraftLabels,
    RunecraftMapLabel[] RunecraftMapLabels,
    bool RunecraftShowMonolithWindow,
    RunecraftMonolithPanelRow[] RunecraftMonolithRows,
    // ── Unified display-rule engine (Phase 1). Resolves an entity to the first matching display rule
    // (or null → not drawn); the rule says hide or how to draw (shape/color/size/label). Replaces the
    // watched/mechanic/category dot decision in DrawMap. Null only if not wired (defensive). ──
    // Tile-landmark styling resolver (Phase 2b): given a tile path, the matching "Tile"-category rule
    // (styling pass) or null. Lets a rule restyle/hide a surfaced landmark; null → default Landmark style.
    // ── Atlas overlay (takes precedence over the minimap/radar when the Atlas screen is open). ──
    bool AtlasOpen = false,                       // the Atlas screen is open → draw atlas highlights + route, suppress radar
    IReadOnlyList<AtlasMark>? AtlasNodes = null,   // atlas nodes to draw (canvas-space coords)
    bool AtlasShowOnScreenNodes = true,
    bool AtlasTrackedOnly = false,
    bool AtlasShowNames = true,
    bool AtlasRevealFog = true,
    bool AtlasOffScreenArrows = true,
    float AtlasIconScale = 1f,
    float AtlasLabelScale = 1f,
    bool AtlasShowRouteChevrons = true,
    float AtlasRouteLineThickness = 3.5f,
    float AtlasRouteChevronSpacing = 28f,
    bool AtlasShowBiomeBorders = true,
    bool AtlasShowContentBadges = true,
    bool AtlasShowContentCount = true,
    bool AtlasShowContentTokens = false,
    float AtlasLabelOffsetX = 0f,
    float AtlasLabelOffsetY = 0f,
    float AtlasAnchorNudgeY = 28f,
    float AtlasUiScale = 1f,
    float AtlasBiomeBorderThickness = 2f,
    bool AtlasShowNodeSprites = false,
    bool AtlasDrawLinesSearchQuery = true,
    bool AtlasShowContentIcons = false,
    float AtlasContentIconSize = 32f,
    bool AtlasUseUniversalFont = true,
    string AtlasDefaultBackgroundColor = "#000000",
    string AtlasDefaultFontColor = "#FFFFFF",
    string AtlasLanguage = "english",
    IReadOnlyList<AtlasTagCatalogEntry>? AtlasTagCatalog = null,
    // Atlas canvas→screen homography coefficients (h0..h7; h8=1). Shear/persp 0 ⇒ plain affine.
    float AtlasScale = 0.5f,   // h0
    float AtlasScaleY = 0.5f,  // h4
    float AtlasOffX = 0f,      // h2
    float AtlasOffY = 0f,      // h5
    float AtlasShearX = 0f,    // h1
    float AtlasShearY = 0f,    // h3
    float AtlasPersX = 0f,     // h6
    float AtlasPersY = 0f,     // h7
    // Atlas route (F10 workflow): START/END tiles in canvas-space (relPos), and the graph path between them.
    // Projected with the same atlas homography as the marks. Start/End draw as markers even before a path
    // exists; AtlasRoute (≥2 pts) is the graph polyline, else the renderer draws a straight START→END line.
    NumVec2? AtlasStart = null,
    NumVec2? AtlasEnd = null,
    IReadOnlyList<NumVec2>? AtlasRoute = null,
    IReadOnlyList<AtlasRouteLine>? AtlasRoutes = null,
    NumVec2? AtlasCurrent = null,
    // Entity under cursor (hover inspect) — title + metadata for the on-screen HUD.
    string? CursorInspectTitle = null,
    string? CursorInspectMeta = null,
    // Map/path diagnostics (extended perf HUD when ShowPerfStats).
    string MapDiag = "",
    string PathDiagNote = "");
