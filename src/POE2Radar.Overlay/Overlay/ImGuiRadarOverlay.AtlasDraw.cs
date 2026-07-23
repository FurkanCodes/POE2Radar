using System.Diagnostics;
using System.Globalization;
using ImGuiNET;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Navigation;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class ImGuiRadarOverlay
{
    private const int AtlasChannelLines = 1;
    private const int AtlasChannelDots = 2;
    private const int AtlasChannelLabels = 3;

    private string? _atlasHoverTooltipName;
    private string? _atlasHoverTooltipDesc;
    private readonly Dictionary<string, int> _atlasRitualSelected = new(StringComparer.Ordinal);
    private static readonly string[] AtlasRitualPalette =
    [
        "#FFD933", "#FF8026", "#FF4D4D", "#4DD9FF", "#73FF73", "#E673FF",
    ];

    private void DrawAtlas(ImDrawListPtr dl, RenderContext ctx)
    {
        var sw = Stopwatch.GetTimestamp();
        var W = ctx.WindowWidth;
        var H = ctx.WindowHeight;
        var uiScale = ctx.AtlasUiScale * ctx.AtlasLabelScale;
        _atlasHoverTooltipName = null;
        _atlasHoverTooltipDesc = null;

        dl.ChannelsSplit(4);

        var routePhase = 0;
        if (ctx.AtlasRoutes is { Count: > 0 } routeLines)
        {
            foreach (var line in routeLines)
            {
                var thickness = MathF.Max(1f, uiScale * line.Thickness);
                DrawAtlasNodePath(dl, line.Points, ColorU32(line.Color, 0.95f), thickness, uiScale,
                    ctx.AtlasRouteChevronSpacing, ctx.AtlasShowRouteChevrons, line.PhaseIndex);
                routePhase++;
            }
        }

        if (ctx.AtlasRoute is { Count: >= 2 } manualRoute)
        {
            var thickness = MathF.Max(1f, uiScale * ctx.AtlasRouteLineThickness);
            var col = ColorU32(59, 219, 255, 0.95f);
            DrawAtlasNodePath(dl, manualRoute, col, thickness, uiScale, ctx.AtlasRouteChevronSpacing,
                ctx.AtlasShowRouteChevrons, routePhase);
        }

        DrawAtlasRitualSelectedPaths(dl, ctx, uiScale);

        if (ctx.AtlasStart is { } startPt)
        {
            dl.ChannelsSetCurrent(AtlasChannelDots);
            var sr = MathF.Max(3f, uiScale * ctx.AtlasRouteLineThickness * 1.3f);
            dl.AddCircleFilled(startPt, sr, ColorU32(51, 255, 51, 1f), 12);
            dl.AddCircle(startPt, sr, ColorU32(0, 0, 0, 0.72f), 12, MathF.Max(1f, sr * 0.35f));
        }

        if (ctx.AtlasEnd is { } endPt)
        {
            dl.ChannelsSetCurrent(AtlasChannelDots);
            dl.AddCircle(endPt, 11f, ColorU32(224, 179, 65, 1f), 0, 3f);
            dl.AddCircle(endPt, 4f, ColorU32(224, 179, 65, 1f), 0, 2f);
        }

        if (ctx.AtlasCurrent is { } cur)
        {
            dl.ChannelsSetCurrent(AtlasChannelDots);
            var r = MathF.Max(3f, uiScale * 4f);
            dl.AddCircleFilled(cur, r, ColorU32(255, 77, 77, 1f), 16);
            dl.AddCircle(cur, r, ColorU32(0, 0, 0, 0.72f), 16, MathF.Max(1f, r * 0.35f));
        }

        var mousePos = ImGui.GetMousePos();
        if (ctx.AtlasNodes is { Count: > 0 } marks)
        {
            foreach (var n in marks)
                DrawAtlasNode(dl, ctx, n, W, H, uiScale, mousePos);
        }

        var hoveredShip = DrawAtlasFogShipsAndLeylines(dl, ctx, mousePos, uiScale);

        if (ctx.AtlasShowRitualPrediction && ctx.AtlasRitualPredictions is { Count: > 0 } preds)
        {
            dl.ChannelsSetCurrent(AtlasChannelLabels);
            foreach (var p in preds)
            {
                var pos = new NumVec2(p.ScreenX, p.ScreenY - 18f * uiScale);
                dl.AddText(pos, ColorU32("#6EEB87", 0.95f), p.Text);
            }
        }

        dl.ChannelsMerge();

        if (ctx.AtlasShowRitualPlanner && ctx.AtlasRitualLineActive && ctx.AtlasRitualPlannerRows is { Count: > 0 })
            DrawAtlasRitualPlannerWindow(ctx);

        if (ctx.AtlasShowIslandRumours && hoveredShip?.Manifest is { } manifest)
        {
            DrawIslandRumourTooltip(manifest);
        }
        else if (_atlasHoverTooltipName is { Length: > 0 })
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(_atlasHoverTooltipName);
            if (_atlasHoverTooltipDesc is { Length: > 0 })
            {
                ImGui.Separator();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 22f);
                ImGui.TextUnformatted(_atlasHoverTooltipDesc);
                ImGui.PopTextWrapPos();
            }
            ImGui.EndTooltip();
        }

        LastAtlasDrawMs = (float)Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
    }

    private AtlasFogShip? DrawAtlasFogShipsAndLeylines(
        ImDrawListPtr dl,
        RenderContext ctx,
        NumVec2 mousePos,
        float uiScale)
    {
        if (ctx.AtlasFogShips is not { Count: > 0 } ships) return null;

        int? hoverChunkX = null, hoverChunkY = null;
        AtlasFogShip? hoveredShip = null;
        var priorityColor = ctx.AtlasShowIslandRumours
            ? ColorU32(ctx.AtlasIslandRumourPriorityColor, 0.98f)
            : 0u;
        var moorColor = ctx.AtlasShowIslandRumours
            ? ColorU32(AtlasIslandRumours.MoorPriorityColor, 1f)
            : 0u;
        dl.ChannelsSetCurrent(AtlasChannelLabels);
        foreach (var ship in ships)
        {
            var c = new NumVec2(ship.ScreenX, ship.ScreenY);
            var h = MathF.Max(8f, ship.Size * uiScale);
            var hasMoor = ship.Manifest?.HasMoorOfFallenSkies == true;
            var halfX = ship.HitWidth > 0f ? ship.HitWidth * 0.5f : h * 0.5f;
            var halfY = ship.HitHeight > 0f ? ship.HitHeight * 0.5f : h * 0.5f;
            if (ship.DrawIcon
                && AtlasContentIcons.TryGetPath("UnchartedShip", out var path)
                && _textures.TryGet(this, path, out var tex)
                && tex.Height > 0)
            {
                var aspect = tex.Width / (float)Math.Max(1, tex.Height);
                var w = h * aspect;
                dl.AddImage(tex.Id, c - new NumVec2(w, h) * 0.5f, c + new NumVec2(w, h) * 0.5f);
                if (ship.HitWidth <= 0f) halfX = w * 0.5f;
                if (ship.HitHeight <= 0f) halfY = h * 0.5f;
            }
            else if (ship.DrawIcon)
            {
                var r = h * 0.35f;
                dl.AddCircleFilled(c, r, ColorU32("#0A1420", 0.9f), 16);
                dl.AddCircle(c, r, ColorU32(ctx.AtlasUnchartedLeylineColor, 0.95f), 16, MathF.Max(1.5f, r * 0.25f));
                if (ship.HitWidth <= 0f) halfX = r;
                if (ship.HitHeight <= 0f) halfY = r;
            }

            if (ship.DrawIcon && hasMoor)
            {
                // Moor is the top farming target, so it gets an exclusive static double ring and label.
                // This is derived from the cached manifest and adds no game-memory reads or animation.
                dl.AddCircle(c, h * 0.68f, moorColor, 32, MathF.Max(2.5f, 3f * uiScale));
                dl.AddCircle(c, h * 0.57f, ColorU32("#FFE66D", 0.98f), 32, MathF.Max(1.5f, 1.75f * uiScale));
                const string moorLabel = "S+ MOOR";
                var moorLabelSize = ImGui.CalcTextSize(moorLabel);
                var moorLabelPadding = new NumVec2(5f, 2f) * uiScale;
                var moorLabelPos = c + new NumVec2(-moorLabelSize.X * 0.5f, h * 0.56f);
                dl.AddRectFilled(
                    moorLabelPos - moorLabelPadding,
                    moorLabelPos + moorLabelSize + moorLabelPadding,
                    ColorU32("#100814", 0.96f),
                    3f * uiScale);
                dl.AddRect(
                    moorLabelPos - moorLabelPadding,
                    moorLabelPos + moorLabelSize + moorLabelPadding,
                    moorColor,
                    3f * uiScale);
                dl.AddText(moorLabelPos, moorColor, moorLabel);
            }
            else if (ship.DrawIcon && ship.Priority)
            {
                dl.AddCircle(c, h * 0.58f, priorityColor, 24, MathF.Max(2f, 2.5f * uiScale));
            }

            if (ship.DrawIcon
                && ctx.AtlasShowIslandRumourBadges
                && ship.Manifest is { TotalIslands: > 0 } badgeManifest)
            {
                var badgeRadius = MathF.Max(8f, 9f * uiScale);
                var badgeCenter = c + new NumVec2(h * 0.42f, -h * 0.36f);
                var badgeColor = hasMoor
                    ? moorColor
                    : ship.Priority
                        ? priorityColor
                        : ColorU32(ctx.AtlasUnchartedLeylineColor, 0.98f);
                dl.AddCircleFilled(badgeCenter, badgeRadius, ColorU32("#08111C", 0.96f), 16);
                dl.AddCircle(badgeCenter, badgeRadius, badgeColor, 16, MathF.Max(1f, 1.5f * uiScale));
                var fontSize = ImGui.GetFontSize();
                var textWidth = badgeManifest.BadgeText.Length * fontSize * 0.55f;
                dl.AddText(
                    badgeCenter - new NumVec2(textWidth * 0.5f, fontSize * 0.5f),
                    ColorU32(255, 255, 255, 1f),
                    badgeManifest.BadgeText);
            }

            if (mousePos.X >= c.X - halfX && mousePos.X <= c.X + halfX
                && mousePos.Y >= c.Y - halfY && mousePos.Y <= c.Y + halfY)
            {
                hoverChunkX = ship.ChunkX;
                hoverChunkY = ship.ChunkY;
                hoveredShip = ship;
            }
        }

        if (!ctx.AtlasShowUnchartedLeylines || ctx.AtlasLeylines is not { Count: > 0 } segs)
            return hoveredShip;
        if (hoverChunkX is null)
            return hoveredShip;

        dl.ChannelsSetCurrent(AtlasChannelLines);
        var col = ColorU32(ctx.AtlasUnchartedLeylineColor, 0.9f);
        var th = MathF.Max(1f, ctx.AtlasUnchartedLeylineThickness * uiScale);
        var points = new HashSet<NumVec2>();
        foreach (var s in segs)
        {
            if (s.ChunkX != hoverChunkX || s.ChunkY != hoverChunkY) continue;
            var p0 = new NumVec2(s.X0, s.Y0);
            var p1 = new NumVec2(s.X1, s.Y1);
            dl.AddLine(p0, p1, col, th);
            points.Add(p0);
            points.Add(p1);
        }
        var nodeRadius = MathF.Max(2.5f, th * 0.55f);
        foreach (var point in points)
            dl.AddCircleFilled(point, nodeRadius, col, 12);
        return hoveredShip;
    }

    private static void DrawIslandRumourTooltip(AtlasIslandRumours.Manifest manifest)
    {
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(manifest.TitleLine);
        ImGui.Separator();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 34f);
        foreach (var row in manifest.Rows)
        {
            var tierRgb = ParseHexColor(row.Definition.TierColor);
            ImGui.TextColored(
                new System.Numerics.Vector4(tierRgb.X, tierRgb.Y, tierRgb.Z, 1f),
                $"[{row.Definition.Tier}] {row.TitleLine}");
            ImGui.TextDisabled(row.TierLine);
            ImGui.TextWrapped(row.DetailLine);
            ImGui.TextColored(
                new System.Numerics.Vector4(0.42f, 0.86f, 1f, 1f),
                row.PreparationLine);
            ImGui.TextWrapped($"Tablet: {row.Definition.Preparation.Tablets}");
            ImGui.TextWrapped($"Waystone: {row.Definition.Preparation.Waystone}");
            if (!ReferenceEquals(row, manifest.Rows[^1]))
                ImGui.Spacing();
        }
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private void DrawAtlasRitualPlannerWindow(RenderContext ctx)
    {
        var open = true;
        ImGui.SetNextWindowSize(new NumVec2(760, 500), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Ritual line rewards", ref open, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }
        ImGui.TextDisabled(ctx.AtlasRitualLineActive ? "Ritual line mode active" : "Open ritual line on the atlas");
        var rows = ctx.AtlasRitualPlannerRows ?? Array.Empty<AtlasRitualPlannerRow>();
        var alive = rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _atlasRitualSelected.Keys.Where(key => !alive.Contains(key)).ToArray())
            _atlasRitualSelected.Remove(stale);
        var filter = (_settings.AtlasRitualRewardFilter ?? "")
            .Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var visible = rows.Where(row => _atlasRitualSelected.ContainsKey(row.Key)
            || filter.Length == 0
            || filter.Any(term => row.ModsLine.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        ImGui.TextDisabled($"Shown: {visible.Length}  |  Chains: {rows.Count}");
        ImGui.Separator();
        if (ImGui.BeginTable("ritualPlanner", 4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new NumVec2(0, 410)))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 28f);
            ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Rewards", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Wt", ImGuiTableColumnFlags.WidthFixed, 36f);
            ImGui.TableHeadersRow();
            foreach (var row in visible)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                var selected = _atlasRitualSelected.ContainsKey(row.Key);
                if (ImGui.Checkbox($"##ritual-{row.Key}", ref selected))
                {
                    if (selected)
                        _atlasRitualSelected[row.Key] = NextAtlasRitualPaletteSlot();
                    else
                        _atlasRitualSelected.Remove(row.Key);
                }
                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(row.PathLine);
                ImGui.TableSetColumnIndex(2);
                ImGui.TextWrapped(row.ModsLine);
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(row.Weight.ToString());
            }
            ImGui.EndTable();
        }
        ImGui.End();
    }

    private void DrawAtlasRitualSelectedPaths(ImDrawListPtr dl, RenderContext ctx, float uiScale)
    {
        if (_atlasRitualSelected.Count == 0 || ctx.AtlasRitualPlannerRows is not { Count: > 0 } rows)
            return;
        foreach (var row in rows)
        {
            if (!_atlasRitualSelected.TryGetValue(row.Key, out var slot) || row.Points.Count < 2)
                continue;
            var colorHex = AtlasRitualPalette[slot % AtlasRitualPalette.Length];
            var color = ColorU32(colorHex, 0.95f);
            DrawAtlasNodePath(dl, row.Points, color, MathF.Max(2f, 2.5f * uiScale), uiScale,
                spacingMul: 24f, chevrons: true, phaseIndex: slot);

            dl.ChannelsSetCurrent(AtlasChannelLabels);
            for (var index = 1; index < row.Points.Count && index - 1 < row.Rewards.Count; index++)
            {
                var label = row.Rewards[index - 1];
                var size = ImGui.CalcTextSize(label);
                var padding = new NumVec2(4f, 2f) * uiScale;
                var position = row.Points[index] - new NumVec2(size.X * 0.5f, size.Y + 12f * uiScale);
                dl.AddRectFilled(position - padding, position + size + padding, ColorU32("#0D0D0D", 0.92f), 3f * uiScale);
                dl.AddRect(position - padding, position + size + padding, color, 3f * uiScale);
                dl.AddText(position, color, label);
            }
        }
    }

    private int NextAtlasRitualPaletteSlot()
    {
        var used = _atlasRitualSelected.Values.ToHashSet();
        for (var slot = 0; ; slot++)
            if (!used.Contains(slot))
                return slot;
    }

    private void DrawAtlasNode(ImDrawListPtr dl, RenderContext ctx, AtlasMark n, float W, float H, float uiScale, NumVec2 mousePos)
    {
        var sx = n.ScreenX;
        var sy = n.ScreenY;
        if (!float.IsFinite(sx) || !float.IsFinite(sy)) return;
        const float labelMargin = 256f;
        var onScreen = sx >= -labelMargin && sx <= W + labelMargin && sy >= -labelMargin && sy <= H + labelMargin;
        if (!onScreen)
        {
            if (ctx.AtlasOffScreenArrows && n.Arrow && n.HighlightLabel is { Length: > 0 } offLabel)
                DrawAtlasArrow(dl, sx, sy, W * 0.5f, H * 0.5f, W, H, ColorU32(string.IsNullOrEmpty(n.Color) ? "#3BDBFF" : n.Color, 0.85f), offLabel);
            return;
        }

        var fullyOnScreen = sx >= 0 && sx <= W && sy >= 0 && sy <= H;
        if (!fullyOnScreen)
        {
            if (ctx.AtlasOffScreenArrows && n.Arrow && n.HighlightLabel is { Length: > 0 } edgeLabel)
                DrawAtlasArrow(dl, sx, sy, W * 0.5f, H * 0.5f, W, H, ColorU32(string.IsNullOrEmpty(n.Color) ? "#3BDBFF" : n.Color, 0.85f), edgeLabel);
            return;
        }

        if (ctx.AtlasTrackedOnly && !n.Selected && !n.Arrow && string.IsNullOrEmpty(n.HighlightLabel) && string.IsNullOrEmpty(n.Color) && n.RouteHops <= 0) return;
        if (!ctx.AtlasShowOnScreenNodes && !n.Selected && !n.Arrow && string.IsNullOrEmpty(n.Color) && n.RouteHops <= 0) return;

        var c = new NumVec2(sx, sy);
        // Always draw a node marker so fogged / unnamed memory nodes stay visible.
        if (ctx.AtlasShowNodeSprites)
            DrawAtlasNodeSprite(dl, ctx, n, c);
        else
            DrawAtlasNodeDot(dl, ctx, n, c);

        string? mapName = null;
        if (ctx.AtlasShowNames)
            mapName = n.HighlightLabel ?? n.MapName;
        else if (n.HighlightLabel is { Length: > 0 })
            mapName = n.HighlightLabel;
        if (mapName is { Length: > 0 } && ctx.AtlasShowNames)
        {
            if (n.EndgameTier == AtlasEndgameTier.Pinnacle) mapName = "★ " + mapName;
            else if (n.EndgameTier == AtlasEndgameTier.KeyHalls) mapName = "◆ " + mapName;
        }

        // Dimmed search non-matches: keep the marker, skip label chrome to reduce clutter.
        if (n.Dimmed || string.IsNullOrEmpty(mapName)) return;

        var textSize = ImGui.CalcTextSize(mapName);
        var nudgeY = ctx.AtlasAnchorNudgeY + ctx.AtlasLabelOffsetY;
        var drawPos = new NumVec2(
            c.X - textSize.X * 0.5f + ctx.AtlasLabelOffsetX,
            c.Y - textSize.Y * 0.5f + nudgeY);
        var padding = new NumVec2(5f, 2f) * uiScale;
        var bgPos = drawPos - padding;
        var bgSize = textSize + padding * 2f;
        var rectCenterY = bgPos.Y + bgSize.Y * 0.5f;
        var labelCenterX = drawPos.X + textSize.X * 0.5f;

        var bgOpacity = n.Completed ? 0.34f : 0.85f;
        var fgOpacity = n.Completed ? 0.5f : 1f;
        var bgHex = n.LabelBg ?? ctx.AtlasDefaultBackgroundColor;
        var fgHex = n.LabelFg ?? ctx.AtlasDefaultFontColor;
        if (!n.Visible && ctx.AtlasRevealFog)
        {
            bgHex = "#9CB8D4";
            fgHex = "#E8F1F8";
            bgOpacity = MathF.Max(bgOpacity, 0.72f);
            fgOpacity = MathF.Max(fgOpacity, 0.9f);
        }

        if (n.ContentNames is { Count: > 0 } && (ctx.AtlasShowContentIcons || ctx.AtlasShowContentTokens))
            DrawAtlasContentRow(dl, ctx, n, drawPos, textSize, uiScale, mousePos, !n.Visible);

        dl.ChannelsSetCurrent(AtlasChannelLabels);

        if (ctx.AtlasShowBiomeBorders && n.BiomeColor is { Length: > 0 })
        {
            var biomeA = n.Completed ? n.BiomeAlpha * 0.4f : n.BiomeAlpha;
            var bTh = MathF.Max(1f, uiScale * ctx.AtlasBiomeBorderThickness);
            var half = bTh * 0.5f;
            var rounding = 3f * uiScale;
            dl.AddRect(bgPos - new NumVec2(half, half), bgPos + bgSize + new NumVec2(half, half),
                ColorU32(n.BiomeColor, biomeA), rounding + half, ImDrawFlags.RoundCornersAll, bTh);
        }

        dl.AddRectFilled(bgPos, bgPos + bgSize, ColorU32(bgHex, bgOpacity), 3f * uiScale);
        dl.AddText(drawPos, ColorU32(fgHex, fgOpacity), mapName);

        if (ctx.AtlasShowIslandRumours
            && mousePos.X >= bgPos.X && mousePos.X <= bgPos.X + bgSize.X
            && mousePos.Y >= bgPos.Y && mousePos.Y <= bgPos.Y + bgSize.Y
            && AtlasIslandRumours.TryGetDefinition(n.MapName, out var island))
        {
            SetAtlasHoverTooltip(
                $"{mapName} — Tier {island.Tier}",
                $"{island.Rumour}\n{island.Kind}\n{island.Summary}\n\n"
                + $"PREP: {island.Preparation.Investment}\n"
                + $"Tablet: {island.Preparation.Tablets}\n"
                + $"Waystone: {island.Preparation.Waystone}\n\n"
                + "Community farming tier and preparation advice; values can shift with balance and the economy.");
        }

        if (n.RouteHops > 0)
        {
            var hopLabel = $"{n.RouteHops}→";
            var htSize = ImGui.CalcTextSize(hopLabel);
            var pillH = 18f * uiScale;
            var pillW = MathF.Max(pillH, htSize.X + 8f * uiScale);
            var pillCenterX = bgPos.X - (4f * uiScale) - pillW * 0.5f;
            DrawAtlasHopPill(dl, hopLabel, pillCenterX, rectCenterY, uiScale);
        }

        var nextRowTop = drawPos.Y + textSize.Y + 4f * uiScale;
        var rowGap = 4f * uiScale;

        if (ctx.AtlasShowContentBadges && n.FlagChips is { Count: > 0 })
            DrawAtlasContentSquares(dl, n.FlagChips, labelCenterX, ref nextRowTop, rowGap, uiScale, mousePos, ctx.AtlasLanguage);

        if (n.ContentChips is { Count: > 0 })
            DrawAtlasContentSquares(dl, n.ContentChips, labelCenterX, ref nextRowTop, rowGap, uiScale, mousePos, ctx.AtlasLanguage);

        if (ctx.AtlasShowContentCount && n.ContentCount > 0)
            DrawAtlasContentDots(dl, n.ContentCount, labelCenterX, ref nextRowTop, rowGap, uiScale);
    }

    /// <summary>Simple circle marker for every memory node (fog + named). Cool tint when fog-revealed.</summary>
    private void DrawAtlasNodeDot(ImDrawListPtr dl, RenderContext ctx, AtlasMark n, NumVec2 c)
    {
        var baseSize = n.Selected || n.Arrow || n.RouteHops > 0 ? 5.5f : 4.25f;
        var size = baseSize * ctx.AtlasIconScale;
        var colHex = string.IsNullOrEmpty(n.Color)
            ? n.Selected || n.Arrow || n.RouteHops > 0 ? "#3BDBFF"
            : !n.Visible && ctx.AtlasRevealFog ? "#9CB8D4"
            : n.HasContent && !n.Visited ? "#FF9E42" : "#6EEB87"
            : n.Color;
        float opacity = n.Visible
            ? n.Visited ? 0.7f : 0.92f
            : ctx.AtlasRevealFog ? 0.88f : n.Selected || n.Arrow ? 0.5f : 0.35f;
        if (n.Dimmed) opacity *= 0.28f;
        if (n.Completed) opacity *= 0.55f;
        dl.ChannelsSetCurrent(AtlasChannelDots);
        dl.AddCircleFilled(c, size, ColorU32(colHex, opacity), 12);
    }

    private void DrawAtlasNodeSprite(ImDrawListPtr dl, RenderContext ctx, AtlasMark n, NumVec2 c)
    {
        var sprite = SpriteCatalog.AtlasNode(n.IconType, n.Biome);
        var baseSize = n.Selected || n.Arrow ? 11f : 9f;
        var tierMul = n.EndgameTier switch
        {
            AtlasEndgameTier.Pinnacle => 1.15f,
            AtlasEndgameTier.KeyHalls => 1.10f,
            _ => 1f,
        };
        var size = baseSize * tierMul * ctx.AtlasIconScale;
        var colHex = string.IsNullOrEmpty(n.Color)
            ? n.Selected || n.Arrow ? "#3BDBFF"
            : !n.Visible && ctx.AtlasRevealFog ? "#9CB8D4"
            : n.HasContent && !n.Visited ? "#FF9E42" : "#6EEB87"
            : n.Color;
        float opacity = n.Visible
            ? n.Visited ? 0.75f : 0.95f
            : ctx.AtlasRevealFog ? 0.95f : n.Selected || n.Arrow ? 0.55f : 0.45f;
        if (n.Dimmed) opacity *= 0.28f;
        if (n.Completed) opacity *= 0.55f;
        var col = ColorU32(colHex, opacity);
        dl.ChannelsSetCurrent(AtlasChannelDots);
        if (IconAtlas.TryResolve(sprite, null, out var tex))
        {
            var half = size;
            dl.AddImage(tex.TextureId, c - new NumVec2(half, half), c + new NumVec2(half, half), tex.UV0, tex.UV1, col);
        }
        else
            dl.AddCircleFilled(c, size, col, 12);
    }

    private void DrawAtlasContentRow(ImDrawListPtr dl, RenderContext ctx, AtlasMark n, NumVec2 drawPos, NumVec2 textSize,
        float uiScale, NumVec2 mousePos, bool allowIcons)
    {
        if (n.ContentNames is not { Count: > 0 } names) return;
        dl.ChannelsSetCurrent(AtlasChannelLabels);
        var iconH = ctx.AtlasContentIconSize * uiScale;
        var y = drawPos.Y - iconH - 4f * uiScale;
        var totalW = names.Count * (iconH + 4f * uiScale);
        var x = drawPos.X + textSize.X * 0.5f - totalW * 0.5f;
        foreach (var name in names)
        {
            var localized = AtlasCatalog.Shared.LocalizedContentName(name, ctx.AtlasLanguage);
            if (ctx.AtlasShowContentIcons && allowIcons
                && AtlasCatalog.Shared.ContentIconBasename(name) is { } iconBase
                && AtlasContentIcons.TryGet(iconBase, out var tex) && tex != IntPtr.Zero)
            {
                dl.AddImage(tex, new NumVec2(x, y), new NumVec2(x + iconH, y + iconH));
            }
            else if (ctx.AtlasShowContentTokens)
            {
                var abbrev = localized.Length > 0 ? localized[..Math.Min(3, localized.Length)] : "?";
                var chip = AtlasCatalog.Shared.ContentInfoFor(name);
                if (chip is { } ci)
                {
                    var w = MathF.Max(iconH, ImGui.CalcTextSize(abbrev).X + 12f * uiScale);
                    var min = new NumVec2(x, y);
                    var max = new NumVec2(x + w, y + iconH);
                    dl.AddRectFilled(min, max, ColorU32(ci.BgR, ci.BgG, ci.BgB, ci.BgA));
                    dl.AddText(min + new NumVec2(6f * uiScale, 2f * uiScale), ColorU32(ci.FgR, ci.FgG, ci.FgB, ci.FgA), abbrev);
                    if (mousePos.X >= min.X && mousePos.X <= max.X && mousePos.Y >= min.Y && mousePos.Y <= max.Y)
                        SetAtlasHoverTooltip(localized, AtlasCatalog.Shared.LocalizedContentDescription(name, ctx.AtlasLanguage));
                }
            }
            x += iconH + 4f * uiScale;
        }
    }

    private void SetAtlasHoverTooltip(string name, string? desc)
    {
        _atlasHoverTooltipName = name;
        _atlasHoverTooltipDesc = desc;
    }

    private static void DrawAtlasHopPill(ImDrawListPtr dl, string label, float pillCenterX, float pillCenterY, float uiScale)
    {
        const float fixedHeightBase = 18f;
        const float paddingBase = 8f;
        var fixedHeight = fixedHeightBase * uiScale;
        var padding = paddingBase * uiScale;
        var textSize = ImGui.CalcTextSize(label);
        var w = MathF.Max(fixedHeight, textSize.X + padding);
        var min = new NumVec2(pillCenterX - w * 0.5f, pillCenterY - fixedHeight * 0.5f);
        dl.AddRectFilled(min, min + new NumVec2(w, fixedHeight), ColorU32(13, 13, 13, 0.85f), 3f * uiScale);
        dl.AddText(min + (new NumVec2(w, fixedHeight) - textSize) * 0.5f, ColorU32(255, 230, 51, 1f), label);
    }

    private void DrawAtlasContentSquares(ImDrawListPtr dl, IReadOnlyList<AtlasContentChip> chips, float centerX,
        ref float nextRowTopY, float rowGap, float uiScale, NumVec2 mousePos, string language)
    {
        if (chips.Count == 0) return;
        const float fixedHeightBase = 18f;
        const float paddingBase = 6f;
        var fixedHeight = fixedHeightBase * uiScale;
        var padding = paddingBase * uiScale;
        var widths = new float[chips.Count];
        var totalW = 0f;
        for (var i = 0; i < chips.Count; i++)
        {
            var abbrev = chips[i].Abbrev;
            var textSize = ImGui.CalcTextSize(abbrev);
            widths[i] = MathF.Max(fixedHeight, textSize.X + padding);
            totalW += widths[i];
        }
        var basePos = new NumVec2(centerX - totalW * 0.5f, nextRowTopY);
        for (var i = 0; i < chips.Count; i++)
        {
            var chip = chips[i];
            var boxSize = new NumVec2(widths[i], fixedHeight);
            var squareMin = basePos;
            var squareMax = squareMin + boxSize;
            dl.AddRectFilled(squareMin, squareMax, ColorU32(chip.BgR, chip.BgG, chip.BgB, chip.BgA));
            var textSize = ImGui.CalcTextSize(chip.Abbrev);
            var textPos = squareMin + (boxSize - textSize) * 0.5f;
            dl.AddText(textPos, ColorU32(chip.FgR, chip.FgG, chip.FgB, chip.FgA), chip.Abbrev);
            if (mousePos.X >= squareMin.X && mousePos.X <= squareMax.X && mousePos.Y >= squareMin.Y && mousePos.Y <= squareMax.Y)
            {
                var info = AtlasCatalog.Shared.ContentInfoFor(chip.Abbrev);
                var name = info is { } ci ? ci.Label : chip.Abbrev;
                SetAtlasHoverTooltip(
                    AtlasCatalog.Shared.LocalizedContentName(name, language),
                    AtlasCatalog.Shared.LocalizedContentDescription(name, language));
            }
            basePos.X += boxSize.X;
        }
        nextRowTopY += fixedHeight + rowGap;
    }

    private static void DrawAtlasContentDots(ImDrawListPtr dl, int count, float centerX, ref float nextRowTopY, float rowGap, float uiScale)
    {
        var pips = Math.Clamp(count, 1, 5);
        var radius = 3.5f * uiScale;
        var spacing = radius * 2.5f;
        var startX = centerX - (pips - 1) * spacing * 0.5f;
        for (var i = 0; i < pips; i++)
            dl.AddCircleFilled(new NumVec2(startX + i * spacing, nextRowTopY + radius), radius, ColorU32(255, 199, 69, 1f), 8);
        nextRowTopY += radius * 2f + rowGap;
    }

    private static void DrawAtlasNodePath(ImDrawListPtr dl, IReadOnlyList<NumVec2> pts, uint col, float thickness,
        float uiScale, float spacingMul, bool chevrons, int phaseIndex)
    {
        if (pts.Count < 2) return;
        var display = ImGui.GetIO().DisplaySize;
        var chevron = MathF.Max(7f * uiScale, thickness * 2.2f);
        var spacing = chevron * MathF.Max(1.5f, spacingMul);
        var guide = MathF.Max(1f, thickness * 0.5f);
        const int phases = 3;
        var carryStart = spacing * (0.15f + (phaseIndex % phases) / (float)phases);
        var carry = carryStart;

        dl.ChannelsSetCurrent(AtlasChannelLines);
        for (var i = 1; i < pts.Count; i++)
        {
            var p = pts[i - 1];
            var c = pts[i];
            if (!AtlasRoutePolylineBuilder.IsDrawableEdge(p, c, display.X, display.Y))
                continue;

            dl.AddLine(p, c, col, guide);
            if (chevrons)
                DrawAtlasPathChevrons(dl, p, c, col, chevron, spacing, ref carry);
        }

        dl.ChannelsSetCurrent(AtlasChannelDots);
        foreach (var c in pts)
            if (AtlasRoutePolylineBuilder.IsInViewport(c, display.X, display.Y))
                dl.AddCircleFilled(c, MathF.Max(2f, thickness * 0.9f), col, 8);
    }

    private static void DrawAtlasPathChevrons(ImDrawListPtr dl, NumVec2 a, NumVec2 b, uint col, float size, float spacing, ref float carry)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f) return;
        var ux = dx / len;
        var uy = dy / len;
        var px = -uy;
        var py = ux;
        var half = size * 0.5f;
        for (var t = carry; t < len; t += spacing)
        {
            var p = new NumVec2(a.X + ux * t, a.Y + uy * t);
            var tip = new NumVec2(p.X + ux * half, p.Y + uy * half);
            var baseMid = new NumVec2(p.X - ux * half, p.Y - uy * half);
            dl.AddTriangleFilled(tip,
                new NumVec2(baseMid.X + px * half, baseMid.Y + py * half),
                new NumVec2(baseMid.X - px * half, baseMid.Y - py * half), col);
        }
        carry = spacing - ((len - carry) % spacing);
        if (carry <= 0 || carry > spacing) carry = spacing;
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

    internal static float ComputeAtlasUiScale(int winW, int winH, float baseW, float baseH, float multiplier)
    {
        var resScale = MathF.Min(winW / MathF.Max(1f, baseW), winH / MathF.Max(1f, baseH));
        return Math.Clamp(multiplier * resScale, 0.5f, 4f);
    }
}
