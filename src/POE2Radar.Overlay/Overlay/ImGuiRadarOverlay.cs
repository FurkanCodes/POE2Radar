using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using POE2Radar.Core.Game;
using POE2Radar.Core.Pathfinding;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Diagnostics;
using POE2Radar.Overlay.Native;
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
    private readonly List<MapLabelCandidate> _atlasLabelScratch = new(256);
    private int _spritePickerRuleIndex = -1;
    private bool _bindingHideHotkey;
    private bool _bindingTrackHotkey;

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
        VSync = false;
    }

    public int OverlayWidth => _width;
    public int OverlayHeight => _height;
    public float LastAtlasDrawMs { get; private set; }

    public void UpdateContext(RenderContext ctx) => _ctx = ctx;

    public void AttachEntityStores(DisplayRules displayRules, ZoneEntityOverrides zoneOverrides,
        DisplayRuleEngine ruleEngine, HiddenEntities hidden)
    {
        _displayRules = displayRules;
        _zoneOverrides = zoneOverrides;
        _ruleEngine = ruleEngine;
        _hidden = hidden;
        _rulesUiGeneration = -1;
        _rulesUiCache.Clear();
    }

    public void UpdateSettings(RadarSettings settings)
    {
        lock (_settingsLock) _settings = settings;
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

    protected override void Render()
    {
        try
        {
            if (_closeRequested) { Close(); return; }

            lock (_boundsLock) { Position = _position; Size = _size; }

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;

            var ctx = _ctx;
            var inGame = ctx is not null && ctx.InGame;

            var dl = ImGui.GetBackgroundDrawList();

            if (inGame && ctx!.Active)
            {
                IconAtlas.EnsureInitialized(this);

                if (ctx.AtlasOpen)
                    DrawAtlas(dl, ctx);
                else
                {
                    if (ctx.Map.IsVisible)
                        DrawMap(dl, ctx, ctx.MapFrame);
                    if (ctx.MiniMap.IsVisible)
                        DrawMap(dl, ctx, ctx.MiniMapFrame);
                    if (!ctx.Map.IsVisible)
                        DrawPathsWorld(dl, ctx);
                }

                if (!ctx.AtlasOpen)
                {
                    DrawNameplates(dl, ctx);
                    DrawPathLabels(dl, ctx);
                }
            }

            if (inGame)
                DrawNavMenu(ctx!);

            if (_settingsOpen)
                DrawSettingsPanel(ctx);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _renderCrashLogged, 1) == 0)
                CrashLog.Write("ImGui render crashed", ex);
            Close();
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
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Flask: ");
        ImGui.SameLine(0, 0);
        ImGui.TextColored(new Vector4(
            flaskColor >> 16 != 0 ? ((flaskColor >> 16) & 0xFF) / 255f : 0,
            ((flaskColor >> 8) & 0xFF) / 255f,
            (flaskColor & 0xFF) / 255f, 1f), ctx.FlaskNote);
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(
            $"Lv {ctx.CharLevel}  {ctx.AreaCode}").X);
        ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), $"Lv {ctx.CharLevel}  {ctx.AreaCode}");
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
        float h0 = ctx.AtlasScale, h1 = ctx.AtlasShearX, h2 = ctx.AtlasOffX,
              h3 = ctx.AtlasShearY, h4 = ctx.AtlasScaleY, h5 = ctx.AtlasOffY,
              h6 = ctx.AtlasPersX, h7 = ctx.AtlasPersY;
        float ccx = W * 0.5f, ccy = H * 0.5f;
        var iconScale = ctx.AtlasIconScale;
        var labelScale = ctx.AtlasLabelScale;

        NumVec2 ProjAtlas(NumVec2 p)
        {
            var pw = h6 * p.X + h7 * p.Y + 1f;
            if (MathF.Abs(pw) < 1e-6f) pw = 1f;
            return new NumVec2((h0 * p.X + h1 * p.Y + h2) / pw, (h3 * p.X + h4 * p.Y + h5) / pw);
        }

        var route = ctx.AtlasRoute;
        if (route is { Count: >= 2 })
        {
            var dark = ColorU32(0, 0, 0, 0.6f);
            var bright = ColorU32(59, 219, 255, 0.95f);
            var pts = new NumVec2[route.Count];
            for (var i = 0; i < route.Count; i++) pts[i] = ProjAtlas(route[i]);
            for (var i = 1; i < pts.Length; i++) dl.AddLine(pts[i - 1], pts[i], dark, 7f);
            for (var i = 1; i < pts.Length; i++) dl.AddLine(pts[i - 1], pts[i], bright, 3.5f);
            for (var i = 1; i < pts.Length - 1; i++) dl.AddCircle(pts[i], 4f, bright, 0, 2f);
        }
        else if (ctx.AtlasStart is { } sa && ctx.AtlasEnd is { } eb)
        {
            var a = ProjAtlas(sa); var b = ProjAtlas(eb);
            dl.AddLine(a, b, ColorU32(0, 0, 0, 0.6f), 6f);
            dl.AddLine(a, b, ColorU32(224, 179, 65, 1f), 2.5f);
        }

        if (ctx.AtlasStart is { } s) { var p = ProjAtlas(s); dl.AddCircleFilled(p, 8f, ColorU32(110, 232, 135, 1f), 12); dl.AddCircleFilled(p, 3f, ColorU32(110, 232, 135, 1f), 8); }
        if (ctx.AtlasEnd is { } e) { var p = ProjAtlas(e); dl.AddCircle(p, 11f, ColorU32(224, 179, 65, 1f), 0, 3f); dl.AddCircle(p, 4f, ColorU32(224, 179, 65, 1f), 0, 2f); }

        if (ctx.AtlasNodes is { Count: > 0 } marks)
        {
            _atlasLabelScratch.Clear();
            var labelCandidates = _atlasLabelScratch;
            foreach (var n in marks)
            {
                var w = h6 * n.X + h7 * n.Y + 1f;
                if (MathF.Abs(w) < 1e-6f) continue;
                var sx = (h0 * n.X + h1 * n.Y + h2) / w;
                var sy = (h3 * n.X + h4 * n.Y + h5) / w;
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

                if (ctx.AtlasTrackedOnly && !n.Selected && string.IsNullOrEmpty(n.HighlightLabel) && string.IsNullOrEmpty(n.Color)) continue;
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
                if (IconAtlas.TryResolve(sprite, null, out var tex))
                {
                    var half = size * iconScale;
                    dl.AddImage(tex.TextureId, new NumVec2(c.X - half, c.Y - half), new NumVec2(c.X + half, c.Y + half), tex.UV0, tex.UV1, col);
                    if (n.Selected || n.Arrow || !string.IsNullOrEmpty(n.Color) || accentRing)
                        dl.AddCircle(c, half + 3f, col, 0, accentRing ? 3f : 2f);
                }
                else
                    dl.AddCircleFilled(c, size, col, 12);

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
                    var textCol = ColorU32(colHex, Math.Min(1f, opacity + 0.05f));
                    labelCandidates.Add(new MapLabelCandidate(c, chipText, textCol, col));
                }
            }

            if (labelCandidates.Count > 0)
                DrawAtlasLabelChips(dl, labelCandidates, 0f, 0f, W, H, labelScale);
        }

        LastAtlasDrawMs = (float)Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
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

    private void DrawMap(ImDrawListPtr dl, RenderContext ctx, MapFrame frame)
    {
        var W = ctx.WindowWidth;
        var H = ctx.WindowHeight;
        var center = frame.Center;
        var scale = MathF.Max(0.01f, frame.Scale);

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

            if (ctx.ShowPath)
                DrawPathsMap(dl, ctx, frame, center, scale);

            var mapLabels = new List<MapLabelCandidate>();
            var clipL = frame.Position.X;
            var clipT = frame.Position.Y;
            var clipR = frame.Position.X + frame.Width;
            var clipB = frame.Position.Y + frame.Height;

            if (ctx.ShowMonsters)
            {
                foreach (var e in ctx.Entities)
                {
                    if (e.IconComplete) continue;   // faded/claimed encounter (e.g. looted expedition) — never draw
                    var rule = ctx.Resolve?.Invoke(e);
                    if (rule is { Hide: true }) continue;
                    if (ctx.ImportantOnly && EntityImportanceHelper.IsTrash(
                            EntityImportanceHelper.Classify(e, ctx.Styles))) continue;
                    var p = Project(e.Grid, ctx.PlayerGrid, center, scale, e.TerrainHeight - frame.PlayerTerrainHeight);
                    if (p.X < -40 || p.Y < -40 || p.X > W + 40 || p.Y > H + 40) continue;
                    var (sprite, shape, radius, color, opacity) = rule is not null
                        ? (rule.Sprite, rule.Shape, rule.Size, rule.Color, rule.Opacity)
                        : ResolveEntityDrawStyle(e, ctx.Styles);
                    DrawIconOrShape(dl, p, radius, color, opacity, sprite, shape, ctx.GlobalIconScale);
                    var entityLabel = EntityDisplayHelper.FormatEntityLabel(e, rule, ctx.Entities, ctx.AreaCode);
                    if (entityLabel.Length > 0)
                    {
                        var textColor = ColorU32(color, 0.9f);
                        mapLabels.Add(new MapLabelCandidate(p, entityLabel, textColor, textColor));
                    }
                }
            }

            foreach (var lm in ctx.Landmarks)
            {
                var tr = ctx.ResolveTile?.Invoke(lm.Path);
                if (tr is { Hide: true }) continue;
                var p = Project(lm.Center, ctx.PlayerGrid, center, scale, -frame.PlayerTerrainHeight);
                if (p.X < -40 || p.Y < -40 || p.X > W + 40 || p.Y > H + 40) continue;
                var lmColor = tr?.Color ?? "#F259F2";
                var lmSize = tr?.Size ?? 4.5f;
                DrawIconOrShape(dl, p, lmSize, lmColor, tr?.Opacity ?? 0.95f, tr?.Sprite ?? ctx.Styles.Landmark.Sprite, tr?.Shape ?? ctx.Styles.Landmark.Shape, ctx.GlobalIconScale);
                var lmCurated = tr?.Label is { Length: > 0 } tileLbl ? tileLbl
                    : (ctx.UseCuratedLandmarks ? lm.CuratedName : null);
                var label = EntityDisplayHelper.FormatLandmarkLabel(
                    lm.Path, lmCurated, lm.Name, ctx.Entities, ctx.AreaCode);
                if (label.Length > 0
                    && EntityDisplayHelper.ShouldDrawBossLandmarkLabel(
                        lm.Path, label, lm.Center, ctx.Entities, ctx.Resolve, ctx.AreaCode))
                {
                    var textColor = ColorU32(lmColor, 0.9f);
                    mapLabels.Add(new MapLabelCandidate(p, label, textColor, textColor));
                }
            }

            if (mapLabels.Count > 0)
                DrawMapLabelChips(dl, mapLabels, clipL, clipT, clipR, clipB);

            if (ctx.ShowPlayerBlip)
                DrawIconOrShape(dl, center, ctx.Styles.Player.Size, ctx.Styles.Player.Color, ctx.Styles.Player.Opacity, ctx.Styles.Player.Sprite, ctx.Styles.Player.Shape, ctx.GlobalIconScale);
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

        var terrainDeltaZ = -frame.PlayerTerrainHeight;
        var p0 = Project(new NumVec2(0, 0), ctx.PlayerGrid, center, scale, terrainDeltaZ);
        var p1 = Project(new NumVec2(terrain.Width, 0), ctx.PlayerGrid, center, scale, terrainDeltaZ);
        var p2 = Project(new NumVec2(terrain.Width, terrain.Height), ctx.PlayerGrid, center, scale, terrainDeltaZ);
        var p3 = Project(new NumVec2(0, terrain.Height), ctx.PlayerGrid, center, scale, terrainDeltaZ);

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

                var p = Project(new NumVec2(x, y), ctx.PlayerGrid, center, scale, -frame.PlayerTerrainHeight);
                if (p.X < -8 || p.Y < -8 || p.X > W + 8 || p.Y > H + 8) continue;

                if (isEdge)
                {
                    if (x + edgeStride < bytesPerRow - 1)
                    {
                        var rightIdx = row + x + edgeStride;
                        if (rightIdx < data.Length && data[rightIdx] != 0)
                        {
                            var pr = Project(new NumVec2(x + edgeStride, y), ctx.PlayerGrid, center, scale, -frame.PlayerTerrainHeight);
                            if (MathF.Abs(pr.X - p.X) < 80f && MathF.Abs(pr.Y - p.Y) < 80f)
                                dl.AddLine(p, pr, edgeCol, thickness);
                        }
                    }

                    if (y + edgeStride < rows - 1)
                    {
                        var bottomIdx = (y + edgeStride) * bytesPerRow + x;
                        if (bottomIdx < data.Length && data[bottomIdx] != 0)
                        {
                            var pb = Project(new NumVec2(x, y + edgeStride), ctx.PlayerGrid, center, scale, -frame.PlayerTerrainHeight);
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

    private static void DrawPathsMap(ImDrawListPtr dl, RenderContext ctx, MapFrame frame, NumVec2 center, float scale)
    {
        foreach (var path in ctx.SelectedPaths)
        {
            if (path.Points.Count < 2) continue;
            var col = PathColor(path.ColorSlot);
            NumVec2? prev = null;
            foreach (var (x, y) in path.Points)
            {
                var p = Project(new NumVec2(x, y), ctx.PlayerGrid, center, scale, -frame.PlayerTerrainHeight);
                if (prev is { } a) dl.AddLine(a, p, col, 2.2f);
                prev = p;
            }
        }
    }

    // ── World-space paths (map closed) ──

    private static void DrawPathsWorld(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.CameraMatrix is not { Length: >= 16 } m) return;
        float W = ctx.WindowWidth, H = ctx.WindowHeight;
        var z = ctx.PlayerWorld.Z;

        foreach (var path in ctx.SelectedPaths)
        {
            if (path.Points.Count == 0) continue;
            var col = PathColor(path.ColorSlot);
            NumVec2? prev = null;
            foreach (var (gx, gy) in path.Points)
            {
                var wx = gx * GridConstants.GridToWorld;
                var wy = gy * GridConstants.GridToWorld;
                var cw = wx * m[3] + wy * m[7] + z * m[11] + m[15];
                if (cw <= 0.0001f) { prev = null; continue; }
                var cx = wx * m[0] + wy * m[4] + z * m[8] + m[12];
                var cy = wx * m[1] + wy * m[5] + z * m[9] + m[13];
                var sx = (cx / cw / 2f + 0.5f) * W;
                var sy = (0.5f - cy / cw / 2f) * H;
                if (!float.IsFinite(sx) || !float.IsFinite(sy)) continue;
                var p = new NumVec2(sx, sy);
                if (prev is { } pr) dl.AddLine(pr, p, col, 2.4f);
                dl.AddCircleFilled(p, 3.5f, col, 8);
                prev = p;
            }
        }
    }

    // ── HP bars (world-space nameplates) ──

    private void DrawNameplates(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.CameraMatrix is not { Length: >= 16 } m) return;
        if (ctx.HpBarTargets is not { Count: > 0 } bars) return;
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
            if (sx < 0 || sx > W || sy < 0 || sy > H) continue;

            var bw = t.Width;
            var bx = sx - bw / 2f + ctx.HpBars.OffsetX;
            var by = sy + ctx.HpBars.OffsetY;

            var backdropAlpha = useFullTex ? 0.35f : 0.78f;
            dl.AddRectFilled(new NumVec2(bx, by), new NumVec2(bx + bw, by + bh), ColorU32(13, 13, 13, backdropAlpha));

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

    private readonly record struct MapLabelCandidate(NumVec2 Pos, string Text, uint TextColor, uint SwatchColor);

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
    private static void DrawAtlasLabelChips(
        ImDrawListPtr dl,
        List<MapLabelCandidate> candidates,
        float clipL, float clipT, float clipR, float clipB,
        float labelScale)
    {
        const int clusterThreshold = 48;
        if (candidates.Count <= clusterThreshold)
        {
            DrawMapLabelChips(dl, candidates, clipL, clipT, clipR, clipB, labelScale);
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

    private static void DrawMapLabelChips(
        ImDrawListPtr dl,
        List<MapLabelCandidate> candidates,
        float clipL, float clipT, float clipR, float clipB,
        float labelScale = 1f)
    {
        var rowH = LabelChipRowH * labelScale;
        foreach (var cluster in BuildMapLabelClusters(candidates, labelScale))
        {
            if (cluster.Count == 1)
            {
                var c = cluster[0];
                var soloW = LabelChipWidth(c.Text, labelScale);
                var soloLeft = Math.Clamp(c.Pos.X + 7f * labelScale, clipL + 4f, clipR - soloW - 4f);
                var soloTop = Math.Clamp(c.Pos.Y - 7f * labelScale, clipT + 4f, clipB - rowH - 4f);
                DrawLabelChip(dl, soloLeft, soloTop, c.Text, c.TextColor, c.SwatchColor, labelScale);
                continue;
            }

            cluster.Sort((a, b) => string.Compare(a.Text, b.Text, StringComparison.Ordinal));
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

    private static void DrawPathLabels(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.SelectedPaths.Count == 0 || ctx.Map.IsVisible) return;
        float W = ctx.WindowWidth, H = ctx.WindowHeight;
        if (ctx.CameraMatrix is not { Length: >= 16 } m) return;

        // Stack path labels at the local player's screen position (not another Player entity in town).
        var playerScreen = ProjectWorldToScreen(ctx.PlayerWorld, m, W, H);

        // Build label rows sorted by color slot so the stack order is stable and matches the legend.
        var labels = new List<(int slot, string text)>();
        foreach (var path in ctx.SelectedPaths)
        {
            var label = string.IsNullOrWhiteSpace(path.Label) ? path.TargetId : path.Label;
            var status = path.Status switch
            {
                NavTargetStatus.Cached when path.IsEntity => " (last seen)",
                NavTargetStatus.NoPath => " (no path)",
                _ => "",
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

        for (var i = 0; i < labels.Count; i++)
        {
            var (slot, text) = labels[i];
            DrawLabelChip(dl, left, startTop + i * textH, text, PathColor(slot), PathColor(slot));
        }
    }

    // ── Nav menu ──

    private void DrawNavMenu(RenderContext ctx)
    {
        _navMenuCorner = ctx.NavMenuCorner;
        var isRight = _navMenuCorner is "TopRight" or "BottomRight";
        var isBottom = _navMenuCorner is "BottomLeft" or "BottomRight";

        var cornerPos = isRight
            ? new System.Numerics.Vector2(ctx.WindowWidth - 6, isBottom ? ctx.WindowHeight - 6 : 6)
            : new System.Numerics.Vector2(6, isBottom ? ctx.WindowHeight - 6 : 6);
        var pivot = new System.Numerics.Vector2(isRight ? 1f : 0f, isBottom ? 1f : 0f);

        ImGui.SetNextWindowBgAlpha(0.80f);
        ImGui.SetNextWindowPos(cornerPos, ImGuiCond.Always, pivot);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        if (!ImGui.Begin("Nav", flags)) { ImGui.End(); return; }

        var selected = 0;
        foreach (var row in ctx.Legend) if (row.IsSelected) selected++;

        var headerText = selected > 0 ? $"POE2Radar {selected}/8" : "POE2Radar";
        if (ImGui.Button(_navMenuExpanded ? "v " + headerText : "> " + headerText))
            _navMenuExpanded = !_navMenuExpanded;
        ImGui.SameLine();

        if (ImGui.SmallButton("+"))
            _enqueue(() => _addNearest());
        ImGui.SameLine();

        if (ImGui.SmallButton("-"))
            _enqueue(() => _clearPaths());
        ImGui.SameLine();
        if (ImGui.SmallButton("\u2699"))
            _settingsOpen = !_settingsOpen;

        ImGui.SameLine();
        foreach (var (label, corner) in new[] { ("TL", "TopLeft"), ("TR", "TopRight"), ("BL", "BottomLeft"), ("BR", "BottomRight") })
        {
            var active = corner == _navMenuCorner;
            if (active) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.95f, 1f, 1f)));
            if (ImGui.SmallButton(label))
            {
                _navMenuCorner = corner;
                _enqueue(() => _setCorner(corner));
            }
            if (active) ImGui.PopStyleColor();
            if (corner != "BottomRight") ImGui.SameLine();
        }

        if (_navMenuExpanded)
        {
            ImGui.Spacing();
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
        }

        if (ctx.ShowPerfStats)
        {
            ImGui.Separator();
            var p = ctx.Perf;
            ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), $"fps {p.Fps:F0}  tick {p.TickMs:F1}  ent {p.EntitiesMs:F1}");
            ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), $"map {p.MapMs:F1}  paths {p.PathsMs:F1}  hp {p.HpBarsMs:F1}");
            if (ctx.AtlasOpen)
                ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), $"atlas draw {p.AtlasMs:F1} ms");
            ImGui.TextColored(new Vector4(0.55f, 0.55f, 0.55f, 1f), $"reads {p.ReadsPerSec / 1000f:F1}k/s  {p.MibPerSec:F2} MiB/s");
        }

        ImGui.End();
    }

    // ── Settings panel ──

    private void DrawSettingsPanel(RenderContext? ctx)
    {
        if (!_settingsOpen) return;

        float wW = ctx?.WindowWidth ?? _width;
        float wH = ctx?.WindowHeight ?? _height;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(560, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(
            (wW - 560) * 0.5f,
            (wH - 440) * 0.5f), ImGuiCond.FirstUseEver);

        const ImGuiWindowFlags sflags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;

        if (!ImGui.Begin("POE2Radar Settings", ref _settingsOpen, sflags)) { ImGui.End(); return; }

        if (ctx is not null)
            DrawSettingsHud(ctx);

        ImGui.Separator();

        RadarSettings s;
        lock (_settingsLock) s = _settings;

        if (ImGui.BeginTabBar("SettingsTabs"))
        {
            if (ImGui.BeginTabItem("Radar")) { DrawRadarTab(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Entities")) { DrawEntitiesTab(s, ctx); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("HP Bars")) { DrawHpBarsTab(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Flask")) { DrawFlaskTab(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Atlas")) { DrawAtlasTab(s); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        PollHotkeyCapture(s);
        ImGui.End();
    }

    private void DrawEntitiesTab(RadarSettings s, RenderContext? ctx)
    {
        if (ImGui.CollapsingHeader("Detection & Auto-path", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int radius = s.EntityDrawRadiusGrid;
            ImGui.SliderInt("Detection radius (grid)", ref radius, 0, 500, radius == 0 ? "Unlimited" : "%d");
            s.EntityDrawRadiusGrid = radius;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Max grid distance from player for entity dots, nav targets, and API list. 0 = no limit.");

            bool ap = s.AutoPathNavigable;
            if (ImGui.Checkbox("Auto-path to nearest targets", ref ap))
            {
                s.AutoPathNavigable = ap;
                if (ap) s.ShowPath = true;
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Continuously draw paths to the nearest navigation targets — endgame mechanics, rares, uniques, POIs by default. Toggle groups/types below.");

            bool showAll = !s.ImportantOnly;
            if (ImGui.Checkbox("Show all monsters (including clutter)", ref showAll))
                s.ImportantOnly = !showAll;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Show normal/magic grey monsters and other map clutter on the radar.");
        }

        if (_displayRules is null)
        {
            ImGui.TextDisabled("Radar rules not wired yet.");
            return;
        }

        DrawTypesInZone(ctx, s);

        if (ImGui.CollapsingHeader("Radar rules (all zones)"))
        {
            var gen = _displayRules.Generation;
            if (gen != _rulesUiGeneration)
            {
                _rulesUiGeneration = gen;
                _rulesUiCache = _displayRules.All.ToList();
            }

            ImGui.TextDisabled("First matching active rule applies. Active = rule can match; Paused = skipped.");
            float iconScale = s.GlobalIconScale;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Global icon scale", ref iconScale, 0.5f, 3f, "%.2f"))
                s.GlobalIconScale = Math.Clamp(iconScale, 0.25f, 4f);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Multiplier on PNG sprite size from icons.png (per-rule scale stacks on top).");

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##rulesearch", "search rules by name…", ref _ruleSearch, 128);

            ImGui.TextDisabled("Active  Hide  Path   Icon  Color  Alpha  Size  Spr   Name");
            ImGui.BeginChild("EntityRulesList", new NumVec2(0, 220));

            var filter = _ruleSearch.Trim();
            for (var i = 0; i < _rulesUiCache.Count; i++)
            {
                var rule = _rulesUiCache[i];
                if (EntityDisplayHelper.IsPerTypeEntityRule(rule)) continue;
                if (filter.Length > 0 && rule.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                ImGui.PushID(i);

                // Controls first at fixed positions so long rule names (placed last) can overflow
                // harmlessly without pushing the interactive widgets off the right edge.
                bool en = rule.Enabled;
                if (ImGui.Checkbox("##en", ref en) && en != rule.Enabled)
                {
                    var c = CloneDisplayRule(rule);
                    c.Enabled = en;
                    _displayRules.Update(i, c);
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                    ImGui.SetTooltip("Active: rule can match. Paused: skipped — next rule below can match.");

                ImGui.SameLine();
                bool hide = rule.Hide;
                if (ImGui.Checkbox("H##hide", ref hide) && hide != rule.Hide)
                {
                    var c = CloneDisplayRule(rule);
                    c.Hide = hide;
                    _displayRules.Update(i, c);
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                    ImGui.SetTooltip("Don't show on map when this rule matches.");

                ImGui.SameLine();
                bool nav = rule.Navigable;
                if (ImGui.Checkbox("Nav##nav", ref nav) && nav != rule.Navigable)
                {
                    var c = CloneDisplayRule(rule);
                    c.Navigable = nav;
                    _displayRules.Update(i, c);
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                    ImGui.SetTooltip("Show path to this (needs Auto-path to nearest targets enabled above).");

                ImGui.SameLine();
                DrawRuleSpriteButton(i, rule);

                var col = ParseHexColor(rule.Color);
                ImGui.SameLine();
                if (ImGui.ColorEdit3("##col", ref col, ImGuiColorEditFlags.NoInputs))
                {
                    var c = CloneDisplayRule(rule);
                    c.Color = FormatHexColor3(col);
                    _displayRules.Update(i, c);
                }

                // Opacity / size: mutate the shared rule object live (the renderer reads these action
                // fields off the resolved rule object each frame), but only persist + recompile on
                // release so a drag doesn't rewrite display_rules.json every frame.
                float op = rule.Opacity;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(58f);
                if (ImGui.DragFloat("##op", ref op, 0.01f, 0f, 1f, "a%.2f"))
                    rule.Opacity = Math.Clamp(op, 0f, 1f);
                if (ImGui.IsItemDeactivatedAfterEdit())
                    _displayRules.Update(i, CloneDisplayRule(rule));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Opacity (0-1)");

                float sz = rule.Size;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(58f);
                if (ImGui.DragFloat("##sz", ref sz, 0.1f, 1f, 24f, "sz%.1f"))
                    rule.Size = Math.Clamp(sz, 1f, 24f);
                if (ImGui.IsItemDeactivatedAfterEdit())
                    _displayRules.Update(i, CloneDisplayRule(rule));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Icon size (px)");

                float sprScale = rule.Sprite?.Scale ?? 1.25f;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(52f);
                if (ImGui.DragFloat("##sprsc", ref sprScale, 0.02f, 0.2f, 4f, "s%.2f"))
                {
                    rule.Sprite ??= SpriteIconRef.Cell(0, 0, sprScale);
                    rule.Sprite.Scale = Math.Clamp(sprScale, 0.2f, 4f);
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                    _displayRules.Update(i, CloneDisplayRule(rule));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                    ImGui.SetTooltip("Sprite scale multiplier (PNG from icons.png)");

                ImGui.SameLine();
                ImGui.TextUnformatted(rule.Name.Length > 0 ? rule.Name : $"Rule {i}");

                ImGui.PopID();
            }

            ImGui.EndChild();
            DrawSpritePickerWindow();
        }

        DrawHideEntityHotkeyRow(s);
        DrawTrackEntityHotkeyRow(s);

        if (_hidden is null) return;

        if (ImGui.CollapsingHeader("Block list (never show)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Never show these anywhere — checked before radar rules.");
            ImGui.SetNextItemWidth(-80f);
            ImGui.InputTextWithHint("##hidepat", "e.g. AbyssCrack, *Daemon*", ref _hidePatternInput, 256);
            ImGui.SameLine();
            if (ImGui.Button("Add") && _hidePatternInput.Trim().Length > 0)
            {
                _hidden.Add(_hidePatternInput.Trim());
                _hidePatternInput = "";
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Substring or glob (* ?) — not on map, not in lists, not for paths.");

            foreach (var p in _hidden.All)
            {
                ImGui.TextUnformatted(p);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##{p}"))
                    _hidden.Remove(p);
            }
        }
    }

    /// <summary>Live inventory for the current zone type. Show/Nav toggles write per-area-code zone
    /// overrides — global semantic rules stay unchanged.</summary>
    private void DrawTypesInZone(RenderContext? ctx, RadarSettings s)
    {
        if (!ImGui.CollapsingHeader("Types in this zone", ImGuiTreeNodeFlags.DefaultOpen)) return;

        if (ctx is not { Entities.Count: > 0 })
        {
            ImGui.TextDisabled("No entities in range (enter a zone / move closer).");
            return;
        }
        var entities = ctx.Entities;
        var areaCode = ctx.AreaCode;

        ImGui.TextDisabled("Show on map · Path. Overrides apply to this zone type only.");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##typesearch", "search types…", ref _typeSearch, 128);

        // tier → token → (label, count, sample)
        var byTier = new Dictionary<EntityImportance, Dictionary<string, (string label, int count, Poe2Live.EntityDot sample)>>();
        foreach (var e in entities)
        {
            var tier = EntityImportanceHelper.Classify(e, ctx.Styles);
            if (s.ImportantOnly && EntityImportanceHelper.IsTrash(tier)) continue;

            var token = TypeToken(e.Metadata);
            if (token.Length == 0) continue;

            if (!byTier.TryGetValue(tier, out var bucket))
                byTier[tier] = bucket = new Dictionary<string, (string, int, Poe2Live.EntityDot)>(StringComparer.Ordinal);

            var label = EntityDisplayHelper.FormatEntityLabel(e, ctx.Resolve?.Invoke(e), entities, areaCode);
            if (label.Length == 0) label = token;

            if (bucket.TryGetValue(token, out var g))
                bucket[token] = (g.label, g.count + 1, g.sample);
            else
                bucket[token] = (label, 1, e);
        }

        var filter = _typeSearch.Trim();
        ImGui.BeginChild("TypesInZone", new NumVec2(0, 280));

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

            if (ImGui.CollapsingHeader($"{EntityImportanceHelper.TierLabel(tier)} ({tierCount})", ImGuiTreeNodeFlags.DefaultOpen))
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

    private void DrawTierGroupToggles(EntityImportance tier)
    {
        var names = EntityImportanceHelper.RuleNamesForTier(tier);
        if (names.Length == 0) return;

        var (shown, nav) = GetGroupRuleState(names);
        bool show = shown;
        if (ImGui.Checkbox("Show##grp", ref show) && show != shown)
            ApplyToRulesByNames(names, hide: !show, navigable: nav);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Show/hide entire tier via category default rules");

        ImGui.SameLine();
        bool navOn = nav;
        if (ImGui.Checkbox("Nav##grp", ref navOn) && navOn != nav)
            ApplyToRulesByNames(names, hide: !shown, navigable: navOn);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Auto-path to this tier via category default rules");

        ImGui.SameLine();
        ImGui.TextDisabled("tier defaults");
    }

    private void DrawTypeRow(RenderContext ctx, string token, string label, int count, Poe2Live.EntityDot sample)
    {
        ImGui.PushID(token);
        var areaCode = ctx.AreaCode;
        var globalRule = _ruleEngine?.ResolveGlobal(sample);
        var mergedRule = ctx.Resolve?.Invoke(sample);
        var hasZoneOverride = _zoneOverrides?.HasOverride(areaCode, token) ?? false;
        var rawHide = mergedRule is { Hide: true };
        var rawNav = mergedRule?.Navigable ?? false;
        var shownNow = !rawHide;
        var navNow = !rawHide && rawNav;

        bool show = shownNow;
        if (ImGui.Checkbox("##show", ref show) && show != shownNow)
            ApplyZoneOverride(areaCode, token, hide: !show, navigable: rawNav, globalRule);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Draw on minimap + large map (zone override for this area type)");

        ImGui.SameLine();
        bool nav = navNow;
        if (ImGui.Checkbox("##nav", ref nav) && nav != navNow)
            ApplyZoneOverride(areaCode, token, hide: rawHide, navigable: nav, globalRule);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Include in auto-path / F6 (zone override for this area type)");

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

    private void DrawRadarTab(RadarSettings s)
    {
        if (ImGui.CollapsingHeader("Display", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool sm = s.ShowMonsters; ImGui.Checkbox("Show Monsters", ref sm); s.ShowMonsters = sm;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Draw entity dots (monsters, NPCs, chests) on the map overlay");

            bool st = s.ShowTerrain; ImGui.Checkbox("Show Terrain", ref st); s.ShowTerrain = st;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Draw walkable terrain boundary edges on the map overlay");

            bool sb = s.ShowPlayerBlip; ImGui.Checkbox("Player Blip", ref sb); s.ShowPlayerBlip = sb;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Show a cyan dot at your position on the map overlay");

            bool sp = s.ShowPath; ImGui.Checkbox("Show Paths", ref sp); s.ShowPath = sp;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Draw guidance route polylines between you and selected targets");

            bool hj = s.HideJunk; ImGui.Checkbox("Hide map clutter", ref hj); s.HideJunk = hj;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Hide cosmetic FX, daemons, and other noise dots on the map.");

            bool cl = s.UseCuratedLandmarks; ImGui.Checkbox("Curated Landmarks", ref cl); s.UseCuratedLandmarks = cl;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Use community-curated friendly names for landmarks instead of raw tile paths");

            bool pf = s.ShowPerfStats; ImGui.Checkbox("Perf Stats", ref pf); s.ShowPerfStats = pf;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Show FPS, timing, and memory read counters in the navigation menu");

            bool ao = s.AlwaysShowOverlay; ImGui.Checkbox("Always Show Overlay", ref ao); s.AlwaysShowOverlay = ao;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Keep the overlay visible even when PoE2 is not the foreground window");

            int fps = s.FpsCap; ImGui.SliderInt("FPS Cap", ref fps, 15, 360); s.FpsCap = fps;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Maximum render rate in Hz — higher is smoother but more GPU load");

            int lcg = s.LandmarkClusterGap; ImGui.SliderInt("Cluster Gap", ref lcg, 0, 64); s.LandmarkClusterGap = lcg;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Max tile distance for merging nearby landmarks into one marker (0 = disable clustering)");
        }

        if (ImGui.CollapsingHeader("Map Calibration"))
        {
            float smul = s.ScaleMul; ImGui.SliderFloat("Scale", ref smul, 0.1f, 3f, "%.2f"); s.ScaleMul = smul;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Adjust the map overlay zoom multiplier relative to the game's minimap");

            float ox = s.OffX; ImGui.SliderFloat("Offset X", ref ox, -200f, 200f, "%.0f"); s.OffX = ox;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Shift the entire map overlay horizontally in pixels");

            float oy = s.OffY; ImGui.SliderFloat("Offset Y", ref oy, -200f, 200f, "%.0f"); s.OffY = oy;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Shift the entire map overlay vertically in pixels");
        }

        if (ImGui.CollapsingHeader("Terrain"))
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
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Color and opacity for the interior of walkable terrain cells");

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
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Color and opacity for walkable terrain boundary edges");
        }

        if (ImGui.CollapsingHeader("Radar rules (web dashboard)"))
        {
            ImGui.TextDisabled("Edit radar rules in the web dashboard (F12) or display_rules.json");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Open the web dashboard (F12) to edit what shows on the map — icons, colors, hide, and paths.");
        }

        if (ImGui.Button("Save Settings"))
            _enqueue(() => SaveSettings());
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Write all current settings to config/radar_settings.json");
    }

    private void DrawHpBarsTab(RadarSettings s)
    {
        if (ImGui.CollapsingHeader("Rarity Toggles", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool bn = s.HpBarNormal; ImGui.Checkbox("Normal", ref bn); s.HpBarNormal = bn;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Show HP bars on normal (white name) monsters");

            ImGui.SameLine(); bool bm = s.HpBarMagic; ImGui.Checkbox("Magic", ref bm); s.HpBarMagic = bm;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Show HP bars on magic (blue name) monsters");

            ImGui.SameLine(); bool br = s.HpBarRare; ImGui.Checkbox("Rare", ref br); s.HpBarRare = br;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Show HP bars on rare (yellow name) monsters");

            ImGui.SameLine(); bool bu = s.HpBarUnique; ImGui.Checkbox("Unique", ref bu); s.HpBarUnique = bu;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Show HP bars on unique (orange name) bosses and monsters");
        }

        if (ImGui.CollapsingHeader("Textures", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var hb = s.HpBars;
            bool textures = hb.UseTextures;
            if (ImGui.Checkbox("Use bar textures", ref textures))
                hb.UseTextures = textures;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Draw gradient HP/ES bars from Overlay/Textures/full_bar.png and hollow_bar.png");
        }

        if (ImGui.CollapsingHeader("Bar Geometry", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var hb = s.HpBars;
            float w = hb.WidthNormal; ImGui.SliderFloat("Width Normal", ref w, 30f, 250f); hb.WidthNormal = w;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("HP bar width in pixels for normal monsters");

            w = hb.WidthMagic; ImGui.SliderFloat("Width Magic", ref w, 30f, 250f); hb.WidthMagic = w;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("HP bar width in pixels for magic monsters");

            w = hb.WidthRare; ImGui.SliderFloat("Width Rare", ref w, 30f, 250f); hb.WidthRare = w;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("HP bar width in pixels for rare monsters");

            w = hb.WidthUnique; ImGui.SliderFloat("Width Unique", ref w, 30f, 250f); hb.WidthUnique = w;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("HP bar width in pixels for unique monsters and bosses");

            ImGui.Separator();
            float h = hb.Height; ImGui.SliderFloat("Bar Height", ref h, 2f, 12f); hb.Height = h;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("HP bar height in pixels — applies to all rarities");

            float ox = hb.OffsetX; ImGui.SliderFloat("Offset X", ref ox, -50f, 50f); hb.OffsetX = ox;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Horizontal offset from the monster's world position in pixels");

            float oy = hb.OffsetY; ImGui.SliderFloat("Offset Y", ref oy, -100f, 50f); hb.OffsetY = oy;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Vertical offset from the monster's world position — negative = above, positive = below");
        }
    }

    private void DrawFlaskTab(RadarSettings s)
    {
        if (ImGui.CollapsingHeader("Life Flask", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int mode = s.LifeFlaskMode switch { "EnergyShield" => 1, "Either" => 2, _ => 0 };
            ImGui.Combo("Trigger Pool", ref mode, "Health\0Energy Shield\0Either\0");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Which resource pool triggers the life flask key — Health only, Energy Shield only, or Either");
            s.LifeFlaskMode = mode switch { 1 => "EnergyShield", 2 => "Either", _ => "Health" };

            float lt = s.LifeThresholdPct; ImGui.SliderFloat("Life Threshold %", ref lt, 0f, 100f, "%.0f"); s.LifeThresholdPct = lt;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Use life flask when HP falls below this percentage");

            float et = s.EsThresholdPct; ImGui.SliderFloat("ES Threshold %", ref et, 0f, 100f, "%.0f"); s.EsThresholdPct = et;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Use life flask when energy shield falls below this percentage (only in ES or Either mode)");

            int lc = s.LifeCooldownMs; ImGui.SliderInt("Cooldown ms", ref lc, 200, 10000); s.LifeCooldownMs = lc;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Minimum delay between life flask activations in milliseconds");

            int lk = s.LifeKey; ImGui.InputInt("Key code (hex)", ref lk, 1, 16); if (lk > 0) s.LifeKey = lk;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Win32 virtual-key code for the life flask key — 0x31 = '1', 0x32 = '2', 0x33 = '3'");
        }

        if (ImGui.CollapsingHeader("Mana Flask", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float mt = s.ManaThresholdPct; ImGui.SliderFloat("Mana Threshold %", ref mt, 0f, 100f, "%.0f"); s.ManaThresholdPct = mt;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Use mana flask when mana falls below this percentage");

            int mc = s.ManaCooldownMs; ImGui.SliderInt("Cooldown ms", ref mc, 200, 10000); s.ManaCooldownMs = mc;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Minimum delay between mana flask activations in milliseconds");

            int mk = s.ManaKey; ImGui.InputInt("Key code (hex)", ref mk, 1, 16); if (mk > 0) s.ManaKey = mk;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Win32 virtual-key code for the mana flask key — 0x31 = '1', 0x32 = '2', 0x33 = '3'");
        }

        if (ImGui.CollapsingHeader("Status"))
        {
            ImGui.BulletText("F8 toggles auto-flask on/off. Settings apply immediately.");
            ImGui.BulletText("Keys are Win32 virtual-key codes (0x31 = '1', 0x32 = '2').");
        }
    }

    private void DrawAtlasTab(RadarSettings s)
    {
        if (ImGui.CollapsingHeader("Display", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool son = s.AtlasShowOnScreenNodes;
            var trackActive = s.AtlasHighlightTags is { Count: > 0 };
            if (trackActive) ImGui.BeginDisabled();
            if (ImGui.Checkbox("Show all nodes", ref son)) { s.AtlasShowOnScreenNodes = son; SaveSettings(); }
            if (trackActive) ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip(trackActive
                    ? "Track filters are active — only tracked nodes are shown. Clear filters or uncheck Track to show all again."
                    : "Draw every atlas tile on the current screen");

            bool sn = s.AtlasShowNames;
            if (ImGui.Checkbox("Show names", ref sn)) { s.AtlasShowNames = sn; SaveSettings(); }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Label on-screen nodes with map name (and highlight tag when matched)");

            bool rf = s.AtlasRevealFog;
            if (ImGui.Checkbox("Reveal fog", ref rf)) { s.AtlasRevealFog = rf; SaveSettings(); }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Draw fogged/hidden nodes at full opacity with a cool tint");

            bool oa = s.AtlasOffScreenArrows;
            if (ImGui.Checkbox("Off-screen arrows (highlights)", ref oa)) { s.AtlasOffScreenArrows = oa; SaveSettings(); }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Edge arrows for arrow-tagged highlights only (e.g. Citadels off-screen)");

            bool sr = s.AtlasShowRoute;
            if (ImGui.Checkbox("Show F10 route", ref sr)) { s.AtlasShowRoute = sr; SaveSettings(); }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Draw the F10 route through the atlas node connection graph");

            bool ucs = s.AtlasUseCurrentStart;
            if (ImGui.Checkbox("Route from current tile", ref ucs)) { s.AtlasUseCurrentStart = ucs; SaveSettings(); }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("When no F10 START is set, route from your current atlas position");

            float atlasIcon = s.AtlasIconScale;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Icon scale", ref atlasIcon, 0.5f, 3f, "%.2f"))
            {
                s.AtlasIconScale = Math.Clamp(atlasIcon, 0.25f, 4f);
                SaveSettings();
            }

            float atlasLabel = s.AtlasLabelScale;
            ImGui.SetNextItemWidth(180f);
            if (ImGui.SliderFloat("Label scale", ref atlasLabel, 0.5f, 2.5f, "%.2f"))
            {
                s.AtlasLabelScale = Math.Clamp(atlasLabel, 0.5f, 3f);
                SaveSettings();
            }
        }

        if (ImGui.CollapsingHeader("Highlights", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (s.AtlasHighlightTags is { Count: > 0 })
                ImGui.TextDisabled("Tracked-only mode — only nodes matching Track filters are drawn.");
            if (ImGui.Button("Add citadel defaults"))
            {
                SeedAtlasCitadelDefaults(s);
                s.AtlasRulesInitialized = true;
                SaveSettings();
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort)) ImGui.SetTooltip("Track + arrow every Citadel map name (gold ring)");
            ImGui.SameLine();
            if (ImGui.Button("Add endgame defaults"))
            {
                AtlasEndgameCatalog.ApplyEndgameDefaults(s, _ctx?.AtlasTagCatalog);
                s.AtlasRulesInitialized = true;
                SaveSettings();
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Highlight Patriarch/Matriarch Halls, Origin Tower, Citadels, Enigma Chambers, and boss content — for repeat Arbiter of Divinity runs");
            ImGui.SameLine();
            if (ImGui.Button("Clear all filters"))
            {
                s.AtlasHighlightTags.Clear();
                s.AtlasArrowTags.Clear();
                s.AtlasHighlightColors.Clear();
                s.AtlasRulesInitialized = true;
                SaveSettings();
            }

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##atlasTagFilter", "Filter tags and map names…", ref _atlasTagFilter, 128);

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
                    ImGui.TableHeadersRow();

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
                            SaveSettings();
                        }

                        ImGui.TableSetColumnIndex(4);
                        bool arrow = AtlasListContains(s.AtlasArrowTags, e.Key);
                        if (ImGui.Checkbox($"##ar_{e.Key}", ref arrow))
                        {
                            AtlasToggleList(s.AtlasArrowTags, e.Key, arrow);
                            s.AtlasRulesInitialized = true;
                            SaveSettings();
                        }

                        ImGui.TableSetColumnIndex(5);
                        if (track)
                        {
                            var hex = s.AtlasHighlightColors.TryGetValue(e.Key, out var hc) ? hc : "#58A6FF";
                            var col = ParseHexColor(hex);
                            ImGui.PushID(e.Key);
                            if (ImGui.ColorEdit3("##col", ref col, ImGuiColorEditFlags.NoInputs))
                            {
                                s.AtlasHighlightColors[e.Key] = FormatHexColor3(col);
                                SaveSettings();
                            }
                            ImGui.PopID();
                        }
                    }
                    ImGui.EndTable();
                }
            }
        }
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
        lock (_settingsLock) _settings.Save();
    }

    private void DrawHideEntityHotkeyRow(RadarSettings s)
    {
        if (!ImGui.CollapsingHeader("Hide under cursor", ImGuiTreeNodeFlags.DefaultOpen)) return;

        ImGui.PushID("HideHotkey");
        ImGui.TextUnformatted("Hotkey:");
        ImGui.SameLine();
        if (_bindingHideHotkey)
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), "Press a key…");
        else
            ImGui.TextUnformatted(VirtualKeyHelper.Name(s.HideEntityHotkey));
        ImGui.SameLine();
        if (ImGui.Button(_bindingHideHotkey ? "…##bind" : "Bind##bind"))
            _bindingHideHotkey = true;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Bind hotkey: hover an entity on the map, press the key to add its type to the block list.");
        ImGui.SameLine();
        if (ImGui.Button("Clear##clear") && s.HideEntityHotkey > 0)
        {
            s.HideEntityHotkey = 0;
            _bindingHideHotkey = false;
            SaveSettings();
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Disable hide-under-cursor hotkey");

        ImGui.TextWrapped("Hover an entity (map, minimap, or 3D view) and press the hotkey to hide that type globally.");
        ImGui.PopID();
    }

    private void DrawTrackEntityHotkeyRow(RadarSettings s)
    {
        if (!ImGui.CollapsingHeader("Inspect under cursor", ImGuiTreeNodeFlags.DefaultOpen)) return;

        ImGui.PushID("TrackHotkey");
        ImGui.TextUnformatted("Hotkey:");
        ImGui.SameLine();
        if (_bindingTrackHotkey)
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.2f, 1f), "Press a key…");
        else
            ImGui.TextUnformatted(VirtualKeyHelper.Name(s.TrackEntityHotkey));
        ImGui.SameLine();
        if (ImGui.Button(_bindingTrackHotkey ? "…##bind" : "Bind##bind"))
            _bindingTrackHotkey = true;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Bind hotkey: hover an entity and press to print its identity to the console (F4 default).");
        ImGui.SameLine();
        if (ImGui.Button("Clear##clear") && s.TrackEntityHotkey > 0)
        {
            s.TrackEntityHotkey = 0;
            _bindingTrackHotkey = false;
            SaveSettings();
        }

        ImGui.TextWrapped("Prints entity info to the console (map, minimap, or 3D view). Does not add path targets — use legend, F6, or Types in zone to track.");
        ImGui.PopID();
    }

    private void PollHotkeyCapture(RadarSettings s)
    {
        if (!_bindingHideHotkey && !_bindingTrackHotkey) return;

        for (var vk = 0x08; vk <= 0xFE; vk++)
        {
            if (vk is 0x0A or 0x0B) continue;
            if (vk is >= 0x01 and <= 0x06) continue;
            if ((OverlayNative.GetAsyncKeyState(vk) & 0x8000) == 0) continue;

            if (_bindingHideHotkey)
            {
                s.HideEntityHotkey = vk;
                _bindingHideHotkey = false;
            }
            else
            {
                s.TrackEntityHotkey = vk;
                _bindingTrackHotkey = false;
            }

            SaveSettings();
            return;
        }
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
            NavTargetStatus.NoPath => " (no path)",
            _ => "",
        };
        return $"{prefix}[{type}] {row.Target.Name}{dist}{status}";
    }

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
        if (EndgameMechanicCatalog.TryMatch(e, out var def))
            return (def!.Sprite.Clone(), def.Shape, def.Size, def.Color, def.Opacity);

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
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("PNG sprite from icons.png (atlas loads on first in-game frame)");
            return;
        }

        if (ImGui.InvisibleButton("##sprbtn", size))
            _spritePickerRuleIndex = ruleIndex;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("PNG sprite from icons.png — click to pick cell");

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

        ImGui.SetNextWindowSize(new NumVec2(520, 420), ImGuiCond.FirstUseEver);
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
        Encounter = r.Encounter,
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
