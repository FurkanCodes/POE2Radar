using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using POE2Radar.Core.Campaign;
using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Campaign;
using POE2Radar.Overlay.Diagnostics;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Native;
using POE2Radar.Overlay.Navigation;
using POE2Radar.Overlay.Pickup;
using POE2Radar.Overlay.Pricing;
using POE2Radar.Overlay.Settings;
using POE2Radar.Overlay.StashUtility;
using POE2Radar.Overlay.Web;
using NumVec2 = System.Numerics.Vector2;
using GameVec2 = POE2Radar.Core.Game.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class ImGuiRadarOverlay : ClickableTransparentOverlay.Overlay
{
    private readonly object _settingsLock;
    private volatile RadarSettings _settings;
    private volatile RenderContext? _ctx;
    private volatile bool _closeRequested;
    private int _renderCrashLogged;
    private int _width = 800;
    private int _height = 600;
    private readonly object _boundsLock = new();
    private System.Drawing.Point _position;
    private System.Drawing.Size _size = new(800, 600);
    private readonly Action<Action> _enqueue;
    private readonly Action<string> _toggleTarget;
    private readonly Action<string> _setCorner;
    private readonly Action<float, float> _setTaskbarPosition;
    private readonly Action _toggleRendering;
    private readonly Action _addNearest;
    private readonly Action _clearPaths;
    private readonly Action _newLootSession;
    private readonly Action _startWaystoneAlchemy;
    private readonly Action _stopWaystoneAlchemy;
    private readonly Action _runExpeditionPlanner;
    private readonly Action _completeCampaignObjective;
    private readonly Action _backCampaignObjective;
    private readonly Action<bool> _setCampaignDismissed;
    private readonly Action _resetCampaignCharacter;
    private readonly Action<string[], bool> _setCampaignObjectivesComplete;
    private readonly Func<string> _exportCampaignProgress;
    private readonly Func<string, bool> _importCampaignProgress;
    private readonly Action<float, float> _setCampaignPosition;
    private Action? _openExternalSettings;
    private readonly TextureRegistry _textures = new();
    private readonly TerrainTextureCache _terrainTextures = new();
    private readonly OverlayRenderMetrics _renderMetrics = new();
    private readonly Dictionary<OverlayTextMeasureKey, NumVec2> _overlayTextMeasures = new();

    private bool _navMenuExpanded;
    private bool _settingsOpen;
    private string _navMenuCorner = "TopLeft";
    private bool _navTaskbarPositionInitialized;
    private bool _navTaskbarWasDragging;
    private bool _campaignPositionInitialized;
    private bool _campaignWasDragging;
    private bool _campaignGuideOpen;
    private string _campaignGuideChapter = "";
    private string _campaignGuideZone = "";
    private string _campaignImportCode = "";
    private string _campaignTransferStatus = "";
    private bool _campaignResetChapterOnly;
    private DisplayRules? _displayRules;
    private ZoneEntityOverrides? _zoneOverrides;
    private DisplayRuleEngine? _ruleEngine;
    private HiddenEntities? _hidden;
    private int _rulesUiGeneration = -1;
    private List<DisplayRule> _rulesUiCache = new();
    private string _hidePatternInput = "";
    private string _typeSearch = "";
    private string _ruleSearch = "";
    private string _stashUtilityModSearch = "";
    private string _atlasTargetGroupName = "";
    private string _atlasAddContentFilter = "";
    private string _atlasAddMapFilter = "";
    private readonly Dictionary<string, string> _atlasGroupMapBuffers = new(StringComparer.Ordinal);
    private readonly List<MapLabelCandidate> _atlasLabelScratch = new(256);
    private readonly Dictionary<string, ScreenPointState> _screenPoints = new(StringComparer.Ordinal);
    private readonly HashSet<string> _screenKeysThisFrame = new(StringComparer.Ordinal);
    private long _renderStamp;
    private long _lastRenderStamp;
    private int _runecraftMonolithDebugSelection;
    private int _spritePickerRuleIndex = -1;
    private int _selectedRuleIndex = -1;
    private bool _forceOpenDisplayRules;
    private int _scrollToRuleIndex = -1;
    private int _highlightRuleIndex = -1;
    private long _highlightRuleUntil;
    private string? _hotkeyBindTarget;
    private bool _hotkeyBindArmed;
    private string _activeSettingsTab = "";
    private bool _settingsPanelWasOpen;
    private int _lootDetailsPage = -1;
    private bool _lootTrackerHidden;
    private int _drawEnabled;
    private int _appliedDrawEnabled = -1;
    private readonly SettingsAutoSaveDebouncer _settingsAutoSave = new();
    private int _appliedUiFontSize = -1;
    private string _appliedUiFontPath = "";
    private bool? _appliedHideFromCapture;
    private UiFontGlyphRange _appliedUiGlyphRange = (UiFontGlyphRange)(-1);

    private static float UiW(float factor = 12f) => ImGuiTheme.ControlWidth(factor);

    private static readonly Vector4[] PathPalette =
    [
        new(0.20f, 0.90f, 0.40f, 1f),
        new(1.00f, 0.55f, 0.10f, 1f),
        new(0.30f, 0.70f, 1.00f, 1f),
        new(1.00f, 0.30f, 0.70f, 1f),
        new(0.95f, 0.90f, 0.20f, 1f),
        new(0.60f, 0.40f, 1.00f, 1f),
        new(0.20f, 1.00f, 0.85f, 1f),
        new(1.00f, 0.40f, 0.40f, 1f),
    ];

    private static readonly string[] ShapeNames = ["Circle", "Diamond", "Triangle", "Square", "Star", "Hexagon", "Pentagon", "Cross", "Plus", "Ring", "Heart", "Shield", "Gem"];

    private static readonly (string Id, string Label)[] CampaignChapters =
    [
        ("act1", "ACT I"),
        ("act2", "ACT II"),
        ("act3", "ACT III"),
        ("act4", "ACT IV"),
        ("interludes", "INTERLUDES"),
    ];

    private static readonly string[] RuleGroupOptions = ["Any", "Monster", "Chest", "Npc", "Object", "Other", "Transition", "Player", "Tile"];

    private static readonly (string Field, string Label, string[] Options)[] RuleConditionFields =
    [
        ("Rarity", "Rarity", ["Normal", "Magic", "Rare", "Unique"]),
        ("Reaction", "Reaction", ["Hostile", "Friendly"]),
        ("Life", "Life", ["Alive", "Dead"]),
        ("Chest", "Opened", ["Opened", "Unopened"]),
        ("Poi", "POI icon", ["Yes", "No"]),
    ];

    private readonly record struct ScreenPointState(NumVec2 Value, long SeenStamp);
    private readonly record struct OverlayTextMeasureKey(string Text, float FontSize, float CurrentFontSize);

    public ImGuiRadarOverlay(
        Action<Action> enqueue,
        Action<string> toggleTarget,
        Action<string> setCorner,
        Action<float, float> setTaskbarPosition,
        Action toggleRendering,
        Action addNearest,
        Action clearPaths,
        Action newLootSession,
        Action startWaystoneAlchemy,
        Action stopWaystoneAlchemy,
        Action runExpeditionPlanner,
        Action completeCampaignObjective,
        Action backCampaignObjective,
        Action<bool> setCampaignDismissed,
        Action resetCampaignCharacter,
        Action<string[], bool> setCampaignObjectivesComplete,
        Func<string> exportCampaignProgress,
        Func<string, bool> importCampaignProgress,
        Action<float, float> setCampaignPosition,
        RadarSettings settings,
        string windowTitle)
        : base(windowTitle, true, 3840, 2160)
    {
        _enqueue = enqueue;
        _toggleTarget = toggleTarget;
        _setCorner = setCorner;
        _setTaskbarPosition = setTaskbarPosition;
        _toggleRendering = toggleRendering;
        _addNearest = addNearest;
        _clearPaths = clearPaths;
        _newLootSession = newLootSession;
        _startWaystoneAlchemy = startWaystoneAlchemy;
        _stopWaystoneAlchemy = stopWaystoneAlchemy;
        _runExpeditionPlanner = runExpeditionPlanner;
        _completeCampaignObjective = completeCampaignObjective;
        _backCampaignObjective = backCampaignObjective;
        _setCampaignDismissed = setCampaignDismissed;
        _resetCampaignCharacter = resetCampaignCharacter;
        _setCampaignObjectivesComplete = setCampaignObjectivesComplete;
        _exportCampaignProgress = exportCampaignProgress;
        _importCampaignProgress = importCampaignProgress;
        _setCampaignPosition = setCampaignPosition;
        _settings = settings;
        _settingsLock = new object();
        _navMenuCorner = settings.NavMenuCorner;
        VSync = settings.OverlayVSync;
    }

    public int OverlayWidth => _width;
    public int OverlayHeight => _height;
    public float LastAtlasDrawMs { get; private set; }

    public OverlayRenderMetrics GetRenderMetrics() => _renderMetrics;

    public void UpdateContext(RenderContext ctx) => _ctx = ctx;

    /// <summary>Enable or suppress all POE2Radar draw calls while keeping the overlay backend alive.</summary>
    public void SetDrawEnabled(bool enabled)
    {
        var desired = enabled ? 1 : 0;
        if (Interlocked.Exchange(ref _drawEnabled, desired) == desired) return;
        ApplyDrawEnabled();
    }

    private void ApplyDrawEnabled()
    {
        if (window is null) return;
        var desired = Volatile.Read(ref _drawEnabled);
        if (Interlocked.Exchange(ref _appliedDrawEnabled, desired) == desired) return;
        OverlayNative.ShowWindow(window.Handle, WindowShowCommandForDrawEnabled(desired != 0));
    }

    internal static int WindowShowCommandForDrawEnabled(bool enabled) => OverlayNative.SW_SHOWNOACTIVATE;

    public void AttachEntityStores(DisplayRules displayRules, ZoneEntityOverrides zoneOverrides,
        DisplayRuleEngine ruleEngine, HiddenEntities hidden)
    {
        _displayRules = displayRules;
        _zoneOverrides = zoneOverrides;
        _ruleEngine = ruleEngine;
        _hidden = hidden;
        _rulesUiGeneration = -1;
        _rulesUiCache.Clear();
        _selectedRuleIndex = -1;
    }

    public void UpdateSettings(RadarSettings settings)
    {
        lock (_settingsLock) _settings = settings;
        VSync = settings.OverlayVSync;
        ApplyCaptureAffinity(settings);
    }

    protected override Task PostInitialized()
    {
        ImGuiTheme.Apply();
        OverlayFonts.Apply(this, _settings);
        _appliedUiFontSize = _settings.UiFontSize;
        _appliedUiFontPath = _settings.UiFontPath;
        _appliedUiGlyphRange = _settings.UiFontGlyphRange;
        ApplyDrawEnabled();
        ApplyCaptureAffinity(_settings);
        return base.PostInitialized();
    }

    private void ApplyCaptureAffinity(RadarSettings settings)
    {
        if (window is null) return;
        var hide = settings.HideFromScreenCapture;
        if (_appliedHideFromCapture == hide) return;
        OverlayNative.ApplyCaptureExclusion(window.Handle, hide);
        _appliedHideFromCapture = hide;
    }

    private void MaybeReapplyOverlayFont(RadarSettings s)
    {
        if (s.UiFontSize == _appliedUiFontSize
            && string.Equals(s.UiFontPath, _appliedUiFontPath, StringComparison.OrdinalIgnoreCase)
            && s.UiFontGlyphRange == _appliedUiGlyphRange)
            return;

        if (!OverlayFonts.Apply(this, s)) return;
        _appliedUiFontSize = s.UiFontSize;
        _appliedUiFontPath = s.UiFontPath;
        _appliedUiGlyphRange = s.UiFontGlyphRange;
        _overlayTextMeasures.Clear();
    }

    public void SetGameBounds(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        lock (_boundsLock)
        {
            _position = new System.Drawing.Point(x, y);
            _size = new System.Drawing.Size(width, height);
            _width = width;
            _height = height;
        }
    }

    public void RequestClose() => _closeRequested = true;

    public void ToggleSettings() => _settingsOpen = !_settingsOpen;

    public bool IsSettingsOpen => _settingsOpen;

    /// <summary>Route the taskbar gear to an external settings shell when one is available.</summary>
    public void SetExternalSettingsAction(Action action) => _openExternalSettings = action;

    /// <summary>
    /// True only while this overlay window itself owns foreground focus. This is intentionally
    /// separate from game focus so clicking our menus does not look like an external Alt-Tab.
    /// </summary>
    public bool IsOverlayFocused
        => window is not null && OverlayNative.IsForeground(window.Handle);

    /// <summary>
    /// Controller-safe loot details action: open, advance through every page, then close.
    /// The same action is used by the on-overlay mouse button and a configurable keyboard/gamepad bind.
    /// </summary>
    public void CycleLootDetails()
    {
        var rowCount = _ctx?.LootTracker.BreakdownItems?.Length ?? 0;
        var pageCount = Math.Max(1, (rowCount + LootDetailsPageSize - 1) / LootDetailsPageSize);
        var current = Volatile.Read(ref _lootDetailsPage);
        var next = current < 0 ? 0 : current + 1 < pageCount ? current + 1 : -1;
        Interlocked.Exchange(ref _lootDetailsPage, next);
    }

    private NumVec2 SmoothScreenPoint(string key, NumVec2 target, int smoothingMs, bool enabled)
    {
        if (string.IsNullOrEmpty(key) || !enabled || smoothingMs <= 0)
            return target;

        _screenKeysThisFrame.Add(key);
        if (!_screenPoints.TryGetValue(key, out var state))
        {
            _screenPoints[key] = new ScreenPointState(target, _renderStamp);
            return target;
        }

        var elapsedMs = _lastRenderStamp == 0
            ? 0f
            : Math.Max(0.001f, (float)((_renderStamp - _lastRenderStamp) * 1000.0 / Stopwatch.Frequency));
        var alpha = 1f - MathF.Exp(-elapsedMs / Math.Max(1f, smoothingMs));
        var value = state.Value + (target - state.Value) * alpha;
        _screenPoints[key] = new ScreenPointState(value, _renderStamp);
        return value;
    }

    private void PruneScreenSmoothing()
    {
        if (_screenPoints.Count == 0) return;
        foreach (var key in _screenPoints.Keys.ToArray())
        {
            if (!_screenKeysThisFrame.Contains(key))
                _screenPoints.Remove(key);
        }
    }

    private static NumVec2 PixelSnap(NumVec2 p, bool enabled)
        => enabled ? new NumVec2(MathF.Round(p.X), MathF.Round(p.Y)) : p;

    private static float PixelSnap(float v, bool enabled)
        => enabled ? MathF.Round(v) : v;

    protected override void Render()
    {
        var frameStart = Stopwatch.GetTimestamp();
        _renderStamp = frameStart;
        _screenKeysThisFrame.Clear();
        double mapMs = 0, pathsMs = 0, nameplatesMs = 0, navMenuMs = 0, atlasMs = 0;
        try
        {
            if (_closeRequested) { Close(); return; }
            ApplyDrawEnabled();
            ApplyCaptureAffinity(_settings);
            if (Volatile.Read(ref _drawEnabled) == 0) return;

            lock (_boundsLock) { Position = _position; Size = _size; }

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;
            VSync = _settings.OverlayVSync;

            var ctx = _ctx;
            var inGame = ctx is not null && ctx.InGame;
            var drawOverlay = inGame && ctx!.Active;

            var dl = ImGui.GetBackgroundDrawList();

            if (drawOverlay)
            {
                IconAtlas.EnsureInitialized(this);

                if (ctx!.AtlasOpen)
                {
                    var t = Stopwatch.GetTimestamp();
                    DrawAtlas(dl, ctx);
                    atlasMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                    LastAtlasDrawMs = (float)atlasMs;
                }
                else
                {
                    var largeMapOpen = ShouldDrawLargeMapOverlay(ctx.Map);
                    var miniMapOpen = ShouldDrawMinimapOverlay(ctx.Map, ctx.MiniMap);
                    if (largeMapOpen || miniMapOpen)
                    {
                        var t = Stopwatch.GetTimestamp();
                        if (largeMapOpen)
                            DrawMap(dl, ctx, ctx.MapFrame);
                        else if (miniMapOpen)
                            DrawMap(dl, ctx, ctx.MiniMapFrame);
                        mapMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                    }
                    if (!largeMapOpen
                        && ((ctx.ShowPathWorld && ctx.ShowGroundWaypoints)
                            || ctx.CampaignPath.FullPoints.Length > 0
                            || ctx.Campaign.Target.HasPosition))
                    {
                        var t = Stopwatch.GetTimestamp();
                        DrawPathsWorld(dl, ctx);
                        pathsMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                    }
                }

                if (!ctx.AtlasOpen)
                {
                    var t = Stopwatch.GetTimestamp();
                    DrawNameplates(dl, ctx);
                    DrawPathLabels(dl, ctx);
                    DrawExpeditionNextPlacementWorld(dl, ctx);
                    DrawRitualLabels(ImGui.GetForegroundDrawList(), ctx);
                    DrawRunecraftLabels(ImGui.GetForegroundDrawList(), ctx);
                    DrawRunecraftMapLabels(ImGui.GetForegroundDrawList(), ctx);
                    // Borders belong above the game but below our ImGui windows, so hover panels
                    // such as Tablet tiers are never crossed by filter outlines.
                    DrawStashUtilityHighlights(ImGui.GetBackgroundDrawList(), ctx);
                    DrawHoveredStashTier(ctx);
                    DrawWaystoneAlchemyHints(ImGui.GetForegroundDrawList(), ctx);
                    DrawPickupTargetHint(ImGui.GetForegroundDrawList(), ctx);
                    DrawStashValueLabels(ImGui.GetForegroundDrawList(), ctx);
                    DrawSekhemaScreenOverlaySafe(ImGui.GetForegroundDrawList(), ctx);
                    DrawAmanamuWorldOverlay(ImGui.GetForegroundDrawList(), ctx);
                    nameplatesMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                }

                DrawCursorInspect(dl, ctx);
            }

            // The taskbar remains available while overlay content is hidden so the eye control can
            // restore it without requiring the hotkey.
            if (inGame)
            {
                var navT = Stopwatch.GetTimestamp();
                DrawNavMenu(ctx!);
                if (ctx is { Active: true })
                {
                    DrawCampaignWidget(ctx);
                    DrawCampaignGuideBrowser(ctx);
                }
                navMenuMs = Stopwatch.GetElapsedTime(navT).TotalMilliseconds;
            }

            if (ctx is { Active: true, RunecraftShowMonolithWindow: true })
                DrawRunecraftMonolithWindow(ctx);

            if (ctx is { Active: true, RunecraftShowMonolithDebugWindow: true })
                DrawRunecraftMonolithDebugWindow(ctx);

            if (ctx is { Active: true, ExpeditionPlanner.Active: true })
                DrawExpeditionPlannerWindow(ctx);

            if (ctx is { Active: true, RitualShowPricesWindow: true })
                DrawRitualPricesWindow(ctx);

            if (ctx is { Active: true } && _settings.StashValue.ShowDebugInfo)
                DrawStashValueDebugWindow(ctx);

            if (ctx is { Active: true })
                DrawLootTracker(ctx);

            if (_settingsOpen && inGame)
                DrawSettingsPanel(ctx);
            else if (_settingsPanelWasOpen)
                FlushSettingsNow();

            _settingsPanelWasOpen = _settingsOpen;
        }
        catch (Exception ex)
        {
            // Log once per session; keep the overlay alive — Close() used to kill the whole map draw
            // loop after a cross-thread "collection modified" in DrawNameplates.
            if (Interlocked.Exchange(ref _renderCrashLogged, 1) == 0)
                CrashLog.Write("ImGui render error (overlay kept alive)", ex);
        }
        finally
        {
            PruneScreenSmoothing();
            _lastRenderStamp = frameStart;
            _renderMetrics.Record(
                Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds,
                mapMs, pathsMs, nameplatesMs, navMenuMs, atlasMs);
        }
    }

    // ── Settings HUD (HP/ES/Mana bars inside settings panel) ──

    private static void DrawSettingsHud(RenderContext ctx)
    {
        var dl = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var barH = 16f;
        var gap = 6f;
        var numBars = 3;
        var labelW = 20f;
        var textW = 36f;
        var barW = (avail - gap * (numBars + 1) - numBars * (labelW + textW)) / numBars;

        float x = cursor.X + gap;
        float y = cursor.Y + 2f;

        DrawColoredBar(dl, x, y, barW, barH, labelW, textW, "HP", ctx.HpPct,
            ctx.HpPct > 60f ? ColorU32(46, 204, 113, 0.9f) :
            ctx.HpPct > 30f ? ColorU32(241, 196, 15, 0.9f) : ColorU32(231, 76, 60, 0.9f));

        x += barW + labelW + textW + gap;
        DrawColoredBar(dl, x, y, barW, barH, labelW, textW, "ES", ctx.EsPct,
            ColorU32(52, 152, 219, 0.85f));

        x += barW + labelW + textW + gap;
        DrawColoredBar(dl, x, y, barW, barH, labelW, textW, "MP", ctx.ManaPct,
            ColorU32(52, 152, 219, 0.85f));

        // Reserve space
        ImGui.Dummy(new System.Numerics.Vector2(avail, barH + 4f));

        // Status line
        var flaskColor = ctx.FlaskNote == "armed"
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.18f, 0.80f, 0.44f, 1f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.77f, 0.06f, 1f));
        TextColoredUnformatted(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Flask: ");
        ImGui.SameLine(0, 0);
        TextColoredUnformatted(new Vector4(
            flaskColor >> 16 != 0 ? ((flaskColor >> 16) & 0xFF) / 255f : 0,
            ((flaskColor >> 8) & 0xFF) / 255f,
            (flaskColor & 0xFF) / 255f, 1f), ctx.FlaskNote);
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(
            $"Lv {ctx.CharLevel}  {ctx.AreaCode}").X);
        TextColoredUnformatted(new Vector4(0.55f, 0.55f, 0.55f, 1f), $"Lv {ctx.CharLevel}  {ctx.AreaCode}");
    }

    private static void DrawColoredBar(ImDrawListPtr dl, float x, float y, float w, float h,
        float labelW, float textW, string label, float pct, uint fillColor)
    {
        dl.AddText(new NumVec2(x, y - 1f), ColorU32(180, 180, 180, 0.75f), label);
        float bx = x + labelW + 2f;
        float frac = Math.Clamp(pct / 100f, 0f, 1f);
        uint bgBar = ColorU32(30, 30, 35, 0.75f);
        dl.AddRectFilled(new NumVec2(bx, y + 2f), new NumVec2(bx + w, y + h - 2f), bgBar, 3f);
        if (frac > 0.005f)
            dl.AddRectFilled(new NumVec2(bx, y + 2f), new NumVec2(bx + w * frac, y + h - 2f), fillColor, 3f);
        dl.AddText(new NumVec2(bx + w + 3f, y - 1f), ColorU32(220, 220, 220, 0.8f), pct.ToString("F0") + "%");
    }

    // ── Atlas overlay ──

    // ── Map overlay ──

    private static bool ShouldDrawLargeMapOverlay(Poe2Live.MapUi map)
        => MapOverlayDrawPolicy.ShouldDrawLargeMap(map);

    private static bool ShouldDrawMinimapOverlay(Poe2Live.MapUi largeMap, Poe2Live.MapUi miniMap)
        => MapOverlayDrawPolicy.ShouldDrawMinimap(largeMap, miniMap);

    private void DrawMap(ImDrawListPtr dl, RenderContext ctx, MapFrame frame)
    {
        var W = ctx.WindowWidth;
        var H = ctx.WindowHeight;
        var center = frame.Center;
        var scale = MathF.Max(0.01f, frame.Scale);
        var player = MapProjectionMotion.PlayerReference(
            frame, ctx.SmoothOverlayMotion, ctx.PlayerGrid, ctx.RawPlayerGrid);

        var clipped = frame.IsMinimap && frame.Width > 1f && frame.Height > 1f;
        if (clipped)
            dl.PushClipRect(
                frame.Position,
                new NumVec2(frame.Position.X + frame.Width, frame.Position.Y + frame.Height),
                true);

        try
        {
            if (ctx.ShowTerrain && ctx.Terrain is { } terrain)
            {
                if (!DrawTerrainTexture(dl, ctx, terrain, frame, center, scale))
                    DrawTerrainEdges(dl, ctx, terrain, frame, center, scale);
            }

            if ((frame.IsMinimap ? ctx.ShowPathMinimap : ctx.ShowPathMap)
                || ctx.CampaignPath.Points.Length > 0
                || ctx.Campaign.Target.HasPosition)
                DrawPathsMap(dl, ctx, frame, center, scale);

            if (_settings.Runecraft.ShowExpeditionRouteOnMap)
                DrawExpeditionRouteMap(dl, ctx, frame, center, scale);

            if (!frame.IsMinimap)
                DrawSekhemaMapSafe(dl, ctx, frame, center, scale);

            var mapLabels = new List<MapLabelCandidate>();
            var clipL = frame.Position.X;
            var clipT = frame.Position.Y;
            var clipR = frame.Position.X + frame.Width;
            var clipB = frame.Position.Y + frame.Height;

            if (ctx.ShowMonsters)
            {
                foreach (var e in ctx.MapEntities)
                {
                    var p = Project(
                        e.Grid,
                        player,
                        center,
                        scale,
                        e.TerrainHeight - frame.PlayerTerrainHeight);
                    if (p.X < clipL - 40 || p.Y < clipT - 40 || p.X > clipR + 40 || p.Y > clipB + 40) continue;
                    DrawIconOrShapePacked(dl, p, e.Size, e.Color, e.Sprite, e.Shape, ctx.GlobalIconScale);
                    if (e.Label.Length > 0 && !MapLabelAlreadyPresent(mapLabels, e.Label))
                        mapLabels.Add(new MapLabelCandidate("map:" + e.Key, p, e.Label, e.Color, e.Color));
                }
            }

            foreach (var lm in ctx.MapLandmarks)
            {
                var p = Project(lm.Center, player, center, scale, -frame.PlayerTerrainHeight);
                if (p.X < clipL - 40 || p.Y < clipT - 40 || p.X > clipR + 40 || p.Y > clipB + 40) continue;
                DrawIconOrShapePacked(dl, p, lm.Size, lm.Color, lm.Sprite, lm.Shape, ctx.GlobalIconScale);
                if (lm.Label.Length > 0 && !MapLabelAlreadyPresent(mapLabels, lm.Label))
                    mapLabels.Add(new MapLabelCandidate("map:" + lm.Key, p, lm.Label, lm.Color, lm.Color));
            }

            foreach (var s in ctx.MapServerIcons)
            {
                var p = Project(
                    s.Grid,
                    player,
                    center,
                    scale,
                    s.TerrainHeight - frame.PlayerTerrainHeight);
                if (p.X < clipL - 40 || p.Y < clipT - 40 || p.X > clipR + 40 || p.Y > clipB + 40) continue;
                DrawIconOrShapePacked(dl, p, s.Size, s.Color, s.Sprite, s.Shape, ctx.GlobalIconScale);
                if (s.Label.Length > 0 && !MapLabelAlreadyPresent(mapLabels, s.Label))
                    mapLabels.Add(new MapLabelCandidate("map:" + s.Key, p, s.Label, s.Color, s.Color));
            }

            DrawAmanamuMapMarkers(dl, ctx, player, center, scale, mapLabels, clipL, clipT, clipR, clipB);

            if (mapLabels.Count > 0)
                DrawMapLabelChips(
                    dl,
                    mapLabels,
                    clipL,
                    clipT,
                    clipR,
                    clipB,
                    smooth: !frame.IsMinimap && ctx.SmoothOverlayMotion,
                    pixelSnap: ctx.PixelSnapLabels);

            if (ctx.ShowPlayerBlip)
            {
                var playerPoint = Project(player, player, center, scale);
                DrawIconOrShape(
                    dl,
                    playerPoint,
                    ctx.Styles.Player.Size,
                    ctx.Styles.Player.Color,
                    ctx.Styles.Player.Opacity,
                    ctx.Styles.Player.Sprite,
                    ctx.Styles.Player.Shape,
                    ctx.GlobalIconScale);
            }
        }
        finally
        {
            if (clipped)
                dl.PopClipRect();
        }
    }

    private bool DrawTerrainTexture(ImDrawListPtr dl, RenderContext ctx, Poe2Live.TerrainData terrain, MapFrame frame, NumVec2 center, float scale)
    {
        if (!_terrainTextures.TryGet(this, _textures, terrain, ctx.AreaHash, ctx.TerrainStyle, out var tex))
            return false;

        var player = MapProjectionMotion.PlayerReference(
            frame, ctx.SmoothOverlayMotion, ctx.PlayerGrid, ctx.RawPlayerGrid);
        var deltaZ = -frame.PlayerTerrainHeight;
        var p0 = Project(new NumVec2(0, 0), player, center, scale, deltaZ);
        var p1 = Project(new NumVec2(terrain.Width, 0), player, center, scale, deltaZ);
        var p2 = Project(new NumVec2(terrain.Width, terrain.Height), player, center, scale, deltaZ);
        var p3 = Project(new NumVec2(0, terrain.Height), player, center, scale, deltaZ);

        dl.AddImageQuad(
            tex.Id,
            p0, p1, p2, p3,
            new NumVec2(0, 0),
            new NumVec2(1, 0),
            new NumVec2(1, 1),
            new NumVec2(0, 1),
            0xFFFFFFFF);
        return true;
    }

    private static void DrawIconOrShape(
        ImDrawListPtr dl,
        NumVec2 center,
        float size,
        string color,
        float opacity,
        SpriteIconRef? sprite,
        string? shape,
        float globalIconScale = 1f)
    {
        if (IconAtlas.TryResolve(sprite, shape, out var icon))
        {
            var scaleMul = Math.Clamp(sprite?.Scale ?? 1f, 0.2f, 4f) * Math.Clamp(globalIconScale, 0.25f, 4f);
            var half = MathF.Max(1f, size * scaleMul);
            var tint = ColorU32(color, opacity);
            dl.AddImage(
                icon.TextureId,
                new NumVec2(center.X - half, center.Y - half),
                new NumVec2(center.X + half, center.Y + half),
                icon.UV0,
                icon.UV1,
                tint);
            return;
        }

        dl.AddCircleFilled(center, MathF.Max(1f, size), ColorU32(color, opacity), 16);
    }


    private static void DrawIconOrShapePacked(
        ImDrawListPtr dl,
        NumVec2 center,
        float size,
        uint color,
        SpriteIconRef? sprite,
        string? shape,
        float globalIconScale = 1f)
    {
        if (IconAtlas.TryResolve(sprite, shape, out var icon))
        {
            var scaleMul = Math.Clamp(sprite?.Scale ?? 1f, 0.2f, 4f) * Math.Clamp(globalIconScale, 0.25f, 4f);
            var half = MathF.Max(1f, size * scaleMul);
            dl.AddImage(
                icon.TextureId,
                new NumVec2(center.X - half, center.Y - half),
                new NumVec2(center.X + half, center.Y + half),
                icon.UV0,
                icon.UV1,
                color);
            return;
        }

        dl.AddCircleFilled(center, MathF.Max(1f, size), color, 16);
    }

    private static void DrawTerrainEdges(ImDrawListPtr dl, RenderContext ctx, Poe2Live.TerrainData terrain, MapFrame frame, NumVec2 center, float scale)
    {
        var data = terrain.Walkable;
        var bytesPerRow = terrain.Width;
        if (data.Length == 0 || bytesPerRow <= 0) return;

        var edgeCol = ColorU32(ctx.TerrainStyle.EdgeColor, ctx.TerrainStyle.EdgeOpacity);
        var interiorCol = ColorU32(ctx.TerrainStyle.InteriorColor, ctx.TerrainStyle.InteriorOpacity);
        var rows = data.Length / bytesPerRow;
        var W = ctx.WindowWidth;
        var H = ctx.WindowHeight;
        var player = MapProjectionMotion.PlayerReference(
            frame, ctx.SmoothOverlayMotion, ctx.PlayerGrid, ctx.RawPlayerGrid);
        var deltaZ = -frame.PlayerTerrainHeight;

        var edgeStride = Math.Max(1, (int)MathF.Ceiling(0.8f / MathF.Max(scale, 0.15f)));
        const int maxFallbackSamples = 350_000;
        var budgetStride = (int)Math.Ceiling(Math.Sqrt(data.Length / (double)maxFallbackSamples));
        edgeStride = Math.Max(edgeStride, budgetStride);
        var thickness = Math.Clamp(1.2f * scale, 0.8f, 4f);

        var interiorStride = Math.Max(2, edgeStride * 3);

        for (var y = 1; y < rows - 2; y += edgeStride)
        {
            var row = y * bytesPerRow;
            for (var x = 1; x < bytesPerRow - 2; x += edgeStride)
            {
                var idx = row + x;
                if (idx < 0 || idx >= data.Length || data[idx] == 0) continue;

                var isEdge = data[idx - 1] == 0 || data[idx + 1] == 0
                          || data[idx - bytesPerRow] == 0 || data[idx + bytesPerRow] == 0;

                var p = Project(new NumVec2(x, y), player, center, scale, deltaZ);
                if (p.X < -8 || p.Y < -8 || p.X > W + 8 || p.Y > H + 8) continue;

                if (isEdge)
                {
                    if (x + edgeStride < bytesPerRow - 1)
                    {
                        var rightIdx = row + x + edgeStride;
                        if (rightIdx < data.Length && data[rightIdx] != 0)
                        {
                            var pr = Project(new NumVec2(x + edgeStride, y), player, center, scale, deltaZ);
                            if (MathF.Abs(pr.X - p.X) < 80f && MathF.Abs(pr.Y - p.Y) < 80f)
                                dl.AddLine(p, pr, edgeCol, thickness);
                        }
                    }

                    if (y + edgeStride < rows - 1)
                    {
                        var bottomIdx = (y + edgeStride) * bytesPerRow + x;
                        if (bottomIdx < data.Length && data[bottomIdx] != 0)
                        {
                            var pb = Project(new NumVec2(x, y + edgeStride), player, center, scale, deltaZ);
                            if (MathF.Abs(pb.X - p.X) < 80f && MathF.Abs(pb.Y - p.Y) < 80f)
                                dl.AddLine(p, pb, edgeCol, thickness);
                        }
                    }
                }
                else if (y % interiorStride == 0 && x % interiorStride == 0 && scale > 0.15f
                         && ctx.TerrainStyle.InteriorOpacity > 0.01f)
                {
                    dl.AddCircleFilled(p, Math.Clamp(1.2f * scale, 0.6f, 2.5f), interiorCol, 4);
                }
            }
        }
    }

    private void DrawPathsMap(ImDrawListPtr dl, RenderContext ctx, MapFrame frame, NumVec2 center, float scale)
    {
        var projectionOrigin = MapProjectionMotion.PlayerReference(
            frame, ctx.SmoothOverlayMotion, ctx.PlayerGrid, ctx.RawPlayerGrid);
        var smoothPaths = !frame.IsMinimap && ctx.SmoothOverlayMotion;
        foreach (var path in ctx.SelectedPaths)
        {
            var poly = NavigationPathBuilder.BuildDrawPolyline(projectionOrigin, path.Points, path.LiveGoal);
            if (poly.Count < 2) continue;
            var col = PathColor(path.ColorSlot);
            NumVec2? prev = null;
            for (var i = 0; i < poly.Count; i++)
            {
                var (x, y) = poly[i];
                var p = Project(new NumVec2(x, y), projectionOrigin, center, scale);
                if (smoothPaths)
                    p = SmoothScreenPoint($"path:map:{path.TargetId}:{i}", p, ctx.OverlaySmoothingMs, true);
                if (prev is { } a) dl.AddLine(a, p, col, 2.2f);
                prev = p;
            }
        }
        DrawCampaignPathMap(dl, ctx, frame, center, scale, projectionOrigin, smoothPaths);
    }

    private void DrawCampaignPathMap(
        ImDrawListPtr dl,
        RenderContext ctx,
        MapFrame frame,
        NumVec2 center,
        float scale,
        NumVec2 projectionOrigin,
        bool smoothPaths)
    {
        if (!ctx.Campaign.Visible || ctx.Campaign.Current is null) return;
        var gold = ColorU32(216, 180, 90, 0.96f);
        var goal = ctx.CampaignPath.ResolvedGoal;
        if (goal is null && ctx.Campaign.Target.Grid is { } target)
            goal = ((int)MathF.Round(target.X), (int)MathF.Round(target.Y));
        var poly = NavigationPathBuilder.BuildDrawPolyline(
            projectionOrigin,
            ctx.CampaignPath.Points,
            goal);
        NumVec2? previous = null;
        for (var index = 0; index < poly.Count; index++)
        {
            var cell = poly[index];
            var point = Project(new NumVec2(cell.x, cell.y), projectionOrigin, center, scale);
            if (smoothPaths)
                point = SmoothScreenPoint(
                    $"campaign:path:map:{ctx.Campaign.Current.Id}:{index}",
                    point,
                    ctx.OverlaySmoothingMs,
                    true);
            if (previous is { } prior) dl.AddLine(prior, point, gold, 3.1f);
            previous = point;
        }

        if (ctx.Campaign.Target.Grid is not { } grid) return;
        var marker = Project(grid, projectionOrigin, center, scale);
        var radius = Math.Clamp(10f * _settings.Campaign.WidgetScale, 8f, 16f);
        dl.AddCircle(marker, radius + 4f, ColorU32(28, 24, 17, 0.90f), 24, 5f);
        dl.AddCircle(marker, radius + 2f, gold, 24, 2.5f);
        dl.AddQuadFilled(
            marker + new NumVec2(0f, -radius),
            marker + new NumVec2(radius * 0.72f, 0f),
            marker + new NumVec2(0f, radius),
            marker + new NumVec2(-radius * 0.72f, 0f),
            gold);
    }

    // ── World-space paths (map closed) ──

    private void DrawPathsWorld(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.CameraMatrix is not { Length: >= 16 } m) return;
        float W = ctx.WindowWidth, H = ctx.WindowHeight;
        var wz = ctx.PlayerWorld.Z;
        const float margin = 50f;

        foreach (var path in ctx.SelectedPaths)
        {
            if (path.FullPoints.Length < 2) continue;
            var goal = path.ResolvedGoal ?? path.LiveGoal;
            var gridLine = NavigationPathBuilder.DecimateForWorldDisplay(
                NavigationPathBuilder.AppendResolvedGoal(path.FullPoints, goal));
            if (gridLine.Count < 2) continue;

            var col = PathColor(path.ColorSlot);
            NumVec2? prev = null;
            for (var i = 0; i < gridLine.Count; i++)
            {
                var (gx, gy) = gridLine[i];
                if (!TryProjectGridToScreen(gx, gy, wz, m, W, H, out var sx, out var sy)) { prev = null; continue; }
                if (sx < -margin || sx > W + margin || sy < -margin || sy > H + margin) { prev = null; continue; }

                var p = SmoothScreenPoint($"path:world:{path.TargetId}:{i}", new NumVec2(sx, sy),
                    ctx.OverlaySmoothingMs, ctx.SmoothOverlayMotion);
                if (prev is { } pr) dl.AddLine(pr, p, col, 2f);
                var dotR = i >= gridLine.Count - 1 ? 6f : 3f;
                dl.AddCircleFilled(p, dotR, col, 12);
                prev = p;
            }

            if (goal is not { } g) continue;
            if (!TryProjectGridToScreen(g.x, g.y, wz, m, W, H, out var gsx, out var gsy)) continue;
            if (gsx < 0 || gsx > W || gsy < 0 || gsy > H) continue;
            var gp = new NumVec2(gsx, gsy);
            dl.AddCircle(gp, 10f, col, 24, 2f);
            dl.AddCircleFilled(gp, 5f, col, 16);
        }
        DrawCampaignPathWorld(dl, ctx, m, W, H, wz);
    }

    private void DrawCampaignPathWorld(
        ImDrawListPtr dl,
        RenderContext ctx,
        float[] matrix,
        float width,
        float height,
        float worldZ)
    {
        if (!ctx.Campaign.Visible || ctx.Campaign.Current is null) return;
        var gold = ColorU32(216, 180, 90, 0.96f);
        var goal = ctx.CampaignPath.ResolvedGoal;
        if (goal is null && ctx.Campaign.Target.Grid is { } target)
            goal = ((int)MathF.Round(target.X), (int)MathF.Round(target.Y));

        var gridLine = NavigationPathBuilder.DecimateForWorldDisplay(
            NavigationPathBuilder.AppendResolvedGoal(ctx.CampaignPath.FullPoints, goal));
        NumVec2? previous = null;
        for (var index = 0; index < gridLine.Count; index++)
        {
            var cell = gridLine[index];
            if (!TryProjectGridToScreen(cell.x, cell.y, worldZ, matrix, width, height, out var sx, out var sy))
            {
                previous = null;
                continue;
            }
            if (sx < -50f || sx > width + 50f || sy < -50f || sy > height + 50f)
            {
                previous = null;
                continue;
            }
            var point = SmoothScreenPoint(
                $"campaign:path:world:{ctx.Campaign.Current.Id}:{index}",
                new NumVec2(sx, sy),
                ctx.OverlaySmoothingMs,
                ctx.SmoothOverlayMotion);
            if (previous is { } prior) dl.AddLine(prior, point, gold, 3f);
            dl.AddCircleFilled(point, index == gridLine.Count - 1 ? 5f : 2.5f, gold, 12);
            previous = point;
        }

        if (ctx.Campaign.Target.Grid is not { } targetGrid) return;
        if (!TryProjectGridToScreen(
                (int)MathF.Round(targetGrid.X),
                (int)MathF.Round(targetGrid.Y),
                worldZ,
                matrix,
                width,
                height,
                out var targetX,
                out var targetY))
            return;
        DrawCampaignQuestMarker(dl, new NumVec2(targetX, targetY), width, height, gold);
    }

    private static void DrawCampaignQuestMarker(
        ImDrawListPtr dl,
        NumVec2 target,
        float width,
        float height,
        uint gold)
    {
        const float margin = 28f;
        if (target.X >= margin && target.X <= width - margin
            && target.Y >= margin && target.Y <= height - margin)
        {
            dl.AddCircle(target, 15f, ColorU32(22, 18, 12, 0.90f), 24, 6f);
            dl.AddCircle(target, 13f, gold, 24, 2.5f);
            dl.AddQuadFilled(
                target + new NumVec2(0f, -9f),
                target + new NumVec2(7f, 0f),
                target + new NumVec2(0f, 9f),
                target + new NumVec2(-7f, 0f),
                gold);
            return;
        }

        var center = new NumVec2(width * 0.5f, height * 0.5f);
        var direction = target - center;
        if (direction.LengthSquared() < 0.001f) return;
        direction = NumVec2.Normalize(direction);
        var horizontal = Math.Max(1f, width * 0.5f - margin) / Math.Max(0.001f, MathF.Abs(direction.X));
        var vertical = Math.Max(1f, height * 0.5f - margin) / Math.Max(0.001f, MathF.Abs(direction.Y));
        var tip = center + direction * MathF.Min(horizontal, vertical);
        var perpendicular = new NumVec2(-direction.Y, direction.X);
        var basePoint = tip - direction * 18f;
        dl.AddTriangleFilled(tip, basePoint + perpendicular * 9f, basePoint - perpendicular * 9f, gold);
        dl.AddCircle(tip - direction * 23f, 5f, gold, 16, 2f);
    }

    private static bool TryProjectGridToScreen(int gx, int gy, float wz, float[] m, float w, float h,
        out float sx, out float sy)
    {
        var wx = gx * GridConstants.GridToWorld;
        var wy = gy * GridConstants.GridToWorld;
        var cw = wx * m[3] + wy * m[7] + wz * m[11] + m[15];
        if (cw <= 0.0001f) { sx = sy = 0; return false; }
        var cx = wx * m[0] + wy * m[4] + wz * m[8] + m[12];
        var cy = wx * m[1] + wy * m[5] + wz * m[9] + m[13];
        sx = (cx / cw / 2f + 0.5f) * w;
        sy = (0.5f - cy / cw / 2f) * h;
        return float.IsFinite(sx) && float.IsFinite(sy);
    }

    // ── HP bars (world-space nameplates) ──

    private void DrawNameplates(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.CameraMatrix is not { Length: >= 16 } m) return;
        if (ctx.HpBarTargets is not { Length: > 0 } bars) return;
        float W = ctx.WindowWidth, H = ctx.WindowHeight;
        var bh = ctx.HpBars.Height;
        TextureRegistry.TextureHandle fullTex = default;
        TextureRegistry.TextureHandle hollowTex = default;
        var useFullTex = ctx.HpBars.UseTextures && _textures.TryGetOutputTexture(this, "full_bar.png", out fullTex);
        var useHollowTex = ctx.HpBars.UseTextures && _textures.TryGetOutputTexture(this, "hollow_bar.png", out hollowTex);

        foreach (var t in bars)
        {
            var w = t.World;
            var cw = w.X * m[3] + w.Y * m[7] + w.Z * m[11] + m[15];
            if (cw <= 0.0001f) continue;
            var cx = w.X * m[0] + w.Y * m[4] + w.Z * m[8] + m[12];
            var cy = w.X * m[1] + w.Y * m[5] + w.Z * m[9] + m[13];
            var sx = (cx / cw / 2f + 0.5f) * W;
            var sy = (0.5f - cy / cw / 2f) * H;
            if (sx < -120 || sx > W + 120 || sy < -120 || sy > H + 120) continue;

            var bw = t.Width;
            var bx = sx - bw / 2f + ctx.HpBars.OffsetX;
            var by = sy + ctx.HpBars.OffsetY;
            var barMax = new NumVec2(bx + bw, by + bh);

            if (useHollowTex)
                dl.AddImage(hollowTex.Id, new NumVec2(bx, by), barMax, new NumVec2(0, 0), new NumVec2(1, 1),
                    ColorU32(255, 255, 255, 0.92f));
            else
                dl.AddRectFilled(new NumVec2(bx, by), barMax, ColorU32(13, 13, 13, 0.78f));

            uint fillCol;
            if (t.Frac < 0.3f)
                fillCol = ColorU32(255, 51, 51, 0.95f);
            else
                fillCol = ColorU32((byte)((t.Fill >> 16) & 0xFF), (byte)((t.Fill >> 8) & 0xFF), (byte)(t.Fill & 0xFF), ((t.Fill >> 24) & 0xFF) / 255f);

            var hpFrac = Math.Clamp(t.Frac, 0f, 1f);
            if (useFullTex)
                DrawPartialImage(dl, fullTex, bx, by, bw, bh, hpFrac, fillCol);
            else
                dl.AddRectFilled(new NumVec2(bx, by), new NumVec2(bx + bw * hpFrac, by + bh), fillCol);

            var esFrac = Math.Clamp(t.EsFrac, 0f, 1f);
            if (esFrac > 0.005f)
            {
                var esCol = ColorU32(ctx.HpBars.EnergyShieldColor, 0.86f);
                if (useHollowTex)
                    DrawPartialImage(dl, hollowTex, bx, by, bw, bh, esFrac, esCol);
                else
                    dl.AddRect(new NumVec2(bx, by), new NumVec2(bx + bw * esFrac, by + bh), esCol, 0, 0, 1.5f);
            }

            if (t.BorderWidth > 0f)
            {
                uint borderCol = ColorU32((byte)((t.Border >> 16) & 0xFF), (byte)((t.Border >> 8) & 0xFF), (byte)(t.Border & 0xFF), ((t.Border >> 24) & 0xFF) / 255f);
                dl.AddRect(new NumVec2(bx, by), new NumVec2(bx + bw, by + bh), borderCol, 0, 0, t.BorderWidth);
            }
        }
    }

    private void DrawRitualLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.RitualLabels is not { Length: > 0 } labels) return;

        var font = ImGui.GetFont();
        var currentFontSize = Math.Max(1f, ImGui.GetFontSize());
        const uint shadowColor = 0xCC000000u;

        foreach (var label in labels)
        {
            if (!string.IsNullOrEmpty(label.ValueText))
            {
                var textPos = label.Pos;
                dl.AddText(font, label.FontSize, textPos + new NumVec2(1f, 1f), shadowColor, label.ValueText);
                dl.AddText(font, label.FontSize, textPos, label.TextColor, label.ValueText);

                var textWidth = label.TextWidth > 0
                    ? label.TextWidth
                    : MeasureOverlayText(label.ValueText, label.FontSize, currentFontSize).X;
                var iconPos = textPos + new NumVec2(textWidth + 3f, 0f);
                var iconH = Math.Max(1f, label.IconHeight);
                var iconW = Math.Max(1f, label.IconWidth);
                if (_textures.TryGet(this, RitualCurrencyIcons.PathFor(label.IconFile), out var tex))
                {
                    if (tex.Height > 0)
                        iconW = iconH * tex.Width / (float)tex.Height;
                    dl.AddImage(tex.Id, iconPos, iconPos + new NumVec2(iconW, iconH));
                }
            }

            if (!string.IsNullOrEmpty(label.DebugText))
            {
                var color = label.DebugText.Contains("NO PRICE", StringComparison.Ordinal)
                    ? 0xFF4040FFu
                    : 0xFF40FF40u;
                dl.AddText(font, label.DebugFontSize, label.DebugPos + new NumVec2(1f, 1f), shadowColor, label.DebugText);
                dl.AddText(font, label.DebugFontSize, label.DebugPos, color, label.DebugText);
            }
        }
    }

    private NumVec2 MeasureOverlayText(string text, float fontSize, float currentFontSize)
    {
        var key = new OverlayTextMeasureKey(text, fontSize, currentFontSize);
        if (_overlayTextMeasures.TryGetValue(key, out var size))
            return size;

        if (_overlayTextMeasures.Count >= 256)
            _overlayTextMeasures.Clear();

        size = ImGui.CalcTextSize(text) * (fontSize / currentFontSize);
        _overlayTextMeasures[key] = size;
        return size;
    }

    private void DrawStashValueLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.StashValueLabels is not { Length: > 0 } labels) return;

        var font = ImGui.GetFont();
        var currentFontSize = Math.Max(1f, ImGui.GetFontSize());
        const uint shadowColor = 0xCC000000u;
        const uint chipBg = 0xB0000000u;
        const uint debugBox = 0xFFFF00FFu;

        foreach (var label in labels)
        {
            var slotMin = new NumVec2(
                label.Pos.X - _settings.StashValue.PriceOffsetX,
                label.Pos.Y - label.Size.Y + label.FontSize - _settings.StashValue.PriceOffsetY);
            var slotMax = slotMin + label.Size;

            if (label.Debug)
            {
                dl.AddRect(slotMin, slotMax, debugBox, 0f, ImDrawFlags.None, 2f);
                if (!string.IsNullOrEmpty(label.DebugText))
                    dl.AddText(font, label.FontSize, slotMin, 0xFFFFFFFFu, label.DebugText);
            }

            if (label.HidePrice || string.IsNullOrEmpty(label.ValueText))
                continue;

            var k = label.FontSize / currentFontSize;
            var textSize = ImGui.CalcTextSize(label.ValueText) * k;
            var bgPad = new NumVec2(3f, 1f);
            dl.AddRectFilled(label.Pos - bgPad, label.Pos + new NumVec2(textSize.X, label.FontSize) + bgPad, chipBg, 3f);
            dl.AddText(font, label.FontSize, label.Pos + new NumVec2(1f, 1f), shadowColor, label.ValueText);
            dl.AddText(font, label.FontSize, label.Pos, label.TextColor, label.ValueText);
        }
    }

    private static void DrawStashUtilityHighlights(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.StashUtilityHighlights is not { Length: > 0 } highlights) return;

        foreach (var item in highlights)
        {
            var scale = Math.Max(0.5f, item.Size.X / 52f);
            var margin = item.BorderMargin * scale;
            var half = item.BorderThickness * 0.5f;
            var min = item.Pos + new NumVec2(margin + half, margin + half);
            var max = item.Pos + item.Size - new NumVec2(margin + half, margin + half);
            DrawStashUtilityRect(dl, min, max, item.BorderColor, item.BorderThickness, item.BorderStyle);

            if (item.ShowRarityCorner)
            {
                var size = item.RarityCornerSize * scale;
                var at = new NumVec2(max.X, min.Y);
                dl.AddTriangleFilled(at - new NumVec2(size, 0f), at, at + new NumVec2(0f, size), item.RarityColor);
            }

            if (!item.Great || !item.ShowGreatArrow) continue;
            var arrow = item.GreatArrowSize * scale;
            var padding = 4f * scale + margin + item.BorderThickness;
            NumVec2 tip = item.GreatArrowCorner switch
            {
                1 => item.Pos + new NumVec2(item.Size.X - padding - arrow * 0.5f, padding + (item.ShowRarityCorner ? item.RarityCornerSize * scale : 0f)),
                2 => item.Pos + new NumVec2(padding + arrow * 0.5f, item.Size.Y - padding - arrow),
                3 => item.Pos + new NumVec2(item.Size.X - padding - arrow * 0.5f, item.Size.Y - padding - arrow),
                _ => item.Pos + new NumVec2(padding + arrow * 0.5f, padding),
            };
            var left = tip + new NumVec2(-arrow * 0.5f, arrow);
            var right = tip + new NumVec2(arrow * 0.5f, arrow);
            dl.AddTriangleFilled(tip, left, right, item.GreatColor);
            dl.AddTriangle(tip, left, right, 0xFF000000u, Math.Max(1f, 1.5f * scale));
        }
    }

    private static void DrawHoveredStashTier(RenderContext ctx)
    {
        if (ctx.HoveredStashTier is not { } hover)
            return;

        var width = Math.Clamp(ctx.WindowWidth * 0.28f, 340f, 520f);
        var metricsHeight = hover.Metrics.Length > 0
            ? 24f + hover.Metrics.Length * 22f
            : 0f;
        var estimatedHeight = 78f + metricsHeight + hover.Modifiers.Length * 42f;
        var cursor = ImGui.GetMousePos();
        const float cursorGap = 18f;
        const float screenPadding = 8f;
        var x = cursor.X + cursorGap;
        if (x + width > ctx.WindowWidth - screenPadding)
            x = cursor.X - width - cursorGap;
        x = Math.Clamp(x, screenPadding, Math.Max(screenPadding, ctx.WindowWidth - width - screenPadding));

        var y = cursor.Y + cursorGap;
        if (y + estimatedHeight > ctx.WindowHeight - screenPadding)
            y = cursor.Y - estimatedHeight - cursorGap;
        y = Math.Clamp(
            y,
            screenPadding,
            Math.Max(screenPadding, ctx.WindowHeight - estimatedHeight - screenPadding));

        var dl = ImGui.GetForegroundDrawList();
        var badgeSize = Math.Clamp(hover.Size.X * 0.46f, 22f, 36f);
        var badgeMin = hover.Pos + new NumVec2(2f, 2f);
        var badgeMax = badgeMin + new NumVec2(badgeSize, badgeSize);
        dl.AddRectFilled(badgeMin, badgeMax, 0xE6111518u, 4f);
        dl.AddRect(badgeMin, badgeMax, hover.OverallColor, 4f, ImDrawFlags.None, 2f);
        var badgeTextSize = ImGui.CalcTextSize(hover.OverallTier);
        dl.AddText(
            badgeMin + new NumVec2((badgeSize - badgeTextSize.X) * 0.5f, (badgeSize - badgeTextSize.Y) * 0.5f),
            hover.OverallColor,
            hover.OverallTier);

        ImGui.SetNextWindowPos(new NumVec2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new NumVec2(width, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.96f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration
                                       | ImGuiWindowFlags.NoInputs
                                       | ImGuiWindowFlags.NoNav
                                       | ImGuiWindowFlags.NoFocusOnAppearing
                                       | ImGuiWindowFlags.AlwaysAutoResize;
        if (!ImGui.Begin("##hovered_tablet_tiers", flags))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted(hover.ItemType);
        ImGui.SameLine();
        ImGui.TextDisabled("Overall");
        ImGui.SameLine();
        ImGui.TextColored(ColorVector(hover.OverallColor), hover.OverallTier);
        ImGui.Separator();

        if (hover.Metrics.Length > 0)
        {
            ImGui.TextDisabled("TOP REWARD TOTALS");
            foreach (var metric in hover.Metrics)
            {
                ImGui.TextDisabled(metric.Label);
                ImGui.SameLine(205f);
                ImGui.TextUnformatted(metric.Value);
                ImGui.SameLine(292f);
                ImGui.TextColored(ColorVector(metric.TierColor), metric.Tier);
            }
            ImGui.Separator();
        }

        if (hover.Modifiers.Length == 0)
        {
            ImGui.TextDisabled("No ranked explicit modifiers");
        }
        else
        {
            foreach (var modifier in hover.Modifiers)
            {
                ImGui.TextColored(ColorVector(modifier.TierColor), modifier.Tier);
                ImGui.SameLine();
                if (!string.IsNullOrEmpty(modifier.Roll))
                {
                    ImGui.TextColored(ColorVector(modifier.TierColor), $"[{modifier.Roll}]");
                    ImGui.SameLine();
                }
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - 68f);
                ImGui.TextUnformatted(modifier.Modifier);
                ImGui.PopTextWrapPos();
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled(hover.TierNote);
        ImGui.End();
    }

    private static Vector4 ColorVector(uint color)
        => new(
            ((color >> 16) & 0xFF) / 255f,
            ((color >> 8) & 0xFF) / 255f,
            (color & 0xFF) / 255f,
            ((color >> 24) & 0xFF) / 255f);

    private void DrawWaystoneAlchemyHints(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.WaystoneAlchemyHints is not { Length: > 0 } hints) return;
        var font = ImGui.GetFont();
        var fontSize = Math.Max(11f, ImGui.GetFontSize() * 0.85f);
        var ambient = Math.Max(1f, ImGui.GetFontSize());
        foreach (var hint in hints)
        {
            var textSize = MeasureOverlayText(hint.Text, fontSize, ambient);
            var at = new NumVec2(hint.Pos.X + (hint.Size.X - textSize.X) * 0.5f, hint.Pos.Y - textSize.Y - 3f);
            dl.AddRectFilled(at - new NumVec2(3f, 1f), at + textSize + new NumVec2(3f, 1f), 0xD0000000u, 3f);
            dl.AddText(font, fontSize, at, hint.Color, hint.Text);
            if (hint.Active)
                dl.AddRect(hint.Pos, hint.Pos + hint.Size, 0xFF00FFFFu, 2f, ImDrawFlags.None, 3f);
        }
    }

    private static void DrawPickupTargetHint(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.PickupTarget is not { } hint || !hint.ShowHighlight) return;
        var min = hint.Pos;
        var max = hint.Pos + hint.Size;
        if (hint.Size.X <= 6f || hint.Size.Y <= 6f)
        {
            dl.AddCircle((min + max) * 0.5f, 10f, 0xFF47E8A4u, 20, 2f);
            return;
        }

        dl.AddRectFilled(min, max, 0x2838D899u, 3f);
        dl.AddRect(min, max, 0xFF47E8A4u, 3f, ImDrawFlags.None, 2f);
    }

    private static void DrawStashUtilityRect(
        ImDrawListPtr dl,
        NumVec2 min,
        NumVec2 max,
        uint color,
        float thickness,
        int style)
    {
        if (style == 0)
        {
            dl.AddRect(min, max, color, 3f, ImDrawFlags.RoundCornersAll, thickness);
            return;
        }

        var segment = style == 1 ? 10f : 3f;
        var gap = style == 1 ? 5f : 4f;
        DrawStashUtilityLine(dl, new NumVec2(min.X, min.Y), new NumVec2(max.X, min.Y), color, thickness, segment, gap);
        DrawStashUtilityLine(dl, new NumVec2(max.X, min.Y), new NumVec2(max.X, max.Y), color, thickness, segment, gap);
        DrawStashUtilityLine(dl, new NumVec2(max.X, max.Y), new NumVec2(min.X, max.Y), color, thickness, segment, gap);
        DrawStashUtilityLine(dl, new NumVec2(min.X, max.Y), new NumVec2(min.X, min.Y), color, thickness, segment, gap);
    }

    private static void DrawStashUtilityLine(
        ImDrawListPtr dl,
        NumVec2 start,
        NumVec2 end,
        uint color,
        float thickness,
        float segment,
        float gap)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length <= 0f) return;
        var direction = delta / length;
        for (var offset = 0f; offset < length; offset += segment + gap)
            dl.AddLine(start + direction * offset, start + direction * Math.Min(length, offset + segment), color, thickness);
    }

    private void DrawRunecraftLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.RunecraftLabels is not { Length: > 0 } labels) return;

        var font = ImGui.GetFont();
        float ambient = Math.Max(1f, ImGui.GetFontSize());
        const uint shadow = 0xCC000000u;
        const uint plate = 0xE6000000u;
        const uint gold = 0xFF00D7FFu;

        foreach (var label in labels)
        {
            if (label.HasClip)
            {
                float centreY = label.Pos.Y + label.Size.Y * 0.5f;
                if (centreY < label.ClipTop || centreY > label.ClipBottom) continue;
            }

            float k = label.FontPx / ambient;
            var ts = MeasureOverlayText(label.ValueText, label.FontPx, ambient);
            float x = label.PriceLeftX + label.OffsetX;
            float y = label.Pos.Y + (label.Size.Y - ts.Y) * 0.5f;
            var at = new NumVec2(x, y);
            var bgPad = new NumVec2(4f * k, 2f * k);
            dl.AddRectFilled(at - bgPad, at + ts + bgPad, plate, 3f * k);
            if (label.Locked)
                dl.AddRect(at - bgPad, at + ts + bgPad, gold, 3f * k, ImDrawFlags.None, 2f * k);
            dl.AddText(font, label.FontPx, at + new NumVec2(1f, 1f), shadow, label.ValueText);
            dl.AddText(font, label.FontPx, at, label.TextColor, label.ValueText);
        }
    }

    private void DrawRunecraftMapLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.RunecraftMapLabels is not { Length: > 0 } labels) return;
        if (ctx.MapFrame.IsMinimap) return;

        var font = ImGui.GetFont();
        float ambient = Math.Max(1f, ImGui.GetFontSize());
        float fontPx = ambient * 1.5f;
        const uint shadow = 0xCC000000u;
        var bgCol = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
        bgCol.W = 0.55f;
        uint monoBg = ImGui.GetColorU32(bgCol);

        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            var text = label.ValueText;
            var ts = MeasureOverlayText(text, fontPx, ambient);
            var screenPos = SmoothScreenPoint(
                $"runecraft:map:{i}",
                label.ScreenPos,
                ctx.OverlaySmoothingMs,
                ctx.SmoothOverlayMotion);
            var at = new NumVec2(screenPos.X - ts.X * 0.5f, screenPos.Y + 6f);
            var pad = new NumVec2(3f, 1f);
            dl.AddRectFilled(at - pad, at + ts + pad, monoBg, 2f);
            dl.AddText(font, fontPx, at + new NumVec2(1f, 1f), shadow, text);
            dl.AddText(font, fontPx, at, label.TextColor, text);
        }
    }

    private void DrawRunecraftMonolithWindow(RenderContext ctx)
    {
        if (!ctx.RunecraftShowMonolithWindow) return;
        var rows = ctx.RunecraftMonolithRows ?? [];
        if (rows.Length == 0) return;

        ImGui.SetNextWindowSizeConstraints(new NumVec2(260, 0), new NumVec2(640, 900));
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Appearing);
        if (!ImGui.Begin("Monolith Rewards", ImGuiWindowFlags.AlwaysAutoResize)) { ImGui.End(); return; }

        foreach (var row in rows)
        {
            if (row.HeaderColor != 0xFFFFFFFFu)
                ImGui.PushStyleColor(ImGuiCol.Text, row.HeaderColor);
            var open = ImGui.CollapsingHeader(row.Header);
            if (row.HeaderColor != 0xFFFFFFFFu)
                ImGui.PopStyleColor();
            if (!open) continue;

            if (row.ShowAnchorWarning)
            {
                ImGui.TextDisabled("  anchor not resolved (station unavailable)");
                continue;
            }

            if (!ImGui.BeginTable($"rcm{row.MonolithKey}", 4,
                    ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp,
                    new NumVec2(430f, 0f)))
                continue;

            ImGui.TableSetupColumn("Reward", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("x", ImGuiTableColumnFlags.WidthFixed, 26f);
            ImGui.TableSetupColumn("Unit", ImGuiTableColumnFlags.WidthFixed, 58f);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 62f);
            ImGui.TableHeadersRow();

            if (row.Candidates.Length == 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextDisabled("(nothing above threshold)");
            }
            else
            {
                foreach (var c in row.Candidates)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text(c.Reward);
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip(c.RunesTooltip);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text(c.Count.ToString());

                    ImGui.TableSetColumnIndex(2);
                    if (c.Priced)
                        ImGui.Text(c.UnitEx.ToString("F0"));
                    else
                        ImGui.Text("—");

                    ImGui.TableSetColumnIndex(3);
                    if (c.Priced && c.TotalColor != 0xFFFFFFFFu)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, c.TotalColor);
                        ImGui.Text(c.TotalEx.ToString("F0"));
                        ImGui.PopStyleColor();
                    }
                    else if (c.Priced)
                        ImGui.Text(c.TotalEx.ToString("F0"));
                    else
                        ImGui.Text("—");
                }
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }

    private void DrawExpeditionPlannerWindow(RenderContext ctx)
    {
        var view = ctx.ExpeditionPlanner;
        ImGui.SetNextWindowPos(new NumVec2(ctx.WindowWidth - 18f, 170f), ImGuiCond.FirstUseEver, new NumVec2(1f, 0f));
        ImGui.SetNextWindowSizeConstraints(new NumVec2(280f, 0f), new NumVec2(460f, 560f));
        var flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavInputs;
        if (!ImGui.Begin("Expedition Planner###RunecraftExpeditionPlanner", flags)) { ImGui.End(); return; }

        var remaining = Math.Max(0, view.Total - view.Placed);
        TextColoredUnformatted(new Vector4(0.45f, 0.90f, 0.62f, 1f),
            $"{remaining} / {view.Total} explosives remaining");
        ImGui.SameLine();
        ImGui.TextDisabled($"({view.CountSource})");

        ImGui.TextUnformatted($"{view.EncounterSize} · range {view.EffectiveDistance:F0} · blast {view.EffectiveRadius:F0}");
        if (view.PlacementRangePercent != 0 || view.BlastRadiusPercent != 0)
            ImGui.TextDisabled($"Map modifiers: range {view.PlacementRangePercent:+#;-#;0}% · radius {view.BlastRadiusPercent:+#;-#;0}%");

        ImGui.Separator();
        if (view.Planning)
            TextColoredUnformatted(new Vector4(0.95f, 0.77f, 0.20f, 1f), view.Status);
        else
            ImGui.TextWrapped(view.Status);
        if (view.Planning) ImGui.BeginDisabled();
        var stale = view.Status.Contains("Run*", StringComparison.Ordinal);
        if (stale)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.55f, 0.20f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.70f, 0.26f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.16f, 0.45f, 0.16f, 1f));
        }
        if (ImGui.Button(stale ? "Run*" : "Run", new NumVec2(90f, 0f)))
            _enqueue(_runExpeditionPlanner);
        if (stale) ImGui.PopStyleColor(3);
        if (view.Planning) ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("build the complete route from the detonator");
        ImGui.TextUnformatted($"Targets: {view.TargetCount} · covered: {view.CapturedCount} · score: {view.CapturedWeight:F0}");
        if (view.ComputeMilliseconds > 0)
            ImGui.TextDisabled($"Route compute: {view.ComputeMilliseconds:F1} ms (background)");

        if (view.Route.Length > 0)
        {
            ImGui.Separator();
            var nextIndex = NextExpeditionRouteIndex(view);
            if (nextIndex >= 0)
            {
                var next = view.Route[nextIndex];
                TextColoredUnformatted(new Vector4(0.35f, 1f, 0.55f, 1f),
                    $"NEXT #{view.PlanBasePlaced + nextIndex + 1}: {next.Label}");
                var previewEnd = Math.Min(view.Route.Length, nextIndex + 5);
                for (var i = nextIndex + 1; i < previewEnd; i++)
                {
                    var p = view.Route[i];
                    ImGui.TextDisabled($"#{view.PlanBasePlaced + i + 1} {(p.Bridge ? "bridge" : p.Label)}");
                }
                if (view.Route.Length > previewEnd)
                    ImGui.TextDisabled($"…and {view.Route.Length - previewEnd} more");
            }
            else
                ImGui.TextDisabled("Locked route completed");
        }

        ImGui.Separator();
        ImGui.TextDisabled("Run once: the route stays fixed; placed explosives advance NEXT");
        ImGui.End();
    }

    private void DrawRunecraftMonolithDebugWindow(RenderContext ctx)
    {
        var rows = ctx.RunecraftMonolithRows ?? [];
        ImGui.SetNextWindowSize(new NumVec2(680f, 460f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowCollapsed(false, ImGuiCond.Appearing);
        if (!ImGui.Begin("Monolith Debug###RunecraftMonolithDebug"))
        {
            ImGui.End();
            return;
        }

        if (rows.Length == 0)
        {
            ImGui.TextDisabled("No monoliths detected in this area.");
            ImGui.End();
            return;
        }

        var labels = rows.Select(r => r.DebugLabel).ToArray();
        _runecraftMonolithDebugSelection = Math.Clamp(_runecraftMonolithDebugSelection, 0, labels.Length - 1);
        ImGui.SetNextItemWidth(420f);
        ImGui.Combo("Monolith", ref _runecraftMonolithDebugSelection, labels, labels.Length);

        var row = rows[_runecraftMonolithDebugSelection];
        ImGui.Separator();
        if (row.IsUnique)
            ImGui.Text($"Unique monolith (no anchor) — offers all recipes with size <= N ({row.HoleCount}).");
        else if (row.AnchorIndex < 0)
            TextColoredUnformatted(new Vector4(1f, 0.45f, 0.45f, 1f), "Anchor not resolved — no recipes.");
        else
            ImGui.Text($"Anchor: {row.AnchorName} (idx {row.AnchorIndex})    p={row.AnchorPosition}  (hole {row.AnchorPosition + 1})");

        if (row.SocketsState >= 0 && row.SocketsState != row.HoleCount)
            TextColoredUnformatted(new Vector4(1f, 0.45f, 0.45f, 1f),
                $"N = {row.HoleCount}  (station +0x38)    sockets state = {row.SocketsState}   <- differ");
        else
            ImGui.Text($"N = {row.HoleCount}    sockets state = {row.SocketsState}");

        ImGui.Text($"Area level: {row.AreaLevel}");
        ImGui.TextDisabled($"device 0x{row.MonolithKey:X}   station 0x{row.StationAddress:X}   +0x40={row.Field40}  +0x44={row.Field44}");
        if (!string.IsNullOrEmpty(row.StatesDump))
            ImGui.TextDisabled($"SM states: {row.StatesDump}");

        if (ImGui.Button("Copy report"))
            ImGui.SetClipboardText(BuildRunecraftMonolithDebugReport(row));
        ImGui.SameLine();
        ImGui.TextDisabled($"{row.Candidates.Length} recipe(s) offered");

        ImGui.Separator();
        if (ImGui.BeginTable("mdbg", 8,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("row", ImGuiTableColumnFlags.WidthFixed, 44f);
            ImGui.TableSetupColumn("sz", ImGuiTableColumnFlags.WidthFixed, 26f);
            ImGui.TableSetupColumn("gate", ImGuiTableColumnFlags.WidthFixed, 40f);
            ImGui.TableSetupColumn("cat", ImGuiTableColumnFlags.WidthFixed, 28f);
            ImGui.TableSetupColumn("reward", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("FK / Id", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("lvl", ImGuiTableColumnFlags.WidthFixed, 56f);
            ImGui.TableSetupColumn("holes (anchor in [])", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var candidate in row.Candidates)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); ImGui.Text(candidate.Row.ToString());
                ImGui.TableSetColumnIndex(1); ImGui.Text(candidate.Size.ToString());
                ImGui.TableSetColumnIndex(2); ImGui.Text(candidate.Full ? "N" : "RW");
                ImGui.TableSetColumnIndex(3); ImGui.Text(candidate.Category.ToString());
                ImGui.TableSetColumnIndex(4); ImGui.Text(candidate.Reward);
                ImGui.TableSetColumnIndex(5); ImGui.TextDisabled($"{candidate.RewardIdx} / {candidate.RewardId}");
                ImGui.TableSetColumnIndex(6); ImGui.Text($"{candidate.MinLevel}-{candidate.MaxLevel}");
                ImGui.TableSetColumnIndex(7); ImGui.Text(candidate.RunesTooltip);
            }

            if (row.Candidates.Length == 0)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(4);
                ImGui.TextDisabled("(no recipes)");
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }

    private static string BuildRunecraftMonolithDebugReport(RunecraftMonolithPanelRow row)
    {
        var lines = new List<string>
        {
            row.DebugLabel,
            $"device=0x{row.MonolithKey:X} station=0x{row.StationAddress:X}",
            $"anchor={row.AnchorName} idx={row.AnchorIndex} pos={row.AnchorPosition} N={row.HoleCount} sockets={row.SocketsState}",
            $"areaLevel={row.AreaLevel} field40={row.Field40} field44={row.Field44}",
            $"states={row.StatesDump}",
        };
        lines.AddRange(row.Candidates.Select(c =>
            $"row={c.Row} size={c.Size} gate={(c.Full ? "N" : "RW")} cat={c.Category} reward={c.Reward} " +
            $"fk={c.RewardIdx} id={c.RewardId} level={c.MinLevel}-{c.MaxLevel} runes={c.RunesTooltip}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static int NextExpeditionRouteIndex(ExpeditionPlannerView view)
        => RadarApp.ExpeditionNextRouteIndex(view.Placed, view.Route.Length);

    private static void DrawExpeditionRouteMap(
        ImDrawListPtr dl, RenderContext ctx, MapFrame frame, NumVec2 center, float scale)
    {
        var view = ctx.ExpeditionPlanner;
        if (!view.Active || view.Route.Length == 0) return;
        var player = MapProjectionMotion.PlayerReference(
            frame, ctx.SmoothOverlayMotion, ctx.PlayerGrid, ctx.RawPlayerGrid);

        NumVec2 ProjectPlacement(ExpeditionPlacementView p)
            => Project(p.Grid, player, center, scale, p.TerrainHeight - frame.PlayerTerrainHeight);

        var previous = Project(
            view.RouteStartGrid, player, center, scale,
            view.RouteStartHeight - frame.PlayerTerrainHeight);
        dl.AddCircleFilled(previous, 5f, ColorU32(90, 190, 255, 0.95f), 16);
        var nextIndex = NextExpeditionRouteIndex(view);

        for (var i = 0; i < view.Route.Length; i++)
        {
            var placement = view.Route[i];
            var point = ProjectPlacement(placement);
            var color = i < nextIndex
                ? ColorU32(145, 150, 155, 0.72f)
                : i == nextIndex
                ? ColorU32(75, 255, 120, 0.98f)
                : placement.Bridge
                    ? ColorU32(255, 205, 70, 0.92f)
                    : ColorU32(255, 105, 75, 0.92f);
            dl.AddLine(previous, point, color, i == nextIndex ? 3.5f : 2.5f);
            DrawExpeditionMapRadius(dl, ctx, frame, center, scale, placement, view.EffectiveRadius, color);
            dl.AddCircleFilled(point, i == nextIndex ? 8f : 6f, color, 20);
            var number = (view.PlanBasePlaced + i + 1).ToString();
            var size = ImGui.CalcTextSize(number);
            dl.AddText(point - size * 0.5f, 0xFF101010u, number);
            previous = point;
        }
    }

    private static void DrawExpeditionMapRadius(
        ImDrawListPtr dl,
        RenderContext ctx,
        MapFrame frame,
        NumVec2 center,
        float scale,
        ExpeditionPlacementView placement,
        float radius,
        uint color)
    {
        const int segments = 40;
        NumVec2? previous = null;
        NumVec2 first = default;
        var player = MapProjectionMotion.PlayerReference(
            frame, ctx.SmoothOverlayMotion, ctx.PlayerGrid, ctx.RawPlayerGrid);
        for (var i = 0; i <= segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            var grid = placement.Grid + new NumVec2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var p = Project(
                grid,
                player,
                center,
                scale,
                placement.TerrainHeight - frame.PlayerTerrainHeight);
            if (i == 0) first = p;
            if (previous is { } prev) dl.AddLine(prev, p, color & 0xAAFFFFFFu, 1.2f);
            previous = p;
        }
        if (previous is { } last) dl.AddLine(last, first, color & 0xAAFFFFFFu, 1.2f);
    }

    private void DrawExpeditionNextPlacementWorld(ImDrawListPtr dl, RenderContext ctx)
    {
        if (!_settings.Runecraft.ShowExpeditionNextPlacementWorld || ctx.Map.IsVisible) return;
        var view = ctx.ExpeditionPlanner;
        if (!view.Active || view.Route.Length == 0 || ctx.CameraMatrix is not { Length: >= 16 } matrix) return;
        var nextIndex = NextExpeditionRouteIndex(view);
        if (nextIndex < 0) return;
        var next = view.Route[nextIndex];
        var W = (float)ctx.WindowWidth;
        var H = (float)ctx.WindowHeight;
        const uint color = 0xFF60FF70u;

        NumVec2? previous = null;
        const int segments = 48;
        for (var i = 0; i <= segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            var gx = next.Grid.X + MathF.Cos(angle) * view.EffectiveRadius;
            var gy = next.Grid.Y + MathF.Sin(angle) * view.EffectiveRadius;
            if (!TryProjectGridToScreen((int)MathF.Round(gx), (int)MathF.Round(gy), next.TerrainHeight, matrix, W, H, out var sx, out var sy))
            {
                previous = null;
                continue;
            }
            var p = new NumVec2(sx, sy);
            if (previous is { } prev) dl.AddLine(prev, p, color, 2.5f);
            previous = p;
        }

        if (!TryProjectGridToScreen(
                (int)MathF.Round(next.Grid.X), (int)MathF.Round(next.Grid.Y), next.TerrainHeight,
                matrix, W, H, out var cx, out var cy)) return;
        var center = new NumVec2(cx, cy);
        dl.AddCircleFilled(center, 7f, color, 20);
        var text = $"NEXT #{view.PlanBasePlaced + nextIndex + 1} · {next.Label}";
        var textSize = ImGui.CalcTextSize(text);
        var at = center + new NumVec2(-textSize.X * 0.5f, -30f);
        dl.AddRectFilled(at - new NumVec2(5f, 3f), at + textSize + new NumVec2(5f, 3f), 0xD9111518u, 4f);
        dl.AddText(at, color, text);
    }

    private void DrawRitualPricesWindow(RenderContext ctx)
    {
        if (!ctx.RitualShowPricesWindow) return;

        ImGui.SetNextWindowSizeConstraints(new NumVec2(280, 0), new NumVec2(720, 900));
        if (!ImGui.Begin("Ritual Prices", ImGuiWindowFlags.AlwaysAutoResize)) { ImGui.End(); return; }

        var rows = ctx.RitualPanelRows ?? [];
        if (rows.Length > 0)
        {
            if (!ImGui.BeginTable("ritualPrices", 2,
                    ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp,
                    new NumVec2(480f, 0f)))
            {
                ImGui.End();
                return;
            }

            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 96f);
            ImGui.TableHeadersRow();

            foreach (var row in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(row.ItemName);
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(row.Rarity);

                ImGui.TableSetColumnIndex(1);
                if (row.HasPrice)
                    DrawRitualPriceInline(row.PriceText, row.IconFile, row.TextColor);
                else
                    ImGui.TextDisabled("—");
            }

            ImGui.EndTable();
            ImGui.End();
            return;
        }

        if (ctx.RitualShopOpen)
            ImGui.TextDisabled("Resolving tribute prices…");
        else
            ImGui.TextDisabled("Open the Ritual tribute shop in-game to see rewards.");

        ImGui.End();
    }

    private static void DrawStashValueDebugWindow(RenderContext ctx)
    {
        ImGui.Begin("StashValue Debugger");
        var d = ctx.StashValueDebug;
        ImGui.TextUnformatted($"Active: {d.Active}");
        ImGui.TextUnformatted($"Slots: {d.SlotCount}");
        ImGui.TextUnformatted($"Labels: {d.LabelCount}");
        ImGui.TextUnformatted($"Candidate item slots: {d.CandidateSlots}");
        ImGui.TextUnformatted($"Scanned UI nodes: {d.ScannedNodes}");
        ImGui.TextUnformatted($"Hovered: {d.AnyHovered}");
        ImGui.TextUnformatted($"Scan ms: {d.LastScanMs:F2}");
        ImGui.TextUnformatted($"League: {d.League}");
        ImGui.TextUnformatted($"Loaded prices: {PoeNinjaPriceFetcher.LoadedItemCount}");
        ImGui.End();
    }

    private void DrawLootTracker(RenderContext ctx)
    {
        var lt = ctx.LootTracker;
        if (!lt.Enabled || !_settings.LootTracker.Enabled) return;
        var barMode = LootTrackerDrawPolicy.BarMode(
            lt.Enabled,
            _settings.LootTracker.Enabled,
            lt.OnMap,
            _settings.LootTracker.KeepVisibleAfterRun,
            !string.IsNullOrWhiteSpace(lt.BreakdownTitle));
        if (barMode == LootTrackerBarMode.None)
            return;

        if (_lootTrackerHidden)
        {
            DrawLootTrackerRestoreButton(ctx);
            return;
        }

        if (_settings.LootTracker.ShowPickupToasts && lt.Toasts is { Length: > 0 })
            DrawLootTrackerToasts(ctx, lt);

        if (barMode == LootTrackerBarMode.Map)
            DrawLootTrackerMapBar(ctx, lt);
        else if (barMode == LootTrackerBarMode.Compact)
            DrawLootTrackerCompactBar(ctx, lt);

        if (Volatile.Read(ref _lootDetailsPage) >= 0 && !string.IsNullOrWhiteSpace(lt.BreakdownTitle))
            DrawLootTrackerBreakdown(ctx, lt);
    }

    private void DrawLootTrackerMapBar(RenderContext ctx, LootTrackerView lt)
    {
        var s = _settings.LootTracker;
        var margin = 8f;
        var x = s.BarOnRight ? ctx.WindowWidth - margin : margin;
        var y = ctx.WindowHeight - Math.Clamp(s.BarBottomOffset, 0f, 200f);
        ImGui.SetNextWindowPos(new NumVec2(x, y), ImGuiCond.FirstUseEver, new NumVec2(s.BarOnRight ? 1f : 0f, 1f));
        ImGui.SetNextWindowBgAlpha(Math.Clamp(s.BarOpacity, 0f, 1f));
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing;

        if (!ImGui.Begin("Loot Tracker##loottracker_bar", flags)) { ImGui.End(); return; }
        ImGui.SetWindowFontScale(LootTrackerUiScale(ctx));
        const float gap = 14f;
        const float pad = 5f;

        if (DrawLootIcon("Map")) ImGui.SameLine(0f, pad);
        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(lt.MapName) ? "Map" : lt.MapName);

        ImGui.SameLine(0f, gap);
        if (DrawLootIcon("Time")) ImGui.SameLine(0f, pad);
        ImGui.TextUnformatted(lt.ActiveTimeText);

        ImGui.SameLine(0f, gap);
        ImGui.TextColored(lt.ActiveProfitEx >= 0 ? new Vector4(0.5f, 0.9f, 0.5f, 1f) : new Vector4(0.9f, 0.5f, 0.5f, 1f),
            lt.ActiveProfitText);
        ImGui.SameLine(0f, pad);
        DrawLootCurrencyIcon(lt.ActiveProfitEx);

        ImGui.SameLine(0f, gap);
        ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.25f, 1f), $"Gold {lt.ActiveGoldText}");
        ImGui.SameLine(0f, 6f);
        ImGui.TextDisabled($"({lt.TotalGoldText})");

        if (s.ShowKills)
        {
            ImGui.SameLine(0f, gap);
            DrawLootKillStat("NormalMob", lt.NormalKills);
            ImGui.SameLine(0f, gap);
            DrawLootKillStat("MagicMob", lt.MagicKills);
            ImGui.SameLine(0f, gap);
            DrawLootKillStat("RareMob", lt.RareKills);
            ImGui.SameLine(0f, gap);
            DrawLootKillStat("UniqueMob", lt.UniqueKills);
        }

        ImGui.SameLine(0f, gap);
        DrawLootDetailsButton(lt);

        ImGui.End();
    }

    private void DrawLootDetailsButton(LootTrackerView lt)
    {
        var detailsOpen = Volatile.Read(ref _lootDetailsPage) >= 0;
        var tooltip = detailsOpen
            ? "Close session loot details"
            : $"Open session loot details · {lt.BreakdownItemCount:N0} items";
        if (DrawLootIconButton("##loot_details", "Exalt", tooltip, detailsOpen))
            CycleLootDetails();
        ImGui.SameLine(0f, 4f);
        ImGui.TextDisabled(lt.BreakdownItemCount.ToString("N0"));
    }

    private const int LootDetailsPageSize = 12;

    private void DrawLootTrackerBreakdown(RenderContext ctx, LootTrackerView lt)
    {
        var s = _settings.LootTracker;
        var rows = lt.BreakdownItems ?? [];
        var pageCount = Math.Max(1, (rows.Length + LootDetailsPageSize - 1) / LootDetailsPageSize);
        var page = Math.Clamp(Volatile.Read(ref _lootDetailsPage), 0, pageCount - 1);
        if (page != Volatile.Read(ref _lootDetailsPage))
            Interlocked.Exchange(ref _lootDetailsPage, page);

        var x = s.BarOnRight ? ctx.WindowWidth - 8f : 8f;
        var y = ctx.WindowHeight - Math.Clamp(s.BarBottomOffset, 0f, 200f) - 38f;
        var window = LootTrackerDrawPolicy.BreakdownWindow;
        ImGui.SetNextWindowPos(
            new NumVec2(x, y),
            window.PositionCondition,
            new NumVec2(s.BarOnRight ? 1f : 0f, 1f));
        ImGui.SetNextWindowBgAlpha(Math.Clamp(s.BarOpacity + 0.12f, 0f, 1f));

        if (!ImGui.Begin("Run Loot##loottracker_breakdown", window.Flags)) { ImGui.End(); return; }
        var scale = LootTrackerUiScale(ctx);
        ImGui.SetWindowFontScale(scale);

        ImGui.TextUnformatted(lt.BreakdownTitle);
        ImGui.SameLine(0f, 12f);
        ImGui.TextColored(new Vector4(0.5f, 0.9f, 0.5f, 1f), lt.BreakdownValueText);
        if (lt.BreakdownGold > 0)
        {
            ImGui.SameLine(0f, 12f);
            ImGui.TextColored(new Vector4(1.0f, 0.84f, 0.25f, 1f), $"Gold {lt.BreakdownGold:N0}");
        }
        ImGui.SameLine(0f, 12f);
        var priceSource = s.PriceSource == PoeNinjaPriceFetcher.SourcePoeNinja
            ? "poe.ninja"
            : "poe2scout";
        ImGui.TextDisabled($"{priceSource} · {lt.League}");

        ImGui.Separator();
        if (rows.Length == 0)
        {
            ImGui.TextDisabled("Nothing has entered the inventory during this run yet.");
        }
        else if (ImGui.BeginTable("loottracker_breakdown_rows", 4,
                     ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, 250f * scale);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 58f * scale);
            ImGui.TableSetupColumn("Each", ImGuiTableColumnFlags.WidthFixed, 92f * scale);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 105f * scale);
            ImGui.TableHeadersRow();

            var first = page * LootDetailsPageSize;
            var last = Math.Min(rows.Length, first + LootDetailsPageSize);
            for (var i = first; i < last; i++)
            {
                var row = rows[i];
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(row.Label);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(row.Count.ToString("N0"));
                ImGui.TableSetColumnIndex(2);
                ImGui.TextDisabled(row.UnitValueText);
                ImGui.TableSetColumnIndex(3);
                if (row.Priced)
                    ImGui.TextColored(new Vector4(0.5f, 0.9f, 0.5f, 1f), row.TotalValueText);
                else
                    ImGui.TextDisabled(row.TotalValueText);
            }
            ImGui.EndTable();
        }

        var binding = HotkeyCodes.DisplayName(s.DetailsHotkey);
        if (pageCount > 1)
            ImGui.TextDisabled($"Page {page + 1}/{pageCount} · {binding}: next page / close");
        else
            ImGui.TextDisabled($"{binding}: close");
        ImGui.SameLine();
        if (ImGui.SmallButton(page + 1 < pageCount ? "Next" : "Close"))
            CycleLootDetails();

        ImGui.End();
    }

    private void DrawLootTrackerCompactBar(RenderContext ctx, LootTrackerView lt)
    {
        var s = _settings.LootTracker;
        var x = s.BarOnRight ? ctx.WindowWidth - 8f : 8f;
        var y = ctx.WindowHeight - Math.Clamp(s.BarBottomOffset, 0f, 200f);
        var window = LootTrackerDrawPolicy.CompactWindow;
        ImGui.SetNextWindowPos(
            new NumVec2(x, y),
            window.PositionCondition,
            new NumVec2(s.BarOnRight ? 1f : 0f, 1f));
        ImGui.SetNextWindowBgAlpha(Math.Clamp(s.BarOpacity, 0f, 1f));

        if (!ImGui.Begin("Loot Tracker##loottracker_bar", window.Flags)) { ImGui.End(); return; }
        var scale = LootTrackerUiScale(ctx);
        ImGui.SetWindowFontScale(scale);
        const float gap = 14f;
        const float pad = 5f;

        if (DrawLootIcon("Map", "Maps in this session")) ImGui.SameLine(0f, pad);
        ImGui.TextUnformatted(lt.CompletedMaps.ToString("N0"));

        ImGui.SameLine(0f, gap);
        if (DrawLootIcon("Time", "Average active map time")) ImGui.SameLine(0f, pad);
        ImGui.TextUnformatted($"AVG {lt.AverageTimeText}");

        ImGui.SameLine(0f, gap);
        if (DrawLootCurrencyIcon(lt.ActiveProfitEx)) ImGui.SameLine(0f, pad);
        ImGui.TextUnformatted($"AVG {lt.AverageProfitText}");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Average priced loot per map");

        ImGui.Separator();
        DrawLootCurrencyIcon(lt.BreakdownValueEx);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Total priced session loot");
        ImGui.SameLine(0f, pad);
        ImGui.TextColored(new Vector4(0.5f, 0.9f, 0.5f, 1f), lt.TotalProfitText);

        ImGui.SameLine(0f, gap);
        ImGui.TextDisabled($"{lt.ProfitPerHourText}/h");

        ImGui.SameLine(0f, gap);
        if (DrawLootIcon("Time", lt.OnMap ? "Active session time · running" : "Active session time · paused"))
            ImGui.SameLine(0f, pad);
        ImGui.TextUnformatted(lt.SessionTimeText);

        if (window.ShowLootButton)
        {
            ImGui.SameLine(0f, gap);
            DrawLootDetailsButton(lt);
        }

        ImGui.SameLine(0f, gap);
        if (DrawEyeButton(
                "##LootTrackerVisibility",
                visible: true,
                visibleTooltip: "Hide Loot Tracker",
                hiddenTooltip: "Show Loot Tracker"))
        {
            _lootTrackerHidden = true;
            Interlocked.Exchange(ref _lootDetailsPage, -1);
        }

        ImGui.End();
    }

    private void DrawLootTrackerRestoreButton(RenderContext ctx)
    {
        var s = _settings.LootTracker;
        var x = s.BarOnRight ? ctx.WindowWidth - 8f : 8f;
        var y = ctx.WindowHeight - Math.Clamp(s.BarBottomOffset, 0f, 200f);
        ImGui.SetNextWindowPos(
            new NumVec2(x, y),
            ImGuiCond.Always,
            new NumVec2(s.BarOnRight ? 1f : 0f, 1f));
        ImGui.SetNextWindowBgAlpha(Math.Clamp(s.BarOpacity, 0.45f, 1f));
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing;
        if (!ImGui.Begin("##loottracker_restore", flags)) { ImGui.End(); return; }
        ImGui.SetWindowFontScale(LootTrackerUiScale(ctx));
        if (ImGui.Button("Show Loot Tracker"))
            _lootTrackerHidden = false;
        ImGui.End();
    }

    private void DrawLootTrackerToasts(RenderContext ctx, LootTrackerView lt)
    {
        var s = _settings.LootTracker;
        var x = s.BarOnRight ? ctx.WindowWidth - 8f : 8f;
        var y = ctx.WindowHeight - Math.Clamp(s.BarBottomOffset, 0f, 200f) - 42f;
        ImGui.SetNextWindowPos(new NumVec2(x, y), ImGuiCond.Always, new NumVec2(s.BarOnRight ? 1f : 0f, 1f));
        ImGui.SetNextWindowBgAlpha(Math.Clamp(s.BarOpacity, 0f, 1f));
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove;
        if (!ImGui.Begin("##loottracker_toasts", flags)) { ImGui.End(); return; }
        ImGui.SetWindowFontScale(LootTrackerUiScale(ctx));

        foreach (var toast in lt.Toasts)
        {
            var a = Math.Clamp(toast.Alpha, 0f, 1f);
            DrawLootCurrencyIcon();
            ImGui.SameLine(0f, 5f);
            ImGui.TextColored(new Vector4(0.92f, 0.92f, 0.92f, a),
                toast.Count > 1 ? $"{toast.Label} x{toast.Count}" : toast.Label);
            ImGui.SameLine(0f, 10f);
            ImGui.TextColored(new Vector4(0.5f, 0.9f, 0.5f, a), $"+{toast.ValueText}");
        }

        ImGui.End();
    }

    private void DrawLootKillStat(string iconKey, int count)
    {
        ImGui.TextUnformatted(count.ToString());
        ImGui.SameLine(0f, 4f);
        DrawLootIcon(iconKey);
    }

    private bool DrawLootIcon(string key, string? tooltip = null)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "LootTracker", "icons", key + ".png");
        if (!_textures.TryGet(this, path, out var tex) || tex.Height <= 0) return false;
        var h = ImGui.GetTextLineHeight();
        var w = h * tex.Width / (float)tex.Height;
        ImGui.Image(tex.Id, new NumVec2(w, h));
        if (!string.IsNullOrWhiteSpace(tooltip) &&
            ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
        {
            ImGui.SetTooltip(tooltip);
        }
        return true;
    }

    private bool DrawLootIconButton(
        string id,
        string key,
        string tooltip,
        bool selected)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "LootTracker", "icons", key + ".png");
        if (!_textures.TryGet(this, path, out var tex) || tex.Height <= 0)
        {
            var clickedFallback = ImGui.SmallButton($"◆{id}");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip(tooltip);
            return clickedFallback;
        }

        var side = ImGui.GetTextLineHeight() + 8f;
        var clicked = ImGui.InvisibleButton(id, new NumVec2(side, side));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();
        var bg = selected
            ? ColorU32(60, 160, 205, hovered ? 0.65f : 0.48f)
            : ColorU32(70, 76, 88, hovered ? 0.7f : 0.42f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(min, max, bg, 4f);
        dl.AddRect(min, max, ColorU32(145, 165, 190, hovered ? 0.9f : 0.55f), 4f, ImDrawFlags.None, 1f);

        var innerMin = min + new NumVec2(4f, 4f);
        var innerMax = max - new NumVec2(4f, 4f);
        var aspect = tex.Width / (float)tex.Height;
        if (aspect > 1f)
        {
            var height = (innerMax.X - innerMin.X) / aspect;
            var inset = ((innerMax.Y - innerMin.Y) - height) * 0.5f;
            innerMin.Y += inset;
            innerMax.Y -= inset;
        }
        else
        {
            var width = (innerMax.Y - innerMin.Y) * aspect;
            var inset = ((innerMax.X - innerMin.X) - width) * 0.5f;
            innerMin.X += inset;
            innerMax.X -= inset;
        }
        dl.AddImage(tex.Id, innerMin, innerMax);

        if (hovered)
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private bool DrawLootCurrencyIcon(double ex = 1)
    {
        var selected = Math.Clamp(
            _settings.LootTracker.DisplayCurrency,
            LootTrackerSettings.CurrencyAuto,
            LootTrackerSettings.CurrencyChaos);
        if (_settings.LootTracker.ShowPricesInDivineOnly &&
            selected == LootTrackerSettings.CurrencyExalted)
        {
            selected = LootTrackerSettings.CurrencyDivine;
        }
        else if (selected == LootTrackerSettings.CurrencyAuto)
        {
            var divRate = PoeNinjaPriceFetcher.DivineToExaltedRate;
            selected = divRate > 0 && ex >= divRate
                ? LootTrackerSettings.CurrencyDivine
                : ex < 1 && PoeNinjaPriceFetcher.GetChaosPerExalted() > 0
                    ? LootTrackerSettings.CurrencyChaos
                    : LootTrackerSettings.CurrencyExalted;
        }

        return selected switch
        {
            LootTrackerSettings.CurrencyDivine => DrawLootIcon("Divine"),
            LootTrackerSettings.CurrencyExalted => DrawLootIcon("Exalt"),
            _ => false,
        };
    }

    private float LootTrackerUiScale(RenderContext ctx)
    {
        var auto = ctx.WindowHeight > 0 ? ctx.WindowHeight / 1600f : 1f;
        return Math.Clamp(auto * _settings.LootTracker.UiScale, 0.5f, 3f);
    }

    private void DrawRitualPriceInline(string priceText, string iconFile, uint textColor)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.TextUnformatted(priceText);
        ImGui.PopStyleColor();
        if (string.IsNullOrEmpty(iconFile)) return;
        ImGui.SameLine(0, 3f);
        float iconH = ImGui.GetTextLineHeight();
        if (_textures.TryGet(this, RitualCurrencyIcons.PathFor(iconFile), out var tex) && tex.Height > 0)
        {
            float iconW = iconH * tex.Width / (float)tex.Height;
            ImGui.Image(tex.Id, new NumVec2(iconW, iconH));
        }
    }

    private void DrawExaltedPriceInline(double value, uint textColor)
    {
        var text = value >= 10 ? value.ToString("F0") : value.ToString("F1");
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.SameLine(0, 3f);
        float iconH = ImGui.GetTextLineHeight();
        if (_textures.TryGet(this, RitualCurrencyIcons.PathFor("exalted.png"), out var tex) && tex.Height > 0)
        {
            float iconW = iconH * tex.Width / (float)tex.Height;
            ImGui.Image(tex.Id, new NumVec2(iconW, iconH));
        }
    }

    private static uint ColorFromU(uint u)
        => ColorU32((byte)((u >> 16) & 0xFF), (byte)((u >> 8) & 0xFF), (byte)(u & 0xFF), ((u >> 24) & 0xFF) / 255f);

    private static void DrawPartialImage(
        ImDrawListPtr dl,
        TextureRegistry.TextureHandle texture,
        float x,
        float y,
        float width,
        float height,
        float fraction,
        uint tint)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        if (fraction <= 0f) return;
        dl.AddImage(
            texture.Id,
            new NumVec2(x, y),
            new NumVec2(x + width * fraction, y + height),
            new NumVec2(0, 0),
            new NumVec2(fraction, 1),
            tint);
    }

    // ── Map overlay label chips (corner minimap + full-screen overlay map) ──

    private readonly record struct MapLabelCandidate(string Key, NumVec2 Pos, string Text, uint TextColor, uint SwatchColor, int StackOrder = 0);

    private const float LabelChipRowH = 18f;
    private const float LabelChipBodyH = LabelChipRowH - 2f;
    private const float MapLabelChipMarginPx = 6f;

    private static float LabelChipWidth(string text, float scale = 1f) =>
        Math.Min(text.Length * 7.2f * scale + 21f * scale, 240f * scale);

    private static void GetSoloChipRect(MapLabelCandidate c, out float left, out float top, out float right, out float bottom, float labelScale = 1f)
    {
        left = c.Pos.X + 7f * labelScale;
        top = c.Pos.Y - 7f * labelScale;
        right = left + LabelChipWidth(c.Text, labelScale);
        bottom = top + LabelChipBodyH * labelScale;
    }

    private static bool LayoutRectsOverlap(
        float l1, float t1, float r1, float b1,
        float l2, float t2, float r2, float b2,
        float margin)
        => l1 - margin < r2 && r1 + margin > l2 && t1 - margin < b2 && b1 + margin > t2;

    private static void DrawLabelChip(ImDrawListPtr dl, float left, float top, string text, uint textColor, uint swatchColor, float scale = 1f)
    {
        var bodyH = LabelChipBodyH * scale;
        var textW = LabelChipWidth(text, scale);
        var bottom = top + bodyH;
        dl.AddRectFilled(new NumVec2(left, top), new NumVec2(left + textW, bottom), ColorU32(13, 13, 13, 0.82f));
        dl.AddRect(new NumVec2(left, top), new NumVec2(left + textW, bottom), ColorU32(56, 56, 56, 0.22f), 0, 0, 1f);
        var swatchSize = 7f * scale;
        var swatchY = top + (bodyH - swatchSize) * 0.5f;
        dl.AddRectFilled(new NumVec2(left + 4f * scale, swatchY), new NumVec2(left + 4f * scale + swatchSize, swatchY + swatchSize), swatchColor);
        var font = ImGui.GetFont();
        var fontSize = font.FontSize * scale;
        dl.AddText(font, fontSize, new NumVec2(left + 15f * scale, top + 1f * scale), textColor, text);
    }

    /// <summary>Atlas labels: cluster when few; solo chips when many (avoids O(n²) overlap merge).</summary>
    private void DrawAtlasLabelChips(
        ImDrawListPtr dl,
        List<MapLabelCandidate> candidates,
        float clipL, float clipT, float clipR, float clipB,
        float labelScale)
    {
        const int clusterThreshold = 48;
        if (candidates.Count <= clusterThreshold)
        {
            DrawMapLabelChips(dl, candidates, clipL, clipT, clipR, clipB, labelScale, smooth: false, pixelSnap: true);
            return;
        }

        var rowH = LabelChipRowH * labelScale;
        foreach (var c in candidates)
        {
            var soloW = LabelChipWidth(c.Text, labelScale);
            var soloLeft = Math.Clamp(c.Pos.X + 7f * labelScale, clipL + 4f, clipR - soloW - 4f);
            var soloTop = Math.Clamp(c.Pos.Y - 7f * labelScale, clipT + 4f, clipB - rowH - 4f);
            DrawLabelChip(dl, soloLeft, soloTop, c.Text, c.TextColor, c.SwatchColor, labelScale);
        }
    }

    /// <summary>One chip per label text on the map overlay — many boss arena tiles share the same zone boss name.</summary>
    private static bool MapLabelAlreadyPresent(List<MapLabelCandidate> labels, string text)
    {
        if (!IsSingleChipBossLabel(text)) return false;
        foreach (var c in labels)
            if (string.Equals(c.Text, text, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsSingleChipBossLabel(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.EndsWith(" (Boss)", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(text, "Boss room", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Boss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Bosses", StringComparison.OrdinalIgnoreCase);
    }

    private static List<MapLabelCandidate> DedupeClusterByText(List<MapLabelCandidate> cluster)
    {
        if (cluster.Count <= 1) return cluster;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<MapLabelCandidate>(cluster.Count);
        foreach (var c in cluster)
        {
            if (seen.Add(c.Text))
                deduped.Add(c);
        }
        return deduped;
    }

    private void DrawMapLabelChips(
        ImDrawListPtr dl,
        List<MapLabelCandidate> candidates,
        float clipL, float clipT, float clipR, float clipB,
        float labelScale = 1f,
        bool smooth = true,
        bool pixelSnap = true)
    {
        var rowH = LabelChipRowH * labelScale;
        foreach (var rawCluster in BuildMapLabelClusters(candidates, labelScale))
        {
            var cluster = DedupeClusterByText(rawCluster);
            if (cluster.Count == 1)
            {
                var c = cluster[0];
                var soloW = LabelChipWidth(c.Text, labelScale);
                var soloLeft = Math.Clamp(c.Pos.X + 7f * labelScale, clipL + 4f, clipR - soloW - 4f);
                var soloTop = Math.Clamp(c.Pos.Y - 7f * labelScale, clipT + 4f, clipB - rowH - 4f);
                var pos = SmoothScreenPoint(c.Key + ":chip", new NumVec2(soloLeft, soloTop), _ctx?.ChipSmoothingMs ?? 70, smooth && (_ctx?.SmoothOverlayMotion ?? true));
                soloLeft = PixelSnap(pos.X, pixelSnap);
                soloTop = PixelSnap(pos.Y, pixelSnap);
                DrawLabelChip(dl, soloLeft, soloTop, c.Text, c.TextColor, c.SwatchColor, labelScale);
                continue;
            }

            cluster.Sort((a, b) => a.StackOrder != b.StackOrder
                ? a.StackOrder.CompareTo(b.StackOrder)
                : string.Compare(a.Text, b.Text, StringComparison.Ordinal));
            var anchor = GetClusterAnchor(cluster);
            var stackH = cluster.Count * rowH;
            var startTop = Math.Clamp(
                anchor.Y - stackH * 0.5f,
                clipT + 4f,
                Math.Max(clipT + 4f, clipB - stackH - 4f));
            var maxW = 0f;
            foreach (var c in cluster)
                maxW = Math.Max(maxW, LabelChipWidth(c.Text, labelScale));
            var left = Math.Clamp(anchor.X + 14f * labelScale, clipL + 4f, clipR - maxW - 4f);
            var stackPos = SmoothScreenPoint(cluster[0].Key + ":cluster", new NumVec2(left, startTop), _ctx?.ChipSmoothingMs ?? 70, smooth && (_ctx?.SmoothOverlayMotion ?? true));
            left = PixelSnap(stackPos.X, pixelSnap);
            startTop = PixelSnap(stackPos.Y, pixelSnap);

            for (var i = 0; i < cluster.Count; i++)
            {
                var c = cluster[i];
                DrawLabelChip(dl, left, startTop + i * rowH, c.Text, c.TextColor, c.SwatchColor, labelScale);
            }
        }
    }

    private static NumVec2 GetClusterAnchor(List<MapLabelCandidate> cluster)
    {
        float cx = 0f, cy = 0f;
        foreach (var c in cluster)
        {
            cx += c.Pos.X;
            cy += c.Pos.Y;
        }
        return new NumVec2(cx / cluster.Count, cy / cluster.Count);
    }

    private static (float left, float top, float right, float bottom) GetClusterLayoutRect(
        List<MapLabelCandidate> cluster, float labelScale = 1f)
    {
        if (cluster.Count == 1)
        {
            GetSoloChipRect(cluster[0], out var left, out var top, out var right, out var bottom, labelScale);
            return (left, top, right, bottom);
        }

        var anchor = GetClusterAnchor(cluster);
        var stackH = cluster.Count * LabelChipRowH * labelScale;
        var maxW = 0f;
        foreach (var c in cluster)
            maxW = Math.Max(maxW, LabelChipWidth(c.Text, labelScale));
        var stackLeft = anchor.X + 14f;
        var stackTop = anchor.Y - stackH * 0.5f;
        return (stackLeft, stackTop, stackLeft + maxW, stackTop + stackH);
    }

    private static List<List<MapLabelCandidate>> BuildMapLabelClusters(
        List<MapLabelCandidate> candidates, float labelScale = 1f)
    {
        var n = candidates.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++) parent[i] = i;

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        void Union(int a, int b) => parent[Find(a)] = Find(b);

        // Merge labels whose default solo chips would overlap (long names need rect checks, not icon distance).
        for (var i = 0; i < n; i++)
        {
            GetSoloChipRect(candidates[i], out var l1, out var t1, out var r1, out var b1, labelScale);
            for (var j = i + 1; j < n; j++)
            {
                GetSoloChipRect(candidates[j], out var l2, out var t2, out var r2, out var b2, labelScale);
                if (LayoutRectsOverlap(l1, t1, r1, b1, l2, t2, r2, b2, MapLabelChipMarginPx * labelScale))
                    Union(i, j);
            }
        }

        var groups = new Dictionary<int, List<MapLabelCandidate>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list))
            {
                list = new List<MapLabelCandidate>();
                groups[root] = list;
            }
            list.Add(candidates[i]);
        }

        var clusters = groups.Values.ToList();
        return MergeOverlappingClusterLayouts(clusters, labelScale);
    }

    private static List<List<MapLabelCandidate>> MergeOverlappingClusterLayouts(
        List<List<MapLabelCandidate>> clusters, float labelScale = 1f)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < clusters.Count && !changed; i++)
            {
                var (l1, t1, r1, b1) = GetClusterLayoutRect(clusters[i], labelScale);
                for (var j = i + 1; j < clusters.Count; j++)
                {
                    var (l2, t2, r2, b2) = GetClusterLayoutRect(clusters[j], labelScale);
                    if (!LayoutRectsOverlap(l1, t1, r1, b1, l2, t2, r2, b2, MapLabelChipMarginPx))
                        continue;
                    clusters[i].AddRange(clusters[j]);
                    clusters.RemoveAt(j);
                    changed = true;
                    break;
                }
            }
        }
        return clusters;
    }

    private static bool AnyPathLayerEnabled(RenderContext ctx)
        => (ctx.ShowPathWorld && ctx.ShowGroundWaypoints) || ctx.ShowPathMap || ctx.ShowPathMinimap;

    // ── Path endpoint labels ──

    private void DrawPathLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.SelectedPaths.Length == 0 || !ctx.ShowPathWorld || !ctx.ShowGroundWaypoints) return;
        if (ShouldDrawLargeMapOverlay(ctx.Map)) return;
        float W = ctx.WindowWidth, H = ctx.WindowHeight;
        if (ctx.CameraMatrix is not { Length: >= 16 } m) return;

        // Stack path labels at the local player's screen position (not another Player entity in town).
        var playerScreen = ProjectWorldToScreen(ctx.PlayerWorld, m, W, H);
        playerScreen = SmoothScreenPoint("path-label:player", playerScreen, ctx.OverlaySmoothingMs, ctx.SmoothOverlayMotion);

        // Build label rows sorted by color slot so the stack order is stable and matches the legend.
        var labels = new List<(int slot, string text)>();
        foreach (var path in ctx.SelectedPaths)
        {
            var label = string.IsNullOrWhiteSpace(path.Label) ? path.TargetId : path.Label;
            var status = path.Status switch
            {
                NavTargetStatus.Cached when path.IsEntity => " (last seen)",
                NavTargetStatus.NoPath => NoPathStatusSuffix(path.RouteStatus),
                _ => RouteStatusSuffix(path.RouteStatus),
            };
            var distSuffix = path.PathDistance >= 0 ? $" ~{path.PathDistance:F0}t" : "";
            labels.Add((path.ColorSlot, $"{path.ColorSlot + 1}. {label}{distSuffix}{status}"));
        }
        if (labels.Count == 0) return;
        labels.Sort((a, b) => a.slot.CompareTo(b.slot));

        // Stack the rows vertically from the cluster anchor so they never mask each other, clamped on-screen.
        const float textH = 18f;
        var stackH = labels.Count * textH;
        var startTop = Math.Clamp(playerScreen.Y - stackH * 0.5f, 4f, Math.Max(4f, H - stackH - 4f));

        var maxW = 0f;
        foreach (var (_, text) in labels)
            maxW = Math.Max(maxW, LabelChipWidth(text));
        var left = Math.Clamp(playerScreen.X + 14f, 4f, W - maxW - 4f);
        var labelPos = SmoothScreenPoint("path-label:stack", new NumVec2(left, startTop), ctx.ChipSmoothingMs, ctx.SmoothOverlayMotion);
        left = PixelSnap(labelPos.X, ctx.PixelSnapLabels);
        startTop = PixelSnap(labelPos.Y, ctx.PixelSnapLabels);

        for (var i = 0; i < labels.Count; i++)
        {
            var (slot, text) = labels[i];
            DrawLabelChip(dl, left, startTop + i * textH, text, PathColor(slot), PathColor(slot));
        }
    }

    // ── Cursor inspect HUD ──

    private static void DrawCursorInspect(ImDrawListPtr dl, RenderContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.CursorInspectTitle)) return;

        var title = ctx.CursorInspectTitle;
        var meta = ctx.CursorInspectMeta ?? "";
        var panelW = MathF.Max(title.Length, meta.Length) * 7.3f + 20f;
        const float panelH = 42f;
        const float x = 10f;
        const float y = 72f;

        dl.AddRectFilled(new NumVec2(x, y), new NumVec2(x + panelW, y + panelH), ColorU32(20, 20, 28, 0.88f), 4f);
        dl.AddRect(new NumVec2(x, y), new NumVec2(x + panelW, y + panelH), ColorU32(120, 180, 255, 0.9f), 4f);
        dl.AddText(new NumVec2(x + 6f, y + 4f), ColorU32(230, 230, 230, 0.95f), title);
        if (meta.Length > 0)
            dl.AddText(new NumVec2(x + 6f, y + 22f), ColorU32(160, 160, 170, 0.9f), meta);
    }

    // ── Nav menu ──

    private void DrawNavMenu(RenderContext ctx)
    {
        _navMenuCorner = ctx.NavMenuCorner;
        var isRight = _navMenuCorner is "TopRight" or "BottomRight";
        var isBottom = _navMenuCorner is "BottomLeft" or "BottomRight";

        var cornerPos = isRight
            ? new System.Numerics.Vector2(ctx.WindowWidth - 10, isBottom ? ctx.WindowHeight - 10 : 10)
            : new System.Numerics.Vector2(10, isBottom ? ctx.WindowHeight - 10 : 10);
        var pivot = new System.Numerics.Vector2(isRight ? 1f : 0f, isBottom ? 1f : 0f);

        ImGui.SetNextWindowBgAlpha(0.92f);
        if (!_navTaskbarPositionInitialized)
        {
            var settings = _settings;
            if (settings.NavTaskbarX >= 0f && settings.NavTaskbarY >= 0f)
            {
                var saved = new NumVec2(
                    Math.Clamp(settings.NavTaskbarX, 0f, Math.Max(0f, ctx.WindowWidth - 40f)),
                    Math.Clamp(settings.NavTaskbarY, 0f, Math.Max(0f, ctx.WindowHeight - 30f)));
                ImGui.SetNextWindowPos(saved, ImGuiCond.Always);
            }
            else
            {
                ImGui.SetNextWindowPos(cornerPos, ImGuiCond.Always, pivot);
            }
            _navTaskbarPositionInitialized = true;
        }

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NumVec2(7f, 5f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new NumVec2(6f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new NumVec2(7f, 3f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 7f);
        if (!ImGui.Begin("Nav", flags))
        {
            ImGui.End();
            ImGui.PopStyleVar(4);
            return;
        }

        var selected = 0;
        foreach (var row in ctx.Legend) if (row.IsSelected) selected++;

        DrawTaskbarDragHandle(ctx);
        ImGui.SameLine(0f, 5f);

        ImGui.InvisibleButton("##ConnectionStatus", new NumVec2(12f, 22f));
        var statusMin = ImGui.GetItemRectMin();
        var statusMax = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddCircleFilled(
            (statusMin + statusMax) * 0.5f,
            4.5f,
            ColorU32(62, 235, 116, 1f),
            16);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Connected to the current area");
        ImGui.SameLine(0f, 4f);

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(ImGuiTheme.Accent));
        if (ImGui.Button(_navMenuExpanded ? "Overlay  ▲" : "Overlay  ▼"))
            _navMenuExpanded = !_navMenuExpanded;
        ImGui.PopStyleColor();
        ImGui.SameLine(0f, 8f);

        var perf = ctx.Perf;
        TextColoredUnformatted(new Vector4(0.70f, 0.92f, 1f, 1f), $"CPU {perf.ProcessCpuPct:F0}%");
        if (ctx.ShowFpsOverlay)
        {
            ImGui.SameLine(0f, 8f);
            ImGui.TextDisabled($"{perf.RenderFps:F0} FPS");
            ImGui.SameLine(0f, 8f);
            ImGui.TextDisabled($"{perf.WorkingSetMb:F0} MB");
        }
        if (selected > 0)
        {
            ImGui.SameLine(0f, 8f);
            TextColoredUnformatted(ImGuiTheme.Accent, $"{selected}/8 routes");
        }
        if (!string.Equals(ctx.PickupStatus, "Disabled", StringComparison.Ordinal))
        {
            ImGui.SameLine(0f, 8f);
            var pickupColor = ctx.PickupRunning
                ? new Vector4(0.28f, 0.91f, 0.64f, 1f)
                : new Vector4(0.75f, 0.80f, 0.86f, 1f);
            var shortStatus = ctx.PickupStatus.StartsWith("PICKING", StringComparison.Ordinal) ? "PICKING" :
                ctx.PickupStatus.StartsWith("TARGET", StringComparison.Ordinal) ? "TARGET" :
                ctx.PickupStatus.StartsWith("PICKED", StringComparison.Ordinal) ? "PICKED" :
                ctx.PickupStatus.StartsWith("MISSED", StringComparison.Ordinal) ? "RETRY" :
                ctx.PickupStatus.StartsWith("ASSIST", StringComparison.Ordinal) ? "ASSIST" :
                ctx.PickupStatus.StartsWith("AUTO armed", StringComparison.Ordinal) ? "ARMED" :
                ctx.PickupStatus.StartsWith("READY", StringComparison.Ordinal) ? "READY" : "STOPPED";
            TextColoredUnformatted(pickupColor, $"Pickup {shortStatus}");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip(ctx.PickupStatus);
        }
        ImGui.SameLine(0f, 8f);
        if (DrawEyeButton(
                "##OverlayVisibility",
                ctx.Active,
                "Hide overlay content",
                "Show overlay content"))
            _enqueue(() => _toggleRendering());
        ImGui.SameLine(0f, 3f);
        if (ImGui.Button("⚙##TaskbarSettings"))
        {
            var useClassicSettings = !string.Equals(
                _settings.InterfaceStyle,
                "Modern",
                StringComparison.OrdinalIgnoreCase);
            if (useClassicSettings && _openExternalSettings is { } openExternalSettings)
                _enqueue(openExternalSettings);
            else
                _settingsOpen = !_settingsOpen;
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Open settings");

        if (_navMenuExpanded)
        {
            ImGui.Separator();
            if (ImGui.Button("+ Nearest"))
                _enqueue(() => _addNearest());
            ImGui.SameLine();
            if (ImGui.Button("Clear routes"))
                _enqueue(() => _clearPaths());
            ImGui.SameLine();
            ImGui.TextDisabled($"{ctx.AreaCode} · Lv {ctx.CharLevel}");

            if (ctx.ShowPerfStats)
                DrawNavPerfStats(ctx);

            if (ctx.Legend.Count > 0)
                ImGui.Separator();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(8f, 6f));
            foreach (var row in ctx.Legend)
            {
                var color = row.IsSelected ? PathColorVec(row.ColorSlot) : new Vector4(0.7f, 0.7f, 0.7f, 0.65f);
                ImGui.PushStyleColor(ImGuiCol.Text, color);
                if (ImGui.Selectable(LegendRowText(row), row.IsSelected))
                {
                    var id = row.Target.Id;
                    _enqueue(() => _toggleTarget(id));
                }
                ImGui.PopStyleColor();
            }
            ImGui.PopStyleVar();
        }

        ImGui.End();
        ImGui.PopStyleVar(4);
    }

    private void DrawTaskbarDragHandle(RenderContext ctx)
    {
        ImGui.InvisibleButton("##TaskbarDrag", new NumVec2(13f, 22f));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        var color = ColorU32(150, 158, 170, ImGui.IsItemHovered() ? 0.95f : 0.65f);
        for (var y = -1; y <= 1; y++)
        {
            dl.AddCircleFilled(
                new NumVec2(min.X + 4f, (min.Y + max.Y) * 0.5f + y * 5f),
                1.25f,
                color,
                8);
            dl.AddCircleFilled(
                new NumVec2(min.X + 9f, (min.Y + max.Y) * 0.5f + y * 5f),
                1.25f,
                color,
                8);
        }

        var dragging = ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left);
        if (dragging)
        {
            var io = ImGui.GetIO();
            var position = ImGui.GetWindowPos() + io.MouseDelta;
            var size = ImGui.GetWindowSize();
            position.X = Math.Clamp(position.X, 0f, Math.Max(0f, ctx.WindowWidth - size.X));
            position.Y = Math.Clamp(position.Y, 0f, Math.Max(0f, ctx.WindowHeight - size.Y));
            ImGui.SetWindowPos(position, ImGuiCond.Always);
            _navTaskbarWasDragging = true;
        }
        else if (_navTaskbarWasDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var position = ImGui.GetWindowPos();
            _navTaskbarWasDragging = false;
            _enqueue(() => _setTaskbarPosition(position.X, position.Y));
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Drag to move the taskbar");
    }

    private static bool DrawEyeButton(
        string id,
        bool visible,
        string visibleTooltip,
        string hiddenTooltip)
    {
        var clicked = ImGui.InvisibleButton(id, new NumVec2(28f, 22f));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var center = (min + max) * 0.5f;
        var hovered = ImGui.IsItemHovered();
        var color = visible
            ? ColorU32(100, 220, 255, hovered ? 1f : 0.9f)
            : ColorU32(145, 150, 160, hovered ? 1f : 0.75f);
        var dl = ImGui.GetWindowDrawList();

        NumVec2? upper = null;
        NumVec2? lower = null;
        const int segments = 14;
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (float)segments;
            var x = min.X + 3f + t * (max.X - min.X - 6f);
            var arc = MathF.Sin(t * MathF.PI) * 5.5f;
            var up = new NumVec2(x, center.Y - arc);
            var down = new NumVec2(x, center.Y + arc);
            if (upper is { } prevUp) dl.AddLine(prevUp, up, color, 1.7f);
            if (lower is { } prevDown) dl.AddLine(prevDown, down, color, 1.7f);
            upper = up;
            lower = down;
        }
        if (visible)
            dl.AddCircleFilled(center, 3.2f, color, 14);
        else
        {
            dl.AddCircle(center, 3.2f, color, 14, 1.5f);
            dl.AddLine(min + new NumVec2(4f, 3f), max - new NumVec2(4f, 3f), ColorU32(245, 105, 105, 0.9f), 1.8f);
        }

        if (hovered)
            ImGui.SetTooltip(visible ? visibleTooltip : hiddenTooltip);
        return clicked;
    }

    private static void DrawNavPerfStats(RenderContext ctx)
    {
        var p = ctx.Perf;
        ImGui.Spacing();

        TextColoredUnformatted(new Vector4(0.55f, 0.92f, 1f, 1f),
            $"tick {p.Fps:F0} fps   render {p.RenderFps:F0} fps   ({p.TickMs:F1} ms)");
        var gpu = FormatGpuPercent(p.GpuPercent);
        var vram = p.GpuMemoryMb >= 0 ? $"{p.GpuMemoryMb:F0} MB VRAM" : "VRAM n/a";
        TextColoredUnformatted(new Vector4(0.75f, 1f, 0.82f, 1f),
            $"Overlay CPU {p.ProcessCpuPct:F0}%   GPU {gpu}   RAM {p.WorkingSetMb:F0} MB   {vram}");

        if (ctx.ShowPerfStats)
        {
            TextColoredUnformatted(new Vector4(0.92f, 0.92f, 0.55f, 1f),
                $"world {p.WorldMs:F1} ms   draw {p.RenderMs:F1} ms   map {p.MapMs:F1}   paths {p.PathsMs:F1}   hp {p.HpBarsMs:F1}");
            TextColoredUnformatted(new Vector4(0.85f, 0.85f, 0.85f, 1f),
                $"reads total {p.TotalReadsPerSec / 1000f:F1}k/s   main {p.MainReadsPerSec / 1000f:F1}k/s   world {p.WorldReadsPerSec / 1000f:F1}k/s   {p.TotalMibPerSec:F2} MiB/s");
            TextColoredUnformatted(new Vector4(0.85f, 0.85f, 0.85f, 1f),
                $"ent {p.EntityCount}   hp {p.HpBarCount}   paths {p.SelectedPathCount}   metrics opt-in");
            if (ctx.AtlasOpen)
                TextColoredUnformatted(new Vector4(0.85f, 0.85f, 0.85f, 1f), $"atlas draw {p.AtlasMs:F1} ms");
            if (!string.IsNullOrEmpty(ctx.MapDiag))
                TextColoredUnformatted(new Vector4(0.75f, 0.85f, 1f, 1f), ctx.MapDiag);
            if (!string.IsNullOrEmpty(ctx.PathDiagNote))
                TextColoredUnformatted(new Vector4(1f, 0.82f, 0.55f, 1f), ctx.PathDiagNote);
        }
    }

    private static string FormatGpuPercent(float pct)
        => pct >= 0 ? $"{pct:F0}%" : "n/a";

    private static void TextColoredUnformatted(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    // ── Settings panel ──

    private void DrawSettingsPanel(RenderContext? ctx)
    {
        if (!_settingsOpen) return;

        float wW = ctx?.WindowWidth ?? _width;
        float wH = ctx?.WindowHeight ?? _height;
        var settingsW = Math.Clamp(wW - 60f, 760f, 1040f);
        var settingsH = Math.Clamp(wH - 80f, 580f, 760f);

        ImGui.SetNextWindowSizeConstraints(new System.Numerics.Vector2(settingsW, settingsH), new System.Numerics.Vector2(float.MaxValue, float.MaxValue));
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(settingsW, settingsH), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(
            (wW - settingsW) * 0.5f,
            (wH - settingsH) * 0.5f), ImGuiCond.FirstUseEver);

        const ImGuiWindowFlags sflags =
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.MenuBar;

        if (!ImGui.Begin("Settings", ref _settingsOpen, sflags)) { ImGui.End(); return; }

        RadarSettings s;
        lock (_settingsLock) s = _settings;

        if (ImGui.BeginMenuBar())
        {
            ImGui.TextDisabled($"v{UpdateChecker.Current}");
            ImGui.SameLine();
            ImGui.TextDisabled($"  {HotkeyCodes.DisplayName(s.ToggleSettingsHotkey)} — toggle");
            ImGui.SameLine();
            ImGui.TextDisabled($"  localhost:{s.ApiPort}");
            ImGui.EndMenuBar();
        }

        if (ctx is not null)
            DrawSettingsHud(ctx);

        ImGui.Spacing();

        _settingsAutoSave.SeedIfNeeded(JsonSerializer.Serialize(s));

        MaybeReapplyOverlayFont(s);

        if (string.IsNullOrEmpty(_activeSettingsTab))
            _activeSettingsTab = string.IsNullOrWhiteSpace(s.LastSettingsTab) ? "Radar" : s.LastSettingsTab;

        // Accordion sections start collapsed on each open; keep the last sidebar tab.
        if (!_settingsPanelWasOpen)
            ImGuiTheme.CollapseSectionsOnNextDraw = true;

        DrawSettingsNavigation(s, ctx);

        PollHotkeyCapture(s);

        MarkSettingsDirty(s);
        FlushSettingsAutoSave();

        ImGui.End();
    }

    private void DrawSettingsNavigation(RadarSettings s, RenderContext? ctx)
    {
        const float railWidth = 184f;
        ImGui.BeginChild(
            "SettingsNavigationRail",
            new NumVec2(railWidth, 0f),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.None);

        DrawSettingsNavGroup("INTERFACE STYLE");
        var modern = string.Equals(s.InterfaceStyle, "Modern", StringComparison.OrdinalIgnoreCase);
        if (ImGui.RadioButton("Modern", modern) && !modern)
        {
            s.InterfaceStyle = "Modern";
            SaveSettings();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Old", !modern) && modern)
        {
            s.InterfaceStyle = "Old";
            SaveSettings();
            _settingsOpen = false;
            if (_openExternalSettings is { } openExternalSettings)
                _enqueue(openExternalSettings);
        }
        ImGui.Spacing();

        DrawSettingsNavGroup("OVERLAY");
        DrawSettingsNavItem("Radar");
        DrawSettingsNavItem("Performance");
        DrawSettingsNavItem("HP Bars");
        DrawSettingsNavItem("Flask");

        DrawSettingsNavGroup("PLUGINS");
        DrawSettingsNavItem("Campaign Helper");
        DrawSettingsNavItem("Stash Value");
        DrawSettingsNavItem("Stash Utility");
        DrawSettingsNavItem("Crafting Assistant");
        DrawSettingsNavItem("Pickup Helper");
        DrawSettingsNavItem("Loot Tracker");
        DrawSettingsNavItem("Amanamu Alert");
        DrawSettingsNavItem("Ritual");
        DrawSettingsNavItem("Runecraft");
        DrawSettingsNavItem("Sekhema");

        DrawSettingsNavGroup("WORLD");
        DrawSettingsNavItem("Atlas");

        DrawSettingsNavGroup("SYSTEM");
        DrawSettingsNavItem("Hotkeys");

        ImGui.EndChild();
        ImGui.SameLine(0f, 10f);

        ImGui.BeginChild(
            "SettingsPage",
            NumVec2.Zero,
            ImGuiChildFlags.None,
            ImGuiWindowFlags.AlwaysVerticalScrollbar);

        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(ImGuiTheme.Accent));
        ImGui.TextUnformatted(_activeSettingsTab);
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        switch (_activeSettingsTab)
        {
            case "Performance": DrawPerformanceTab(s); break;
            case "HP Bars": DrawHpBarsTab(s); break;
            case "Flask": DrawFlaskTab(s); break;
            case "Campaign Helper": DrawCampaignSettingsTab(s, ctx); break;
            case "Stash Value": DrawStashValueTab(s, ctx); break;
            case "Stash Utility": DrawStashUtilityTab(s, ctx); break;
            case "Crafting Assistant": DrawWaystoneAlchemyTab(s, ctx); break;
            case "Pickup Helper": DrawPickupHelperTab(s, ctx); break;
            case "Loot Tracker": DrawLootTrackerTab(s, ctx); break;
            case "Amanamu Alert": DrawAmanamuTab(s, ctx); break;
            case "Ritual": DrawRitualTab(s, ctx); break;
            case "Runecraft": DrawRunecraftTab(s, ctx); break;
            case "Sekhema": DrawSekhemaTab(s, ctx); break;
            case "Atlas": DrawAtlasTab(s); break;
            case "Hotkeys": DrawHotkeysTab(s); break;
            default: DrawRadarTab(s, ctx); break;
        }

        ImGuiTheme.CollapseSectionsOnNextDraw = false;
        ImGui.EndChild();
    }

    private void DrawCampaignSettingsTab(RadarSettings settings, RenderContext? ctx)
    {
        var campaign = settings.Campaign;
        ImGui.TextWrapped(
            "A read-only full-clear guide with per-character progress. Automatic checks are limited "
            + "to stable area entry, an observed boss death, or an observed object state change.");
        ImGui.Spacing();

        var enabled = campaign.Enabled;
        if (ImGui.Checkbox("Enable Campaign Helper", ref enabled)) campaign.Enabled = enabled;
        var automatic = campaign.AutoActivate;
        if (ImGui.Checkbox("Automatically show in campaign areas", ref automatic))
            campaign.AutoActivate = automatic;
        var route = campaign.AutoRoute;
        if (ImGui.Checkbox("Route to exact curated target", ref route)) campaign.AutoRoute = route;
        var autoCheck = campaign.SafeAutoCheck;
        if (ImGui.Checkbox("Safe automatic checkmarks", ref autoCheck)) campaign.SafeAutoCheck = autoCheck;
        var guideMode = (int)campaign.GuideMode;
        ImGui.SetNextItemWidth(UiW());
        if (ImGui.Combo("Objectives", ref guideMode, "Required only\0Full clear\0"))
            campaign.GuideMode = (CampaignGuideMode)Math.Clamp(guideMode, 0, 1);
        var showCompleted = campaign.ShowCompletedObjectives;
        if (ImGui.Checkbox("Show completed objectives in widget", ref showCompleted))
            campaign.ShowCompletedObjectives = showCompleted;

        ImGui.SeparatorText("Widget");
        var scale = campaign.WidgetScale;
        ImGui.SetNextItemWidth(UiW());
        if (ImGui.SliderFloat("Scale", ref scale, 0.75f, 1.50f, "%.2f"))
            campaign.WidgetScale = Math.Clamp(scale, 0.75f, 1.50f);
        var opacity = campaign.WidgetOpacity;
        ImGui.SetNextItemWidth(UiW());
        if (ImGui.SliderFloat("Opacity", ref opacity, 0.35f, 1.00f, "%.2f"))
            campaign.WidgetOpacity = Math.Clamp(opacity, 0.35f, 1f);
        var diagnostics = campaign.ShowDiagnosticTargetStatus;
        if (ImGui.Checkbox("Show diagnostic target status", ref diagnostics))
            campaign.ShowDiagnosticTargetStatus = diagnostics;
        var collapsed = campaign.WidgetCollapsed;
        if (ImGui.Checkbox("Start compact widget collapsed", ref collapsed))
            campaign.WidgetCollapsed = collapsed;
        if (ImGui.Button("Open full campaign guide"))
            _campaignGuideOpen = true;
        ImGui.SameLine();
        if (ImGui.Button("Show compact widget"))
            _enqueue(() => _setCampaignDismissed(false));
        if (ImGui.Button("Reset widget position"))
        {
            campaign.WidgetX = -1f;
            campaign.WidgetY = -1f;
            _campaignPositionInitialized = false;
        }

        ImGui.SeparatorText("Current character");
        if (ctx?.Campaign is { Available: true } view)
        {
            ImGui.TextDisabled(
                view.Current is null
                    ? "Campaign complete"
                    : $"{view.ChapterLabel} · {view.Current.AreaName} · {view.ChapterCompleted}/{view.ChapterTotal}");
            if (view.Current is not null)
            {
                ImGui.TextWrapped(view.Current.Text);
                ImGui.TextDisabled($"Target: {view.TargetStatus}");
                if (view.Target.Diagnostic.Length > 0)
                    ImGui.TextDisabled(view.Target.Diagnostic);
            }
        }
        else
        {
            ImGui.TextDisabled("Character identity is available after entering the game.");
        }

        if (ImGui.Button("Check current objective"))
            _enqueue(_completeCampaignObjective);
        ImGui.SameLine();
        if (ImGui.Button("Back / uncheck"))
            _enqueue(_backCampaignObjective);
        if (ImGui.Button("Reset current character progress"))
            _enqueue(_resetCampaignCharacter);

        ImGui.Spacing();
        ImGui.TextDisabled("Guide source: domistae/poe2-leveling @ 90739c2 · MIT");
    }

    private static void DrawSettingsNavGroup(string label)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
    }

    private void DrawSettingsNavItem(string page)
    {
        var selected = string.Equals(_activeSettingsTab, page, StringComparison.Ordinal);
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(
                ImGuiTheme.Accent.X, ImGuiTheme.Accent.Y, ImGuiTheme.Accent.Z, 0.28f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(
                ImGuiTheme.Accent.X, ImGuiTheme.Accent.Y, ImGuiTheme.Accent.Z, 0.38f));
        }
        if (ImGui.Selectable(page, selected, ImGuiSelectableFlags.None, new NumVec2(0f, 30f))
            && !selected)
        {
            _activeSettingsTab = page;
            lock (_settingsLock) _settings.LastSettingsTab = page;
            ImGuiTheme.CollapseSectionsOnNextDraw = true;
        }
        if (selected) ImGui.PopStyleColor(2);
    }

    private void EnsureRulesUiCache()
    {
        if (_displayRules is null) return;
        var gen = _displayRules.Generation;
        if (gen == _rulesUiGeneration) return;
        _rulesUiGeneration = gen;
        _rulesUiCache = _displayRules.All.ToList();
        if (_selectedRuleIndex >= _rulesUiCache.Count)
            _selectedRuleIndex = -1;
    }

    private void InvalidateRulesUiCache()
    {
        _rulesUiGeneration = -1;
        EnsureRulesUiCache();
    }

    private void DrawDisplayRulesEditor(RadarSettings s)
    {
        if (_displayRules is null)
        {
            ImGui.TextDisabled("Display rules not wired yet.");
            return;
        }

        EnsureRulesUiCache();

        ImGui.TextWrapped("First active matching rule wins — reorder to change priority. Paused rules are skipped.");

        float iconScale = s.GlobalIconScale;
        ImGui.SetNextItemWidth(UiW());
        if (ImGui.SliderFloat("Global icon scale", ref iconScale, 0.5f, 3f, "%.2f"))
            s.GlobalIconScale = Math.Clamp(iconScale, 0.25f, 4f);
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.GlobalIconScale);

        if (ImGui.Button("Add rule"))
        {
            _displayRules.Add(new DisplayRule
            {
                Name = "New rule",
                Shape = "Circle",
                Color = "#ffd926",
                Opacity = 1f,
                Size = 4f,
            });
            InvalidateRulesUiCache();
            _selectedRuleIndex = _rulesUiCache.Count - 1;
        }
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.AddRule);

        ImGui.SameLine();
        if (ImGui.Button("Duplicate") && _selectedRuleIndex >= 0 && _selectedRuleIndex < _rulesUiCache.Count)
        {
            var copy = CloneDisplayRule(_rulesUiCache[_selectedRuleIndex]);
            copy.Name = string.IsNullOrWhiteSpace(copy.Name) ? "Rule copy" : copy.Name + " copy";
            _displayRules.Add(copy);
            InvalidateRulesUiCache();
            var newIdx = _rulesUiCache.Count - 1;
            var insertAt = Math.Min(_selectedRuleIndex + 1, _rulesUiCache.Count - 1);
            if (newIdx != insertAt)
                _displayRules.Move(newIdx, insertAt);
            InvalidateRulesUiCache();
            _selectedRuleIndex = insertAt;
        }
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.DuplicateRule);

        ImGui.SameLine();
        if (ImGui.Button("Delete") && _selectedRuleIndex >= 0 && _selectedRuleIndex < _rulesUiCache.Count)
        {
            _displayRules.RemoveAt(_selectedRuleIndex);
            _selectedRuleIndex = -1;
            InvalidateRulesUiCache();
        }
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.DeleteRule);

        ImGui.SameLine();
        if (ImGui.Button("Move up") && _selectedRuleIndex > 0)
        {
            _displayRules.Move(_selectedRuleIndex, _selectedRuleIndex - 1);
            _selectedRuleIndex--;
            InvalidateRulesUiCache();
        }
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.MoveUp);

        ImGui.SameLine();
        if (ImGui.Button("Move down") && _selectedRuleIndex >= 0 && _selectedRuleIndex < _rulesUiCache.Count - 1)
        {
            _displayRules.Move(_selectedRuleIndex, _selectedRuleIndex + 1);
            _selectedRuleIndex++;
            InvalidateRulesUiCache();
        }
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.MoveDown);

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##rulesearch", "Search rules by name…", ref _ruleSearch, 128);
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.SearchRules);

        ImGui.BeginChild("DisplayRulesTable", new NumVec2(0, 320));
        DrawDisplayRulesTable();
        ImGui.EndChild();

        DrawSpritePickerWindow();
    }

    private void DrawDisplayRulesTable()
    {
        if (_displayRules is null) return;

        var filter = _ruleSearch.Trim();
        var flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                    | ImGuiTableFlags.ScrollX | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("EntityRules", 15, flags)) return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2f);
        ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2.2f);
        ImGui.TableSetupColumn("Hide", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2.4f);
        ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2.6f);
        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3.2f);
        ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3.5f);
        ImGui.TableSetupColumn("Alpha", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 4f);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 4f);
        ImGui.TableSetupColumn("Spr", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3.5f);
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 7f);
        ImGui.TableSetupColumn("No lbl", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3.6f);
        ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 5.5f);
        ImGui.TableSetupColumn("Match", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 8f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 8f);
        ImGui.TableSetupColumn("Adv", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2.8f);
        ImGuiTheme.TableHeadersWithTooltips([
            ("#", SettingHints.DisplayRules.ColumnOrder),
            ("On", SettingHints.DisplayRules.ColumnOn),
            ("Hide", SettingHints.DisplayRules.ColumnHide),
            ("Path", SettingHints.DisplayRules.ColumnPath),
            ("Icon", SettingHints.DisplayRules.ColumnIcon),
            ("Color", SettingHints.DisplayRules.ColumnColor),
            ("Alpha", SettingHints.DisplayRules.ColumnAlpha),
            ("Size", SettingHints.DisplayRules.ColumnSize),
            ("Spr", SettingHints.DisplayRules.ColumnSprite),
            ("Label", SettingHints.DisplayRules.ColumnLabel),
            ("No lbl", SettingHints.DisplayRules.ColumnHideLabel),
            ("Group", SettingHints.DisplayRules.ColumnGroup),
            ("Match", SettingHints.DisplayRules.ColumnMatch),
            ("Name", SettingHints.DisplayRules.ColumnName),
            ("Adv", SettingHints.DisplayRules.ColumnAdvanced),
        ]);

        var highlightActive = _highlightRuleIndex >= 0 && Stopwatch.GetTimestamp() < _highlightRuleUntil;
        if (!highlightActive) _highlightRuleIndex = -1;

        for (var i = 0; i < _rulesUiCache.Count; i++)
        {
            var rule = _rulesUiCache[i];
            if (!TypeDisplayRulePromoter.RuleMatchesSearch(rule, filter))
                continue;

            ImGui.TableNextRow();
            if (i == _scrollToRuleIndex)
            {
                ImGui.SetScrollHereY(0.35f);
                _scrollToRuleIndex = -1;
            }

            if (highlightActive && i == _highlightRuleIndex)
            {
                var hl = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.55f, 0.35f, 0.45f));
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, hl);
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, hl);
            }

            ImGui.PushID(i);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted((i + 1).ToString());

            ImGui.TableNextColumn();
            bool en = rule.Enabled;
            if (ImGui.Checkbox("##en", ref en) && en != rule.Enabled)
                UpdateRuleAt(i, c => c.Enabled = en);

            ImGui.TableNextColumn();
            bool hide = rule.Hide;
            if (ImGui.Checkbox("##hide", ref hide) && hide != rule.Hide)
                UpdateRuleAt(i, c => c.Hide = hide);

            ImGui.TableNextColumn();
            bool nav = rule.Navigable;
            if (ImGui.Checkbox("##nav", ref nav) && nav != rule.Navigable)
                UpdateRuleAt(i, c => c.Navigable = nav);

            ImGui.TableNextColumn();
            DrawRuleSpriteButton(i, rule);

            ImGui.TableNextColumn();
            var col = ParseHexColor(rule.Color);
            if (ImGui.ColorEdit3("##col", ref col, ImGuiColorEditFlags.NoInputs))
                UpdateRuleAt(i, c => c.Color = FormatHexColor3(col));

            ImGui.TableNextColumn();
            float op = rule.Opacity;
            if (ImGui.DragFloat("##op", ref op, 0.01f, 0f, 1f, "a%.2f"))
            {
                var clamped = Math.Clamp(op, 0f, 1f);
                if (Math.Abs(clamped - rule.Opacity) > 0.0001f)
                    UpdateRuleAt(i, c => c.Opacity = clamped);
            }

            ImGui.TableNextColumn();
            float sz = rule.Size;
            if (ImGui.DragFloat("##sz", ref sz, 0.1f, 1f, 24f, "sz%.1f"))
            {
                var clamped = Math.Clamp(sz, 1f, 24f);
                if (Math.Abs(clamped - rule.Size) > 0.0001f)
                    UpdateRuleAt(i, c => c.Size = clamped);
            }

            ImGui.TableNextColumn();
            float sprScale = rule.Sprite?.Scale ?? 1.25f;
            if (ImGui.DragFloat("##sprsc", ref sprScale, 0.02f, 0.2f, 4f, "s%.2f"))
            {
                var clamped = Math.Clamp(sprScale, 0.2f, 4f);
                if (rule.Sprite is null || Math.Abs(clamped - rule.Sprite.Scale) > 0.0001f)
                {
                    UpdateRuleAt(i, c =>
                    {
                        c.Sprite ??= SpriteIconRef.Cell(0, 0, clamped);
                        c.Sprite.Scale = clamped;
                    });
                }
            }

            ImGui.TableNextColumn();
            var mapLabel = rule.Label ?? "";
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##maplbl", ref mapLabel, 64)) { }
            if (ImGui.IsItemDeactivatedAfterEdit())
                UpdateRuleAt(i, c => c.Label = string.IsNullOrWhiteSpace(mapLabel) ? null : mapLabel);

            ImGui.TableNextColumn();
            bool hideLbl = rule.HideLabel;
            if (ImGui.Checkbox("##hidelbl", ref hideLbl) && hideLbl != rule.HideLabel)
                UpdateRuleAt(i, c => c.HideLabel = hideLbl);

            ImGui.TableNextColumn();
            DrawRuleGroupCombo(i, rule);

            ImGui.TableNextColumn();
            var matchText = string.Join(", ", rule.Match);
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##match", ref matchText, 256)) { }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                var terms = matchText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                UpdateRuleAt(i, c => c.Match = terms);
            }

            ImGui.TableNextColumn();
            var ruleName = rule.Name;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputText("##rname", ref ruleName, 128)) { }
            if (ImGui.IsItemDeactivatedAfterEdit())
                UpdateRuleAt(i, c => c.Name = ruleName);

            ImGui.TableNextColumn();
            if (ImGui.SmallButton("…"))
                ImGui.OpenPopup($"ruleAdv{i}");
            DrawRuleAdvancedPopup(i, rule);

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawRuleGroupCombo(int index, DisplayRule rule)
    {
        var current = rule.Categories is { Count: 1 } ? rule.Categories[0] : "Any";
        var idx = Array.FindIndex(RuleGroupOptions, g => string.Equals(g, current, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) idx = 0;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Combo("##grp", ref idx, string.Join('\0', RuleGroupOptions) + "\0"))
        {
            var picked = RuleGroupOptions[idx];
            UpdateRuleAt(index, c =>
            {
                c.Categories = picked == "Any"
                    ? new List<string>()
                    : new List<string> { picked };
            });
        }
    }

    private void DrawRuleAdvancedPopup(int index, DisplayRule rule)
    {
        if (!ImGui.BeginPopup($"ruleAdv{index}"))
            return;

        ImGui.PushID($"adv{index}");
        foreach (var (field, label, options) in RuleConditionFields)
        {
            var current = field switch
            {
                "Rarity" => rule.Rarity,
                "Reaction" => rule.Reaction,
                "Life" => rule.Life,
                "Chest" => rule.Chest,
                "Poi" => rule.Poi,
                _ => null,
            };
            var preview = string.IsNullOrEmpty(current) ? "(any)" : current!;
            ImGui.SetNextItemWidth(UiW(10f));
            if (ImGui.BeginCombo($"##{field}", preview))
            {
                if (ImGui.Selectable("(any)", string.IsNullOrEmpty(current)))
                    ApplyRuleCondition(index, field, null);
                foreach (var opt in options)
                {
                    if (ImGui.Selectable(opt, string.Equals(current, opt, StringComparison.OrdinalIgnoreCase)))
                        ApplyRuleCondition(index, field, opt);
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(label);
            ImGuiTheme.Tooltip(field switch
            {
                "Rarity" => SettingHints.DisplayRules.Rarity,
                "Reaction" => SettingHints.DisplayRules.Reaction,
                "Life" => SettingHints.DisplayRules.Life,
                "Chest" => SettingHints.DisplayRules.MatcherOpened,
                "Poi" => SettingHints.DisplayRules.Poi,
                _ => "",
            });
        }

        var shapeIdx = Array.FindIndex(ShapeNames, sh => string.Equals(sh, rule.Shape, StringComparison.OrdinalIgnoreCase));
        if (shapeIdx < 0) shapeIdx = 0;
        ImGui.SetNextItemWidth(UiW(10f));
        if (ImGui.Combo("##shape", ref shapeIdx, string.Join('\0', ShapeNames) + "\0"))
            UpdateRuleAt(index, c => c.Shape = ShapeNames[shapeIdx]);
        ImGui.SameLine();
        ImGui.TextUnformatted("Shape");
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.Shape);

        ImGui.PopID();
        ImGui.EndPopup();
    }

    private void UpdateRuleAt(int index, Action<DisplayRule> mutate)
    {
        if (_displayRules is null || index < 0 || index >= _rulesUiCache.Count) return;
        var c = CloneDisplayRule(_rulesUiCache[index]);
        mutate(c);
        _displayRules.Update(index, c);
        InvalidateRulesUiCache();
    }

    private void ApplyRuleCondition(int index, string field, string? value)
    {
        UpdateRuleAt(index, c =>
        {
            switch (field)
            {
                case "Rarity": c.Rarity = value; break;
                case "Reaction": c.Reaction = value; break;
                case "Life": c.Life = value; break;
                case "Chest": c.Chest = value; break;
                case "Poi": c.Poi = value; break;
            }
        });
    }

    private void DrawRadarEntitySections(RadarSettings s, RenderContext? ctx)
    {
        bool detectOpen = ImGuiTheme.BeginAccordionSection("DetectAutoPath", "Detection & Auto-path",
            "Entity range, auto-path, and clutter visibility.");
        if (detectOpen)
        {
            int radius = s.EntityDrawRadiusGrid;
            ImGui.SliderInt("Detection radius (grid)", ref radius, 0, 500, radius == 0 ? "Unlimited" : "%d");
            s.EntityDrawRadiusGrid = radius;
            ImGuiTheme.Tooltip(SettingHints.Entities.DetectionRadius);

            bool ap = s.AutoPathNavigable;
            if (ImGui.Checkbox("Auto-path to nearest targets", ref ap))
                s.AutoPathNavigable = ap;
            ImGuiTheme.Tooltip(SettingHints.Entities.AutoPathNearest);

            bool showAll = !s.ImportantOnly;
            if (ImGui.Checkbox("Show all monsters (including clutter)", ref showAll))
                s.ImportantOnly = !showAll;
            ImGuiTheme.Tooltip(SettingHints.Entities.ShowAllMonsters);
        }
        ImGuiTheme.EndAccordionSection(detectOpen);

        DrawTypesInZone(ctx, s);

        if (_forceOpenDisplayRules)
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        bool rulesOpen = ImGuiTheme.BeginAccordionSection("DisplayRules", "Display rules",
            "What entities show on the map — first active match wins.");
        if (rulesOpen)
            DrawDisplayRulesEditor(s);
        if (_forceOpenDisplayRules)
            _forceOpenDisplayRules = false;
        ImGuiTheme.EndAccordionSection(rulesOpen);

        if (_hidden is null) return;

        bool hideOpen = ImGuiTheme.BeginAccordionSection("NeverShow", "Never show (patterns)",
            "Always hidden — checked before radar rules (map, lists, paths).");
        if (hideOpen)
        {
            ImGui.SetNextItemWidth(-80f);
            ImGui.InputTextWithHint("##hidepat", "e.g. AbyssCrack, *Daemon*", ref _hidePatternInput, 256);
            ImGuiTheme.Tooltip(SettingHints.Entities.NeverShowPattern);
            ImGui.SameLine();
            if (ImGui.Button("Add") && _hidePatternInput.Trim().Length > 0)
            {
                _hidden.Add(_hidePatternInput.Trim());
                _hidePatternInput = "";
            }
            ImGuiTheme.Tooltip(SettingHints.Entities.NeverShowAdd);

            foreach (var p in _hidden.All)
            {
                ImGui.TextUnformatted(p);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##{p}"))
                    _hidden.Remove(p);
            }
        }
        ImGuiTheme.EndAccordionSection(hideOpen);
    }

    /// <summary>Live inventory for the current zone type. Show/Nav toggles write per-area-code zone
    /// overrides — global semantic rules stay unchanged.</summary>
    private void DrawTypesInZone(RenderContext? ctx, RadarSettings s)
    {
        bool typesOpen = ImGuiTheme.BeginAccordionSection("TypesInZone", "Types in this zone",
            "Show on map · Path. Overrides apply to this zone type only.");
        if (typesOpen)
        {
            if (ctx is not { Entities.Length: > 0 })
            {
                ImGui.TextDisabled("No entities in range (enter a zone / move closer).");
            }
            else
            {
                var entities = ctx.Entities;
                var areaCode = ctx.AreaCode;

                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("##typesearch", "search types…", ref _typeSearch, 128);
                ImGuiTheme.Tooltip(SettingHints.Entities.TypeSearch);

                // tier → token → (label, count, sample)
                var byTier = new Dictionary<EntityImportance, Dictionary<string, (string label, int count, Poe2Live.EntityDot sample)>>();
                foreach (var e in entities)
                {
                    var rule = _ruleEngine?.Resolve(e, areaCode, s.ImportantOnly, entities);
                    var tier = EntityImportanceHelper.Classify(e, ctx.Styles, rule);
                    if (s.ImportantOnly && EntityImportanceHelper.IsTrash(tier)) continue;

                    var token = TypeToken(e.Metadata);
                    if (token.Length == 0) continue;

                    if (!byTier.TryGetValue(tier, out var bucket))
                        byTier[tier] = bucket = new Dictionary<string, (string, int, Poe2Live.EntityDot)>(StringComparer.Ordinal);

                    var label = EntityDisplayHelper.FormatEntityLabel(e, rule, entities, areaCode);
                    if (label.Length == 0) label = token;

                    if (bucket.TryGetValue(token, out var g))
                        bucket[token] = (g.label, g.count + 1, g.sample);
                    else
                        bucket[token] = (label, 1, e);
                }

                var filter = _typeSearch.Trim();
                ImGui.BeginChild("TypesInZone", new NumVec2(0, 380));

                foreach (var tier in EntityImportanceHelper.DisplayOrder)
                {
                    if (s.ImportantOnly && EntityImportanceHelper.IsTrash(tier)) continue;
                    if (!byTier.TryGetValue(tier, out var bucket) || bucket.Count == 0) continue;

                    var rows = bucket
                        .Where(kv => filter.Length == 0
                                     || kv.Value.label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                                     || kv.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderByDescending(kv => kv.Value.count)
                        .ThenBy(kv => kv.Value.label, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (rows.Count == 0) continue;

                    var tierCount = bucket.Values.Sum(v => v.count);
                    ImGui.PushID((int)tier);

                    if (ImGui.CollapsingHeader($"{EntityImportanceHelper.TierLabel(tier)} ({tierCount})"))
                    {
                        DrawTierGroupToggles(tier);

                        ImGui.TextDisabled("Show  Nav   Count  Name          Rule");
                        foreach (var (token, info) in rows)
                            DrawTypeRow(ctx, token, info.label, info.count, info.sample);

                        ImGui.Spacing();
                    }

                    ImGui.PopID();
                }

                ImGui.EndChild();
            }
        }
        ImGuiTheme.EndAccordionSection(typesOpen);
    }

    private void DrawTierGroupToggles(EntityImportance tier)
    {
        var names = EntityImportanceHelper.RuleNamesForTier(tier);
        if (names.Length == 0) return;

        var (shown, nav) = GetGroupRuleState(names);
        bool show = shown;
        if (ImGui.Checkbox("Show##grp", ref show) && show != shown)
            ApplyToRulesByNames(names, hide: !show, navigable: nav);
        ImGuiTheme.Tooltip(SettingHints.Entities.TierShow);

        ImGui.SameLine();
        bool navOn = nav;
        if (ImGui.Checkbox("Nav##grp", ref navOn) && navOn != nav)
            ApplyToRulesByNames(names, hide: !shown, navigable: navOn);
        ImGuiTheme.Tooltip(SettingHints.Entities.TierNav);

        ImGui.SameLine();
        ImGui.TextDisabled("tier defaults");
    }

    public void FocusDisplayRule(int index)
    {
        if (index < 0) return;
        _settingsOpen = true;
        InvalidateRulesUiCache();
        ScrollToDisplayRule(index);
    }

    private void ScrollToDisplayRule(int index)
    {
        _selectedRuleIndex = index;
        _scrollToRuleIndex = index;
        _highlightRuleIndex = index;
        _highlightRuleUntil = Stopwatch.GetTimestamp() + (long)(1.5 * Stopwatch.Frequency);
        _ruleSearch = "";
        _forceOpenDisplayRules = true;
    }

    private void DrawTypeRow(RenderContext ctx, string token, string label, int count, Poe2Live.EntityDot sample)
    {
        ImGui.PushID(token);
        var areaCode = ctx.AreaCode;
        var globalRule = _ruleEngine?.ResolveGlobal(sample);
        var mergedRule = _ruleEngine?.Resolve(sample, areaCode, _settings.ImportantOnly, ctx.Entities);
        var hasZoneOverride = _zoneOverrides?.HasOverride(areaCode, token) ?? false;
        var hasRule = _displayRules is not null
                      && TypeDisplayRulePromoter.FindRuleIndex(_displayRules.All, token) >= 0;
        var rawHide = mergedRule is { Hide: true };
        var rawNav = mergedRule?.Navigable ?? false;
        var shownNow = !rawHide;
        var navNow = !rawHide && rawNav;

        if (hasRule)
            ImGui.BeginDisabled();

        bool show = shownNow;
        if (ImGui.Checkbox("##show", ref show) && !hasRule && show != shownNow)
            ApplyZoneOverride(areaCode, token, hide: !show, navigable: rawNav, globalRule);
        ImGuiTheme.Tooltip(hasRule ? SettingHints.Entities.TypeShowGlobal : SettingHints.Entities.TypeShow);

        ImGui.SameLine();
        bool nav = navNow;
        if (ImGui.Checkbox("##nav", ref nav) && !hasRule && nav != navNow)
            ApplyZoneOverride(areaCode, token, hide: rawHide, navigable: nav, globalRule);
        ImGuiTheme.Tooltip(hasRule ? SettingHints.Entities.TypeNavGlobal : SettingHints.Entities.TypeNav);

        if (hasRule)
            ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextDisabled($"x{count}");
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        if (hasZoneOverride && !hasRule)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("· zone");
        }

        ImGui.SameLine();
        if (hasRule)
        {
            if (ImGui.SmallButton("In rules##rule"))
            {
                var idx = TypeDisplayRulePromoter.FindRuleIndex(_displayRules!.All, token);
                if (idx >= 0) ScrollToDisplayRule(idx);
            }
            ImGuiTheme.Tooltip(SettingHints.Entities.InDisplayRules);
        }
        else if (ImGui.SmallButton("Add##rule"))
        {
            PromoteTypeToDisplayRule(ctx, token, label, sample);
            ImGuiTheme.Tooltip(SettingHints.Entities.PromoteTypeToRule);
        }

        ImGui.PopID();
    }

    private void PromoteTypeToDisplayRule(RenderContext ctx, string token, string displayLabel, Poe2Live.EntityDot sample)
    {
        if (_displayRules is null || _settings is null) return;

        var merged = _ruleEngine?.Resolve(sample, ctx.AreaCode, _settings.ImportantOnly, ctx.Entities);
        var (index, _) = TypeDisplayRulePromoter.Promote(
            _displayRules,
            _zoneOverrides,
            ctx.AreaCode,
            token,
            sample,
            merged,
            _settings.Styles,
            displayLabel);

        InvalidateRulesUiCache();
        ScrollToDisplayRule(index);
    }

    private (bool shown, bool nav) GetGroupRuleState(IReadOnlyList<string> names)
    {
        if (_displayRules is null || names.Count == 0) return (true, false);

        var matched = _displayRules.All.Where(r => names.Contains(r.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (matched.Count == 0) return (true, EntityImportanceHelper.IsNavDefault(EntityImportance.Mechanic));

        return (matched.All(r => !r.Hide), matched.All(r => r.Navigable));
    }

    private void ApplyToRulesByNames(IReadOnlyList<string> names, bool hide, bool navigable)
    {
        if (_displayRules is null || names.Count == 0) return;

        var all = _displayRules.All.ToList();
        var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        for (var i = 0; i < all.Count; i++)
        {
            if (!nameSet.Contains(all[i].Name)) continue;
            all[i].Hide = hide;
            all[i].Navigable = navigable;
            changed = true;
        }
        if (changed) _displayRules.Replace(all);
    }

    private void ApplyZoneOverride(string areaCode, string token, bool hide, bool navigable, DisplayRule? globalRule)
    {
        if (_zoneOverrides is null || string.IsNullOrEmpty(areaCode) || string.IsNullOrEmpty(token)) return;

        var globalHide = globalRule is { Hide: true };
        var globalNav = globalRule?.Navigable ?? false;
        if (hide == globalHide && navigable == globalNav)
            _zoneOverrides.ClearOverride(areaCode, token);
        else
            _zoneOverrides.SetOverride(areaCode, token, hide, navigable);
    }

    private static string TypeToken(string metadata) => EntityDisplayHelper.TypeToken(metadata);

    private void DrawRadarTab(RadarSettings s, RenderContext? ctx)
    {
        bool mapOpen = ImGuiTheme.BeginAccordionSection("MapOverlay", "Map overlay",
            "What appears on the in-game minimap and Tab map.");
        if (mapOpen)
        {
            bool sm = s.ShowMonsters; ImGui.Checkbox("Show Monsters", ref sm); s.ShowMonsters = sm;
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowMonsters);

            bool st = s.ShowTerrain; ImGui.Checkbox("Show Terrain", ref st); s.ShowTerrain = st;
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowTerrain);

            bool sb = s.ShowPlayerBlip; ImGui.Checkbox("Player Blip", ref sb); s.ShowPlayerBlip = sb;
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowPlayerBlip);

            bool hj = s.HideJunk; ImGui.Checkbox("Hide map clutter", ref hj); s.HideJunk = hj;
            ImGuiTheme.Tooltip(SettingHints.Radar.HideJunk);

            bool cl = s.UseCuratedLandmarks; ImGui.Checkbox("Curated Landmarks", ref cl); s.UseCuratedLandmarks = cl;
            ImGuiTheme.Tooltip(SettingHints.Radar.CuratedLandmarks);

            int lcg = s.LandmarkClusterGap;
            ImGui.SetNextItemWidth(UiW());
            if (ImGui.SliderInt("Cluster Gap", ref lcg, 0, 64)) s.LandmarkClusterGap = lcg;
            ImGuiTheme.Tooltip(SettingHints.Radar.LandmarkClusterGap);

            DrawNavMenuCornerSetting(s);
        }
        ImGuiTheme.EndAccordionSection(mapOpen);

        bool pathsOpen = ImGuiTheme.BeginAccordionSection("NavigationPaths", "Navigation paths",
            "Walking routes to selected nav targets (F6 / nav menu).");
        if (pathsOpen)
        {
            bool pathGround = s.ShowPathWorld && s.ShowGroundWaypoints;
            if (ImGui.Checkbox("Path on ground", ref pathGround))
                s.SetPathGroundEnabled(pathGround);
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowPathWorld);

            bool spm = s.ShowPathMap; ImGui.Checkbox("Path on Tab map", ref spm); s.ShowPathMap = spm;
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowPathMap);
            bool spmi = s.ShowPathMinimap; ImGui.Checkbox("Path on minimap", ref spmi); s.ShowPathMinimap = spmi;
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowPathMinimap);

            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
            ImGui.TextWrapped("Auto-path only adds nearby targets. These three controls independently decide where selected routes are drawn.");
            ImGui.PopStyleColor();
        }
        ImGuiTheme.EndAccordionSection(pathsOpen);

        DrawRadarEntitySections(s, ctx);

        bool calOpen = ImGuiTheme.BeginAccordionSection("MapAlignment", "Map alignment",
            "Fine-tune overlay lock to the game minimap and Tab map.");
        if (calOpen)
        {
            ImGui.SetNextItemWidth(UiW());
            float lmap = s.LargeMapScaleMultiplier;
            if (ImGui.SliderFloat("Large map scale", ref lmap, 0.01f, 1f, "%.4f")) s.LargeMapScaleMultiplier = lmap;
            ImGuiTheme.Tooltip(SettingHints.Radar.LargeMapScale);

            ImGui.SetNextItemWidth(UiW());
            float smul = s.ScaleMul; if (ImGui.SliderFloat("Minimap scale", ref smul, 0.1f, 3f, "%.2f")) s.ScaleMul = smul;
            ImGuiTheme.Tooltip(SettingHints.Radar.MinimapScale);
            ImGui.SetNextItemWidth(UiW());
            float ox = s.OffX; if (ImGui.SliderFloat("Offset X", ref ox, -200f, 200f, "%.0f")) s.OffX = ox;
            ImGuiTheme.Tooltip(SettingHints.Radar.OffsetX);
            ImGui.SetNextItemWidth(UiW());
            float oy = s.OffY; if (ImGui.SliderFloat("Offset Y", ref oy, -200f, 200f, "%.0f")) s.OffY = oy;
            ImGuiTheme.Tooltip(SettingHints.Radar.OffsetY);

            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
            ImGui.TextWrapped("Map shift/zoom refresh at overlay FPS when the map is visible. Vitals/entity reads stay on Live refresh Hz → Performance tab.");
            ImGui.PopStyleColor();
        }
        ImGuiTheme.EndAccordionSection(calOpen);

        bool terrainOpen = ImGuiTheme.BeginAccordionSection("Terrain", "Terrain",
            "Walkable grid colors on the map.");
        if (terrainOpen)
        {
            var ti = s.Terrain.InteriorColor;
            var te = s.Terrain.EdgeColor;
            float tia = s.Terrain.InteriorOpacity;
            float tea = s.Terrain.EdgeOpacity;
            var iv = new Vector4(
                int.TryParse(ti.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var ir) ? ir / 255f : 1f,
                int.TryParse(ti.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var ig) ? ig / 255f : 1f,
                int.TryParse(ti.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var ib) ? ib / 255f : 1f,
                tia);
            if (ImGui.ColorEdit4("Interior", ref iv, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
            {
                s.Terrain.InteriorColor = $"#{(int)(iv.X * 255):X2}{(int)(iv.Y * 255):X2}{(int)(iv.Z * 255):X2}";
                s.Terrain.InteriorOpacity = iv.W;
            }
            ImGuiTheme.Tooltip(SettingHints.Radar.TerrainInterior);

            var ev = new Vector4(
                int.TryParse(te.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var er) ? er / 255f : 1f,
                int.TryParse(te.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var eg) ? eg / 255f : 1f,
                int.TryParse(te.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var eb) ? eb / 255f : 1f,
                tea);
            if (ImGui.ColorEdit4("Edge", ref ev, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
            {
                s.Terrain.EdgeColor = $"#{(int)(ev.X * 255):X2}{(int)(ev.Y * 255):X2}{(int)(ev.Z * 255):X2}";
                s.Terrain.EdgeOpacity = ev.W;
            }
            ImGuiTheme.Tooltip(SettingHints.Radar.TerrainEdge);

            ImGui.SetNextItemWidth(UiW());
            int edgeDetail = s.Terrain.ImGuiEdgeDetail;
            if (ImGui.SliderInt("Edge detail", ref edgeDetail, 1, 8)) s.Terrain.ImGuiEdgeDetail = edgeDetail;
            ImGuiTheme.Tooltip(SettingHints.Radar.TerrainEdgeDetail);

            ImGui.SetNextItemWidth(UiW());
            float edgeThick = s.Terrain.ImGuiEdgeThickness;
            if (ImGui.SliderFloat("Edge thickness", ref edgeThick, 0.5f, 4f, "%.1f")) s.Terrain.ImGuiEdgeThickness = edgeThick;
            ImGuiTheme.Tooltip(SettingHints.Radar.TerrainEdgeThickness);
        }
        ImGuiTheme.EndAccordionSection(terrainOpen);

        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
        ImGui.TextWrapped("FPS, refresh rates, smoothing, and metrics HUD → Performance tab.");
        ImGui.PopStyleColor();
    }

    private void DrawPerformanceTab(RadarSettings s)
    {
        DrawPerformanceSettings(s);
    }

    private void DrawPerformanceSettings(RadarSettings s)
    {
        bool refreshOpen = ImGuiTheme.BeginAccordionSection("RefreshCadence", "Refresh cadence",
            "How often memory is read and the overlay redraws.");
        if (refreshOpen)
        {
            bool lowImpact = s.LowImpactMode; ImGui.Checkbox("Low impact mode", ref lowImpact); s.LowImpactMode = lowImpact;
            ImGuiTheme.Tooltip(SettingHints.Performance.LowImpactMode);

            ImGui.SetNextItemWidth(UiW());
            int fps = s.FpsCap; if (ImGui.SliderInt("FPS cap", ref fps, 15, 360)) s.FpsCap = fps;
            ImGuiTheme.Tooltip(SettingHints.Performance.FpsCap);

            ImGui.SetNextItemWidth(UiW());
            int liveHz = s.LiveRefreshHz; if (ImGui.SliderInt("Live refresh Hz", ref liveHz, 5, 120)) s.LiveRefreshHz = liveHz;
            ImGuiTheme.Tooltip(SettingHints.Performance.LiveRefreshHz);

            ImGui.SetNextItemWidth(UiW());
            int worldHz = s.WorldRefreshHz; if (ImGui.SliderInt("World refresh Hz", ref worldHz, 1, 60)) s.WorldRefreshHz = worldHz;
            ImGuiTheme.Tooltip(SettingHints.Performance.WorldRefreshHz);

            ImGui.SetNextItemWidth(UiW());
            int inactiveHz = s.InactiveRefreshHz; if (ImGui.SliderInt("Inactive refresh Hz", ref inactiveHz, 1, 10)) s.InactiveRefreshHz = inactiveHz;
            ImGuiTheme.Tooltip(SettingHints.Performance.InactiveRefreshHz);

            ImGui.SetNextItemWidth(UiW());
            int hpHz = s.HpBarRefreshHz; if (ImGui.SliderInt("HP bar refresh Hz", ref hpHz, 1, 30)) s.HpBarRefreshHz = hpHz;
            ImGuiTheme.Tooltip(SettingHints.Performance.HpBarRefreshHz);

            ImGui.SetNextItemWidth(UiW());
            int maxHp = s.MaxLiveHpBars; if (ImGui.SliderInt("Max live HP bars", ref maxHp, 0, 256)) s.MaxLiveHpBars = maxHp;
            ImGuiTheme.Tooltip(SettingHints.Performance.MaxLiveHpBars);
        }
        ImGuiTheme.EndAccordionSection(refreshOpen);

        bool smoothOpen = ImGuiTheme.BeginAccordionSection("Smoothing", "Smoothing",
            "Visual interpolation between memory samples.");
        if (smoothOpen)
        {
            bool smooth = s.SmoothOverlayMotion; ImGui.Checkbox("Smooth overlay motion", ref smooth); s.SmoothOverlayMotion = smooth;
            ImGuiTheme.Tooltip(SettingHints.Performance.SmoothOverlayMotion);

            ImGui.SetNextItemWidth(UiW());
            int smoothMs = s.OverlaySmoothingMs; if (ImGui.SliderInt("Overlay smoothing ms", ref smoothMs, 0, 150)) s.OverlaySmoothingMs = smoothMs;
            ImGuiTheme.Tooltip(SettingHints.Performance.OverlaySmoothingMs);

            ImGui.SetNextItemWidth(UiW());
            int chipMs = s.ChipSmoothingMs; if (ImGui.SliderInt("Chip smoothing ms", ref chipMs, 0, 250)) s.ChipSmoothingMs = chipMs;
            ImGuiTheme.Tooltip(SettingHints.Performance.ChipSmoothingMs);

            bool snapLabels = s.PixelSnapLabels; ImGui.Checkbox("Pixel snap labels", ref snapLabels); s.PixelSnapLabels = snapLabels;
            ImGuiTheme.Tooltip(SettingHints.Performance.PixelSnapLabels);
            bool vsync = s.OverlayVSync; ImGui.Checkbox("Overlay VSync", ref vsync); s.OverlayVSync = vsync;
            ImGuiTheme.Tooltip(SettingHints.Performance.OverlayVSync);
        }
        ImGuiTheme.EndAccordionSection(smoothOpen);

        bool metricsOpen = ImGuiTheme.BeginAccordionSection("MetricsHud", "On-screen metrics",
            "FPS / CPU / GPU HUD under the nav menu.");
        if (metricsOpen)
        {
            bool fpsHud = s.ShowFpsOverlay; ImGui.Checkbox("FPS / resource overlay", ref fpsHud); s.ShowFpsOverlay = fpsHud;
            ImGuiTheme.Tooltip(SettingHints.Performance.FpsResourceOverlay);

            bool pf = s.ShowPerfStats; ImGui.Checkbox("Extended perf stats", ref pf); s.ShowPerfStats = pf;
            ImGuiTheme.Tooltip(SettingHints.Performance.ExtendedPerfStats);

            ImGui.SetNextItemWidth(UiW());
            int metricsHz = s.MetricsRefreshHz; if (ImGui.SliderInt("Metrics refresh Hz", ref metricsHz, 1, 10)) s.MetricsRefreshHz = metricsHz;
            ImGuiTheme.Tooltip(SettingHints.Performance.MetricsRefreshHz);

            ImGui.SetNextItemWidth(UiW());
            int gpuSeconds = s.GpuMetricsRefreshSeconds; if (ImGui.SliderInt("GPU metrics seconds", ref gpuSeconds, 1, 30)) s.GpuMetricsRefreshSeconds = gpuSeconds;
            ImGuiTheme.Tooltip(SettingHints.Performance.GpuMetricsSeconds);
        }
        ImGuiTheme.EndAccordionSection(metricsOpen);

        bool privacyOpen = ImGuiTheme.BeginAccordionSection("Privacy", "Privacy",
            "Capture exclusion and related privacy controls.");
        if (privacyOpen)
        {
            bool hideCapture = s.HideFromScreenCapture;
            if (ImGui.Checkbox("Hide from screen capture", ref hideCapture))
            {
                s.HideFromScreenCapture = hideCapture;
                ApplyCaptureAffinity(s);
            }
            ImGuiTheme.Tooltip(SettingHints.Performance.HideFromScreenCapture);
        }
        ImGuiTheme.EndAccordionSection(privacyOpen);

        bool uiOpen = ImGuiTheme.BeginAccordionSection("SettingsUi", "Settings UI",
            "In-game panel font (GameHelper defaults).");
        if (uiOpen)
        {
            ImGui.TextDisabled($"Active font: {OverlayFonts.LastResolvedLabel}");
            ImGui.TextWrapped(OverlayFonts.LastResolvedPath);
            ImGui.SetNextItemWidth(UiW());
            int uiSize = s.UiFontSize;
            if (ImGui.SliderInt("UI font size", ref uiSize, 13, 40))
                s.UiFontSize = uiSize;
            ImGuiTheme.Tooltip(SettingHints.Performance.UiFontSize);
        }
        ImGuiTheme.EndAccordionSection(uiOpen);
    }

    private void DrawNavMenuCornerSetting(RadarSettings s)
    {
        var corners = new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
        var idx = Array.IndexOf(corners, s.NavMenuCorner);
        if (idx < 0) idx = 0;
        ImGui.SetNextItemWidth(UiW(8f));
        if (ImGui.BeginCombo("Taskbar anchor", corners[idx]))
        {
            for (var i = 0; i < corners.Length; i++)
            {
                if (ImGui.Selectable(corners[i], i == idx))
                {
                    s.NavMenuCorner = corners[i];
                    s.NavTaskbarX = -1f;
                    s.NavTaskbarY = -1f;
                    _navMenuCorner = corners[i];
                    _navTaskbarPositionInitialized = false;
                }
            }
            ImGui.EndCombo();
        }
        ImGuiTheme.Tooltip(SettingHints.Performance.NavMenuCorner);
    }

    private void DrawHotkeysTab(RadarSettings s)
    {
        DrawHotkeysSection(s);
    }

    private void DrawHpBarsTab(RadarSettings s)
    {
        bool rarityOpen = ImGuiTheme.BeginAccordionSection("HpRarity", "Rarity toggles",
            "World-space monster health bars by rarity.");
        if (rarityOpen)
        {
            bool bn = s.HpBarNormal; ImGui.Checkbox("Normal", ref bn); s.HpBarNormal = bn;
            ImGuiTheme.Tooltip(SettingHints.HpBars.Normal);

            ImGui.SameLine(); bool bm = s.HpBarMagic; ImGui.Checkbox("Magic", ref bm); s.HpBarMagic = bm;
            ImGuiTheme.Tooltip(SettingHints.HpBars.Magic);

            ImGui.SameLine(); bool br = s.HpBarRare; ImGui.Checkbox("Rare", ref br); s.HpBarRare = br;
            ImGuiTheme.Tooltip(SettingHints.HpBars.Rare);

            ImGui.SameLine(); bool bu = s.HpBarUnique; ImGui.Checkbox("Unique", ref bu); s.HpBarUnique = bu;
            ImGuiTheme.Tooltip(SettingHints.HpBars.Unique);
        }
        ImGuiTheme.EndAccordionSection(rarityOpen);

        bool texOpen = ImGuiTheme.BeginAccordionSection("HpTextures", "Textures",
            "Gradient bar textures from Overlay/Textures.");
        if (texOpen)
        {
            var hb = s.HpBars;
            bool textures = hb.UseTextures;
            if (ImGui.Checkbox("Use bar textures", ref textures))
                hb.UseTextures = textures;
            ImGuiTheme.Tooltip(SettingHints.HpBars.UseTextures);
        }
        ImGuiTheme.EndAccordionSection(texOpen);

        bool geomOpen = ImGuiTheme.BeginAccordionSection("HpGeometry", "Bar geometry",
            "Width, height, and screen offset per rarity.");
        if (geomOpen)
        {
            var hb = s.HpBars;
            float w = hb.WidthNormal;
            ImGui.SetNextItemWidth(UiW());
            if (ImGui.SliderFloat("Width Normal", ref w, 30f, 250f)) hb.WidthNormal = w;
            ImGuiTheme.Tooltip(SettingHints.HpBars.WidthNormal);

            w = hb.WidthMagic; ImGui.SliderFloat("Width Magic", ref w, 30f, 250f); hb.WidthMagic = w;
            ImGuiTheme.Tooltip(SettingHints.HpBars.WidthMagic);

            w = hb.WidthRare; ImGui.SliderFloat("Width Rare", ref w, 30f, 250f); hb.WidthRare = w;
            ImGuiTheme.Tooltip(SettingHints.HpBars.WidthRare);

            w = hb.WidthUnique; ImGui.SliderFloat("Width Unique", ref w, 30f, 250f); hb.WidthUnique = w;
            ImGuiTheme.Tooltip(SettingHints.HpBars.WidthUnique);

            ImGui.Separator();
            float h = hb.Height; ImGui.SliderFloat("Bar Height", ref h, 2f, 12f); hb.Height = h;
            ImGuiTheme.Tooltip(SettingHints.HpBars.BarHeight);

            float ox = hb.OffsetX; ImGui.SliderFloat("Offset X", ref ox, -50f, 50f); hb.OffsetX = ox;
            ImGuiTheme.Tooltip(SettingHints.HpBars.OffsetX);

            float oy = hb.OffsetY; ImGui.SliderFloat("Offset Y", ref oy, -100f, 50f); hb.OffsetY = oy;
            ImGuiTheme.Tooltip(SettingHints.HpBars.OffsetY);
        }
        ImGuiTheme.EndAccordionSection(geomOpen);
    }

    private void DrawFlaskTab(RadarSettings s)
    {
        bool lifeOpen = ImGuiTheme.BeginAccordionSection("LifeFlask", "Life flask",
            "Opt-in flask automation when PoE2 is focused.");
        if (lifeOpen)
        {
            int mode = s.LifeFlaskMode switch { "EnergyShield" => 1, "Either" => 2, _ => 0 };
            ImGui.Combo("Trigger Pool", ref mode, "Health\0Energy Shield\0Either\0");
            ImGuiTheme.Tooltip(SettingHints.Flask.TriggerPool);
            s.LifeFlaskMode = mode switch { 1 => "EnergyShield", 2 => "Either", _ => "Health" };

            float lt = s.LifeThresholdPct; ImGui.SliderFloat("Life Threshold %", ref lt, 0f, 100f, "%.0f"); s.LifeThresholdPct = lt;
            ImGuiTheme.Tooltip(SettingHints.Flask.LifeThreshold);

            float et = s.EsThresholdPct; ImGui.SliderFloat("ES Threshold %", ref et, 0f, 100f, "%.0f"); s.EsThresholdPct = et;
            ImGuiTheme.Tooltip(SettingHints.Flask.EsThreshold);

            int lc = s.LifeCooldownMs; ImGui.SliderInt("Cooldown ms", ref lc, 200, 10000); s.LifeCooldownMs = lc;
            ImGuiTheme.Tooltip(SettingHints.Flask.LifeCooldown);

            int lk = s.LifeKey; ImGui.InputInt("Key code (hex)", ref lk, 1, 16); if (lk > 0) s.LifeKey = lk;
            ImGuiTheme.Tooltip(SettingHints.Flask.LifeKey);
        }
        ImGuiTheme.EndAccordionSection(lifeOpen);

        bool manaOpen = ImGuiTheme.BeginAccordionSection("ManaFlask", "Mana flask",
            "Mana pool threshold and cooldown.");
        if (manaOpen)
        {
            float mt = s.ManaThresholdPct; ImGui.SliderFloat("Mana Threshold %", ref mt, 0f, 100f, "%.0f"); s.ManaThresholdPct = mt;
            ImGuiTheme.Tooltip(SettingHints.Flask.ManaThreshold);

            int mc = s.ManaCooldownMs; ImGui.SliderInt("Cooldown ms", ref mc, 200, 10000); s.ManaCooldownMs = mc;
            ImGuiTheme.Tooltip(SettingHints.Flask.ManaCooldown);

            int mk = s.ManaKey; ImGui.InputInt("Key code (hex)", ref mk, 1, 16); if (mk > 0) s.ManaKey = mk;
            ImGuiTheme.Tooltip(SettingHints.Flask.ManaKey);
        }
        ImGuiTheme.EndAccordionSection(manaOpen);

        bool statusOpen = ImGuiTheme.BeginAccordionSection("FlaskStatus", "Status",
            "F8 master kill-switch and key code notes.");
        if (statusOpen)
        {
            ImGui.BulletText("F8 toggles auto-flask on/off. Settings apply immediately.");
            ImGui.BulletText("Keys are Win32 virtual-key codes (0x31 = '1', 0x32 = '2').");
        }
        ImGuiTheme.EndAccordionSection(statusOpen);
    }

    private void DrawStashValueTab(RadarSettings s, RenderContext? ctx)
    {
        var sv = s.StashValue;

        var overlayOpen = ImGuiTheme.BeginAccordionSection("StashValueOverlay", "Overlay",
            "Stash and inventory item value labels.");
        if (overlayOpen)
        {
            var show = sv.ShowOverlay;
            if (ImGui.Checkbox("Show stash item prices", ref show)) sv.ShowOverlay = show;

            var inv = sv.ShowInventoryOverlay;
            if (ImGui.Checkbox("Show inventory item prices", ref inv)) sv.ShowInventoryOverlay = inv;

            var hover = sv.HidePriceOnHover;
            if (ImGui.Checkbox("Hide price when hovering item", ref hover)) sv.HidePriceOnHover = hover;

            var maxThreshold = sv.DisplayCurrency switch
            {
                0 => 100f,
                1 => 200f,
                _ => 1000f,
            };
            var currencyLabel = sv.DisplayCurrency switch
            {
                0 => "div",
                1 => "ex",
                _ => "c",
            };
            var min = Math.Clamp(sv.MinValueEx, 0f, maxThreshold);
            ImGui.SetNextItemWidth(UiW(10f));
            if (ImGui.SliderFloat($"Min Price Threshold ({currencyLabel})##stashValueThreshold", ref min, 0f, maxThreshold, $"%.2f {currencyLabel}"))
                sv.MinValueEx = Math.Clamp(min, 0f, maxThreshold);

            var debug = sv.ShowDebugInfo;
            if (ImGui.Checkbox("Show Debug Info (Draw Boxes & Diagnostics)", ref debug)) sv.ShowDebugInfo = debug;
        }
        ImGuiTheme.EndAccordionSection(overlayOpen);

        var currencyOpen = ImGuiTheme.BeginAccordionSection("StashValueCurrency", "Display Currency",
            "Currency used for item value labels.");
        if (currencyOpen)
        {
            if (ImGui.RadioButton("Chaos", sv.DisplayCurrency == 2)) sv.DisplayCurrency = 2;
            ImGui.SameLine();
            if (ImGui.RadioButton("Exalted", sv.DisplayCurrency == 1)) sv.DisplayCurrency = 1;
            ImGui.SameLine();
            if (ImGui.RadioButton("Divine", sv.DisplayCurrency == 0)) sv.DisplayCurrency = 0;
        }
        ImGuiTheme.EndAccordionSection(currencyOpen);

        var styleOpen = ImGuiTheme.BeginAccordionSection("StashValueStyle", "Label style",
            "Font scale, position and text colour.");
        if (styleOpen)
        {
            var fs = sv.PriceFontScale;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Font Scale", ref fs, 0.5f, 2f, "%.2f")) sv.PriceFontScale = Math.Clamp(fs, 0.5f, 2f);

            var ox = sv.PriceOffsetX;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Horizontal Offset", ref ox, -50f, 50f)) sv.PriceOffsetX = Math.Clamp(ox, -50f, 50f);

            var oy = sv.PriceOffsetY;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Vertical Offset", ref oy, -50f, 50f)) sv.PriceOffsetY = Math.Clamp(oy, -50f, 50f);

            var col = ParseHexColor(sv.PriceTextColor);
            if (ImGui.ColorEdit3("Text Color", ref col, ImGuiColorEditFlags.NoInputs))
                sv.PriceTextColor = FormatHexColor3(col);
        }
        ImGuiTheme.EndAccordionSection(styleOpen);

        var priceOpen = ImGuiTheme.BeginAccordionSection("StashValuePriceSource", "Price Source",
            "Market data source and league.");
        if (priceOpen)
        {
            if (ImGui.RadioButton("poe2scout", sv.PriceSource == PoeNinjaPriceFetcher.SourcePoe2Scout))
                sv.PriceSource = PoeNinjaPriceFetcher.SourcePoe2Scout;
            ImGui.SameLine();
            if (ImGui.RadioButton("poe.ninja", sv.PriceSource == PoeNinjaPriceFetcher.SourcePoeNinja))
                sv.PriceSource = PoeNinjaPriceFetcher.SourcePoeNinja;

            var league = sv.League ?? "";
            ImGui.SetNextItemWidth(UiW(14f));
            if (ImGui.InputText("League", ref league, 64))
                sv.League = string.IsNullOrWhiteSpace(league) ? "Runes of Aldur" : league.Trim();

            var refresh = sv.RefreshIntervalMin;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Refresh interval (min)", ref refresh, 1, 120))
                sv.RefreshIntervalMin = Math.Clamp(refresh, 1, 120);

            if (ImGui.Button("Refresh prices now"))
            {
                var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "StashValue");
                System.IO.Directory.CreateDirectory(dir);
                PoeNinjaPriceFetcher.Configure(Math.Clamp(sv.PriceSource, 0, 1), sv.League ?? "", Math.Max(1, sv.RefreshIntervalMin));
                PoeNinjaPriceFetcher.ForceRefresh(dir, ignoreCooldown: true);
            }

            ImGui.SameLine();
            if (PoeNinjaPriceFetcher.IsFetching)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.2f, 1f), "Loading...");
            }
            else if (PoeNinjaPriceFetcher.LastFetchUtc > DateTime.MinValue)
            {
                var mins = Math.Max(0, (int)(DateTime.UtcNow - PoeNinjaPriceFetcher.LastFetchUtc).TotalMinutes);
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), $"{PoeNinjaPriceFetcher.LoadedItemCount} items | {mins} min ago");
            }
        }
        ImGuiTheme.EndAccordionSection(priceOpen);

        if (sv.ShowDebugInfo)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Labels this frame: {ctx?.StashValueLabels.Length ?? 0}");
        }
    }

    private void DrawStashUtilityTab(RadarSettings settings, RenderContext? ctx)
    {
        var s = settings.StashUtility;
        s.GoodWaystoneMods ??= new();
        s.GreatWaystoneMods ??= new();
        s.BadWaystoneMods ??= new();
        s.GoodTabletMods ??= new();
        s.BadTabletMods ??= new();
        s.GodTabletMods ??= new();
        s.TabletMinimumRolls ??= new();

        var general = ImGuiTheme.BeginAccordionSection("StashUtilityGeneral", "General", defaultOpen: true);
        if (general)
        {
            var waystones = s.EnableWaystones;
            if (ImGui.Checkbox("Enable Waystone Manager", ref waystones)) s.EnableWaystones = waystones;
            var tablets = s.EnableTablets;
            if (ImGui.Checkbox("Enable Tablet Manager", ref tablets)) s.EnableTablets = tablets;
            var stash = s.IncludeStash;
            if (ImGui.Checkbox("Highlight active stash tab", ref stash)) s.IncludeStash = stash;
            ImGui.SameLine();
            var inventory = s.IncludeInventory;
            if (ImGui.Checkbox("Highlight inventory", ref inventory)) s.IncludeInventory = inventory;
            var redPriority = s.RedTakesPriority;
            if (ImGui.Checkbox("Bad rules take priority", ref redPriority)) s.RedTakesPriority = redPriority;

            ImGui.Spacing();
            ImGui.TextUnformatted($"Highlights: {ctx?.StashUtilityHighlights.Length ?? 0}");
            ImGui.TextUnformatted(GamepadInput.IsConnected(settings.GamepadUserIndex)
                ? "Controller: connected · controller stash root active"
                : "Controller: not connected · controller stash root ready");
            ImGui.TextDisabled("The same scan checks keyboard/mouse and controller UI layouts.");
        }
        ImGuiTheme.EndAccordionSection(general);

        var waystoneFilters = ImGuiTheme.BeginAccordionSection("StashUtilityWaystoneFilters", "Waystone Filters",
            "Numerical requirements and GREAT thresholds.");
        if (waystoneFilters)
        {
            var tier = Math.Clamp(s.MinTier, 1, 16);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Minimum tier", ref tier, 1, 16)) s.MinTier = tier;
            var hideNormal = s.HideNormalWaystones;
            if (ImGui.Checkbox("Hide normal Waystones", ref hideNormal)) s.HideNormalWaystones = hideNormal;

            (s.FilterMaxRevives, s.MaxRevives) = DrawStashUtilityFilter("Maximum revives", s.FilterMaxRevives, s.MaxRevives, 0, 5);
            (s.FilterMinItemRarity, s.MinItemRarity) = DrawStashUtilityFilter("Minimum item rarity %", s.FilterMinItemRarity, s.MinItemRarity, 0, 250);
            (s.FilterMinPackSize, s.MinPackSize) = DrawStashUtilityFilter("Minimum pack size %", s.FilterMinPackSize, s.MinPackSize, 0, 150);
            (s.FilterMinMonsterRarity, s.MinMonsterRarity) = DrawStashUtilityFilter("Minimum monster rarity %", s.FilterMinMonsterRarity, s.MinMonsterRarity, 0, 150);
            (s.FilterMinMonsterEffectiveness, s.MinMonsterEffectiveness) = DrawStashUtilityFilter("Minimum monster effectiveness %", s.FilterMinMonsterEffectiveness, s.MinMonsterEffectiveness, 0, 150);
            (s.FilterMinDropChance, s.MinDropChance) = DrawStashUtilityFilter("Minimum Waystone drop chance %", s.FilterMinDropChance, s.MinDropChance, 0, 400);
            (s.FilterMinExplicitMods, s.MinExplicitMods) = DrawStashUtilityFilter("Minimum explicit mods", s.FilterMinExplicitMods, s.MinExplicitMods, 0, 10);
            (s.FilterMaxExplicitMods, s.MaxExplicitMods) = DrawStashUtilityFilter("Maximum explicit mods", s.FilterMaxExplicitMods, s.MaxExplicitMods, 0, 10);

            ImGui.SeparatorText("GREAT requires every enabled condition");
            (s.GreatByItemRarity, s.GreatItemRarity) = DrawStashUtilityFilter("GREAT item rarity %", s.GreatByItemRarity, s.GreatItemRarity, 0, 250);
            (s.GreatByPackSize, s.GreatPackSize) = DrawStashUtilityFilter("GREAT pack size %", s.GreatByPackSize, s.GreatPackSize, 0, 150);
            (s.GreatByDropChance, s.GreatDropChance) = DrawStashUtilityFilter("GREAT drop chance %", s.GreatByDropChance, s.GreatDropChance, 0, 400);
            (s.GreatByExplicitMods, s.GreatExplicitMods) = DrawStashUtilityFilter("GREAT explicit mods", s.GreatByExplicitMods, s.GreatExplicitMods, 0, 10);
        }
        ImGuiTheme.EndAccordionSection(waystoneFilters);

        var waystoneMods = ImGuiTheme.BeginAccordionSection("StashUtilityWaystoneMods", "Waystone Mod Rules",
            "Choose exactly which modifier juices are Required, GREAT, or BAD.");
        if (waystoneMods)
        {
            var hoverTiers = s.ShowWaystoneTiersOnHover;
            if (ImGui.Checkbox("Show reward tiers when hovering Waystones", ref hoverTiers))
                s.ShowWaystoneTiersOnHover = hoverTiers;
            var all = s.RequireAllGoodWaystoneMods;
            if (ImGui.Checkbox("Require all selected Required mods", ref all)) s.RequireAllGoodWaystoneMods = all;
            var allGreat = s.RequireAllGreatWaystoneMods;
            if (ImGui.Checkbox("Require all selected GREAT mods for GREAT", ref allGreat)) s.RequireAllGreatWaystoneMods = allGreat;
            var gatedBad = s.BadOnlyWhenNumericalFiltersPass;
            if (ImGui.Checkbox("Only show bad mods on otherwise qualifying Waystones", ref gatedBad)) s.BadOnlyWhenNumericalFiltersPass = gatedBad;
            ImGui.TextDisabled("When modifier rules are selected, unrelated Waystones are filtered out.");
            DrawStashUtilitySearch();
            ImGui.BeginChild("StashUtilityWaystoneModList", new NumVec2(0, 330));
            foreach (var def in StashUtilityCatalog.WaystoneMods.Where(MatchesStashUtilitySearch))
                DrawWaystoneModRule(s, def);
            ImGui.EndChild();
        }
        ImGuiTheme.EndAccordionSection(waystoneMods);

        var tabletMods = ImGuiTheme.BeginAccordionSection("StashUtilityTabletMods", "Tablet Mod Rules",
            "Filter Tablet juice independently as Required, GREAT, or BAD.");
        if (tabletMods)
        {
            var hoverTiers = s.ShowTabletTiersOnHover;
            if (ImGui.Checkbox("Show modifier tiers when hovering Tablets", ref hoverTiers))
                s.ShowTabletTiersOnHover = hoverTiers;
            var allRequired = s.RequireAllGoodTabletMods;
            if (ImGui.Checkbox("Require all selected Required mods##Tablet", ref allRequired))
                s.RequireAllGoodTabletMods = allRequired;
            var allGreat = s.RequireAllGreatTabletMods;
            if (ImGui.Checkbox("Require all selected GREAT mods for GREAT##Tablet", ref allGreat))
                s.RequireAllGreatTabletMods = allGreat;
            var gatedBad = s.BadTabletOnlyWhenOtherRulesPass;
            if (ImGui.Checkbox("Only show BAD Tablets when Required/GREAT rules pass", ref gatedBad))
                s.BadTabletOnlyWhenOtherRulesPass = gatedBad;
            var hideBad = s.HideBadTablets;
            if (ImGui.Checkbox("Do not highlight bad Tablets", ref hideBad)) s.HideBadTablets = hideBad;

            ImGui.Spacing();
            ImGui.TextDisabled("Same rule model as Waystones. Minimum rolls remain per modifier.");
            ImGui.TextDisabled("Required: cyan border  |  GREAT: arrow  |  BAD: red border");
            ImGui.TextDisabled("Choose the Tablet you are rolling. Universal selections are shared across Tablet tabs.");
            ImGui.TextDisabled("Market tiers (Runes of Aldur, Jul 2026): S gold | A green | B blue | C grey | D red.");
            ImGui.TextDisabled("Tiers are price-informed guidance; exact value still depends on roll, uses remaining, and modifier combinations.");
            if (ImGui.BeginTabBar("StashUtilityTabletTypes", ImGuiTabBarFlags.FittingPolicyScroll))
            {
                foreach (var group in StashUtilityCatalog.TabletGroups)
                {
                    var visible = ImGui.BeginTabItem(group.Name);
                    ImGuiTheme.Tooltip(group.Description);
                    if (!visible) continue;

                    DrawTabletRuleGroup(s, group);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGuiTheme.EndAccordionSection(tabletMods);

        var visuals = ImGuiTheme.BeginAccordionSection("StashUtilityVisuals", "Visuals",
            "Border styles, colours, rarity corner and GREAT arrow.");
        if (visuals)
        {
            var thickness = s.BorderThickness;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Border thickness", ref thickness, 1f, 10f, "%.1f px")) s.BorderThickness = thickness;
            var margin = s.BorderMargin;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Border margin", ref margin, 0f, 20f, "%.1f px")) s.BorderMargin = margin;
            s.GoodBorderStyle = DrawStashUtilityStyleCombo("Good border", s.GoodBorderStyle);
            s.BadBorderStyle = DrawStashUtilityStyleCombo("Bad border", s.BadBorderStyle);

            var rarity = s.ShowRarityCorner;
            if (ImGui.Checkbox("Rarity corner", ref rarity)) s.ShowRarityCorner = rarity;
            if (s.ShowRarityCorner)
            {
                var raritySize = s.RarityCornerSize;
                ImGui.SetNextItemWidth(UiW(8f));
                if (ImGui.SliderFloat("Rarity corner size", ref raritySize, 3f, 30f, "%.0f px")) s.RarityCornerSize = raritySize;
            }
            var great = s.ShowGreatArrow;
            if (ImGui.Checkbox("GREAT arrow", ref great)) s.ShowGreatArrow = great;
            if (s.ShowGreatArrow)
            {
                var greatSize = s.GreatArrowSize;
                ImGui.SetNextItemWidth(UiW(8f));
                if (ImGui.SliderFloat("GREAT arrow size", ref greatSize, 5f, 60f, "%.0f px")) s.GreatArrowSize = greatSize;
                s.GreatArrowCorner = DrawStashUtilityCornerCombo("GREAT arrow corner", s.GreatArrowCorner);
            }

            s.WaystoneGoodColor = DrawStashUtilityColor("Waystone good", s.WaystoneGoodColor);
            s.WaystoneBadColor = DrawStashUtilityColor("Waystone bad", s.WaystoneBadColor);
            s.WaystoneGreatColor = DrawStashUtilityColor("Waystone GREAT", s.WaystoneGreatColor);
            s.TabletGoodColor = DrawStashUtilityColor("Tablet good", s.TabletGoodColor);
            s.TabletBadColor = DrawStashUtilityColor("Tablet bad", s.TabletBadColor);
            s.TabletGreatColor = DrawStashUtilityColor("Tablet GREAT", s.TabletGreatColor);
        }
        ImGuiTheme.EndAccordionSection(visuals);
    }

    private void DrawWaystoneAlchemyTab(RadarSettings settings, RenderContext? ctx)
    {
        var s = settings.WaystoneAlchemy;
        var general = ImGuiTheme.BeginAccordionSection("WaystoneAlchemyGeneral", "Crafting Assistant", defaultOpen: true);
        if (general)
        {
            var enabled = s.Enabled;
            if (ImGui.Checkbox("Enable Crafting Assistant", ref enabled)) s.Enabled = enabled;
            ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.Enabled);

            var targets = new[] { "Waystones", "Tablets" };
            s.TargetType = Math.Clamp(s.TargetType, 0, targets.Length - 1);
            ImGui.SetNextItemWidth(UiW(14f));
            if (ImGui.BeginCombo("Target", targets[s.TargetType]))
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    if (!ImGui.Selectable(targets[i], s.TargetType == i)) continue;
                    s.TargetType = i;
                    s.Recipe = 0;
                }
                ImGui.EndCombo();
            }
            ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.Target);

            if (ImGui.RadioButton("MANUAL / guided", s.Mode == 0)) s.Mode = 0;
            ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.ModeManual);
            ImGui.SameLine();
            if (ImGui.RadioButton("AUTO", s.Mode == 1)) s.Mode = 1;
            ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.ModeAuto);

            if (s.Mode == 1)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.65f, 0.2f, 1f));
                ImGui.TextWrapped("AUTO moves the cursor and applies currency. Open inventory, click Start, and use emergency stop if anything looks wrong.");
                ImGui.PopStyleColor();
                var ack = s.AutoModeAcknowledged;
                if (ImGui.Checkbox("I understand and enable automatic inventory clicks", ref ack)) s.AutoModeAcknowledged = ack;
                ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.AutoAck);
            }

            var recipes = s.TargetType == 1
                ? new[]
                {
                    "Upgrade (Transmute / Augment / Regal / Exalted)",
                    "Corrupt finished Tablets (Ancient Infuser)",
                    "Alchemy (Normal/Magic → Rare with 4 modifiers)",
                }
                : new[]
                {
                    "Upgrade (Alchemy / Regal / Exalted)",
                    "Corrupt rare Waystones",
                    "Distilled Paranoia (guided)",
                };
            s.Recipe = Math.Clamp(s.Recipe, 0, recipes.Length - 1);
            ImGui.SetNextItemWidth(UiW(20f));
            if (ImGui.BeginCombo("Recipe", recipes[s.Recipe]))
            {
                for (var i = 0; i < recipes.Length; i++)
                    if (ImGui.Selectable(recipes[i], s.Recipe == i)) s.Recipe = i;
                ImGui.EndCombo();
            }
            ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.Recipe);
            if (s.TargetType == 0 && s.Recipe == 2)
                ImGui.TextDisabled("Paranoia remains guided until the KBM and controller Instilling panels are mapped live.");

            if (s.TargetType == 1)
            {
                var mods = Math.Clamp(s.DesiredTabletExplicitMods, 2, 4);
                ImGui.SetNextItemWidth(UiW(8f));
                if (ImGui.SliderInt("Tablet modifier target", ref mods, 2, 4))
                    s.DesiredTabletExplicitMods = mods;
                ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.TabletModTarget);
                ImGui.TextWrapped(
                    "Prefer Upgrade for bulk tablets. Use 2 mods by default; pick 3 or 4 only after Reverse Transcription / Partial Translation. Rejected currency skips that item and continues.");
                if (s.Recipe == 1)
                    ImGui.TextDisabled("Tablet corruption uses Ancient Infusers, not Vaal Orbs.");
                else if (s.Recipe == 2)
                {
                    ImGui.TextWrapped(
                        "Alchemy upgrades Normal or Magic (blue) tablets to Rare with 4 new random mods (blue mods are wiped). Same flow as Waystone Alchemy. If a click is rejected (missing Partial Translation), that tablet is skipped.");
                }
            }
            else
            {
                var tier = Math.Clamp(s.MinimumTier, 1, 16);
                ImGui.SetNextItemWidth(UiW(8f));
                if (ImGui.SliderInt("Minimum tier", ref tier, 1, 16)) s.MinimumTier = tier;
                ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.MinimumTier);
                var regal = s.UseRegalOnMagic;
                if (ImGui.Checkbox("Prefer Regal on Magic Waystones (else Alchemy)", ref regal))
                    s.UseRegalOnMagic = regal;
                ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.UseRegal);
                var exalt = s.ApplyExaltedToRare;
                if (ImGui.Checkbox("Exalt identified Rare Waystones", ref exalt)) s.ApplyExaltedToRare = exalt;
                ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.ApplyExalt);
                if (s.ApplyExaltedToRare)
                {
                    var mods = Math.Clamp(s.DesiredExplicitMods, 3, 6);
                    ImGui.SetNextItemWidth(UiW(8f));
                    if (ImGui.SliderInt("Stop at explicit mods", ref mods, 3, 6)) s.DesiredExplicitMods = mods;
                    ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.DesiredMods);
                }
            }
            var delay = Math.Clamp(s.ActionDelayMs, 150, 1500);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Action delay", ref delay, 150, 1500, "%d ms")) s.ActionDelayMs = delay;
            ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.ActionDelay);
        }
        ImGuiTheme.EndAccordionSection(general);

        var controls = ImGuiTheme.BeginAccordionSection("WaystoneAlchemyControls", "Controls", defaultOpen: true);
        if (controls)
        {
            var canStart = s.Enabled && s.Mode == 1 && s.AutoModeAcknowledged &&
                           !(s.TargetType == 0 && s.Recipe == 2);
            if (!canStart) ImGui.BeginDisabled();
            if (ImGui.Button("Start", new NumVec2(UiW(6f), 0f)))
            {
                FlushSettingsNow();
                _settingsOpen = false;
                _enqueue(_startWaystoneAlchemy);
            }
            if (!canStart) ImGui.EndDisabled();
            ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.Start);
            ImGui.SameLine();
            if (ImGui.Button("Stop", new NumVec2(UiW(6f), 0f)))
                _enqueue(_stopWaystoneAlchemy);
            ImGuiTheme.Tooltip(SettingHints.CraftingAssistant.Stop);

            ImGui.Spacing();
            ImGui.TextDisabled("Optional hotkeys (keyboard or Xbox). Start/Stop buttons are enough for mouse use.");
            DrawHotkeyRow(settings, "waystoneAlchemyRunHotkey", "Toggle AUTO (optional)", SettingHints.CraftingAssistant.RunHotkey);
            DrawHotkeyRow(settings, "waystoneAlchemyStopHotkey", "Emergency stop", SettingHints.CraftingAssistant.EmergencyStop);
            ImGui.TextUnformatted($"Status: {ctx?.WaystoneAlchemyStatus ?? "Waiting for game"}");
            ImGui.TextUnformatted(GamepadInput.IsConnected(settings.GamepadUserIndex)
                ? "Controller: connected"
                : "Controller: ready when connected");
        }
        ImGuiTheme.EndAccordionSection(controls);
    }

    private void DrawPickupHelperTab(RadarSettings settings, RenderContext? ctx)
    {
        var s = settings.PickupHelper;
        var general = ImGuiTheme.BeginAccordionSection("PickupHelperGeneral", "Filter-aware Pickup", defaultOpen: true);
        if (general)
        {
            var enabled = s.Enabled;
            if (ImGui.Checkbox("Enable Pickup Helper", ref enabled)) s.Enabled = enabled;
            ImGui.TextWrapped(
                "The game filter remains the source of truth. Nearby mode only targets visible labels, " +
                "skips equippable gear by default, and can apply additional selection rules.");

            var modes = new[]
            {
                "ASSIST / highlight only",
                "HOVER + hold",
                "NEARBY + hold",
                "AUTOMATIC nearby",
            };
            s.Mode = Math.Clamp(s.Mode, 0, modes.Length - 1);
            ImGui.SetNextItemWidth(UiW(18f));
            if (ImGui.BeginCombo("Mode", modes[s.Mode]))
            {
                for (var i = 0; i < modes.Length; i++)
                    if (ImGui.Selectable(modes[i], s.Mode == i)) s.Mode = i;
                ImGui.EndCombo();
            }

            if (s.Mode is 2 or 3)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.65f, 0.2f, 1f));
                ImGui.TextWrapped(s.Mode == 3
                    ? "Automatic mode: press activation once to start and again to stop. It keeps collecting the closest visible labels without moving your character."
                    : "Nearby mode moves the mouse to the closest visible label while activation is held. It never moves your character and stops if pickup is not confirmed.");
                ImGui.PopStyleColor();
                var acknowledged = s.AutoModeAcknowledged;
                if (ImGui.Checkbox("I understand and enable automatic label clicks", ref acknowledged))
                    s.AutoModeAcknowledged = acknowledged;
            }
            else if (s.Mode == 1)
            {
                ImGui.TextDisabled("Hover mode follows the mouse cursor. Controller users should select Nearby mode.");
            }

            var distance = Math.Clamp(s.MaxPickupDistance, 5, 100);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Maximum pickup range", ref distance, 5, 100, "%d grid"))
                s.MaxPickupDistance = distance;

            var humanSpeed = s.HumanSpeed;
            if (ImGui.Checkbox("Instant human-speed pickup", ref humanSpeed))
                s.HumanSpeed = humanSpeed;
            ImGui.TextDisabled(s.HumanSpeed
                ? "Fast profile: 16 ms scan, immediate reaction, 12 ms click settle, 50 ms confirmed-item cooldown."
                : "Custom timing controls are active.");

            ImGui.BeginDisabled(s.HumanSpeed);
            var minDelay = Math.Clamp(s.MinPickupDelayMs, 0, 500);
            var maxDelay = Math.Clamp(s.MaxPickupDelayMs, minDelay, 750);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Minimum delay", ref minDelay, 0, 500, "%d ms"))
                s.MinPickupDelayMs = minDelay;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Maximum delay", ref maxDelay, minDelay, 750, "%d ms"))
                s.MaxPickupDelayMs = maxDelay;

            var cooldown = Math.Clamp(s.ClickCooldownMs, 100, 1000);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Click cooldown", ref cooldown, 100, 1000, "%d ms"))
                s.ClickCooldownMs = cooldown;
            ImGui.EndDisabled();
            var timeout = Math.Clamp(s.ConfirmationTimeoutMs, 500, 4000);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Confirmation timeout", ref timeout, 500, 4000, "%d ms"))
                s.ConfirmationTimeoutMs = timeout;
            if (s.Mode == 3)
            {
                ImGui.TextDisabled("A missed moving label is reacquired automatically; repeated misses cool down that item.");
                var retry = Math.Clamp(s.MissRetryDelayMs, 100, 2000);
                ImGui.SetNextItemWidth(UiW(8f));
                if (ImGui.SliderInt("Miss retry delay", ref retry, 100, 2000, "%d ms"))
                    s.MissRetryDelayMs = retry;
                var misses = Math.Clamp(s.MaxMissesBeforeCooldown, 1, 8);
                ImGui.SetNextItemWidth(UiW(8f));
                if (ImGui.SliderInt("Misses before item cooldown", ref misses, 1, 8))
                    s.MaxMissesBeforeCooldown = misses;
                var missedCooldown = Math.Clamp(s.MissedItemCooldownMs, 500, 10_000);
                ImGui.SetNextItemWidth(UiW(8f));
                if (ImGui.SliderInt("Missed item cooldown", ref missedCooldown, 500, 10_000, "%d ms"))
                    s.MissedItemCooldownMs = missedCooldown;
            }

            var highlight = s.ShowTargetHighlight;
            if (ImGui.Checkbox("Highlight current target", ref highlight)) s.ShowTargetHighlight = highlight;
            var pauseHidden = s.PauseWhileShowHiddenHeld;
            if (ImGui.Checkbox("Pause while show-hidden-items is held", ref pauseHidden))
                s.PauseWhileShowHiddenHeld = pauseHidden;
        }
        ImGuiTheme.EndAccordionSection(general);

        var selection = ImGuiTheme.BeginAccordionSection(
            "PickupHelperSelection",
            "Selection Rules",
            defaultOpen: false);
        if (selection)
        {
            var policy = s.Policy ??= new PickupPolicySettings();
            ImGui.TextWrapped(
                "Rules only narrow or prioritize labels already shown by the active game filter. " +
                "Fragments are case-insensitive and match either the item name or metadata path.");

            var allowEquipment = policy.AllowEquipment;
            if (ImGui.Checkbox("Allow equippable gear", ref allowEquipment))
                policy.AllowEquipment = allowEquipment;
            if (!policy.AllowEquipment)
                ImGui.TextDisabled("Weapons, armour, jewellery, flasks, and charms remain blocked.");
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.65f, 0.2f, 1f));
                ImGui.TextWrapped("Equipment pickup is enabled; your visible filter and rules decide what is eligible.");
                ImGui.PopStyleColor();
            }

            var allow = policy.AllowPatterns ?? "";
            ImGui.SetNextItemWidth(UiW(28f));
            if (ImGui.InputTextWithHint(
                    "##pickupAllowPatterns",
                    "Optional allow-list fragments, comma separated",
                    ref allow,
                    2048))
                policy.AllowPatterns = allow;
            ImGui.TextDisabled("Empty: allow every visible non-gear item.");

            var deny = policy.DenyPatterns ?? "";
            ImGui.SetNextItemWidth(UiW(28f));
            if (ImGui.InputTextWithHint(
                    "##pickupDenyPatterns",
                    "Deny-list fragments; deny always wins",
                    ref deny,
                    2048))
                policy.DenyPatterns = deny;

            var priority = policy.PriorityPatterns ?? "";
            ImGui.SetNextItemWidth(UiW(28f));
            if (ImGui.InputTextWithHint(
                    "##pickupPriorityPatterns",
                    "Priority fragments, highest first",
                    ref priority,
                    2048))
                policy.PriorityPatterns = priority;
            ImGui.TextDisabled("Example: Divine Orb, Perfect, Waystone, Currency");
        }
        ImGuiTheme.EndAccordionSection(selection);

        var controls = ImGuiTheme.BeginAccordionSection("PickupHelperControls", "Controls and Safety", defaultOpen: true);
        if (controls)
        {
            ImGui.TextDisabled(s.Mode == 3
                ? "Bindings accept keyboard or Xbox controller buttons. Press activation once to start or stop."
                : "Bindings accept keyboard or Xbox controller buttons. Hold activation to run.");
            DrawHotkeyRow(
                settings,
                "pickupActivationHotkey",
                s.Mode == 3 ? "Start / stop automatic pickup" : "Hold to pick up",
                s.Mode == 3
                    ? "Press once to start automatic nearby pickup; press again to stop."
                    : "Runs Hover or Nearby mode only while held.");
            DrawHotkeyRow(settings, "pickupEmergencyStopHotkey", "Emergency stop", "Stops immediately; release activation before restarting.");
            DrawHotkeyRow(settings, "pickupShowHiddenHotkey", "Show hidden items key", "Pickup stops while this binding is held. Default: Alt.");
            ImGui.TextUnformatted($"Status: {ctx?.PickupStatus ?? "Waiting for game"}");
            ImGui.TextUnformatted(GamepadInput.IsConnected(settings.GamepadUserIndex)
                ? "Controller: connected"
                : "Controller: ready when connected");
        }
        ImGuiTheme.EndAccordionSection(controls);

    }

    private static (bool Enabled, int Value) DrawStashUtilityFilter(string label, bool enabled, int value, int min, int max)
    {
        ImGui.PushID(label);
        ImGui.Checkbox("##enabled", ref enabled);
        ImGui.SameLine();
        ImGui.BeginDisabled(!enabled);
        ImGui.SetNextItemWidth(UiW(8f));
        ImGui.SliderInt(label, ref value, min, max);
        ImGui.EndDisabled();
        value = Math.Clamp(value, min, max);
        ImGui.PopID();
        return (enabled, value);
    }

    private void DrawStashUtilitySearch()
    {
        ImGui.SetNextItemWidth(UiW(18f));
        ImGui.InputTextWithHint(
            "##stashUtilitySearch",
            "Search exact modifier text, tier, ID, or category",
            ref _stashUtilityModSearch,
            128);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear search")) _stashUtilityModSearch = "";
    }

    private bool MatchesStashUtilitySearch(StashUtilityModDefinition definition)
        => string.IsNullOrWhiteSpace(_stashUtilityModSearch)
           || definition.Name.Contains(_stashUtilityModSearch, StringComparison.OrdinalIgnoreCase)
           || definition.Id.Contains(_stashUtilityModSearch, StringComparison.OrdinalIgnoreCase)
           || definition.Category.Contains(_stashUtilityModSearch, StringComparison.OrdinalIgnoreCase);

    private void DrawTabletRuleGroup(StashUtilitySettings settings, TabletRuleGroup group)
    {
        ImGui.Spacing();
        ImGui.TextWrapped(group.Description);

        var groupMods = StashUtilityCatalog.TabletModsFor(group).ToArray();
        var goodCount = groupMods.Count(definition => ContainsSetting(settings.GoodTabletMods, definition.Id));
        var badCount = groupMods.Count(definition => ContainsSetting(settings.BadTabletMods, definition.Id));
        var godCount = groupMods.Count(definition => ContainsSetting(settings.GodTabletMods, definition.Id));
        ImGui.TextDisabled($"Selected in this tab: {goodCount} Required  |  {godCount} GREAT  |  {badCount} BAD");

        DrawStashUtilitySearch();

        var flags = ImGuiTableFlags.BordersInnerH
                    | ImGuiTableFlags.BordersOuter
                    | ImGuiTableFlags.RowBg
                    | ImGuiTableFlags.ScrollY
                    | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable($"StashUtilityTabletRules##{group.Name}", 6, flags, new NumVec2(0, 380)))
            return;

        var checkWidth = ImGui.GetFontSize() * 3.5f;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Modifier", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Tier", ImGuiTableColumnFlags.WidthFixed, checkWidth);
        ImGui.TableSetupColumn("Required", ImGuiTableColumnFlags.WidthFixed, checkWidth * 1.5f);
        ImGui.TableSetupColumn("GREAT", ImGuiTableColumnFlags.WidthFixed, checkWidth);
        ImGui.TableSetupColumn("BAD", ImGuiTableColumnFlags.WidthFixed, checkWidth);
        ImGui.TableSetupColumn("Minimum roll", ImGuiTableColumnFlags.WidthFixed, UiW(8f));
        ImGui.TableHeadersRow();

        foreach (var category in group.ModifierCategories)
        {
            var definitions = groupMods
                .Where(definition => string.Equals(definition.Category, category, StringComparison.OrdinalIgnoreCase))
                .Where(definition => StashUtilityCatalog.MatchesTabletSearch(definition, _stashUtilityModSearch))
                .OrderBy(definition => definition.TierSortOrder)
                .ThenBy(definition => definition.Name)
                .ToArray();
            if (definitions.Length == 0) continue;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextColored(new Vector4(0.35f, 0.78f, 1f, 1f),
                StashUtilityCatalog.TabletCategoryHeading(group, category));
            foreach (var definition in definitions)
                DrawTabletModRule(settings, definition);
        }

        ImGui.EndTable();
    }

    private static void DrawWaystoneModRule(StashUtilitySettings settings, StashUtilityModDefinition definition)
    {
        ImGui.PushID(definition.Id);
        ImGui.TextUnformatted(definition.Name);
        var required = ContainsSetting(settings.GoodWaystoneMods, definition.Id);
        if (ImGui.Checkbox("Required", ref required))
        {
            SetSetting(settings.GoodWaystoneMods, definition.Id, required);
            if (required) SetSetting(settings.BadWaystoneMods, definition.Id, false);
        }
        ImGui.SameLine();
        var great = ContainsSetting(settings.GreatWaystoneMods, definition.Id);
        if (ImGui.Checkbox("GREAT", ref great))
        {
            SetSetting(settings.GreatWaystoneMods, definition.Id, great);
            if (great) SetSetting(settings.BadWaystoneMods, definition.Id, false);
        }
        ImGui.SameLine();
        var bad = ContainsSetting(settings.BadWaystoneMods, definition.Id);
        if (ImGui.Checkbox("BAD", ref bad))
        {
            SetSetting(settings.BadWaystoneMods, definition.Id, bad);
            if (bad)
            {
                SetSetting(settings.GoodWaystoneMods, definition.Id, false);
                SetSetting(settings.GreatWaystoneMods, definition.Id, false);
            }
        }
        ImGui.SameLine();
        ImGui.TextDisabled(definition.Id);
        ImGui.PopID();
    }

    private static void DrawTabletModRule(StashUtilitySettings settings, StashUtilityModDefinition definition)
    {
        ImGui.PushID(definition.Id);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        var tierRgb = ParseHexColor(definition.TierColor);
        TextColoredUnformatted(new Vector4(tierRgb, 1f), definition.Name);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(definition.Id);

        ImGui.TableSetColumnIndex(1);
        ImGui.TextColored(new Vector4(tierRgb, 1f), definition.MarketTier);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Advisory market tier based on current Runes of Aldur listings and reported sales.");

        ImGui.TableSetColumnIndex(2);
        var required = ContainsSetting(settings.GoodTabletMods, definition.Id);
        if (ImGui.Checkbox("##required", ref required))
        {
            SetSetting(settings.GoodTabletMods, definition.Id, required);
            if (required) SetSetting(settings.BadTabletMods, definition.Id, false);
        }

        ImGui.TableSetColumnIndex(3);
        var great = ContainsSetting(settings.GodTabletMods, definition.Id);
        if (ImGui.Checkbox("##great", ref great))
        {
            SetSetting(settings.GodTabletMods, definition.Id, great);
            if (great) SetSetting(settings.BadTabletMods, definition.Id, false);
        }

        ImGui.TableSetColumnIndex(4);
        var bad = ContainsSetting(settings.BadTabletMods, definition.Id);
        if (ImGui.Checkbox("##bad", ref bad))
        {
            SetSetting(settings.BadTabletMods, definition.Id, bad);
            if (bad)
            {
                SetSetting(settings.GoodTabletMods, definition.Id, false);
                SetSetting(settings.GodTabletMods, definition.Id, false);
            }
        }

        ImGui.TableSetColumnIndex(5);
        if ((required || great) && definition.MaxRoll > definition.MinRoll)
        {
            var minimum = settings.TabletMinimumRolls.TryGetValue(definition.Id, out var configured)
                ? configured
                : definition.MinRoll;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("##minimum", ref minimum, definition.MinRoll, definition.MaxRoll, "%.0f"))
                settings.TabletMinimumRolls[definition.Id] = minimum;
        }
        else
        {
            ImGui.TextDisabled(definition.MaxRoll > definition.MinRoll ? "Enable Required/GREAT" : "Fixed");
        }
        ImGui.PopID();
    }

    private static bool ContainsSetting(IReadOnlyList<string> list, string value)
        => list.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));

    private static void SetSetting(List<string> list, string value, bool enabled)
    {
        list.RemoveAll(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
        if (enabled) list.Add(value);
    }

    private static int DrawStashUtilityStyleCombo(string label, int value)
    {
        var names = new[] { "Solid", "Dashed", "Dotted" };
        value = Math.Clamp(value, 0, names.Length - 1);
        ImGui.SetNextItemWidth(UiW(9f));
        if (ImGui.BeginCombo(label, names[value]))
        {
            for (var i = 0; i < names.Length; i++)
                if (ImGui.Selectable(names[i], value == i)) value = i;
            ImGui.EndCombo();
        }
        return value;
    }

    private static int DrawStashUtilityCornerCombo(string label, int value)
    {
        var names = new[] { "Top-left", "Top-right", "Bottom-left", "Bottom-right" };
        value = Math.Clamp(value, 0, names.Length - 1);
        ImGui.SetNextItemWidth(UiW(11f));
        if (ImGui.BeginCombo(label, names[value]))
        {
            for (var i = 0; i < names.Length; i++)
                if (ImGui.Selectable(names[i], value == i)) value = i;
            ImGui.EndCombo();
        }
        return value;
    }

    private static string DrawStashUtilityColor(string label, string value)
    {
        var color = ParseHexColor(value);
        if (ImGui.ColorEdit3(label, ref color, ImGuiColorEditFlags.NoInputs)) value = FormatHexColor3(color);
        return value;
    }

    private void DrawLootTrackerTab(RadarSettings s, RenderContext? ctx)
    {
        var lt = s.LootTracker;

        var generalOpen = ImGuiTheme.BeginAccordionSection("LootTrackerGeneral", "General", defaultOpen: true);
        if (generalOpen)
        {
            var enabled = lt.Enabled;
            if (ImGui.Checkbox("Enable Loot Tracker", ref enabled)) lt.Enabled = enabled;

            if (LootTrackerDrawPolicy.HasRecoveryControl(
                    _lootTrackerHidden,
                    LootTrackerBarMode.None,
                    settingsOpen: true))
            {
                ImGui.SameLine();
                if (ImGui.Button("Show tracker"))
                    _lootTrackerHidden = false;
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                    ImGui.SetTooltip("Restore the Loot Tracker window.");
            }

            if (ImGui.Button("New session"))
                _enqueue(_newLootSession);

            var kills = lt.ShowKills;
            if (ImGui.Checkbox("Show kill counts", ref kills)) lt.ShowKills = kills;

            var currency = Math.Clamp(lt.DisplayCurrency, LootTrackerSettings.CurrencyAuto, LootTrackerSettings.CurrencyChaos);
            if (lt.ShowPricesInDivineOnly && currency == LootTrackerSettings.CurrencyExalted)
                currency = LootTrackerSettings.CurrencyDivine;
            var currencyNames = new[] { "Auto", "Divine", "Exalted", "Chaos" };
            ImGui.SetNextItemWidth(UiW(9f));
            if (ImGui.BeginCombo("Display currency", currencyNames[currency]))
            {
                for (var i = 0; i < currencyNames.Length; i++)
                {
                    if (!ImGui.Selectable(currencyNames[i], currency == i)) continue;
                    lt.DisplayCurrency = i;
                    lt.ShowPricesInDivineOnly = false;
                    currency = i;
                }
                ImGui.EndCombo();
            }

            DrawHotkeyRow(s, "lootDetailsHotkey", "Loot details / next page",
                SettingHints.Hotkeys.LootDetails);

            var history = lt.HistorySize;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Run history", ref history, 1, 200)) lt.HistorySize = Math.Clamp(history, 1, 200);

            var maxSessions = lt.MaxSessions;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Saved sessions", ref maxSessions, 1, 200)) lt.MaxSessions = Math.Clamp(maxSessions, 1, 200);

            var live = ctx?.LootTracker ?? LootTrackerView.Empty;
            ImGui.TextUnformatted($"Current: {(live.OnMap ? live.MapName : "inactive")}");
            ImGui.TextUnformatted($"Inventory: {(live.OnMap && live.InventoryReadable ? $"{live.InventoryItemCount} entries" : "inactive")}");
            ImGui.TextUnformatted($"Gold: {(live.OnMap ? live.TotalGoldText : "inactive")}");
        }
        ImGuiTheme.EndAccordionSection(generalOpen);

        var barsOpen = ImGuiTheme.BeginAccordionSection("LootTrackerBars", "Bars", defaultOpen: true);
        if (barsOpen)
        {
            var keepVisible = lt.KeepVisibleAfterRun;
            if (ImGui.Checkbox("Keep visible after runs", ref keepVisible))
                lt.KeepVisibleAfterRun = keepVisible;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Keep the paused session summary visible in towns and hideouts.");

            var right = lt.BarOnRight;
            if (ImGui.Checkbox("Anchor on right", ref right)) lt.BarOnRight = right;

            var opacity = lt.BarOpacity;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Opacity", ref opacity, 0f, 1f, "%.2f")) lt.BarOpacity = Math.Clamp(opacity, 0f, 1f);

            var bottom = lt.BarBottomOffset;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Bottom offset", ref bottom, 0f, 160f, "%.0f px")) lt.BarBottomOffset = Math.Clamp(bottom, 0f, 500f);

            var scale = lt.UiScale;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("UI scale", ref scale, 0.5f, 3f, "%.2f")) lt.UiScale = Math.Clamp(scale, 0.5f, 3f);

        }
        ImGuiTheme.EndAccordionSection(barsOpen);

        var toastsOpen = ImGuiTheme.BeginAccordionSection("LootTrackerToasts", "Pickup notifications", defaultOpen: false);
        if (toastsOpen)
        {
            var show = lt.ShowPickupToasts;
            if (ImGui.Checkbox("Show pickup toasts", ref show)) lt.ShowPickupToasts = show;

            var min = lt.NotifyMinEx;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Minimum value", ref min, 0f, 500f, "%.0f Ex")) lt.NotifyMinEx = Math.Clamp(min, 0f, 100000f);

            var dur = lt.NotifyDurationSec;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Duration", ref dur, 1f, 6f, "%.1f s")) lt.NotifyDurationSec = Math.Clamp(dur, 1f, 6f);
        }
        ImGuiTheme.EndAccordionSection(toastsOpen);

        var pricingOpen = ImGuiTheme.BeginAccordionSection("LootTrackerPricing", "Price Source", defaultOpen: true);
        if (pricingOpen)
        {
            if (ImGui.RadioButton("poe2scout", lt.PriceSource == PoeNinjaPriceFetcher.SourcePoe2Scout))
                lt.PriceSource = PoeNinjaPriceFetcher.SourcePoe2Scout;
            ImGui.SameLine();
            if (ImGui.RadioButton("poe.ninja", lt.PriceSource == PoeNinjaPriceFetcher.SourcePoeNinja))
                lt.PriceSource = PoeNinjaPriceFetcher.SourcePoeNinja;

            var league = lt.League ?? "";
            ImGui.SetNextItemWidth(UiW(14f));
            if (ImGui.InputText("League", ref league, 96))
                lt.League = string.IsNullOrWhiteSpace(league) ? "Runes of Aldur" : league.Trim();

            var refresh = lt.RefreshIntervalMin;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Refresh interval (min)", ref refresh, 1, 120))
                lt.RefreshIntervalMin = Math.Clamp(refresh, 1, 120);

            if (ImGui.Button("Refresh prices now"))
            {
                var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "LootTracker");
                Directory.CreateDirectory(dir);
                PoeNinjaPriceFetcher.Configure(Math.Clamp(lt.PriceSource, 0, 1), lt.League ?? "", Math.Max(1, lt.RefreshIntervalMin));
                PoeNinjaPriceFetcher.ForceRefresh(dir, ignoreCooldown: true);
            }

            ImGui.SameLine();
            if (PoeNinjaPriceFetcher.IsFetching)
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.2f, 1f), "Loading...");
            else if (PoeNinjaPriceFetcher.LastFetchUtc > DateTime.MinValue)
            {
                var mins = Math.Max(0, (int)(DateTime.UtcNow - PoeNinjaPriceFetcher.LastFetchUtc).TotalMinutes);
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), $"{PoeNinjaPriceFetcher.LoadedItemCount} items | {mins} min ago");
            }

            ImGui.TextUnformatted($"Chaos/Divine: {PoeNinjaPriceFetcher.GetChaosPerDivine():0.##}");
            ImGui.TextUnformatted($"Divine/Exalted: {PoeNinjaPriceFetcher.DivineToExaltedRate:0.##}");
        }
        ImGuiTheme.EndAccordionSection(pricingOpen);
    }

    private void DrawRitualTab(RadarSettings s, RenderContext? ctx)
    {
        var r = s.Ritual;

        var pricingOpen = ImGuiTheme.BeginAccordionSection("RitualPricing", "Pricing",
            defaultOpen: true);
        if (pricingOpen)
        {
            bool show = r.ShowOverlay;
            if (ImGui.Checkbox("In-game price labels", ref show)) r.ShowOverlay = show;
            ImGuiTheme.Tooltip(SettingHints.Ritual.ShowOverlay);

            bool pricesWin = r.ShowPricesWindow;
            if (ImGui.Checkbox("Ritual prices window", ref pricesWin)) r.ShowPricesWindow = pricesWin;
            ImGuiTheme.Tooltip(SettingHints.Ritual.ShowPricesWindow);

            var source = Math.Clamp(r.PriceSource, 0, 1);
            ImGui.SetNextItemWidth(UiW(9f));
            if (ImGui.Combo("Price source", ref source, "poe.ninja\0poe2scout\0"))
                r.PriceSource = source;

            var league = r.League ?? "";
            ImGui.SetNextItemWidth(UiW(14f));
            if (ImGui.InputText("League", ref league, 96))
                r.League = league;

            var leagues = LeagueProvider.Leagues.ToArray();
            if (leagues.Length > 0)
            {
                var current = Array.FindIndex(leagues, l => string.Equals(l, r.League, StringComparison.OrdinalIgnoreCase));
                if (current < 0) current = 0;
                ImGui.SetNextItemWidth(UiW(14f));
                if (ImGui.BeginCombo("Known leagues", leagues[current]))
                {
                    foreach (var option in leagues)
                    {
                        var selected = string.Equals(option, r.League, StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(option, selected))
                            r.League = option;
                    }
                    ImGui.EndCombo();
                }
            }

            if (ImGui.Button("Reload leagues"))
                LeagueProvider.ForceReload();
            ImGui.SameLine();
            if (ImGui.Button("Refresh prices"))
            {
                var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "RitualHelper");
                Directory.CreateDirectory(dir);
                PoeNinjaPriceFetcher.Configure(Math.Clamp(r.PriceSource, 0, 1), r.League ?? "", Math.Max(1, r.RefreshIntervalMin));
                PoeNinjaPriceFetcher.ForceRefresh(dir, ignoreCooldown: true);
            }

            var refresh = Math.Max(1, r.RefreshIntervalMin);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Refresh minutes", ref refresh, 1, 60))
                r.RefreshIntervalMin = refresh;

            var currency = Math.Clamp(r.DisplayCurrency, 0, 2);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.Combo("Display currency", ref currency, "Divine\0Exalted\0Chaos\0"))
                r.DisplayCurrency = currency;

            var minEx = r.MinDisplayExalted;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Min display value", ref minEx, 0f, 500f, "%.0f Ex"))
                r.MinDisplayExalted = Math.Clamp(minEx, 0f, 100000f);
        }
        ImGuiTheme.EndAccordionSection(pricingOpen);

        var alertOpen = ImGuiTheme.BeginAccordionSection("RitualAlerts", "Alerts",
            defaultOpen: true);
        if (alertOpen)
        {
            bool enabled = r.PlayValueAlert;
            if (ImGui.Checkbox("Enable alert", ref enabled)) r.PlayValueAlert = enabled;

            var alertMin = r.AlertMinDivine;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Alert from", ref alertMin, 0.1f, 50f, "%.3f Divine"))
                r.AlertMinDivine = Math.Clamp(alertMin, 0.001f, 1000f);

            var sound = Math.Clamp(r.AlertSound, 0, 4);
            ImGui.SetNextItemWidth(UiW(9f));
            if (ImGui.Combo("Sound", ref sound, "Asterisk\0Exclamation\0Hand\0Question\0Beep\0"))
                r.AlertSound = sound;
            ImGui.SameLine();
            if (ImGui.Button("Test"))
                AlertSoundPlayer.Play(sound);
        }
        ImGuiTheme.EndAccordionSection(alertOpen);

        var styleOpen = ImGuiTheme.BeginAccordionSection("RitualStyle", "Label style",
            defaultOpen: false);
        if (styleOpen)
        {
            var scale = r.PriceFontScale;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Font scale", ref scale, 0.5f, 2.5f, "%.2f"))
                r.PriceFontScale = Math.Clamp(scale, 0.4f, 4f);

            var ox = r.PriceOffsetX;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Offset X", ref ox, -80f, 80f, "%.0f"))
                r.PriceOffsetX = Math.Clamp(ox, -500f, 500f);

            var oy = r.PriceOffsetY;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Offset Y", ref oy, -80f, 80f, "%.0f"))
                r.PriceOffsetY = Math.Clamp(oy, -500f, 500f);

            var col = ParseHexColor(r.PriceTextColor);
            if (ImGui.ColorEdit3("Text color", ref col, ImGuiColorEditFlags.NoInputs))
                r.PriceTextColor = FormatHexColor3(col);
        }
        ImGuiTheme.EndAccordionSection(styleOpen);

        var diagnosticsOpen = ImGuiTheme.BeginAccordionSection("RitualDiagnostics", "Diagnostics",
            defaultOpen: false);
        if (diagnosticsOpen)
        {
            bool diagnose = r.DiagnosePricing;
            if (ImGui.Checkbox("Diagnose pricing", ref diagnose)) r.DiagnosePricing = diagnose;

            bool debug = r.DebugMode;
            if (ImGui.Checkbox("Debug mode", ref debug)) r.DebugMode = debug;

            bool bfs = r.ForceBfsFallback;
            if (ImGui.Checkbox("Force BFS fallback", ref bfs)) r.ForceBfsFallback = bfs;

            var fetchAge = PoeNinjaPriceFetcher.LastFetchUtc == DateTime.MinValue
                ? "never"
                : $"{Math.Max(0, (DateTime.UtcNow - PoeNinjaPriceFetcher.LastFetchUtc).TotalSeconds):F0}s ago";
            ImGui.TextUnformatted($"Loaded prices: {PoeNinjaPriceFetcher.LoadedItemCount}");
            ImGui.TextUnformatted($"Fetching: {(PoeNinjaPriceFetcher.IsFetching ? "yes" : "no")}");
            ImGui.TextUnformatted($"Last fetch: {fetchAge}");
            ImGui.TextUnformatted($"Labels this frame: {ctx?.RitualLabels.Length ?? 0}");
            ImGui.TextUnformatted($"Window rows: {ctx?.RitualPanelRows.Length ?? 0}");
            ImGui.TextUnformatted(ctx?.RitualShopOpen == true ? "Shop: open" : "Shop: closed");
        }
        ImGuiTheme.EndAccordionSection(diagnosticsOpen);
    }

    private void DrawRunecraftTab(RadarSettings s, RenderContext? ctx)
    {
        var rc = s.Runecraft;

        ImGui.TextWrapped("GameHelper RunecraftHelper port: while the in-game Runeshape Combinations " +
                          "panel is open, the poe.ninja Exalted price is drawn on the right edge of " +
                          "each visible reward row. The reward name remains the game's localized text.");
        ImGui.Spacing();

        var panelOpen = ImGuiTheme.BeginAccordionSection("RunecraftPanel", "Combinations panel",
            defaultOpen: true);
        if (panelOpen)
        {
            bool show = rc.ShowOverlay;
            if (ImGui.Checkbox("Show price overlay", ref show)) rc.ShowOverlay = show;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.ShowOverlay);

            var colorMode = Math.Clamp(rc.ColorMode, 0, 2);
            ImGui.SetNextItemWidth(UiW(12f));
            if (ImGui.Combo("Price color", ref colorMode, "Off\0Relative (vs median)\0Absolute (Ex thresholds)\0"))
                rc.ColorMode = colorMode;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.ColorMode);

            var ox = rc.OverlayXOffset;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Price X offset", ref ox, -400f, 400f, "%.0f px"))
                rc.OverlayXOffset = ox;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.OverlayXOffset);

            bool locked = rc.HighlightLockedRecipe;
            if (ImGui.Checkbox("Highlight locked recipe", ref locked)) rc.HighlightLockedRecipe = locked;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.HighlightLockedRecipe);
        }
        ImGuiTheme.EndAccordionSection(panelOpen);

        var pricingOpen = ImGuiTheme.BeginAccordionSection("RunecraftPricing", "Pricing",
            defaultOpen: true);
        if (pricingOpen)
        {
            var source = Math.Clamp(rc.PriceSource, 0, 1);
            ImGui.SetNextItemWidth(UiW(9f));
            if (ImGui.Combo("Price source", ref source, "poe.ninja\0poe2scout\0"))
                rc.PriceSource = source;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.PriceSource);

            var league = rc.League ?? "";
            ImGui.SetNextItemWidth(UiW(14f));
            if (ImGui.InputText("League", ref league, 96))
                rc.League = league;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.League);

            var leagues = LeagueProvider.Leagues.ToArray();
            if (leagues.Length > 0)
            {
                var current = Array.FindIndex(leagues,
                    option => string.Equals(option, rc.League, StringComparison.OrdinalIgnoreCase));
                if (current < 0) current = 0;
                ImGui.SetNextItemWidth(UiW(14f));
                if (ImGui.BeginCombo("Known leagues", leagues[current]))
                {
                    foreach (var option in leagues)
                    {
                        var selected = string.Equals(option, rc.League, StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(option, selected))
                            rc.League = option;
                    }
                    ImGui.EndCombo();
                }
            }

            var refresh = Math.Max(1, rc.RefreshIntervalMin);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Refresh minutes", ref refresh, 1, 60))
                rc.RefreshIntervalMin = refresh;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.RefreshIntervalMin);

            if (ImGui.Button("Refresh prices"))
            {
                var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "RitualHelper");
                Directory.CreateDirectory(dir);
                PoeNinjaPriceFetcher.Configure(Math.Clamp(rc.PriceSource, 0, 1), rc.League ?? "", refresh);
                PoeNinjaPriceFetcher.ForceRefresh(dir, ignoreCooldown: true);
            }
        }
        ImGuiTheme.EndAccordionSection(pricingOpen);

        var mapOpen = ImGuiTheme.BeginAccordionSection("RunecraftMap", "Map labels",
            defaultOpen: false);
        if (mapOpen)
        {
            bool map = rc.ShowMapLabels;
            if (ImGui.Checkbox("Draw value on large map", ref map)) rc.ShowMapLabels = map;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.ShowMapLabels);

            bool hide = rc.HideMapValueWhenPanelOpen;
            if (ImGui.Checkbox("Hide map values when panel open", ref hide)) rc.HideMapValueWhenPanelOpen = hide;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.HideMapValueWhenPanelOpen);

            var scale = rc.MapValueScaleMultiplier;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Map value scale", ref scale, 0.1f, 3f, "%.2f"))
                rc.MapValueScaleMultiplier = Math.Clamp(scale, 0.1f, 3f);
            ImGuiTheme.Tooltip(SettingHints.Runecraft.MapValueScaleMultiplier);

            var mapX = rc.MapValueXOffset;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Map offset X", ref mapX, -200f, 200f, "%.0f px"))
                rc.MapValueXOffset = Math.Clamp(mapX, -200f, 200f);
            ImGuiTheme.Tooltip(SettingHints.Runecraft.MapValueXOffset);

            var mapY = rc.MapValueYOffset;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Map offset Y", ref mapY, -200f, 200f, "%.0f px"))
                rc.MapValueYOffset = Math.Clamp(mapY, -200f, 200f);
            ImGuiTheme.Tooltip(SettingHints.Runecraft.MapValueYOffset);
        }
        ImGuiTheme.EndAccordionSection(mapOpen);

        var monoOpen = ImGuiTheme.BeginAccordionSection("RunecraftMonolith", "Monolith window",
            defaultOpen: false);
        if (monoOpen)
        {
            bool win = rc.ShowMonolithWindow;
            if (ImGui.Checkbox("Show monolith rewards window", ref win)) rc.ShowMonolithWindow = win;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.ShowMonolithWindow);

            bool debugWindow = rc.ShowMonolithDebugWindow;
            if (ImGui.Checkbox("Show monolith debug window", ref debugWindow))
                rc.ShowMonolithDebugWindow = debugWindow;
            bool autoWin = rc.AutoShowMonolithWithGamepad;
            if (ImGui.Checkbox("Auto-open with controller", ref autoWin)) rc.AutoShowMonolithWithGamepad = autoWin;
            ImGuiTheme.Tooltip(SettingHints.Runecraft.AutoShowMonolithWithGamepad);

            var minReward = rc.MonolithRewardsMinExalted;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Min candidate value", ref minReward, 0f, 100f, "%.1f ex"))
                rc.MonolithRewardsMinExalted = Math.Clamp(minReward, 0f, 100000f);
            ImGuiTheme.Tooltip(SettingHints.Runecraft.MonolithRewardsMinExalted);

            var highlight = rc.MonolithHighlightThreshold;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Highlight best from", ref highlight, 0f, 500f, "%.0f ex"))
                rc.MonolithHighlightThreshold = Math.Clamp(highlight, 0f, 100000f);
            ImGuiTheme.Tooltip(SettingHints.Runecraft.MonolithHighlightThreshold);

            ImGui.TextUnformatted($"Panel labels: {ctx?.RunecraftLabels.Length ?? 0}");
            ImGui.TextUnformatted($"Monolith rows: {ctx?.RunecraftMonolithRows.Length ?? 0}");
            ImGui.TextUnformatted(GamepadInput.IsConnected(s.GamepadUserIndex)
                ? "Controller: connected · controller UI branch enabled"
                : "Controller: not connected · controller UI branch ready");
        }
        ImGuiTheme.EndAccordionSection(monoOpen);

        var expeditionOpen = ImGuiTheme.BeginAccordionSection(
            "RunecraftExpedition", "Expedition", defaultOpen: true);
        if (expeditionOpen)
        {
            var enabled = rc.ShowExpeditionPlanner;
            ImGui.TextDisabled("Explosive-chain route planner");
            if (ImGui.Checkbox("Show route planner", ref enabled)) rc.ShowExpeditionPlanner = enabled;
            ImGui.TextDisabled("The Expedition Planner window appears while a live detonator is detected.");

            var showMap = rc.ShowExpeditionRouteOnMap;
            if (ImGui.Checkbox("Numbered route on map + minimap", ref showMap)) rc.ShowExpeditionRouteOnMap = showMap;

            var showWorld = rc.ShowExpeditionNextPlacementWorld;
            if (ImGui.Checkbox("Next placement in world", ref showWorld)) rc.ShowExpeditionNextPlacementWorld = showWorld;

            var manual = Math.Clamp(rc.ExpeditionManualCharges, 1, 64);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Manual charge fallback", ref manual, 1, 20)) rc.ExpeditionManualCharges = manual;

            var minMonolith = rc.ExpeditionMonolithMinExalted;
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderFloat("Min monolith value", ref minMonolith, 0f, 100f, "%.1f ex"))
                rc.ExpeditionMonolithMinExalted = Math.Max(0f, minMonolith);

            var minMarkers = Math.Clamp(rc.ExpeditionMinMarkersPerSpareCharge, 1, 3);
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.SliderInt("Min markers / spare charge", ref minMarkers, 1, 3))
                rc.ExpeditionMinMarkersPerSpareCharge = minMarkers;

            if (ImGui.TreeNode("Target weights"))
            {
                rc.ExpeditionTinyMarkerWeight = DrawExpeditionWeightSlider("Tiny marker", rc.ExpeditionTinyMarkerWeight, 0f, 100f);
                rc.ExpeditionWhiteMarkerWeight = DrawExpeditionWeightSlider("White marker", rc.ExpeditionWhiteMarkerWeight, 0f, 150f);
                rc.ExpeditionMagicMarkerWeight = DrawExpeditionWeightSlider("Magic marker", rc.ExpeditionMagicMarkerWeight, 0f, 200f);
                rc.ExpeditionGoldMarkerWeight = DrawExpeditionWeightSlider("Gold marker", rc.ExpeditionGoldMarkerWeight, 0f, 300f);
                rc.ExpeditionLogbookMarkerWeight = DrawExpeditionWeightSlider("Logbook marker", rc.ExpeditionLogbookMarkerWeight, 0f, 500f);
                rc.ExpeditionPreferredRelicWeight = DrawExpeditionWeightSlider("Preferred remnant", rc.ExpeditionPreferredRelicWeight, 0f, 300f);
                rc.ExpeditionDangerousRelicPenalty = DrawExpeditionWeightSlider("Danger penalty", rc.ExpeditionDangerousRelicPenalty, 0f, 500f);
                ImGui.SeparatorText("Grand reward markers");
                ImGui.TextDisabled("GameHelper profile weights · 0 ignores a reward type");
                DrawExpeditionRewardWeight(rc, "RewardChestCurrencyRare", "Currency (rare)", 40f);
                DrawExpeditionRewardWeight(rc, "RewardChestCurrency", "Currency", 25f);
                DrawExpeditionRewardWeight(rc, "RewardChestRunes", "Runes", 1f);
                DrawExpeditionRewardWeight(rc, "RewardChestArmour", "Armour", 10f);
                DrawExpeditionRewardWeight(rc, "RewardChestWeapons", "Weapons", 10f);
                DrawExpeditionRewardWeight(rc, "RewardChestUnique", "Uniques", 1f);
                DrawExpeditionRewardWeight(rc, "RewardChestGems", "Gems", 1f);
                DrawExpeditionRewardWeight(rc, "RewardChestMaps", "Maps / Waystones", 1f);
                DrawExpeditionRewardWeight(rc, "RewardChestTrinkets", "Trinkets", 1f);
                DrawExpeditionRewardWeight(rc, "RewardChestBreach", "Breach", 1f);
                DrawExpeditionRewardWeight(rc, "RewardChestRitual", "Ritual", 1f);
                DrawExpeditionRewardWeight(rc, "RewardChestExpedition", "Expedition", 1f);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Build-breaking remnant mods"))
            {
                ImGui.TextWrapped("Select immunities your build cannot run. They reduce the remnant's net route-anchor weight.");
                var hazards = new (string Id, string Label)[]
                {
                    ("ExpeditionRelicDownsideImmunePhysicalDamage", "Immune to Physical"),
                    ("ExpeditionRelicDownsideImmuneFireDamage", "Immune to Fire"),
                    ("ExpeditionRelicDownsideImmuneColdDamage", "Immune to Cold"),
                    ("ExpeditionRelicDownsideImmuneLightningDamage", "Immune to Lightning"),
                    ("ExpeditionRelicDownsideImmuneChaosDamage", "Immune to Chaos"),
                    ("ExpeditionRelicDownsideCannotBeCrit", "Cannot be Crit"),
                    ("ExpeditionRelicDownsideCannotBeLeechedFrom", "Cannot be Leeched From"),
                    ("ExpeditionRelicDownsideGrantNoFlaskCharges", "No Flask Charges"),
                };
                rc.ExpeditionDangerousRelicMods ??= [];
                foreach (var (id, label) in hazards)
                {
                    var selected = rc.ExpeditionDangerousRelicMods.Contains(id, StringComparer.OrdinalIgnoreCase);
                    if (!ImGui.Checkbox(label, ref selected)) continue;
                    rc.ExpeditionDangerousRelicMods.RemoveAll(v => string.Equals(v, id, StringComparison.OrdinalIgnoreCase));
                    if (selected) rc.ExpeditionDangerousRelicMods.Add(id);
                }
                ImGui.TreePop();
            }

            if (ctx?.ExpeditionPlanner is { Active: true } expedition)
            {
                ImGui.Separator();
                ImGui.TextUnformatted($"Live: {expedition.Total - expedition.Placed}/{expedition.Total} charges · {expedition.TargetCount} targets");
                ImGui.TextDisabled(expedition.Status);
            }
            else
            {
                ImGui.TextDisabled("Live: waiting for an Expedition detonator");
            }
        }
        ImGuiTheme.EndAccordionSection(expeditionOpen);

        var diagnosticsOpen = ImGuiTheme.BeginAccordionSection("RunecraftDiagnostics", "Diagnostics",
            defaultOpen: false);
        if (diagnosticsOpen)
        {
            var fetchAge = PoeNinjaPriceFetcher.LastFetchUtc == DateTime.MinValue
                ? "never"
                : $"{Math.Max(0, (DateTime.UtcNow - PoeNinjaPriceFetcher.LastFetchUtc).TotalSeconds):F0}s ago";
            ImGui.TextUnformatted($"Loaded prices: {PoeNinjaPriceFetcher.LoadedItemCount}");
            ImGui.TextUnformatted($"Fetching: {(PoeNinjaPriceFetcher.IsFetching ? "yes" : "no")}");
            ImGui.TextUnformatted($"Last fetch: {fetchAge}");
            ImGui.TextUnformatted($"Panel labels: {ctx?.RunecraftLabels.Length ?? 0}");
            ImGui.TextUnformatted($"Nearby monoliths: {ctx?.RunecraftMonolithRows.Length ?? 0}");
            ImGui.TextDisabled("Panel discovery checks both keyboard/mouse and controller UI roots.");
        }
        ImGuiTheme.EndAccordionSection(diagnosticsOpen);
    }

    private static float DrawExpeditionWeightSlider(string label, float value, float min, float max)
    {
        var next = value;
        ImGui.SetNextItemWidth(UiW(8f));
        return ImGui.SliderFloat(label, ref next, min, max, "%.0f")
            ? Math.Clamp(next, min, max)
            : value;
    }

    private static void DrawExpeditionRewardWeight(
        RunecraftSettings settings,
        string icon,
        string label,
        float defaultValue)
    {
        settings.ExpeditionRewardWeights ??= new Dictionary<string, float>(StringComparer.Ordinal);
        var value = settings.ExpeditionRewardWeights.TryGetValue(icon, out var configured)
            ? configured
            : defaultValue;
        var next = DrawExpeditionWeightSlider(label, value, 0f, 500f);
        if (MathF.Abs(next - value) > 0.001f)
            settings.ExpeditionRewardWeights[icon] = next;
    }

    private void DrawAtlasTab(RadarSettings s)
    {
        ImGui.TextWrapped("Atlas2 QoL: every memory node, search, path categories, badges, Uncharted + ritual line.");
        ImGui.Spacing();

        var q = s.AtlasSearchQuery ?? "";
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("##atlasSearch", "Search maps (e.g. Moor skies). Comma = OR…", ref q, 256))
            s.AtlasSearchQuery = q;
        ImGuiTheme.Tooltip(SettingHints.Atlas.SearchQuery);
        if (ImGui.Button("Clear search")) s.AtlasSearchQuery = "";
        ImGuiTheme.Tooltip("Clear search and show the full atlas evenly.");

        ImGui.Spacing();
        bool sn = s.AtlasShowNames;
        if (ImGui.Checkbox("Show names", ref sn)) s.AtlasShowNames = sn;
        ImGuiTheme.Tooltip(SettingHints.Atlas.ShowNames);
        bool hc = s.AtlasHideCompletedMaps;
        if (ImGui.Checkbox("Hide completed", ref hc)) s.AtlasHideCompletedMaps = hc;
        ImGuiTheme.Tooltip(SettingHints.Atlas.HideCompleted);
        bool hna = s.AtlasHideNotAccessibleMaps;
        if (ImGui.Checkbox("Hide not-accessible", ref hna)) s.AtlasHideNotAccessibleMaps = hna;
        ImGuiTheme.Tooltip(SettingHints.Atlas.HideNotAccessible);

        bool displayOpen = ImGuiTheme.BeginAccordionSection("AtlasDisplay", "Display",
            "Badges, biome borders, content icons.");
        if (displayOpen)
        {
            bool bb = s.AtlasShowBiomeBorders;
            if (ImGui.Checkbox("Biome borders", ref bb)) s.AtlasShowBiomeBorders = bb;
            ImGuiTheme.Tooltip(SettingHints.Atlas.BiomeBorders);
            bool cb = s.AtlasShowContentBadges;
            if (ImGui.Checkbox("Content badges", ref cb)) s.AtlasShowContentBadges = cb;
            ImGuiTheme.Tooltip(SettingHints.Atlas.ContentBadges);
            bool sci = s.AtlasShowContentIcons;
            if (ImGui.Checkbox("Content icons", ref sci)) s.AtlasShowContentIcons = sci;
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShowContentIcons);
            bool cc = s.AtlasShowContentCount;
            if (ImGui.Checkbox("Content count pips", ref cc)) s.AtlasShowContentCount = cc;
            ImGuiTheme.Tooltip(SettingHints.Atlas.ContentCount);
            float atlasIcon = s.AtlasIconScale;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Icon scale", ref atlasIcon, 0.5f, 3f, "%.2f"))
                s.AtlasIconScale = Math.Clamp(atlasIcon, 0.25f, 4f);
            ImGuiTheme.Tooltip(SettingHints.Atlas.IconScale);
        }
        ImGuiTheme.EndAccordionSection(displayOpen);

        bool pathOpen = ImGuiTheme.BeginAccordionSection("AtlasPathCategories", "Path categories",
            "Atlas2 MapGroups — toggle Draw path per category.", defaultOpen: true);
        if (pathOpen)
        {
            var manualColor = ParseHexColor(s.AtlasManualRouteColor);
            if (ImGui.ColorEdit3("Manual route color", ref manualColor, ImGuiColorEditFlags.NoInputs))
                s.AtlasManualRouteColor = FormatHexColor3(manualColor);
            ImGuiTheme.Tooltip(SettingHints.Atlas.ManualRouteColor);

            var searchColor = ParseHexColor(s.AtlasSearchRouteColor);
            if (ImGui.ColorEdit3("Search route color", ref searchColor, ImGuiColorEditFlags.NoInputs))
                s.AtlasSearchRouteColor = FormatHexColor3(searchColor);
            ImGuiTheme.Tooltip(SettingHints.Atlas.SearchRouteColor);

            float opacity = s.AtlasRouteOpacity;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Route opacity", ref opacity, 0.1f, 1f, "%.2f"))
                s.AtlasRouteOpacity = Math.Clamp(opacity, 0.1f, 1f);
            ImGuiTheme.Tooltip(SettingHints.Atlas.RouteOpacity);

            float globalThickness = s.AtlasRouteLineThickness;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Manual/search thickness", ref globalThickness, 1f, 8f, "%.1f"))
                s.AtlasRouteLineThickness = Math.Clamp(globalThickness, 1f, 8f);
            ImGuiTheme.Tooltip(SettingHints.Atlas.RouteThickness);

            bool chevrons = s.AtlasShowRouteChevrons;
            if (ImGui.Checkbox("Route chevrons", ref chevrons))
                s.AtlasShowRouteChevrons = chevrons;
            ImGuiTheme.Tooltip(SettingHints.Atlas.RouteChevrons);

            if (chevrons)
            {
                float spacing = s.AtlasRouteChevronSpacing;
                ImGui.SetNextItemWidth(180f);
                if (ImGui.SliderFloat("Chevron spacing", ref spacing, 8f, 80f, "%.0f"))
                    s.AtlasRouteChevronSpacing = Math.Clamp(spacing, 8f, 80f);
                ImGuiTheme.Tooltip(SettingHints.Atlas.ChevronSpacing);
            }

            ImGui.SeparatorText("Target categories");
            for (var gi = 0; gi < s.AtlasRouteGroups.Count; gi++)
            {
                var g = s.AtlasRouteGroups[gi];
                ImGui.PushID("atlas2cat_" + gi);
                bool draw = g.DrawPaths;
                if (ImGui.Checkbox($"##draw{gi}", ref draw)) g.DrawPaths = draw;
                ImGuiTheme.Tooltip(SettingHints.Atlas.RouteGroupDraw);
                ImGui.SameLine();
                var open = ImGui.TreeNode($"{g.Name}##tree{gi}");
                if (open)
                {
                    var groupColor = ParseHexColor(g.Color);
                    if (ImGui.ColorEdit3("Category color", ref groupColor, ImGuiColorEditFlags.NoInputs))
                    {
                        var colorHex = FormatHexColor3(groupColor);
                        g.Color = colorHex;
                        foreach (var entry in g.Entries)
                            entry.Color = colorHex;
                    }
                    ImGuiTheme.Tooltip(SettingHints.Atlas.RouteGroupColor);

                    float th = g.LineThickness;
                    ImGui.SetNextItemWidth(140f);
                    if (ImGui.SliderFloat("Thickness", ref th, 1f, 8f, "%.1f"))
                        g.LineThickness = Math.Clamp(th, 1f, 8f);
                    ImGuiTheme.Tooltip(SettingHints.Atlas.RouteGroupThickness);
                    int hops = g.MaxHops;
                    ImGui.SetNextItemWidth(140f);
                    if (ImGui.SliderInt("Max hops", ref hops, 0, 200))
                        g.MaxHops = Math.Clamp(hops, 0, 500);
                    ImGuiTheme.Tooltip(SettingHints.Atlas.RouteMaxHops);
                    for (var ei = 0; ei < g.Entries.Count; ei++)
                    {
                        var e = g.Entries[ei];
                        ImGui.PushID(ei);
                        bool ed = e.DrawPath;
                        if (ImGui.Checkbox("##draw", ref ed))
                            e.DrawPath = ed;
                        ImGuiTheme.Tooltip(SettingHints.Atlas.RouteEntryDraw);
                        ImGui.SameLine();
                        var entryColor = ParseHexColor(e.Color);
                        if (ImGui.ColorEdit3("##color", ref entryColor, ImGuiColorEditFlags.NoInputs))
                            e.Color = FormatHexColor3(entryColor);
                        ImGuiTheme.Tooltip(SettingHints.Atlas.RouteEntryColor);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(e.Name.Length > 0 ? e.Name : e.Match);
                        ImGui.PopID();
                    }
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
        }
        ImGuiTheme.EndAccordionSection(pathOpen);

        bool uwOpen = ImGuiTheme.BeginAccordionSection("AtlasUncharted", "Uncharted Waters",
            "Fog ships and hover leylines.");
        if (uwOpen)
        {
            bool ships = s.AtlasShowShipsInFog;
            if (ImGui.Checkbox("Show ships in fog", ref ships)) s.AtlasShowShipsInFog = ships;
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShowShipsInFog);
            bool ley = s.AtlasShowUnchartedLeylines;
            if (ImGui.Checkbox("Show leylines on hover", ref ley)) s.AtlasShowUnchartedLeylines = ley;
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShowUnchartedLeylines);
            float shipSz = s.AtlasShipIconSize;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Ship icon size", ref shipSz, 16f, 96f, "%.0f"))
                s.AtlasShipIconSize = Math.Clamp(shipSz, 8f, 128f);
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShipIconSize);
            bool rumours = s.AtlasShowIslandRumours;
            if (ImGui.Checkbox("Show all island rumours + tiers", ref rumours))
                s.AtlasShowIslandRumours = rumours;
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShowIslandRumours);
            bool rumourBadges = s.AtlasShowIslandRumourBadges;
            if (ImGui.Checkbox("Show island-count badges", ref rumourBadges))
                s.AtlasShowIslandRumourBadges = rumourBadges;
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShowIslandRumourBadges);
            var priorityFilter = s.AtlasIslandRumourPriorityFilter ?? "";
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint(
                    "##islandRumourPriority",
                    "Priority rumours or destinations (| separated)…",
                    ref priorityFilter,
                    512))
                s.AtlasIslandRumourPriorityFilter = priorityFilter;
            ImGuiTheme.Tooltip(SettingHints.Atlas.IslandRumourPriorityFilter);
            var priorityColor = s.AtlasIslandRumourPriorityColor ?? "#FFD166";
            ImGui.SetNextItemWidth(120f);
            if (ImGui.InputText("Priority color", ref priorityColor, 16))
                s.AtlasIslandRumourPriorityColor = priorityColor;
            ImGuiTheme.Tooltip(SettingHints.Atlas.IslandRumourPriorityColor);
        }
        ImGuiTheme.EndAccordionSection(uwOpen);

        bool ritOpen = ImGuiTheme.BeginAccordionSection("AtlasRitualLine", "Ritual atlas line",
            "Predict Rite mods while ritual line mode is open (separate from Ritual shop prices).");
        if (ritOpen)
        {
            bool pred = s.AtlasShowRitualPrediction;
            if (ImGui.Checkbox("Show ritual prediction", ref pred)) s.AtlasShowRitualPrediction = pred;
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShowRitualPrediction);
            bool plan = s.AtlasShowRitualPlanner;
            if (ImGui.Checkbox("Show ritual planner window", ref plan)) s.AtlasShowRitualPlanner = plan;
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShowRitualPlanner);
            var rf = s.AtlasRitualRewardFilter ?? "";
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##ritFilter", "Reward filter (comma)…", ref rf, 256))
                s.AtlasRitualRewardFilter = rf;
            ImGuiTheme.Tooltip(SettingHints.Atlas.RitualRewardFilter);
        }
        ImGuiTheme.EndAccordionSection(ritOpen);

        var langs = new[] { "english", "french", "german", "japanese", "korean", "portuguese", "russian", "spanish", "thai", "traditional chinese" };
        var lang = string.IsNullOrWhiteSpace(s.AtlasLanguage) ? "english" : s.AtlasLanguage;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("Language", lang))
        {
            foreach (var option in langs)
            {
                var selected = string.Equals(lang, option, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(option, selected)) s.AtlasLanguage = option;
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGuiTheme.Tooltip(SettingHints.Atlas.Language);
    }


    private void DrawAtlasTargetFarming(RadarSettings s)
    {
        ImGui.SetNextItemWidth(-UiW(8f));
        ImGui.InputTextWithHint("##atlasTargetGroupName", "group name", ref _atlasTargetGroupName, 128);
        ImGui.SameLine();
        if (ImGui.Button("Add content group"))
        {
            s.AtlasRouteGroups.Add(new AtlasRouteGroupSettings
            {
                Name = string.IsNullOrWhiteSpace(_atlasTargetGroupName) ? "Content Group" : _atlasTargetGroupName.Trim(),
                DrawPaths = true,
                LineThickness = 1f,
            });
            _atlasTargetGroupName = "";
        }
        ImGuiTheme.Tooltip("Create a target-farming group. Add content like Great Beast, or add specific maps.");

        for (var gi = 0; gi < s.AtlasRouteGroups.Count; gi++)
        {
            var group = s.AtlasRouteGroups[gi];
            ImGui.PushID($"atlasTargetGroup_{gi}");
            var title = group.Locked ? $"{group.Name} (built-in)" : group.Name.Length > 0 ? group.Name : "Content Group";
            if (group.Locked && s.AtlasRouteGroups.Count == 1)
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);

            if (ImGui.TreeNodeEx("##group", ImGuiTreeNodeFlags.DefaultOpen, title))
            {
                DrawAtlasTargetGroupBody(s, group, gi);
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
    }

    private void DrawAtlasTargetGroupBody(RadarSettings s, AtlasRouteGroupSettings group, int groupIndex)
    {
        bool draw = group.DrawPaths;
        if (ImGui.Checkbox("Draw paths", ref draw)) group.DrawPaths = draw;
        ImGuiTheme.Tooltip(SettingHints.Atlas.RouteGroupDraw);

        var thick = group.LineThickness;
        ImGui.SetNextItemWidth(UiW(7f));
        if (ImGui.SliderFloat("Line thickness", ref thick, 1f, 8f, "%.3f"))
            group.LineThickness = Math.Clamp(thick, 1f, 8f);
        ImGuiTheme.Tooltip(SettingHints.Atlas.RouteGroupThickness);

        if (!group.Locked)
        {
            ImGui.SameLine();
            if (ImGui.Button("Delete group"))
            {
                s.AtlasRouteGroups.RemoveAt(groupIndex);
                return;
            }
            DrawAtlasAddTargetPickers(group, groupIndex);
        }

        for (var i = 0; i < group.Entries.Count; i++)
        {
            var entry = group.Entries[i];
            ImGui.PushID($"entry_{i}_{entry.Match}");
            DrawAtlasTargetEntryRow(group, entry, i);
            ImGui.PopID();
        }
    }

    private void DrawAtlasAddTargetPickers(AtlasRouteGroupSettings group, int groupIndex)
    {
        ImGui.SetNextItemWidth(UiW(8f));
        if (ImGui.BeginCombo($"##atlasAddContent_{groupIndex}", "Add content..."))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint($"##atlasAddContentFilter_{groupIndex}", "filter...", ref _atlasAddContentFilter, 64);
            var filter = _atlasAddContentFilter.Trim();
            foreach (var content in AtlasCatalog.Shared.MapContents
                         .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                         .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Entries.Any(e => AtlasRouteMatchEquals(e.Match, "content", content.Name))) continue;
                var label = AtlasCatalog.Shared.LocalizedContentName(content.Name, _settings.AtlasLanguage);
                var desc = AtlasCatalog.Shared.LocalizedContentDescription(content.Name, _settings.AtlasLanguage) ?? content.Description;
                if (filter.Length > 0
                    && !label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !content.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !desc.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                var display = string.IsNullOrWhiteSpace(desc) ? label : $"{label} - {TruncateAtlasText(desc, 72)}";
                if (ImGui.Selectable($"{display}##content_{content.Name}"))
                {
                    group.Entries.Add(new AtlasRouteEntrySettings
                    {
                        Name = content.Name,
                        Match = "content:" + content.Name,
                        Color = "#FFD933",
                        DrawPath = true,
                        MaxHops = 0,
                    });
                    _atlasAddContentFilter = "";
                }
            }
            ImGui.EndCombo();
        }
        ImGuiTheme.Tooltip("Add a content target such as Great Beast. Routes draw to matching Atlas nodes.");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(UiW(8f));
        if (ImGui.BeginCombo($"##atlasAddMap_{groupIndex}", "Add map..."))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint($"##atlasAddMapFilter_{groupIndex}", "filter...", ref _atlasAddMapFilter, 64);
            var filter = _atlasAddMapFilter.Trim();
            foreach (var map in AtlasCatalog.Shared.Maps
                         .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.First())
                         .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(map.Name)) continue;
                if (group.Entries.Any(e => AtlasRouteMatchEquals(e.Match, "name", map.Name))) continue;
                var localized = AtlasCatalog.Shared.LocalizedMapName(map.Code, _settings.AtlasLanguage);
                if (filter.Length > 0
                    && !localized.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !map.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !map.Code.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ImGui.Selectable($"{localized}##map_{map.Name}"))
                {
                    group.Entries.Add(new AtlasRouteEntrySettings
                    {
                        Name = map.Name,
                        Match = "name:" + map.Name,
                        Color = "#FFD933",
                        DrawPath = true,
                        MaxHops = 0,
                    });
                    _atlasAddMapFilter = "";
                }
            }
            ImGui.EndCombo();
        }
        ImGuiTheme.Tooltip("Add a map target by display name.");
    }

    private void DrawAtlasTargetEntryRow(AtlasRouteGroupSettings group, AtlasRouteEntrySettings entry, int entryIndex)
    {
        bool on = entry.DrawPath;
        if (ImGui.Checkbox("##draw", ref on)) entry.DrawPath = on;
        ImGuiTheme.Tooltip(SettingHints.Atlas.RouteEntryDraw);

        ImGui.SameLine();
        var col = ParseHexColor(entry.Color);
        if (ImGui.ColorEdit3("##color", ref col, ImGuiColorEditFlags.NoInputs))
            entry.Color = FormatHexColor3(col);
        ImGuiTheme.Tooltip(SettingHints.Atlas.RouteEntryColor);

        ImGui.SameLine();
        int maxHops = entry.MaxHops;
        ImGui.SetNextItemWidth(UiW(3f));
        if (ImGui.DragInt("##hops", ref maxHops, 0.1f, 0, 1000))
            entry.MaxHops = Math.Clamp(maxHops, 0, 1000);
        ImGuiTheme.Tooltip(SettingHints.Atlas.RouteMaxHops);

        ImGui.SameLine();
        var label = AtlasRouteEntryDisplayName(entry);
        if (DrawAtlasContentIconInline(AtlasRouteEntryContentName(entry)))
            ImGui.SameLine();
        ImGui.TextUnformatted(label);
        var desc = entry.Match.StartsWith("content:", StringComparison.OrdinalIgnoreCase)
            ? AtlasCatalog.Shared.LocalizedContentDescription(AtlasRouteEntryContentName(entry), _settings.AtlasLanguage)
            : null;
        if (!string.IsNullOrWhiteSpace(desc)) ImGuiTheme.Tooltip(desc);

        if (!group.Locked)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
                group.Entries.RemoveAt(entryIndex);
        }
    }

    private bool DrawAtlasContentIconInline(string contentName)
    {
        if (string.IsNullOrWhiteSpace(contentName)) return false;
        var basename = AtlasCatalog.Shared.ContentIconBasename(contentName);
        if (string.IsNullOrWhiteSpace(basename)) return false;
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "atlas-content-icons", basename + ".png");
        if (!_textures.TryGet(this, path, out var tex)) return false;
        var h = ImGui.GetFontSize();
        var w = h * tex.Width / Math.Max(1f, tex.Height);
        ImGui.Image(tex.Id, new NumVec2(w, h));
        return true;
    }

    private static string AtlasRouteEntryContentName(AtlasRouteEntrySettings entry)
        => entry.Match.StartsWith("content:", StringComparison.OrdinalIgnoreCase)
            ? entry.Match["content:".Length..]
            : entry.Name;

    private string AtlasRouteEntryDisplayName(AtlasRouteEntrySettings entry)
    {
        if (entry.Match.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
        {
            var name = entry.Match["content:".Length..];
            return AtlasCatalog.Shared.LocalizedContentName(name, _settings.AtlasLanguage);
        }
        if (entry.Match.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
        {
            var code = entry.Match["id:".Length..];
            return AtlasCatalog.Shared.LocalizedMapName(code, _settings.AtlasLanguage);
        }
        if (entry.Match.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            return entry.Match["name:".Length..];
        return string.IsNullOrWhiteSpace(entry.Name) ? entry.Match : entry.Name;
    }

    private static bool AtlasRouteMatchEquals(string match, string kind, string value)
        => string.Equals(match, $"{kind}:{value}", StringComparison.OrdinalIgnoreCase);

    private static string TruncateAtlasText(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";

    private void SeedAtlasCitadelDefaults(RadarSettings s)
    {
        var catalog = _ctx?.AtlasTagCatalog;
        if (catalog is null) return;
        foreach (var e in catalog)
        {
            if (e.Kind != "map" || !e.Key.Contains("Citadel", StringComparison.OrdinalIgnoreCase)) continue;
            if (!AtlasListContains(s.AtlasHighlightTags, e.Key)) s.AtlasHighlightTags.Add(e.Key);
            if (!AtlasListContains(s.AtlasArrowTags, e.Key)) s.AtlasArrowTags.Add(e.Key);
            s.AtlasHighlightColors[e.Key] = "#e0b341";
        }
    }

    private static bool AtlasListContains(List<string> list, string key)
    {
        foreach (var t in list)
            if (string.Equals(t, key, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void AtlasToggleList(List<string> list, string key, bool on)
    {
        var idx = -1;
        for (var i = 0; i < list.Count; i++)
            if (string.Equals(list[i], key, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        if (on && idx < 0) list.Add(key);
        else if (!on && idx >= 0) list.RemoveAt(idx);
    }

    private void SaveSettings()
    {
        lock (_settingsLock)
        {
            _settings.Save();
            _settingsAutoSave.NoteSaved(JsonSerializer.Serialize(_settings));
        }
    }

    private void MarkSettingsDirty(RadarSettings s)
        => _settingsAutoSave.Touch(JsonSerializer.Serialize(s), Stopwatch.GetTimestamp());

    private void FlushSettingsNow() => FlushSettingsAutoSave(force: true);

    private void FlushSettingsAutoSave(bool force = false)
    {
        if (!_settingsAutoSave.ShouldFlush(Stopwatch.GetTimestamp(), force)) return;
        SaveSettings();
    }

    private void DrawHotkeysSection(RadarSettings s)
    {
        bool open = ImGuiTheme.BeginAccordionSection("Hotkeys", "Hotkeys",
            "Keyboard + Xbox (when gamepad hotkeys enabled).");
        if (open)
        {
            bool gp = s.GamepadHotkeysEnabled;
            if (ImGui.Checkbox("Gamepad hotkeys", ref gp)) s.GamepadHotkeysEnabled = gp;
            ImGuiTheme.Tooltip(SettingHints.Hotkeys.GamepadEnabled);

            int gi = s.GamepadUserIndex;
            ImGui.SetNextItemWidth(120f);
            if (ImGui.SliderInt("Pad slot", ref gi, 0, 3)) s.GamepadUserIndex = gi;
            ImGuiTheme.Tooltip(SettingHints.Hotkeys.PadSlot);

            ImGui.TextDisabled("Bind: click Bind, then press a key or controller button.");

            DrawHotkeyRow(s, "hideEntityHotkey", "Never show under cursor", SettingHints.Hotkeys.HideEntity);
            DrawHotkeyRow(s, "trackEntityHotkey", "Inspect under cursor", SettingHints.Hotkeys.TrackEntity);
            DrawHotkeyRow(s, "toggleRenderingHotkey", "Toggle rendering", SettingHints.Hotkeys.ToggleRendering);
            DrawHotkeyRow(s, "autoPathToggleHotkey", "Auto-path toggle", SettingHints.Hotkeys.AutoPathToggle);
            DrawHotkeyRow(s, "addNearestPathHotkey", "Add nearest path", SettingHints.Hotkeys.AddNearestPath);
            DrawHotkeyRow(s, "clearPathsHotkey", "Clear paths", SettingHints.Hotkeys.ClearPaths);
            DrawHotkeyRow(s, "autoFlaskToggleHotkey", "Auto-flask toggle", SettingHints.Hotkeys.AutoFlaskToggle);
            DrawHotkeyRow(s, "atlasPickHotkey", "Atlas tile pick", SettingHints.Hotkeys.AtlasPick);
            DrawHotkeyRow(s, "lootDetailsHotkey", "Loot details / next page", SettingHints.Hotkeys.LootDetails);
            DrawHotkeyRow(s, "toggleSettingsHotkey", "Overlay settings", SettingHints.Hotkeys.ToggleSettings);
            DrawHotkeyRow(s, "openDashboardHotkey", "Open dashboard", SettingHints.Hotkeys.OpenDashboard);
            DrawHotkeyRow(s, "quitHotkey", "Quit overlay", SettingHints.Hotkeys.Quit);
        }
        ImGuiTheme.EndAccordionSection(open);
    }

    private void DrawHotkeyRow(RadarSettings s, string settingKey, string label, string tooltip)
    {
        ImGui.PushID(settingKey);
        ImGui.TextUnformatted(label);
        ImGuiTheme.Tooltip(tooltip);
        ImGui.SameLine(200f);
        var binding = GetHotkey(s, settingKey);
        if (_hotkeyBindTarget == settingKey)
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), "Press key / pad…");
        else
            ImGui.TextUnformatted(HotkeyCodes.DisplayName(binding));
        ImGui.SameLine();
        if (ImGui.Button(_hotkeyBindTarget == settingKey ? "…" : "Bind"))
        {
            _hotkeyBindTarget = settingKey;
            _hotkeyBindArmed = false;
        }
        ImGuiTheme.Tooltip(SettingHints.Hotkeys.Bind);
        ImGui.SameLine();
        if (ImGui.Button("Clear") && binding > 0)
        {
            SetHotkey(s, settingKey, 0);
            if (_hotkeyBindTarget == settingKey) _hotkeyBindTarget = null;
            MarkSettingsDirty(s);
            FlushSettingsNow();
        }
        ImGuiTheme.Tooltip(SettingHints.Hotkeys.Clear);
        ImGui.PopID();
    }

    private static int GetHotkey(RadarSettings s, string key) => key switch
    {
        "hideEntityHotkey" => s.HideEntityHotkey,
        "trackEntityHotkey" => s.TrackEntityHotkey,
        "toggleRenderingHotkey" => s.ToggleRenderingHotkey,
        "autoPathToggleHotkey" => s.AutoPathToggleHotkey,
        "addNearestPathHotkey" => s.AddNearestPathHotkey,
        "clearPathsHotkey" => s.ClearPathsHotkey,
        "autoFlaskToggleHotkey" => s.AutoFlaskToggleHotkey,
        "atlasPickHotkey" => s.AtlasPickHotkey,
        "toggleSettingsHotkey" => s.ToggleSettingsHotkey,
        "openDashboardHotkey" => s.OpenDashboardHotkey,
        "quitHotkey" => s.QuitHotkey,
        "waystoneAlchemyRunHotkey" => s.WaystoneAlchemy.RunHotkey,
        "waystoneAlchemyStopHotkey" => s.WaystoneAlchemy.EmergencyStopHotkey,
        "pickupActivationHotkey" => s.PickupHelper.ActivationHotkey,
        "pickupEmergencyStopHotkey" => s.PickupHelper.EmergencyStopHotkey,
        "pickupShowHiddenHotkey" => s.PickupHelper.ShowHiddenItemsHotkey,
        "lootDetailsHotkey" => s.LootTracker.DetailsHotkey,
        _ => 0,
    };

    private static void SetHotkey(RadarSettings s, string key, int value)
    {
        switch (key)
        {
            case "hideEntityHotkey": s.HideEntityHotkey = value; break;
            case "trackEntityHotkey": s.TrackEntityHotkey = value; break;
            case "toggleRenderingHotkey": s.ToggleRenderingHotkey = value; break;
            case "autoPathToggleHotkey": s.AutoPathToggleHotkey = value; break;
            case "addNearestPathHotkey": s.AddNearestPathHotkey = value; break;
            case "clearPathsHotkey": s.ClearPathsHotkey = value; break;
            case "autoFlaskToggleHotkey": s.AutoFlaskToggleHotkey = value; break;
            case "atlasPickHotkey": s.AtlasPickHotkey = value; break;
            case "toggleSettingsHotkey": s.ToggleSettingsHotkey = value; break;
            case "openDashboardHotkey": s.OpenDashboardHotkey = value; break;
            case "quitHotkey": s.QuitHotkey = value; break;
            case "waystoneAlchemyRunHotkey": s.WaystoneAlchemy.RunHotkey = value; break;
            case "waystoneAlchemyStopHotkey": s.WaystoneAlchemy.EmergencyStopHotkey = value; break;
            case "pickupActivationHotkey": s.PickupHelper.ActivationHotkey = value; break;
            case "pickupEmergencyStopHotkey": s.PickupHelper.EmergencyStopHotkey = value; break;
            case "pickupShowHiddenHotkey": s.PickupHelper.ShowHiddenItemsHotkey = value; break;
            case "lootDetailsHotkey": s.LootTracker.DetailsHotkey = value; break;
        }
    }

    private void PollHotkeyCapture(RadarSettings s)
    {
        if (_hotkeyBindTarget is null) return;

        if (!_hotkeyBindArmed)
        {
            if (IsAnyMouseButtonDown()) return;
            _hotkeyBindArmed = true;
            if (s.GamepadHotkeysEnabled) GamepadInput.ArmBindCapture();
            return;
        }

        if (!TryPollPressedBinding(s.GamepadHotkeysEnabled, out var code)) return;

        if (code == 0x1B)
        {
            _hotkeyBindTarget = null;
            return;
        }

        SetHotkey(s, _hotkeyBindTarget, code);
        _hotkeyBindTarget = null;
        MarkSettingsDirty(s);
        FlushSettingsNow();
    }

    private static bool TryPollPressedBinding(bool gamepadEnabled, out int code)
    {
        if (gamepadEnabled && GamepadInput.TryGetBindPress(out var mask))
        {
            code = HotkeyCodes.EncodeGamepad(mask);
            return true;
        }
        return TryPollPressedVk(out code);
    }

    private static bool IsAnyMouseButtonDown()
    {
        foreach (var vk in VirtualKeyHelper.MouseButtonVks)
            if ((OverlayNative.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        return false;
    }

    private static bool TryPollPressedVk(out int vk)
    {
        foreach (var mb in VirtualKeyHelper.MouseButtonVks)
        {
            if ((OverlayNative.GetAsyncKeyState(mb) & 0x8000) != 0)
            {
                vk = mb;
                return true;
            }
        }

        for (vk = 0x08; vk <= 0xFE; vk++)
        {
            if (vk is 0x0A or 0x0B) continue;
            if ((OverlayNative.GetAsyncKeyState(vk) & 0x8000) == 0) continue;
            return true;
        }

        vk = 0;
        return false;
    }

    // ── Helpers ──

    private static string LegendRowText(LegendEntry row)
    {
        var prefix = row.IsSelected ? $"{row.ColorSlot + 1}. " : "   ";
        var type = row.Target.IsEntity ? "E" : "L";
        var dist = row.IsSelected && row.PathDistance >= 0
            ? $" ~{row.PathDistance:F0}t"
            : row.Distance >= 0 ? $" {row.Distance:F0}c" : "";
        var status = row.Status switch
        {
            NavTargetStatus.Cached when row.Target.IsEntity => " (last seen)",
            NavTargetStatus.NoPath => NoPathStatusSuffix(row.RouteStatus),
            _ => RouteStatusSuffix(row.RouteStatus),
        };
        return $"{prefix}[{type}] {row.Target.Name}{dist}{status}";
    }

    private static string NoPathStatusSuffix(RoutePlanStatus status)
    {
        var suffix = RouteStatusSuffix(status);
        return suffix.Length > 0 ? suffix : " (no path)";
    }

    private static string RouteStatusSuffix(RoutePlanStatus status)
        => status switch
        {
            RoutePlanStatus.Unplanned => "",
            RoutePlanStatus.Planned => "",
            RoutePlanStatus.Planning => " (planning)",
            RoutePlanStatus.WaitingForTerrain => " (no terrain)",
            RoutePlanStatus.TargetUnavailable => " (target gone)",
            RoutePlanStatus.NoWalkableStart => " (bad start)",
            RoutePlanStatus.NoReachableGoal => " (no anchor)",
            RoutePlanStatus.NoPath => " (no path)",
            RoutePlanStatus.Error => " (route error)",
            _ => "",
        };

    private static NumVec2 Project(NumVec2 cell, NumVec2 player, NumVec2 center, float scale, float deltaWorldZ = 0f)
    {
        var d = cell - player;
        var md = MapProjection.GridDeltaToMapDelta(new GameVec2 { X = d.X, Y = d.Y }, scale, deltaWorldZ);
        return new NumVec2(center.X + md.X, center.Y + md.Y);
    }

    private static NumVec2 ProjectWorldToScreen(System.Numerics.Vector3 world, float[] m, float w, float h)
    {
        var cw = world.X * m[3] + world.Y * m[7] + world.Z * m[11] + m[15];
        if (cw <= 0.0001f)
            return new NumVec2(w * 0.5f, h * 0.5f);
        var cx = world.X * m[0] + world.Y * m[4] + world.Z * m[8] + m[12];
        var cy = world.X * m[1] + world.Y * m[5] + world.Z * m[9] + m[13];
        return new NumVec2((cx / cw / 2f + 0.5f) * w, (0.5f - cy / cw / 2f) * h);
    }

    private static uint PathColor(int slot)
    {
        var v = PathColorVec(slot);
        return ImGui.ColorConvertFloat4ToU32(v);
    }

    private static Vector4 PathColorVec(int slot)
    {
        return PathPalette[((slot % PathPalette.Length) + PathPalette.Length) % PathPalette.Length];
    }

    private static (SpriteIconRef? Sprite, string? Shape, float Size, string Color, float Opacity) ResolveEntityDrawStyle(
        Poe2Live.EntityDot e, RadarStyles styles)
    {
        switch (e.Category)
        {
            case Poe2Live.EntityCategory.Monster:
                switch (e.Rarity)
                {
                    case Poe2Live.Rarity.Unique:
                        return (styles.MonsterUnique.Sprite, styles.MonsterUnique.Shape, styles.MonsterUnique.Size,
                            styles.MonsterUnique.Color, styles.MonsterUnique.Opacity);
                    case Poe2Live.Rarity.Rare:
                        return (styles.MonsterRare.Sprite, styles.MonsterRare.Shape, styles.MonsterRare.Size,
                            styles.MonsterRare.Color, styles.MonsterRare.Opacity);
                    case Poe2Live.Rarity.Magic:
                        return (styles.MonsterMagic.Sprite, styles.MonsterMagic.Shape, styles.MonsterMagic.Size,
                            styles.MonsterMagic.Color, styles.MonsterMagic.Opacity);
                    default:
                        return (styles.MonsterNormal.Sprite, styles.MonsterNormal.Shape, styles.MonsterNormal.Size,
                            styles.MonsterNormal.Color, styles.MonsterNormal.Opacity);
                }
            case Poe2Live.EntityCategory.Player:
                return (styles.Player.Sprite, styles.Player.Shape, styles.Player.Size, styles.Player.Color, styles.Player.Opacity);
            case Poe2Live.EntityCategory.Npc:
                return (styles.Npc.Sprite, styles.Npc.Shape, styles.Npc.Size, styles.Npc.Color, styles.Npc.Opacity);
            case Poe2Live.EntityCategory.Chest:
                return e.Rarity == Poe2Live.Rarity.Unique
                    ? (styles.ChestUnique.Sprite, styles.ChestUnique.Shape, styles.ChestUnique.Size, styles.ChestUnique.Color, styles.ChestUnique.Opacity)
                    : (styles.ChestRare.Sprite, styles.ChestRare.Shape, styles.ChestRare.Size, styles.ChestRare.Color, styles.ChestRare.Opacity);
            case Poe2Live.EntityCategory.Transition:
                return (styles.Transition.Sprite, styles.Transition.Shape, styles.Transition.Size, styles.Transition.Color, styles.Transition.Opacity);
            default:
                if (e.Poi)
                    return (styles.Poi.Sprite, styles.Poi.Shape, styles.Poi.Size, styles.Poi.Color, styles.Poi.Opacity);
                return (styles.MonsterNormal.Sprite, styles.MonsterNormal.Shape, EntityRadius(e), EntityColor(e), 0.95f);
        }
    }

    private static string EntityColor(Poe2Live.EntityDot e) => e.Category switch
    {
        Poe2Live.EntityCategory.Monster => e.Rarity switch
        {
            Poe2Live.Rarity.Unique => "#AF6025",
            Poe2Live.Rarity.Rare => "#FFFF77",
            Poe2Live.Rarity.Magic => "#8888FF",
            _ => "#FF4040",
        },
        Poe2Live.EntityCategory.Player => "#4CF2FF",
        Poe2Live.EntityCategory.Npc => "#FFFFFF",
        Poe2Live.EntityCategory.Chest => "#FFCC55",
        Poe2Live.EntityCategory.Transition => "#66FF99",
        _ => "#B0B0B0",
    };

    private static float EntityRadius(Poe2Live.EntityDot e) => e.Category switch
    {
        Poe2Live.EntityCategory.Monster => e.Rarity is Poe2Live.Rarity.Rare or Poe2Live.Rarity.Unique ? 4.4f : 3.2f,
        Poe2Live.EntityCategory.Player => 4.2f,
        Poe2Live.EntityCategory.Npc => 3.8f,
        Poe2Live.EntityCategory.Chest => 3.5f,
        Poe2Live.EntityCategory.Transition => 4.8f,
        _ => 3f,
    };

    private static uint ColorU32(string hex, float opacity)
    {
        if (hex.Length == 7 && hex[0] == '#'
            && byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            return ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, Math.Clamp(opacity, 0f, 1f)));
        return ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, Math.Clamp(opacity, 0f, 1f)));
    }

    private static uint ColorU32(byte r, byte g, byte b, float a)
    {
        return ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, Math.Clamp(a, 0f, 1f)));
    }

    private void DrawRuleSpriteButton(int ruleIndex, DisplayRule rule)
    {
        const float btn = 18f;
        var size = new NumVec2(btn, btn);
        if (!IconAtlas.IsInitialized)
        {
            if (ImGui.Button("?", size))
                _spritePickerRuleIndex = ruleIndex;
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.SpritePickerLoading);
            return;
        }

        if (ImGui.InvisibleButton("##sprbtn", size))
            _spritePickerRuleIndex = ruleIndex;
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.SpritePicker);

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (IconAtlas.TryResolve(rule.Sprite, rule.Shape, out var tex))
            dl.AddImage(tex.TextureId, min, max, tex.UV0, tex.UV1, 0xFFFFFFFF);
        else
            dl.AddRectFilled(min, max, ColorU32(60, 60, 70, 0.9f));
    }

    private void DrawSpritePickerWindow()
    {
        if (_spritePickerRuleIndex < 0 || _displayRules is null) return;
        if (_spritePickerRuleIndex >= _rulesUiCache.Count) { _spritePickerRuleIndex = -1; return; }

        ImGui.SetNextWindowSize(new NumVec2(640, 480), ImGuiCond.FirstUseEver);
        bool open = true;
        if (!ImGui.Begin("Pick icon (icons.png)", ref open, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            if (!open) _spritePickerRuleIndex = -1;
            return;
        }

        if (!IconAtlas.IsInitialized)
        {
            ImGui.TextDisabled("Icon atlas not loaded yet — enter a zone so the overlay can upload icons.png.");
            ImGui.End();
            if (!open) _spritePickerRuleIndex = -1;
            return;
        }

        var cols = IconAtlas.ColCount;
        var rows = IconAtlas.RowCount;
        ImGui.TextDisabled($"Click a cell ({cols}×{rows}, 64px each). SVG shapes are dashboard-only.");
        ImGui.BeginChild("SpriteGrid", new NumVec2(0, 0));

        const float cellPx = 28f;
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                ImGui.PushID(row * cols + col);
                if (ImGui.InvisibleButton("cell", new NumVec2(cellPx, cellPx)))
                {
                    var rule = _rulesUiCache[_spritePickerRuleIndex];
                    var scale = rule.Sprite?.Scale ?? 1.25f;
                    var updated = CloneDisplayRule(rule);
                    updated.Sprite = SpriteIconRef.Cell(col, row, scale);
                    _displayRules.Update(_spritePickerRuleIndex, updated);
                    _spritePickerRuleIndex = -1;
                }
                if (IconAtlas.TryGet(SpriteIconRef.Cell(col, row, 1f), out var tex))
                {
                    var min = ImGui.GetItemRectMin();
                    var max = ImGui.GetItemRectMax();
                    ImGui.GetWindowDrawList().AddImage(tex.TextureId, min, max, tex.UV0, tex.UV1, 0xFFFFFFFF);
                }
                if (col < cols - 1) ImGui.SameLine(0f, 1f);
                ImGui.PopID();
            }
        }

        ImGui.EndChild();
        ImGui.End();
        if (!open) _spritePickerRuleIndex = -1;
    }

    private static DisplayRule CloneDisplayRule(DisplayRule r) => new()
    {
        Enabled = r.Enabled,
        Name = r.Name,
        Categories = new List<string>(r.Categories),
        Match = new List<string>(r.Match),
        Rarity = r.Rarity,
        Reaction = r.Reaction,
        Life = r.Life,
        Chest = r.Chest,
        Poi = r.Poi,
        Hide = r.Hide,
        Shape = r.Shape,
        Color = r.Color,
        Opacity = r.Opacity,
        Size = r.Size,
        Sprite = r.Sprite?.Clone(),
        Label = r.Label,
        HideLabel = r.HideLabel,
        Navigable = r.Navigable,
    };

    private static System.Numerics.Vector3 ParseHexColor(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#'
            && byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
            && byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
            && byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            return new System.Numerics.Vector3(r / 255f, g / 255f, b / 255f);
        return new System.Numerics.Vector3(1f, 1f, 1f);
    }

    private static string FormatHexColor3(System.Numerics.Vector3 v)
        => $"#{(int)(v.X * 255):X2}{(int)(v.Y * 255):X2}{(int)(v.Z * 255):X2}";
}
