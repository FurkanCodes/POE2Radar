using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using ImGuiNET;
using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Diagnostics;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Native;
using POE2Radar.Overlay.Navigation;
using POE2Radar.Overlay.Settings;
using POE2Radar.Overlay.Web;
using NumVec2 = System.Numerics.Vector2;
using GameVec2 = POE2Radar.Core.Game.Vector2;

namespace POE2Radar.Overlay;

public sealed class ImGuiRadarOverlay : ClickableTransparentOverlay.Overlay
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
    private readonly Action _addNearest;
    private readonly Action _clearPaths;
    private readonly TextureRegistry _textures = new();
    private readonly TerrainTextureCache _terrainTextures = new();
    private readonly OverlayRenderMetrics _renderMetrics = new();
    private Func<RenderContext, MapFrame, MapPanLockSample?>? _readMapPanLock;

    private bool _navMenuExpanded;
    private bool _settingsOpen;
    private string _navMenuCorner = "TopLeft";
    private DisplayRules? _displayRules;
    private ZoneEntityOverrides? _zoneOverrides;
    private DisplayRuleEngine? _ruleEngine;
    private HiddenEntities? _hidden;
    private int _rulesUiGeneration = -1;
    private List<DisplayRule> _rulesUiCache = new();
    private string _hidePatternInput = "";
    private string _typeSearch = "";
    private string _ruleSearch = "";
    private string _atlasTagFilter = "";
    private readonly Dictionary<string, string> _atlasGroupMapBuffers = new(StringComparer.Ordinal);
    private readonly List<MapLabelCandidate> _atlasLabelScratch = new(256);
    private readonly Dictionary<string, ScreenPointState> _screenPoints = new(StringComparer.Ordinal);
    private readonly HashSet<string> _screenKeysThisFrame = new(StringComparer.Ordinal);
    private long _renderStamp;
    private long _lastRenderStamp;
    private int _spritePickerRuleIndex = -1;
    private int _selectedRuleIndex = -1;
    private string? _hotkeyBindTarget;
    private bool _hotkeyBindArmed;
    private string _activeSettingsTab = "";
    private bool _settingsPanelWasOpen;
    private readonly SettingsAutoSaveDebouncer _settingsAutoSave = new();
    private int _appliedUiFontSize = -1;
    private string _appliedUiFontPath = "";
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

    private static readonly string[] RuleCategories = ["Monster", "Chest", "Npc", "Object", "Other", "Transition", "Player", "Tile"];

    private static readonly (string Field, string Label, string[] Options)[] RuleConditionFields =
    [
        ("Rarity", "Rarity", ["Normal", "Magic", "Rare", "Unique"]),
        ("Reaction", "Reaction", ["Hostile", "Friendly"]),
        ("Life", "Life", ["Alive", "Dead"]),
        ("Chest", "Chest", ["Opened", "Unopened"]),
        ("Poi", "POI icon", ["Yes", "No"]),
    ];

    private readonly record struct ScreenPointState(NumVec2 Value, long SeenStamp);

    public ImGuiRadarOverlay(Action<Action> enqueue, Action<string> toggleTarget, Action<string> setCorner,
        Action addNearest, Action clearPaths, RadarSettings settings)
        : base("POE2Radar", true, 3840, 2160)
    {
        _enqueue = enqueue;
        _toggleTarget = toggleTarget;
        _setCorner = setCorner;
        _addNearest = addNearest;
        _clearPaths = clearPaths;
        _settings = settings;
        _settingsLock = new object();
        _navMenuCorner = settings.NavMenuCorner;
        VSync = settings.OverlayVSync;
        FPSLimit = Math.Clamp(settings.FpsCap, 15, 360);
    }

    public int OverlayWidth => _width;
    public int OverlayHeight => _height;
    public float LastAtlasDrawMs { get; private set; }

    public OverlayRenderMetrics GetRenderMetrics() => _renderMetrics;

    public void UpdateContext(RenderContext ctx) => _ctx = ctx;

    public void SetMapPanLockReader(Func<RenderContext, MapFrame, MapPanLockSample?>? reader)
        => _readMapPanLock = reader;

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
        FPSLimit = Math.Clamp(settings.FpsCap, 15, 360);
    }

    protected override Task PostInitialized()
    {
        ImGuiTheme.Apply();
        OverlayFonts.Apply(this, _settings);
        _appliedUiFontSize = _settings.UiFontSize;
        _appliedUiFontPath = _settings.UiFontPath;
        _appliedUiGlyphRange = _settings.UiFontGlyphRange;
        return base.PostInitialized();
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

            lock (_boundsLock) { Position = _position; Size = _size; }

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;
            VSync = _settings.OverlayVSync;
            FPSLimit = Math.Clamp(_settings.FpsCap, 15, 360);

            var ctx = _ctx;
            var inGame = ctx is not null && ctx.InGame;

            var dl = ImGui.GetBackgroundDrawList();

            if (inGame && ctx!.Active)
            {
                IconAtlas.EnsureInitialized(this);

                if (ctx.AtlasOpen)
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
                    if (!largeMapOpen && ctx.ShowPathWorld && ctx.ShowGroundWaypoints)
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
                    DrawLootValueOverlays(dl, ctx);
                    if (ctx.ShowPathWorld)
                        DrawPathLabels(dl, ctx);
                    nameplatesMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                }
                else
                {
                    DrawLootValueOverlays(dl, ctx);
                }
            }

            if (inGame)
            {
                var t = Stopwatch.GetTimestamp();
                DrawNavMenu(ctx!);
                navMenuMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                DrawCursorInspect(dl, ctx!);
            }

            if (_settingsOpen)
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

    private void DrawAtlas(ImDrawListPtr dl, RenderContext ctx)
    {
        var sw = Stopwatch.GetTimestamp();
        var W = ctx.WindowWidth;
        var H = ctx.WindowHeight;
        float ccx = W * 0.5f, ccy = H * 0.5f;
        var iconScale = ctx.AtlasIconScale;
        var labelScale = ctx.AtlasLabelScale;

        if (ctx.AtlasRoutes is { Count: > 0 } routeLines)
        {
            foreach (var line in routeLines)
                DrawAtlasRoutePolyline(dl, line.Points, ColorU32(line.Color, 0.95f), ctx.AtlasRouteLineThickness, ctx.AtlasShowRouteChevrons, ctx.AtlasRouteChevronSpacing, line.Hops, line.Label);
        }

        var route = ctx.AtlasRoute;
        if (route is { Count: >= 2 })
        {
            var dark = ColorU32(0, 0, 0, 0.6f);
            var bright = ColorU32(59, 219, 255, 0.95f);
            var pts = route.ToArray();
            for (var i = 1; i < pts.Length; i++) dl.AddLine(pts[i - 1], pts[i], dark, 7f);
            for (var i = 1; i < pts.Length; i++) dl.AddLine(pts[i - 1], pts[i], bright, ctx.AtlasRouteLineThickness);
            for (var i = 1; i < pts.Length - 1; i++) dl.AddCircle(pts[i], 4f, bright, 0, 2f);
            if (ctx.AtlasShowRouteChevrons)
                DrawAtlasRouteChevrons(dl, pts, bright, ctx.AtlasRouteChevronSpacing);
        }

        if (ctx.AtlasStart is { } s) { dl.AddCircleFilled(s, 8f, ColorU32(110, 232, 135, 1f), 12); dl.AddCircleFilled(s, 3f, ColorU32(110, 232, 135, 1f), 8); }
        if (ctx.AtlasEnd is { } e) { dl.AddCircle(e, 11f, ColorU32(224, 179, 65, 1f), 0, 3f); dl.AddCircle(e, 4f, ColorU32(224, 179, 65, 1f), 0, 2f); }
        if (ctx.AtlasCurrent is { } cur) { dl.AddCircleFilled(cur, 6f, ColorU32(231, 76, 60, 0.95f), 16); dl.AddCircle(cur, 9f, ColorU32(0, 0, 0, 0.65f), 16, 2f); }

        if (ctx.AtlasNodes is { Count: > 0 } marks)
        {
            _atlasLabelScratch.Clear();
            var labelCandidates = _atlasLabelScratch;
            foreach (var n in marks)
            {
                var sx = n.ScreenX;
                var sy = n.ScreenY;
                if (!float.IsFinite(sx) || !float.IsFinite(sy)) continue;
                var onScreen = sx >= 0 && sx <= W && sy >= 0 && sy <= H;

                var colHex = string.IsNullOrEmpty(n.Color)
                    ? n.Selected || n.Arrow ? "#3BDBFF"
                    : !n.Visible && ctx.AtlasRevealFog ? "#9CB8D4"
                    : n.HasContent && !n.Visited ? "#FF9E42" : "#6EEB87"
                    : n.Color;
                float opacity = n.Visible
                    ? n.Visited ? 0.75f : 0.95f
                    : ctx.AtlasRevealFog ? 0.95f : n.Selected || n.Arrow ? 0.55f : 0.45f;
                var col = ColorU32(colHex, opacity);

                if (!onScreen)
                {
                    if (ctx.AtlasOffScreenArrows && n.Arrow && n.HighlightLabel is { Length: > 0 } offLabel)
                        DrawAtlasArrow(dl, sx, sy, ccx, ccy, W, H, col, offLabel);
                    continue;
                }

                if (ctx.AtlasTrackedOnly && !n.Selected && !n.Arrow && string.IsNullOrEmpty(n.HighlightLabel) && string.IsNullOrEmpty(n.Color)) continue;
                else if (!ctx.AtlasShowOnScreenNodes && !n.Selected && !n.Arrow && string.IsNullOrEmpty(n.Color)) continue;

                var c = new NumVec2(sx, sy);
                var sprite = SpriteCatalog.AtlasNode(n.IconType, n.Biome);
                var baseSize = n.Selected || n.Arrow ? 11f : 9f;
                var tierMul = n.EndgameTier switch
                {
                    AtlasEndgameTier.Pinnacle => 1.15f,
                    AtlasEndgameTier.KeyHalls => 1.10f,
                    _ => 1f,
                };
                var size = baseSize * tierMul;
                var accentRing = n.EndgameTier is AtlasEndgameTier.Pinnacle or AtlasEndgameTier.KeyHalls;
                if (ctx.AtlasShowBiomeBorders && n.BiomeColor is { Length: > 0 })
                {
                    var halfW = MathF.Max(12f, n.ScreenW * 0.36f * iconScale);
                    var halfH = MathF.Max(12f, n.ScreenH * 0.36f * iconScale);
                    dl.AddRect(new NumVec2(c.X - halfW, c.Y - halfH), new NumVec2(c.X + halfW, c.Y + halfH), ColorU32(n.BiomeColor, 0.82f), 5f, 0, 2f);
                }
                if (IconAtlas.TryResolve(sprite, null, out var tex))
                {
                    var half = size * iconScale;
                    dl.AddImage(tex.TextureId, new NumVec2(c.X - half, c.Y - half), new NumVec2(c.X + half, c.Y + half), tex.UV0, tex.UV1, col);
                    if (n.Selected || n.Arrow || !string.IsNullOrEmpty(n.Color) || accentRing)
                        dl.AddCircle(c, half + 3f, col, 0, accentRing ? 3f : 2f);
                }
                else
                    dl.AddCircleFilled(c, size, col, 12);

                if (ctx.AtlasShowContentCount && n.ContentCount > 0)
                {
                    var pips = Math.Clamp(n.ContentCount, 1, 5);
                    var startX = c.X - (pips - 1) * 3f;
                    for (var pi = 0; pi < pips; pi++)
                        dl.AddCircleFilled(new NumVec2(startX + pi * 6f, c.Y + size + 8f), 2f, ColorU32(255, 255, 255, 0.82f), 8);
                }

                if (ctx.AtlasShowContentBadges && n.Badges is { Count: > 0 })
                    DrawAtlasBadges(dl, c.X, c.Y - size - 20f, n.Badges, col);

                string? chipText = null;
                if (ctx.AtlasShowNames)
                    chipText = n.HighlightLabel ?? n.MapName;
                else if (n.HighlightLabel is { Length: > 0 })
                    chipText = n.HighlightLabel;
                if (chipText is { Length: > 0 } && ctx.AtlasShowNames)
                {
                    if (n.EndgameTier == AtlasEndgameTier.Pinnacle) chipText = "★ " + chipText;
                    else if (n.EndgameTier == AtlasEndgameTier.KeyHalls) chipText = "◆ " + chipText;
                }
                if (chipText is { Length: > 0 })
                {
                    var textCol = n.LabelFg is { Length: > 0 } ? ColorU32(n.LabelFg, 0.96f) : ColorU32(colHex, Math.Min(1f, opacity + 0.05f));
                    var chipCol = n.LabelBg is { Length: > 0 } ? ColorU32(n.LabelBg, 0.95f) : col;
                    var labelPos = new NumVec2(c.X + ctx.AtlasLabelOffsetX, c.Y + ctx.AtlasLabelOffsetY);
                    labelCandidates.Add(new MapLabelCandidate($"atlas:{n.X:F1}:{n.Y:F1}:{chipText}", labelPos, chipText, textCol, chipCol));
                }
            }

            if (labelCandidates.Count > 0)
                DrawAtlasLabelChips(dl, labelCandidates, 0f, 0f, W, H, labelScale);
        }

        LastAtlasDrawMs = (float)Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
    }

    private static void DrawAtlasRoutePolyline(ImDrawListPtr dl, IReadOnlyList<NumVec2> pts, uint col, float thickness, bool chevrons, float spacing, int hops, string label)
    {
        if (pts.Count < 2) return;
        var dark = ColorU32(0, 0, 0, 0.62f);
        for (var i = 1; i < pts.Count; i++) dl.AddLine(pts[i - 1], pts[i], dark, thickness + 4f);
        for (var i = 1; i < pts.Count; i++) dl.AddLine(pts[i - 1], pts[i], col, thickness);
        if (chevrons) DrawAtlasRouteChevrons(dl, pts, col, spacing);
        var start = pts[0];
        var startRadius = MathF.Max(3f, thickness * 1.3f);
        dl.AddCircleFilled(start, startRadius, ColorU32(90, 238, 126, 0.95f), 12);
        dl.AddCircle(start, startRadius, ColorU32(0, 0, 0, 0.72f), 12, MathF.Max(1f, startRadius * 0.35f));
        var target = pts[^1];
        if (hops > 0)
        {
            var text = $"{hops}->";
            var sz = ImGui.CalcTextSize(text);
            var min = new NumVec2(target.X - sz.X - 18f, target.Y - 10f);
            var max = new NumVec2(target.X - 10f, target.Y + 10f);
            dl.AddRectFilled(min, max, ColorU32(13, 13, 13, 0.82f), 5f);
            dl.AddRect(min, max, col, 5f, 0, 1f);
            dl.AddText(new NumVec2(min.X + 5f, min.Y + 2f), col, text);
        }
        if (!string.IsNullOrEmpty(label))
            dl.AddText(new NumVec2(target.X + 12f, target.Y - 9f), ColorU32(235, 235, 235, 0.88f), label);
    }

    private static void DrawAtlasRouteChevrons(ImDrawListPtr dl, IReadOnlyList<NumVec2> pts, uint col, float spacing)
    {
        spacing = Math.Clamp(spacing, 8f, 80f);
        var carry = spacing;
        for (var i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1]; var b = pts[i];
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1f) continue;
            var ux = dx / len; var uy = dy / len;
            var px = -uy; var py = ux;
            for (var d = carry; d < len; d += spacing)
            {
                var cx = a.X + ux * d; var cy = a.Y + uy * d;
                var tip = new NumVec2(cx + ux * 6f, cy + uy * 6f);
                var l = new NumVec2(cx - ux * 4f + px * 4f, cy - uy * 4f + py * 4f);
                var r = new NumVec2(cx - ux * 4f - px * 4f, cy - uy * 4f - py * 4f);
                dl.AddTriangleFilled(tip, l, r, col);
            }
            carry = spacing - ((len - carry) % spacing);
            if (carry <= 0 || carry > spacing) carry = spacing;
        }
    }

    private static void DrawAtlasBadges(ImDrawListPtr dl, float cx, float y, IReadOnlyList<string> badges, uint border)
    {
        var shown = Math.Min(badges.Count, 4);
        var widths = new float[shown];
        var total = 0f;
        for (var i = 0; i < shown; i++)
        {
            widths[i] = ImGui.CalcTextSize(badges[i]).X + 10f;
            total += widths[i] + (i == 0 ? 0 : 4f);
        }
        var x = cx - total * 0.5f;
        for (var i = 0; i < shown; i++)
        {
            var text = badges[i];
            var w = widths[i];
            var min = new NumVec2(x, y);
            var max = new NumVec2(x + w, y + 16f);
            dl.AddRectFilled(min, max, ColorU32(13, 13, 13, 0.78f), 4f);
            dl.AddRect(min, max, border, 4f, 0, 1f);
            dl.AddText(new NumVec2(x + 5f, y + 1f), ColorU32(245, 245, 245, 0.9f), text);
            x += w + 4f;
        }
    }

    private static void DrawAtlasArrow(ImDrawListPtr dl, float sx, float sy, float cx, float cy, float W, float H, uint col, string? label)
    {
        float dx = sx - cx, dy = sy - cy;
        float len = MathF.Sqrt(dx * dx + dy * dy); if (len < 1f) return;
        float ux = dx / len, uy = dy / len;
        const float margin = 46f;
        float tX = MathF.Abs(ux) > 1e-4f ? (W * 0.5f - margin) / MathF.Abs(ux) : 1e9f;
        float tY = MathF.Abs(uy) > 1e-4f ? (H * 0.5f - margin) / MathF.Abs(uy) : 1e9f;
        float t = MathF.Min(tX, tY);
        float ex = cx + ux * t, ey = cy + uy * t;
        float px = -uy, py = ux;
        var tip = new NumVec2(ex + ux * 11f, ey + uy * 11f);
        var bl = new NumVec2(ex - ux * 9f + px * 10f, ey - uy * 9f + py * 10f);
        var br = new NumVec2(ex - ux * 9f - px * 10f, ey - uy * 9f - py * 10f);
        dl.AddTriangleFilled(tip, bl, br, col);
        if (label != null)
            dl.AddText(new NumVec2(ex - ux * 56f - 95f, ey - uy * 18f - 8f), ColorU32(255, 255, 255, 0.9f), label);
    }

    // ── Map overlay ──

    private static bool ShouldDrawLargeMapOverlay(Poe2Live.MapUi map)
        => MapOverlayDrawPolicy.ShouldDrawLargeMap(map);

    private static bool ShouldDrawMinimapOverlay(Poe2Live.MapUi largeMap, Poe2Live.MapUi miniMap)
        => MapOverlayDrawPolicy.ShouldDrawMinimap(largeMap, miniMap);

    private void DrawMap(ImDrawListPtr dl, RenderContext ctx, MapFrame frame)
    {
        var W = ctx.WindowWidth;
        var H = ctx.WindowHeight;
        var (player, center, scale) = ResolveMapDrawState(ctx, frame);
        scale = MathF.Max(0.01f, scale);

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
                if (!DrawTerrainTexture(dl, ctx, terrain, player, center, scale))
                    DrawTerrainEdges(dl, ctx, terrain, player, center, scale);
            }

            if (frame.IsMinimap ? ctx.ShowPathMinimap : ctx.ShowPathMap)
                DrawPathsMap(dl, ctx, frame, player, center, scale);

            var mapLabels = new List<MapLabelCandidate>();
            var clipL = frame.Position.X;
            var clipT = frame.Position.Y;
            var clipR = frame.Position.X + frame.Width;
            var clipB = frame.Position.Y + frame.Height;

            if (ctx.ShowMonsters)
            {
                foreach (var e in ctx.MapEntities)
                {
                    var p = Project(e.Grid, player, center, scale);
                    if (p.X < clipL - 40 || p.Y < clipT - 40 || p.X > clipR + 40 || p.Y > clipB + 40) continue;
                    DrawIconOrShapePacked(dl, p, e.Size, e.Color, e.Sprite, e.Shape, ctx.GlobalIconScale);
                    if (e.Label.Length > 0 && !MapLabelAlreadyPresent(mapLabels, e.Label))
                        mapLabels.Add(new MapLabelCandidate("map:" + e.Key, p, e.Label, e.Color, e.Color));
                }
            }

            foreach (var lm in ctx.MapLandmarks)
            {
                var p = Project(lm.Center, player, center, scale);
                if (p.X < clipL - 40 || p.Y < clipT - 40 || p.X > clipR + 40 || p.Y > clipB + 40) continue;
                DrawIconOrShapePacked(dl, p, lm.Size, lm.Color, lm.Sprite, lm.Shape, ctx.GlobalIconScale);
                if (lm.Label.Length > 0 && !MapLabelAlreadyPresent(mapLabels, lm.Label))
                    mapLabels.Add(new MapLabelCandidate("map:" + lm.Key, p, lm.Label, lm.Color, lm.Color));
            }

            foreach (var s in ctx.MapServerIcons)
            {
                var p = Project(s.Grid, player, center, scale);
                if (p.X < clipL - 40 || p.Y < clipT - 40 || p.X > clipR + 40 || p.Y > clipB + 40) continue;
                DrawIconOrShapePacked(dl, p, s.Size, s.Color, s.Sprite, s.Shape, ctx.GlobalIconScale);
                if (s.Label.Length > 0 && !MapLabelAlreadyPresent(mapLabels, s.Label))
                    mapLabels.Add(new MapLabelCandidate("map:" + s.Key, p, s.Label, s.Color, s.Color));
            }

            if (ctx.ShowMonolithMapLabel && ctx.Monoliths is { Count: > 0 } monoliths)
            {
                foreach (var m in monoliths)
                {
                    var p = Project(m.Grid, player, center, scale);
                    if (p.X < clipL - 40 || p.Y < clipT - 40 || p.X > clipR + 40 || p.Y > clipB + 40) continue;
                    var item = !string.IsNullOrEmpty(m.BestName)
                        ? m.BestName
                        : $"{m.AnchorName} {m.Holes}h";
                    if (item.Length == 0) continue;
                    var text = m.BestEx > 0 ? $"{item}  {m.BestEx:F0}ex" : item;
                    var key = $"mono:{m.Grid.X:F0}:{m.Grid.Y:F0}";
                    mapLabels.Add(new MapLabelCandidate(key, p, text, m.Color, m.Color, 1));
                }
            }

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

            DrawMonolithMapMarkers(dl, ctx, player, center, scale, clipL, clipT, clipR, clipB);

            if (ctx.ShowPlayerBlip)
                DrawIconOrShape(dl, center, ctx.Styles.Player.Size, ctx.Styles.Player.Color, ctx.Styles.Player.Opacity, ctx.Styles.Player.Sprite, ctx.Styles.Player.Shape, ctx.GlobalIconScale);
        }
        finally
        {
            if (clipped)
                dl.PopClipRect();
        }
    }

    private (NumVec2 Player, NumVec2 Center, float Scale) ResolveMapDrawState(RenderContext ctx, MapFrame frame)
    {
        var player = ctx.RawPlayerGrid;
        var center = frame.Center;
        var scale = frame.Scale;
        if (_readMapPanLock?.Invoke(ctx, frame) is not { } live)
            return (player, center, scale);

        player = live.PlayerGrid;
        if (frame.IsMinimap)
        {
            var clipL = frame.Position.X;
            var clipT = frame.Position.Y;
            var clipR = clipL + frame.Width;
            var clipB = clipT + frame.Height;
            (center, scale) = MapFrameBuilder.MiniMapProjection(
                ctx.WindowWidth,
                ctx.WindowHeight,
                live.ShiftX,
                live.ShiftY,
                live.Zoom,
                ctx.ScaleMul,
                clipL,
                clipT,
                clipR,
                clipB);
        }
        else
        {
            (center, scale) = MapFrameBuilder.LargeMapProjection(
                ctx.WindowWidth,
                ctx.WindowHeight,
                live.ShiftX,
                live.ShiftY,
                live.Zoom,
                ctx.OffsetX,
                ctx.OffsetY,
                ctx.ScaleMul);
        }

        return (player, center, scale);
    }

    private bool DrawTerrainTexture(ImDrawListPtr dl, RenderContext ctx, Poe2Live.TerrainData terrain, NumVec2 player, NumVec2 center, float scale)
    {
        if (!_terrainTextures.TryGet(this, _textures, terrain, ctx.AreaHash, ctx.TerrainStyle, out var tex))
            return false;

        var p0 = Project(new NumVec2(0, 0), player, center, scale);
        var p1 = Project(new NumVec2(terrain.Width, 0), player, center, scale);
        var p2 = Project(new NumVec2(terrain.Width, terrain.Height), player, center, scale);
        var p3 = Project(new NumVec2(0, terrain.Height), player, center, scale);

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

    private static void DrawTerrainEdges(ImDrawListPtr dl, RenderContext ctx, Poe2Live.TerrainData terrain, NumVec2 player, NumVec2 center, float scale)
    {
        var data = terrain.Walkable;
        var bytesPerRow = terrain.Width;
        if (data.Length == 0 || bytesPerRow <= 0) return;

        var edgeCol = ColorU32(ctx.TerrainStyle.EdgeColor, ctx.TerrainStyle.EdgeOpacity);
        var interiorCol = ColorU32(ctx.TerrainStyle.InteriorColor, ctx.TerrainStyle.InteriorOpacity);
        var rows = data.Length / bytesPerRow;
        var W = ctx.WindowWidth;
        var H = ctx.WindowHeight;

        var edgeStride = Math.Max(1, (int)MathF.Ceiling(0.8f / MathF.Max(scale, 0.15f)));
        if (edgeStride > 3) edgeStride = 3;
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

                var p = Project(new NumVec2(x, y), player, center, scale);
                if (p.X < -8 || p.Y < -8 || p.X > W + 8 || p.Y > H + 8) continue;

                if (isEdge)
                {
                    if (x + edgeStride < bytesPerRow - 1)
                    {
                        var rightIdx = row + x + edgeStride;
                        if (rightIdx < data.Length && data[rightIdx] != 0)
                        {
                            var pr = Project(new NumVec2(x + edgeStride, y), player, center, scale);
                            if (MathF.Abs(pr.X - p.X) < 80f && MathF.Abs(pr.Y - p.Y) < 80f)
                                dl.AddLine(p, pr, edgeCol, thickness);
                        }
                    }

                    if (y + edgeStride < rows - 1)
                    {
                        var bottomIdx = (y + edgeStride) * bytesPerRow + x;
                        if (bottomIdx < data.Length && data[bottomIdx] != 0)
                        {
                            var pb = Project(new NumVec2(x, y + edgeStride), player, center, scale);
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

    private void DrawPathsMap(ImDrawListPtr dl, RenderContext ctx, MapFrame frame, NumVec2 player, NumVec2 center, float scale)
    {
        var smoothPaths = frame.IsMinimap && ctx.SmoothOverlayMotion;
        foreach (var path in ctx.SelectedPaths)
        {
            var poly = NavigationPathBuilder.BuildDrawPolyline(player, path.Points, path.LiveGoal);
            if (poly.Count < 2) continue;
            var col = PathColor(path.ColorSlot);
            NumVec2? prev = null;
            for (var i = 0; i < poly.Count; i++)
            {
                var (x, y) = poly[i];
                var p = Project(new NumVec2(x, y), player, center, scale);
                if (smoothPaths)
                    p = SmoothScreenPoint($"path:map:{path.TargetId}:{i}", p, ctx.OverlaySmoothingMs, true);
                if (prev is { } a) dl.AddLine(a, p, col, 2.2f);
                prev = p;
            }
        }
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

    private static uint ColorFromU(uint u)
        => ColorU32((byte)((u >> 16) & 0xFF), (byte)((u >> 8) & 0xFF), (byte)(u & 0xFF), ((u >> 24) & 0xFF) / 255f);

    private static readonly uint ColItemHi = ColorU32(255, 204, 51, 1f);
    private static readonly uint ColItemText = ColorU32(235, 235, 235, 1f);
    private static readonly uint ColPanelBg = ColorU32(13, 13, 13, 0.82f);

    private void DrawLootValueOverlays(ImDrawListPtr dl, RenderContext ctx)
    {
        DrawItemLabels(dl, ctx);
        DrawRuneforgePanel(dl, ctx);
        DrawRitualLabels(dl, ctx);
        DrawLootTagLabels(dl, ctx);
        DrawMonolithPanel(dl, ctx);
    }

    private void DrawItemLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.CameraMatrix is not { Length: >= 16 } m || ctx.ItemLabels is not { Count: > 0 } labels) return;
        float W = ctx.WindowWidth, H = ctx.WindowHeight;
        foreach (var it in labels)
        {
            var w = it.World;
            var cw = w.X * m[3] + w.Y * m[7] + w.Z * m[11] + m[15];
            if (cw <= 0.0001f) continue;
            var cx = w.X * m[0] + w.Y * m[4] + w.Z * m[8] + m[12];
            var cy = w.X * m[1] + w.Y * m[5] + w.Z * m[9] + m[13];
            var sx = (cx / cw / 2f + 0.5f) * W;
            var sy = (0.5f - cy / cw / 2f) * H;
            if (sx < 0 || sx > W || sy < 0 || sy > H) continue;

            if (it.ShowName)
            {
                var text = $"{it.Name}\n{it.Value}";
                var halfW = MathF.Max(48f, 4.5f * MathF.Max(it.Name.Length, it.Value.Length + 3));
                const float halfH = 19f;
                dl.AddRectFilled(new NumVec2(sx - halfW, sy - halfH), new NumVec2(sx + halfW, sy + halfH), ColPanelBg);
                if (it.Highlight) dl.AddRect(new NumVec2(sx - halfW, sy - halfH), new NumVec2(sx + halfW, sy + halfH), ColItemHi, 0, 0, 2.5f);
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new NumVec2(sx - halfW + 4f, sy - halfH + 2f),
                    it.Highlight ? ColItemHi : ColItemText, text);
            }
            else
            {
                var halfW = MathF.Max(26f, 4.5f * (it.Value.Length + 1));
                const float halfH = 11f;
                dl.AddRectFilled(new NumVec2(sx - halfW, sy - halfH), new NumVec2(sx + halfW, sy + halfH), ColPanelBg);
                if (it.Highlight) dl.AddRect(new NumVec2(sx - halfW, sy - halfH), new NumVec2(sx + halfW, sy + halfH), ColItemHi, 0, 0, 2f);
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new NumVec2(sx - halfW + 3f, sy - halfH + 1f),
                    it.Highlight ? ColItemHi : ColItemText, it.Value);
            }
        }
    }

    private static void DrawRuneforgePanel(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.RuneforgePanel is not { Rows.Count: > 0 } panel) return;
        const float w = 268f, pad = 6f, lineH = 15f, headH = 17f, titleH = 18f;
        var rows = panel.Rows;
        float h = pad * 2f + titleH + headH + lineH * rows.Count;
        float x = ctx.WindowWidth - w - 10f;
        float y = 90f + EstimateMonolithPanelHeight(ctx);
        dl.AddRectFilled(new NumVec2(x, y), new NumVec2(x + w, y + h), ColPanelBg);
        var font = ImGui.GetFont();
        var fs = font.FontSize;
        float cy = y + pad;
        dl.AddText(font, fs, new NumVec2(x + pad, cy), ColItemText, $"Combinations ({rows.Count})");
        cy += titleH;
        var hdr = panel.BestEx > 0 ? $"{panel.BestEx:F0}ex · {panel.BestLabel}" : panel.BestLabel;
        dl.AddText(font, fs, new NumVec2(x + pad, cy), ColorFromU(panel.HeaderColor), hdr);
        cy += headH;
        foreach (var r in rows)
        {
            dl.AddText(font, fs, new NumVec2(x + pad, cy), ColorFromU(r.Color), $"  {r.Ex,4:F0}  {r.Label}");
            cy += lineH;
        }
    }

    private static float EstimateMonolithPanelHeight(RenderContext ctx)
    {
        if (!ctx.ShowMonolithPanel || ctx.Monoliths is not { Count: > 0 } monos) return 0f;
        const float pad = 6f, lineH = 15f, headH = 17f, titleH = 18f;
        var list = monos.OrderByDescending(m => m.BestEx).Take(6).ToList();
        float h = pad * 2f + titleH;
        foreach (var m in list)
        {
            var rowCount = 0;
            foreach (var r in m.Rewards) if (r.Ex > 0 && rowCount < 3) rowCount++;
            h += headH + lineH * rowCount;
        }
        return h + 8f;
    }

    private static void DrawRitualLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.RitualRewards is not { Count: > 0 } labels) return;
        const float boxH = 20f;
        foreach (var r in labels)
        {
            var boxW = MathF.Max(44f, 7.5f * (r.Text.Length + 1));
            var cx = r.X + r.W * 0.5f;
            var top = r.Y + r.H - boxH;
            dl.AddRectFilled(new NumVec2(cx - boxW * 0.5f, top), new NumVec2(cx + boxW * 0.5f, top + boxH), ColPanelBg);
            if (r.Highlight) dl.AddRect(new NumVec2(cx - boxW * 0.5f, top), new NumVec2(cx + boxW * 0.5f, top + boxH), ColItemHi, 0, 0, 2f);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new NumVec2(cx - boxW * 0.5f + 3f, top + 1f), ColorFromU(r.Color), r.Text);
        }
    }

    private static void DrawLootTagLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.LootTags is not { Count: > 0 } labels) return;
        const float gap = 6f, boxH = 18f;
        foreach (var t in labels)
        {
            var lx = t.X + t.W + gap;
            var cy = t.Y + t.H * 0.5f;
            var boxW = MathF.Max(40f, 7.5f * (t.Value.Length + 1));
            dl.AddRectFilled(new NumVec2(lx, cy - boxH * 0.5f), new NumVec2(lx + boxW, cy + boxH * 0.5f), ColPanelBg);
            if (t.Highlight) dl.AddRect(new NumVec2(lx, cy - boxH * 0.5f), new NumVec2(lx + boxW, cy + boxH * 0.5f), ColItemHi, 0, 0, 2f);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new NumVec2(lx + 4f, cy - boxH * 0.5f + 1f),
                t.Highlight ? ColItemHi : ColItemText, t.Value);
        }
    }

    private static void DrawMonolithPanel(ImDrawListPtr dl, RenderContext ctx)
    {
        if (!ctx.ShowMonolithPanel || ctx.Monoliths is not { Count: > 0 } monos) return;
        var list = monos.OrderByDescending(m => m.BestEx).Take(6).ToList();
        const float w = 248f, pad = 6f, lineH = 15f, headH = 17f, titleH = 18f;
        float h = pad * 2f + titleH;
        foreach (var m in list)
        {
            var rows = 0;
            foreach (var r in m.Rewards) if (r.Ex > 0 && rows < 3) rows++;
            h += headH + lineH * rows;
        }
        float x = ctx.WindowWidth - w - 10f, y = 90f;
        dl.AddRectFilled(new NumVec2(x, y), new NumVec2(x + w, y + h), ColPanelBg);
        var font = ImGui.GetFont();
        var fs = font.FontSize;
        float cy = y + pad;
        dl.AddText(font, fs, new NumVec2(x + pad, cy), ColItemText, $"Monoliths ({monos.Count})");
        cy += titleH;
        foreach (var m in list)
        {
            var hdr = m.BestEx > 0 ? $"{m.BestEx:F0}ex · {m.AnchorName} {m.Holes}h" : $"{m.AnchorName} {m.Holes}h";
            dl.AddText(font, fs, new NumVec2(x + pad, cy), ColorFromU(m.Color), hdr);
            cy += headH;
            var shown = 0;
            foreach (var r in m.Rewards)
            {
                if (r.Ex <= 0 || shown >= 3) continue;
                dl.AddText(font, fs, new NumVec2(x + pad, cy), ColItemText, $"  {r.Ex,4:F0}  {r.Name}");
                cy += lineH; shown++;
            }
        }
    }

    private static void DrawMonolithMapMarkers(ImDrawListPtr dl, RenderContext ctx, NumVec2 player, NumVec2 center, float scale,
        float clipL, float clipT, float clipR, float clipB)
    {
        if (ctx.Monoliths is not { Count: > 0 } monos) return;
        foreach (var m in monos)
        {
            var p = Project(m.Grid, player, center, scale);
            if (p.X < clipL - 40 || p.Y < clipT - 40 || p.X > clipR + 40 || p.Y > clipB + 40) continue;
            dl.AddCircle(p, 9f, ColorFromU(m.Color), 24, 2.4f);
            dl.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new NumVec2(p.X - 4f, p.Y - 8f), ColItemText, m.Holes.ToString());
        }
    }

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

    // ── Path endpoint labels ──

    private void DrawPathLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (!ctx.ShowPathWorld || ctx.SelectedPaths.Length == 0 || ShouldDrawLargeMapOverlay(ctx.Map)) return;
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

        ImGui.SetNextWindowBgAlpha(0.88f);
        ImGui.SetNextWindowPos(cornerPos, ImGuiCond.Always, pivot);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        if (!ImGui.Begin("Nav", flags)) { ImGui.End(); return; }

        var selected = 0;
        foreach (var row in ctx.Legend) if (row.IsSelected) selected++;

        var headerText = selected > 0 ? $"POE2Radar {selected}/8" : "POE2Radar";
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(ImGuiTheme.Accent));
        if (ImGui.Button(_navMenuExpanded ? "v " + headerText : "> " + headerText))
            _navMenuExpanded = !_navMenuExpanded;
        ImGui.PopStyleColor();
        ImGui.SameLine();

        if (ImGui.Button("+"))
            _enqueue(() => _addNearest());
        ImGui.SameLine();

        if (ImGui.Button("-"))
            _enqueue(() => _clearPaths());
        ImGui.SameLine();
        if (ImGui.Button("\u2699"))
            _settingsOpen = !_settingsOpen;

        ImGui.SameLine();
        foreach (var (label, corner) in new[] { ("TL", "TopLeft"), ("TR", "TopRight"), ("BL", "BottomLeft"), ("BR", "BottomRight") })
        {
            var active = corner == _navMenuCorner;
            if (active) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(ImGuiTheme.Accent));
            if (ImGui.Button(label))
            {
                _navMenuCorner = corner;
                _enqueue(() => _setCorner(corner));
            }
            if (active) ImGui.PopStyleColor();
            if (corner != "BottomRight") ImGui.SameLine();
        }

        if (ctx.ShowFpsOverlay || ctx.ShowPerfStats)
            DrawNavPerfStats(ctx);

        if (_navMenuExpanded)
        {
            ImGui.Spacing();
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
    }

    private static void DrawNavPerfStats(RenderContext ctx)
    {
        var p = ctx.Perf;
        ImGui.Spacing();

        TextColoredUnformatted(new Vector4(0.55f, 0.92f, 1f, 1f),
            $"app tick {p.Fps:F0} fps   draw {p.RenderFps:F0} fps   ({p.TickMs:F1} ms)");
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
        const float settingsW = 800f;
        const float settingsH = 600f;

        ImGui.SetNextWindowSizeConstraints(new System.Numerics.Vector2(settingsW, settingsH), new System.Numerics.Vector2(float.MaxValue, float.MaxValue));
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(settingsW, settingsH), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(
            (wW - settingsW) * 0.5f,
            (wH - settingsH) * 0.5f), ImGuiCond.FirstUseEver);

        const ImGuiWindowFlags sflags =
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.MenuBar;

        if (!ImGui.Begin("POE2Radar Settings", ref _settingsOpen, sflags)) { ImGui.End(); return; }

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

        if (!_settingsPanelWasOpen)
            _activeSettingsTab = "";

        if (ImGui.BeginTabBar("SettingsTabs"))
        {
            BeginSettingsTab("Radar", () => DrawRadarTab(s, ctx));
            BeginSettingsTab("Performance", () => DrawPerformanceTab(s));
            BeginSettingsTab("HP Bars", () => DrawHpBarsTab(s));
            BeginSettingsTab("Flask", () => DrawFlaskTab(s));
            BeginSettingsTab("Atlas", () => DrawAtlasTab(s));
            BeginSettingsTab("Hotkeys", () => DrawHotkeysTab(s));
            ImGui.EndTabBar();
        }

        PollHotkeyCapture(s);

        MarkSettingsDirty(s);
        FlushSettingsAutoSave();

        ImGui.End();
    }

    private void BeginSettingsTab(string tabId, Action draw)
    {
        if (!ImGui.BeginTabItem(tabId)) return;

        if (_activeSettingsTab != tabId)
        {
            _activeSettingsTab = tabId;
            ImGuiTheme.CollapseSectionsOnNextDraw = true;
        }

        ImGuiTheme.BeginTabScroll(tabId + "Scroll");
        draw();
        ImGuiTheme.CollapseSectionsOnNextDraw = false;
        ImGuiTheme.EndTabScroll();
        ImGui.EndTabItem();
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

        ImGui.BeginChild("DisplayRulesTable", new NumVec2(0, 240));
        DrawDisplayRulesTable();
        ImGui.EndChild();

        if (_selectedRuleIndex >= 0 && _selectedRuleIndex < _rulesUiCache.Count)
            DrawDisplayRuleDetailPanel(_selectedRuleIndex);

        DrawSpritePickerWindow();
    }

    private void DrawDisplayRulesTable()
    {
        if (_displayRules is null) return;

        var filter = _ruleSearch.Trim();
        var flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY
                    | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("EntityRules", 10, flags)) return;

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2f);
        ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2.2f);
        ImGui.TableSetupColumn("Hide", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2.4f);
        ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2.6f);
        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3.2f);
        ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3.5f);
        ImGui.TableSetupColumn("Alpha", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 4f);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 4f);
        ImGui.TableSetupColumn("Spr", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 3.5f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
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
            ("Name", SettingHints.DisplayRules.ColumnName),
        ]);

        for (var i = 0; i < _rulesUiCache.Count; i++)
        {
            var rule = _rulesUiCache[i];
            if (EntityDisplayHelper.IsPerTypeEntityRule(rule)) continue;
            if (filter.Length > 0 && rule.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            ImGui.TableNextRow();
            ImGui.PushID(i);
            var selected = _selectedRuleIndex == i;

            ImGui.TableNextColumn();
            ImGui.TextUnformatted((i + 1).ToString());
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnOrder);

            ImGui.TableNextColumn();
            bool en = rule.Enabled;
            if (ImGui.Checkbox("##en", ref en) && en != rule.Enabled)
                UpdateRuleAt(i, c => c.Enabled = en);
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnOn);

            ImGui.TableNextColumn();
            bool hide = rule.Hide;
            if (ImGui.Checkbox("##hide", ref hide) && hide != rule.Hide)
                UpdateRuleAt(i, c => c.Hide = hide);
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnHide);

            ImGui.TableNextColumn();
            bool nav = rule.Navigable;
            if (ImGui.Checkbox("##nav", ref nav) && nav != rule.Navigable)
                UpdateRuleAt(i, c => c.Navigable = nav);
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnPath);

            ImGui.TableNextColumn();
            DrawRuleSpriteButton(i, rule);
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnIcon);

            ImGui.TableNextColumn();
            var col = ParseHexColor(rule.Color);
            if (ImGui.ColorEdit3("##col", ref col, ImGuiColorEditFlags.NoInputs))
                UpdateRuleAt(i, c => c.Color = FormatHexColor3(col));
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnColor);

            ImGui.TableNextColumn();
            float op = rule.Opacity;
            if (ImGui.DragFloat("##op", ref op, 0.01f, 0f, 1f, "a%.2f"))
                rule.Opacity = Math.Clamp(op, 0f, 1f);
            if (ImGui.IsItemDeactivatedAfterEdit())
                UpdateRuleAt(i, c => c.Opacity = rule.Opacity);
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnAlpha);

            ImGui.TableNextColumn();
            float sz = rule.Size;
            if (ImGui.DragFloat("##sz", ref sz, 0.1f, 1f, 24f, "sz%.1f"))
                rule.Size = Math.Clamp(sz, 1f, 24f);
            if (ImGui.IsItemDeactivatedAfterEdit())
                UpdateRuleAt(i, c => c.Size = rule.Size);
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnSize);

            ImGui.TableNextColumn();
            float sprScale = rule.Sprite?.Scale ?? 1.25f;
            if (ImGui.DragFloat("##sprsc", ref sprScale, 0.02f, 0.2f, 4f, "s%.2f"))
            {
                rule.Sprite ??= SpriteIconRef.Cell(0, 0, sprScale);
                rule.Sprite.Scale = Math.Clamp(sprScale, 0.2f, 4f);
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
                UpdateRuleAt(i, c => c.Sprite = rule.Sprite?.Clone());
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnSprite);

            ImGui.TableNextColumn();
            var label = rule.Name.Length > 0 ? rule.Name : $"Rule {i + 1}";
            if (ImGui.Selectable(label, selected))
                _selectedRuleIndex = i;
            ImGuiTheme.Tooltip(SettingHints.DisplayRules.ColumnName);

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawDisplayRuleDetailPanel(int index)
    {
        var rule = _rulesUiCache[index];
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled($"Edit rule #{index + 1} — matchers blank = any");

        ImGui.PushID($"ruleDetail{index}");

        var name = rule.Name;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("Name", ref name, 256)) { }
        if (ImGui.IsItemDeactivatedAfterEdit())
            UpdateRuleAt(index, c => c.Name = name);
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.RuleName);

        var matchText = string.Join(", ", rule.Match);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("Match contains", "Waypoint, Strongbox, *Daemon* — comma-separated", ref matchText, 512)) { }
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            var terms = matchText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            UpdateRuleAt(index, c => c.Match = terms);
            rule = _rulesUiCache[index];
        }
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.MatchContains);

        ImGui.TextUnformatted("Entity type");
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.EntityType);
        for (var ci = 0; ci < RuleCategories.Length; ci++)
        {
            var cat = RuleCategories[ci];
            if (ci > 0) ImGui.SameLine();
            bool on = rule.Categories.Contains(cat, StringComparer.OrdinalIgnoreCase);
            if (ImGui.Checkbox(cat, ref on))
            {
                var cats = new List<string>(rule.Categories);
                if (on && !cats.Contains(cat, StringComparer.OrdinalIgnoreCase))
                    cats.Add(cat);
                else if (!on)
                    cats.RemoveAll(c => string.Equals(c, cat, StringComparison.OrdinalIgnoreCase));
                UpdateRuleAt(index, c => c.Categories = new List<string>(cats));
                rule = _rulesUiCache[index];
            }
        }

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
            ImGui.SetNextItemWidth(UiW(8f));
            if (ImGui.BeginCombo(label, preview))
            {
                if (ImGui.Selectable("(any)", string.IsNullOrEmpty(current)))
                {
                    ApplyRuleCondition(index, field, null);
                    rule = _rulesUiCache[index];
                }
                foreach (var opt in options)
                {
                    if (ImGui.Selectable(opt, string.Equals(current, opt, StringComparison.OrdinalIgnoreCase)))
                    {
                        ApplyRuleCondition(index, field, opt);
                        rule = _rulesUiCache[index];
                    }
                }
                ImGui.EndCombo();
            }
            ImGuiTheme.Tooltip(field switch
            {
                "Rarity" => SettingHints.DisplayRules.Rarity,
                "Reaction" => SettingHints.DisplayRules.Reaction,
                "Life" => SettingHints.DisplayRules.Life,
                "Chest" => SettingHints.DisplayRules.Chest,
                "Poi" => SettingHints.DisplayRules.Poi,
                _ => "",
            });
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Then on the map");

        bool hide = rule.Hide;
        if (ImGui.Checkbox("Don't show on map", ref hide) && hide != rule.Hide)
            UpdateRuleAt(index, c => c.Hide = hide);
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.HideOnMap);

        bool navigable = rule.Navigable;
        ImGui.SameLine();
        if (ImGui.Checkbox("Auto-path target", ref navigable) && navigable != rule.Navigable)
            UpdateRuleAt(index, c => c.Navigable = navigable);
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.AutoPathTarget);

        var shapeIdx = Array.FindIndex(ShapeNames, sh => string.Equals(sh, rule.Shape, StringComparison.OrdinalIgnoreCase));
        if (shapeIdx < 0) shapeIdx = 0;
        ImGui.SetNextItemWidth(UiW(8f));
        if (ImGui.Combo("Shape", ref shapeIdx, string.Join('\0', ShapeNames) + "\0"))
            UpdateRuleAt(index, c => c.Shape = ShapeNames[shapeIdx]);
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.Shape);

        var labelText = rule.Label ?? "";
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("Map label", "optional text beside the dot", ref labelText, 128)) { }
        if (ImGui.IsItemDeactivatedAfterEdit())
            UpdateRuleAt(index, c => c.Label = string.IsNullOrWhiteSpace(labelText) ? null : labelText);
        ImGuiTheme.Tooltip(SettingHints.DisplayRules.MapLabel);

        ImGui.PopID();
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
            SettingHints.Radar.AutoPathSection);
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
            ImGui.TextDisabled("Target picking only — use Path display for where routes draw.");

            bool showAll = !s.ImportantOnly;
            if (ImGui.Checkbox("Show all monsters (including clutter)", ref showAll))
                s.ImportantOnly = !showAll;
            ImGuiTheme.Tooltip(SettingHints.Entities.ShowAllMonsters);
        }
        ImGuiTheme.EndAccordionSection(detectOpen);

        DrawTypesInZone(ctx, s);

        bool rulesOpen = ImGuiTheme.BeginAccordionSection("DisplayRules", "Display rules",
            "What entities show on the map — first active match wins.");
        if (rulesOpen)
            DrawDisplayRulesEditor(s);
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

                        ImGui.TextDisabled("Show  Nav   Count  Type");
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

    private void DrawTypeRow(RenderContext ctx, string token, string label, int count, Poe2Live.EntityDot sample)
    {
        ImGui.PushID(token);
        var areaCode = ctx.AreaCode;
        var globalRule = _ruleEngine?.ResolveGlobal(sample);
        var mergedRule = _ruleEngine?.Resolve(sample, areaCode, _settings.ImportantOnly, ctx.Entities);
        var hasZoneOverride = _zoneOverrides?.HasOverride(areaCode, token) ?? false;
        var rawHide = mergedRule is { Hide: true };
        var rawNav = mergedRule?.Navigable ?? false;
        var shownNow = !rawHide;
        var navNow = !rawHide && rawNav;

        bool show = shownNow;
        if (ImGui.Checkbox("##show", ref show) && show != shownNow)
            ApplyZoneOverride(areaCode, token, hide: !show, navigable: rawNav, globalRule);
        ImGuiTheme.Tooltip(SettingHints.Entities.TypeShow);

        ImGui.SameLine();
        bool nav = navNow;
        if (ImGui.Checkbox("##nav", ref nav) && nav != navNow)
            ApplyZoneOverride(areaCode, token, hide: rawHide, navigable: nav, globalRule);
        ImGuiTheme.Tooltip(SettingHints.Entities.TypeNav);

        ImGui.SameLine();
        ImGui.TextDisabled($"x{count}");
        ImGui.SameLine();
        ImGui.TextUnformatted(label);
        if (hasZoneOverride)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("· zone");
        }
        ImGui.PopID();
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
        bool mapOpen = ImGuiTheme.BeginAccordionSection("MapDisplay", "Map display",
            "What appears on the radar overlay.");
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

            bool ao = s.AlwaysShowOverlay; ImGui.Checkbox("Always Show Overlay", ref ao); s.AlwaysShowOverlay = ao;
            ImGuiTheme.Tooltip(SettingHints.Radar.AlwaysShowOverlay);

            int lcg = s.LandmarkClusterGap;
            ImGui.SetNextItemWidth(UiW());
            if (ImGui.SliderInt("Cluster Gap", ref lcg, 0, 64)) s.LandmarkClusterGap = lcg;
            ImGuiTheme.Tooltip(SettingHints.Radar.LandmarkClusterGap);

            bool drawAllLm = s.DrawAllLandmarkPaths;
            if (ImGui.Checkbox("Draw all landmark paths", ref drawAllLm)) s.DrawAllLandmarkPaths = drawAllLm;
            ImGuiTheme.Tooltip(SettingHints.Radar.DrawAllLandmarkPaths);

            DrawNavMenuCornerSetting(s);
        }
        ImGuiTheme.EndAccordionSection(mapOpen);

        bool pathOpen = ImGuiTheme.BeginAccordionSection("PathDisplay", "Path display",
            SettingHints.Radar.PathDisplaySection);
        if (pathOpen)
        {
            bool spw = s.ShowPathWorld;
            if (ImGui.Checkbox("Paths on world view", ref spw)) s.ShowPathWorld = spw;
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowPathWorld);

            ImGui.BeginDisabled(!s.ShowPathWorld);
            bool sgw = s.ShowGroundWaypoints;
            if (ImGui.Checkbox("Ground waypoints", ref sgw)) s.ShowGroundWaypoints = sgw;
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowGroundWaypoints);
            ImGui.EndDisabled();

            bool spm = s.ShowPathMap;
            if (ImGui.Checkbox("Paths on Tab map", ref spm)) s.ShowPathMap = spm;
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowPathMap);

            bool spmi = s.ShowPathMinimap;
            if (ImGui.Checkbox("Paths on minimap", ref spmi)) s.ShowPathMinimap = spmi;
            ImGuiTheme.Tooltip(SettingHints.Radar.ShowPathMinimap);
        }
        ImGuiTheme.EndAccordionSection(pathOpen);

        DrawRadarEntitySections(s, ctx);

        bool calOpen = ImGuiTheme.BeginAccordionSection("MapCalibration", "Map calibration",
            "Fine-tune overlay alignment with the game minimap.");
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
            "How often POE2Radar reads memory and redraws.");
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
        if (ImGui.BeginCombo("Nav menu corner", corners[idx]))
        {
            for (var i = 0; i < corners.Length; i++)
            {
                if (ImGui.Selectable(corners[i], i == idx))
                {
                    s.NavMenuCorner = corners[i];
                    _navMenuCorner = corners[i];
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
            "Opt-in keyboard flask automation when PoE2 is focused.");
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

    private void DrawAtlasTab(RadarSettings s)
    {
        bool displayOpen = ImGuiTheme.BeginAccordionSection("AtlasDisplay", "Display",
            "In-game atlas map highlights and routing.");
        if (displayOpen)
        {
            var langs = new[] { "english", "french", "german", "japanese", "korean", "portuguese", "russian", "spanish", "thai", "traditional chinese" };
            var lang = string.IsNullOrWhiteSpace(s.AtlasLanguage) ? "english" : s.AtlasLanguage;
            if (ImGui.BeginCombo("Language", lang))
            {
                foreach (var option in langs)
                {
                    var selected = string.Equals(lang, option, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(option, selected))
                    {
                        s.AtlasLanguage = option;
                    }
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGuiTheme.Tooltip(SettingHints.Atlas.Language);

            bool son = s.AtlasShowOnScreenNodes;
            var trackActive = s.AtlasHighlightTags is { Count: > 0 };
            if (ImGui.Checkbox("Show all nodes", ref son)) { s.AtlasShowOnScreenNodes = son; }
            ImGuiTheme.Tooltip(trackActive ? SettingHints.Atlas.ShowAllNodesTracked : SettingHints.Atlas.ShowAllNodes);

            bool sn = s.AtlasShowNames;
            if (ImGui.Checkbox("Show names", ref sn)) { s.AtlasShowNames = sn; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShowNames);

            bool rf = s.AtlasRevealFog;
            if (ImGui.Checkbox("Reveal fog", ref rf)) { s.AtlasRevealFog = rf; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.RevealFog);

            bool oa = s.AtlasOffScreenArrows;
            if (ImGui.Checkbox("Off-screen arrows (highlights)", ref oa)) { s.AtlasOffScreenArrows = oa; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.OffScreenArrows);

            bool sr = s.AtlasShowRoute;
            if (ImGui.Checkbox("Show F10 route", ref sr)) { s.AtlasShowRoute = sr; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.ShowRoute);

            bool ucs = s.AtlasUseCurrentStart;
            if (ImGui.Checkbox("Route from current tile", ref ucs)) { s.AtlasUseCurrentStart = ucs; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.RouteFromCurrent);

            bool chev = s.AtlasShowRouteChevrons;
            if (ImGui.Checkbox("Route chevrons", ref chev)) { s.AtlasShowRouteChevrons = chev; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.RouteChevrons);

            bool bb = s.AtlasShowBiomeBorders;
            if (ImGui.Checkbox("Biome borders", ref bb)) { s.AtlasShowBiomeBorders = bb; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.BiomeBorders);

            bool cb = s.AtlasShowContentBadges;
            if (ImGui.Checkbox("Content badges", ref cb)) { s.AtlasShowContentBadges = cb; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.ContentBadges);

            bool cc = s.AtlasShowContentCount;
            if (ImGui.Checkbox("Content count pips", ref cc)) { s.AtlasShowContentCount = cc; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.ContentCount);

            float atlasIcon = s.AtlasIconScale;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Icon scale", ref atlasIcon, 0.5f, 3f, "%.2f"))
            {
                s.AtlasIconScale = Math.Clamp(atlasIcon, 0.25f, 4f);
            }
            ImGuiTheme.Tooltip(SettingHints.Atlas.IconScale);

            float atlasLabel = s.AtlasLabelScale;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Label scale", ref atlasLabel, 0.5f, 2.5f, "%.2f"))
            {
                s.AtlasLabelScale = Math.Clamp(atlasLabel, 0.5f, 3f);
            }
            ImGuiTheme.Tooltip(SettingHints.Atlas.LabelScale);

            float routeThickness = s.AtlasRouteLineThickness;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Route thickness", ref routeThickness, 1f, 8f, "%.1f"))
            {
                s.AtlasRouteLineThickness = Math.Clamp(routeThickness, 1f, 8f);
            }
            ImGuiTheme.Tooltip(SettingHints.Atlas.RouteThickness);

            float routeSpacing = s.AtlasRouteChevronSpacing;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Chevron spacing", ref routeSpacing, 8f, 80f, "%.0f"))
            {
                s.AtlasRouteChevronSpacing = Math.Clamp(routeSpacing, 8f, 80f);
            }
            ImGuiTheme.Tooltip(SettingHints.Atlas.ChevronSpacing);
        }
        ImGuiTheme.EndAccordionSection(displayOpen);

        bool searchOpen = ImGuiTheme.BeginAccordionSection("AtlasSearch", "Search and filters",
            "Hide nodes by completion state; comma-separated search.");
        if (searchOpen)
        {
            var q = s.AtlasSearchQuery ?? "";
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##atlasSearch", "Search map names, ids, or content (comma = OR)…", ref q, 256))
            {
                s.AtlasSearchQuery = q;
            }
            ImGuiTheme.Tooltip(SettingHints.Atlas.SearchQuery);

            bool hc = s.AtlasHideCompletedMaps;
            if (ImGui.Checkbox("Hide completed maps", ref hc)) { s.AtlasHideCompletedMaps = hc; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.HideCompleted);
            bool hna = s.AtlasHideNotAccessibleMaps;
            if (ImGui.Checkbox("Hide not-accessible maps", ref hna)) { s.AtlasHideNotAccessibleMaps = hna; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.HideNotAccessible);
            bool hav = s.AtlasHideAvailableMaps;
            if (ImGui.Checkbox("Hide available maps", ref hav)) { s.AtlasHideAvailableMaps = hav; }
            ImGuiTheme.Tooltip(SettingHints.Atlas.HideAvailable);
        }
        ImGuiTheme.EndAccordionSection(searchOpen);

        bool stylesOpen = ImGuiTheme.BeginAccordionSection("AtlasMapStyles", "Map styles",
            "Per-group colors and map name lists.");
        if (stylesOpen)
        {
            foreach (var g in s.AtlasMapGroups)
            {
                ImGui.PushID("atlasGroup_" + g.Name);
                bool en = g.Enabled;
                if (ImGui.Checkbox("##enabled", ref en)) { g.Enabled = en; }
                ImGuiTheme.Tooltip(SettingHints.Atlas.GroupEnabled);
                ImGui.SameLine();
                ImGui.TextUnformatted(g.Name);
                ImGui.SameLine();
                var bg = ParseHexColor(g.Color);
                if (ImGui.ColorEdit3("Background", ref bg, ImGuiColorEditFlags.NoInputs))
                {
                    g.Color = FormatHexColor3(bg);
                }
                ImGuiTheme.Tooltip(SettingHints.Atlas.GroupColor);
                var key = g.Name;
                if (!_atlasGroupMapBuffers.TryGetValue(key, out var maps))
                    maps = _atlasGroupMapBuffers[key] = string.Join(", ", g.Maps);
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.InputTextWithHint("Maps", "comma-separated map names…", ref maps, 512))
                {
                    _atlasGroupMapBuffers[key] = maps;
                    g.Maps = maps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                }
                ImGuiTheme.Tooltip(SettingHints.Atlas.GroupMaps);
                ImGui.PopID();
            }
        }
        ImGuiTheme.EndAccordionSection(stylesOpen);

        bool farmingOpen = ImGuiTheme.BeginAccordionSection("AtlasFarming", "Target farming",
            "Per-content route groups and hop limits.");
        if (farmingOpen)
        {
            foreach (var group in s.AtlasRouteGroups)
            {
                ImGui.PushID("atlasRouteGroup_" + group.Name);
                bool draw = group.DrawPaths;
                if (ImGui.Checkbox(group.Name, ref draw)) { group.DrawPaths = draw; }
                ImGuiTheme.Tooltip(SettingHints.Atlas.RouteGroupDraw);
                float thick = group.LineThickness;
                ImGui.SetNextItemWidth(160f);
                if (ImGui.SliderFloat("Line thickness", ref thick, 1f, 8f, "%.1f")) { group.LineThickness = Math.Clamp(thick, 1f, 8f); }
                ImGuiTheme.Tooltip(SettingHints.Atlas.RouteGroupThickness);
                foreach (var e in group.Entries)
                {
                    ImGui.PushID(e.Match);
                    bool on = e.DrawPath;
                    if (ImGui.Checkbox(e.Name, ref on)) { e.DrawPath = on; }
                    ImGuiTheme.Tooltip(SettingHints.Atlas.RouteEntryDraw);
                    ImGui.SameLine();
                    int maxHops = e.MaxHops;
                    ImGui.SetNextItemWidth(80f);
                    if (ImGui.InputInt("max hops", ref maxHops)) { e.MaxHops = Math.Clamp(maxHops, 0, 1000); }
                    ImGuiTheme.Tooltip(SettingHints.Atlas.RouteMaxHops);
                    ImGui.SameLine();
                    var col = ParseHexColor(e.Color);
                    if (ImGui.ColorEdit3("color", ref col, ImGuiColorEditFlags.NoInputs)) { e.Color = FormatHexColor3(col); }
                    ImGuiTheme.Tooltip(SettingHints.Atlas.RouteEntryColor);
                    ImGui.PopID();
                }
                ImGui.PopID();
            }
        }
        ImGuiTheme.EndAccordionSection(farmingOpen);

        bool hlOpen = ImGuiTheme.BeginAccordionSection("AtlasHighlights", "Highlights",
            "Track filters, citadel defaults, and tag catalog.");
        if (hlOpen)
        {
            if (s.AtlasHighlightTags is { Count: > 0 })
                ImGui.TextDisabled("Tracked-only mode — only nodes matching Track filters are drawn.");
            if (ImGui.Button("Add citadel defaults"))
            {
                SeedAtlasCitadelDefaults(s);
                s.AtlasRulesInitialized = true;
            }
            ImGuiTheme.Tooltip(SettingHints.Atlas.AddCitadelDefaults);
            ImGui.SameLine();
            if (ImGui.Button("Add endgame defaults"))
            {
                AtlasEndgameCatalog.ApplyEndgameDefaults(s, _ctx?.AtlasTagCatalog);
                s.AtlasRulesInitialized = true;
            }
            ImGuiTheme.Tooltip(SettingHints.Atlas.AddEndgameDefaults);
            ImGui.SameLine();
            if (ImGui.Button("Clear all filters"))
            {
                s.AtlasHighlightTags.Clear();
                s.AtlasArrowTags.Clear();
                s.AtlasHighlightColors.Clear();
                s.AtlasRulesInitialized = true;
            }
            ImGuiTheme.Tooltip(SettingHints.Atlas.ClearFilters);

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##atlasTagFilter", "Filter tags and map names…", ref _atlasTagFilter, 128);
            ImGuiTheme.Tooltip(SettingHints.Atlas.TagFilter);

            var catalog = _ctx?.AtlasTagCatalog;
            if (catalog is null or { Count: 0 })
            {
                ImGui.TextWrapped("Open the Atlas map in-game to populate the tag list.");
            }
            else
            {
                var filter = _atlasTagFilter.Trim();
                if (ImGui.BeginTable("atlasHl", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new System.Numerics.Vector2(0, 220f)))
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Kind", ImGuiTableColumnFlags.WidthFixed, 36f);
                    ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 28f);
                    ImGui.TableSetupColumn("Track", ImGuiTableColumnFlags.WidthFixed, 44f);
                    ImGui.TableSetupColumn("Arrow", ImGuiTableColumnFlags.WidthFixed, 44f);
                    ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 56f);
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGuiTheme.TableHeadersWithTooltips([
                        ("Name", SettingHints.Atlas.ColName),
                        ("Kind", SettingHints.Atlas.ColKind),
                        ("#", SettingHints.Atlas.ColCount),
                        ("Track", SettingHints.Atlas.ColTrack),
                        ("Arrow", SettingHints.Atlas.ColArrow),
                        ("Color", SettingHints.Atlas.ColColor),
                    ]);

                    foreach (var e in catalog)
                    {
                        if (filter.Length > 0 && e.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) == false) continue;

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted(e.Key);
                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextUnformatted(e.Kind);
                        ImGui.TableSetColumnIndex(2);
                        ImGui.TextUnformatted(e.Count.ToString());

                        ImGui.TableSetColumnIndex(3);
                        bool track = AtlasListContains(s.AtlasHighlightTags, e.Key);
                        if (ImGui.Checkbox($"##tr_{e.Key}", ref track))
                        {
                            AtlasToggleList(s.AtlasHighlightTags, e.Key, track);
                            if (track && !s.AtlasHighlightColors.ContainsKey(e.Key))
                                s.AtlasHighlightColors[e.Key] = "#58A6FF";
                            s.AtlasRulesInitialized = true;
                        }
                        ImGuiTheme.Tooltip(SettingHints.Atlas.ColTrack);

                        ImGui.TableSetColumnIndex(4);
                        bool arrow = AtlasListContains(s.AtlasArrowTags, e.Key);
                        if (ImGui.Checkbox($"##ar_{e.Key}", ref arrow))
                        {
                            AtlasToggleList(s.AtlasArrowTags, e.Key, arrow);
                            s.AtlasRulesInitialized = true;
                        }
                        ImGuiTheme.Tooltip(SettingHints.Atlas.ColArrow);

                        ImGui.TableSetColumnIndex(5);
                        if (track)
                        {
                            var hex = s.AtlasHighlightColors.TryGetValue(e.Key, out var hc) ? hc : "#58A6FF";
                            var col = ParseHexColor(hex);
                            ImGui.PushID(e.Key);
                            if (ImGui.ColorEdit3("##col", ref col, ImGuiColorEditFlags.NoInputs))
                            {
                                s.AtlasHighlightColors[e.Key] = FormatHexColor3(col);
                            }
                            ImGuiTheme.Tooltip(SettingHints.Atlas.ColColor);
                            ImGui.PopID();
                        }
                    }
                    ImGui.EndTable();
                }
            }
        }
        ImGuiTheme.EndAccordionSection(hlOpen);
    }

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
            DrawHotkeyRow(s, "autoPathToggleHotkey", "Auto-path toggle", SettingHints.Hotkeys.AutoPathToggle);
            DrawHotkeyRow(s, "addNearestPathHotkey", "Add nearest path", SettingHints.Hotkeys.AddNearestPath);
            DrawHotkeyRow(s, "clearPathsHotkey", "Clear paths", SettingHints.Hotkeys.ClearPaths);
            DrawHotkeyRow(s, "autoFlaskToggleHotkey", "Auto-flask toggle", SettingHints.Hotkeys.AutoFlaskToggle);
            DrawHotkeyRow(s, "atlasPickHotkey", "Atlas tile pick", SettingHints.Hotkeys.AtlasPick);
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
        "autoPathToggleHotkey" => s.AutoPathToggleHotkey,
        "addNearestPathHotkey" => s.AddNearestPathHotkey,
        "clearPathsHotkey" => s.ClearPathsHotkey,
        "autoFlaskToggleHotkey" => s.AutoFlaskToggleHotkey,
        "atlasPickHotkey" => s.AtlasPickHotkey,
        "toggleSettingsHotkey" => s.ToggleSettingsHotkey,
        "openDashboardHotkey" => s.OpenDashboardHotkey,
        "quitHotkey" => s.QuitHotkey,
        _ => 0,
    };

    private static void SetHotkey(RadarSettings s, string key, int value)
    {
        switch (key)
        {
            case "hideEntityHotkey": s.HideEntityHotkey = value; break;
            case "trackEntityHotkey": s.TrackEntityHotkey = value; break;
            case "autoPathToggleHotkey": s.AutoPathToggleHotkey = value; break;
            case "addNearestPathHotkey": s.AddNearestPathHotkey = value; break;
            case "clearPathsHotkey": s.ClearPathsHotkey = value; break;
            case "autoFlaskToggleHotkey": s.AutoFlaskToggleHotkey = value; break;
            case "atlasPickHotkey": s.AtlasPickHotkey = value; break;
            case "toggleSettingsHotkey": s.ToggleSettingsHotkey = value; break;
            case "openDashboardHotkey": s.OpenDashboardHotkey = value; break;
            case "quitHotkey": s.QuitHotkey = value; break;
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
