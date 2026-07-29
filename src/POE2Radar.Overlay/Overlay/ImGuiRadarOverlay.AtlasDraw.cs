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
    private readonly Dictionary<string, AtlasRitualPlannerRow> _atlasRitualSelectedRows = new(StringComparer.Ordinal);
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
                DrawAtlasNodePath(dl, line.Points, ColorU32(line.Color, ctx.AtlasRouteOpacity), thickness, uiScale,
                    ctx.AtlasRouteChevronSpacing, ctx.AtlasShowRouteChevrons, line.PhaseIndex);
                routePhase++;
            }
        }

        if (ctx.AtlasRoute is { Count: >= 2 } manualRoute)
        {
            var thickness = MathF.Max(1f, uiScale * ctx.AtlasRouteLineThickness);
            var col = ColorU32(ctx.AtlasManualRouteColor, ctx.AtlasRouteOpacity);
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

        DrawAtlasRitualSearchHighlights(dl, ctx, uiScale);

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

        if (ctx.AtlasShowRitualPlanner)
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
        ImGui.SetNextWindowSize(new NumVec2(980, 560), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new NumVec2(680, 360), new NumVec2(1280, 800));
        if (!ImGui.Begin("Ritual predictions", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        ImGui.SetWindowFontScale(Math.Clamp(ctx.AtlasRitualPlannerFontScale, 0.75f, 1.75f));
        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.82f, 0.25f, 1f), "RITE OF THE NAMELESS");
        ImGui.SameLine();
        if (ctx.AtlasRitualLineActive)
        {
            var totalMaps = Math.Max(ctx.AtlasRitualLineLength, 1);
            ImGui.TextDisabled($"  {totalMaps}-map line  |  {ctx.AtlasRitualCommittedMaps}/{totalMaps} chosen");
        }
        else
        {
            ImGui.TextDisabled("  Waiting for Ritual line mode");
        }

        var rewardSearch = _settings.AtlasRitualRewardFilter ?? "";
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.025f, 0.028f, 0.024f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Border, new System.Numerics.Vector4(0.84f, 0.69f, 0.35f, 0.72f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.SetNextItemWidth(-72f);
        var searchEdited = ImGui.InputTextWithHint(
            "##ritualRewardSearch",
            "Paste a reward modifier, e.g. Rerolling Favours costs 20% reduced Tribute",
            ref rewardSearch,
            512);
        var searchFinished = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
        if (searchEdited)
            _settings.AtlasRitualRewardFilter = rewardSearch;
        if (searchFinished)
            _settings.Save();
        ImGui.SameLine();
        var hasSearch = !string.IsNullOrWhiteSpace(rewardSearch);
        if (!hasSearch)
            ImGui.BeginDisabled();
        if (ImGui.Button("Clear"))
        {
            rewardSearch = "";
            _settings.AtlasRitualRewardFilter = "";
            _settings.Save();
        }
        if (!hasSearch)
            ImGui.EndDisabled();

        var rows = ctx.AtlasRitualPlannerRows ?? Array.Empty<AtlasRitualPlannerRow>();

        if (!ctx.AtlasRitualLineActive)
        {
            _atlasRitualSelected.Clear();
            _atlasRitualSelectedRows.Clear();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextWrapped("Open the Atlas and activate the Ritual line. Predictions will appear here as complete selectable map lines.");
            ImGui.End();
            return;
        }

        var cap = ctx.AtlasRitualPlannerCapped ? "+" : "";
        ImGui.TextDisabled(
            $"Showing {rows.Count} choices from {ctx.AtlasRitualPlannerTotalChains}{cap} valid lines. "
            + "Tick a line to draw it on the Atlas.");
        if (_atlasRitualSelected.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Clear selected ({_atlasRitualSelected.Count})"))
            {
                _atlasRitualSelected.Clear();
                _atlasRitualSelectedRows.Clear();
            }
        }
        if (hasSearch)
        {
            var matchCount = AtlasRitualPresentation.FindRewardMatches(rows, rewardSearch).Length;
            ImGui.TextColored(
                new System.Numerics.Vector4(0.84f, 0.69f, 0.35f, 1f),
                $"{matchCount} matching reward node{(matchCount == 1 ? "" : "s")} highlighted in the shown lines");
        }
        ImGui.Separator();

        if (rows.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(rewardSearch)
                ? "No complete line is currently available. Move the Atlas to the Ritual region or choose another starting map."
                : "No complete line matches this reward search. Clear the search or paste a shorter part of the modifier.");
            ImGui.End();
            return;
        }

        var mapColumns = Math.Max(2, rows.Max(row => row.MapNames.Count));
        var tableFlags = ImGuiTableFlags.Borders
            | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.ScrollX
            | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Resizable;
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new System.Numerics.Vector4(0.12f, 0.13f, 0.11f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new System.Numerics.Vector4(0.045f, 0.05f, 0.04f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new System.Numerics.Vector4(0.075f, 0.08f, 0.065f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Border, new System.Numerics.Vector4(0.23f, 0.24f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new System.Numerics.Vector4(0.025f, 0.028f, 0.024f, 1f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, new System.Numerics.Vector4(0.84f, 0.69f, 0.35f, 1f));
        if (ImGui.BeginTable("ritualPlanner", mapColumns + 2, tableFlags, new NumVec2(0, -1)))
        {
            ImGui.TableSetupScrollFreeze(1, 1);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 30f);
            for (var step = 0; step < mapColumns; step++)
                ImGui.TableSetupColumn($"Map {step + 1}", ImGuiTableColumnFlags.WidthFixed, 138f);
            ImGui.TableSetupColumn("Score", ImGuiTableColumnFlags.WidthFixed, 48f);
            ImGui.TableHeadersRow();
            foreach (var row in rows)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 78f);
                ImGui.TableSetColumnIndex(0);
                var selected = _atlasRitualSelected.ContainsKey(row.Key);
                if (ImGui.Checkbox($"##ritual-{row.Key}", ref selected))
                {
                    if (selected)
                    {
                        _atlasRitualSelected[row.Key] = NextAtlasRitualPaletteSlot();
                        _atlasRitualSelectedRows[row.Key] = row;
                    }
                    else
                    {
                        _atlasRitualSelected.Remove(row.Key);
                        _atlasRitualSelectedRows.Remove(row.Key);
                    }
                }

                for (var step = 0; step < mapColumns; step++)
                {
                    ImGui.TableSetColumnIndex(step + 1);
                    if (step >= row.MapNames.Count)
                    {
                        ImGui.TextDisabled("-");
                        continue;
                    }

                    ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.93f, 0.90f, 0.83f, 1f));
                    ImGui.TextWrapped(row.MapNames[step]);
                    ImGui.PopStyleColor();
                    var reward = step == 0
                        ? (ctx.AtlasRitualCommittedMaps > 0 ? "Current" : "Start")
                        : step - 1 < row.Rewards.Count ? row.Rewards[step - 1] : "";
                    var rewardMatch = step > 0
                        && step - 1 < row.RewardSearchTexts.Count
                        && AtlasRitualPlanner.MatchesRewardQuery(
                            [row.RewardSearchTexts[step - 1]],
                            rewardSearch);
                    if (rewardMatch)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ColorU32("#493A16", 0.96f));
                    if (!string.IsNullOrEmpty(reward))
                    {
                        ImGui.PushStyleColor(
                            ImGuiCol.Text,
                            rewardMatch
                                ? new System.Numerics.Vector4(1f, 0.86f, 0.36f, 1f)
                                : new System.Numerics.Vector4(0.84f, 0.69f, 0.35f, 1f));
                        ImGui.TextWrapped(reward);
                        ImGui.PopStyleColor();
                    }
                }

                ImGui.TableSetColumnIndex(mapColumns + 1);
                ImGui.TextUnformatted(row.Weight == 0 ? "-" : row.Weight.ToString("+0;-0"));
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleColor(6);
        ImGui.End();
    }

    private void DrawAtlasRitualSearchHighlights(ImDrawListPtr dl, RenderContext ctx, float uiScale)
    {
        var query = _settings.AtlasRitualRewardFilter;
        if (string.IsNullOrWhiteSpace(query)
            || !ctx.AtlasRitualLineActive
            || ctx.AtlasRitualPlannerRows is not { Count: > 0 } rows
            || ctx.AtlasRitualNodeCenters is not { Count: > 0 } centers)
            return;

        var matches = AtlasRitualPresentation.FindRewardMatches(rows, query);
        foreach (var match in matches)
        {
            if (!centers.TryGetValue((match.Grid.X, match.Grid.Y), out var center)
                || !float.IsFinite(center.X)
                || !float.IsFinite(center.Y))
                continue;

            var radius = MathF.Max(11f, 13f * uiScale);
            dl.ChannelsSetCurrent(AtlasChannelDots);
            dl.AddCircle(
                center,
                radius + 2f * uiScale,
                ColorU32("#080908", 0.96f),
                24,
                MathF.Max(4f, 5f * uiScale));
            dl.AddCircle(
                center,
                radius,
                ColorU32("#E2B84F", 1f),
                24,
                MathF.Max(2f, 2.5f * uiScale));
            dl.AddCircle(
                center,
                radius - 4f * uiScale,
                ColorU32("#FFF0A6", 0.92f),
                24,
                MathF.Max(1f, 1.25f * uiScale));

            dl.ChannelsSetCurrent(AtlasChannelLabels);
            var labelSize = ImGui.CalcTextSize(match.Label);
            var padding = new NumVec2(5f, 3f) * uiScale;
            var labelPos = center + new NumVec2(-labelSize.X * 0.5f, radius + 7f * uiScale);
            dl.AddRectFilled(
                labelPos - padding,
                labelPos + labelSize + padding,
                ColorU32("#080908", 0.98f),
                3f * uiScale);
            dl.AddRect(
                labelPos - padding,
                labelPos + labelSize + padding,
                ColorU32("#E2B84F", 1f),
                3f * uiScale);
            dl.AddText(labelPos, ColorU32("#F8EBC8", 1f), match.Label);
        }
    }

    private void DrawAtlasRitualSelectedPaths(ImDrawListPtr dl, RenderContext ctx, float uiScale)
    {
        if (!ctx.AtlasRitualLineActive)
        {
            _atlasRitualSelected.Clear();
            _atlasRitualSelectedRows.Clear();
            return;
        }
        if (_atlasRitualSelected.Count == 0
            || ctx.AtlasRitualNodeCenters is not { Count: > 0 } centers)
            return;

        if (ctx.AtlasRitualPlannerRows is { Count: > 0 } currentRows)
        {
            foreach (var row in currentRows)
                if (_atlasRitualSelected.ContainsKey(row.Key))
                    _atlasRitualSelectedRows[row.Key] = row;
        }

        foreach (var pair in _atlasRitualSelectedRows)
        {
            var row = pair.Value;
            if (!_atlasRitualSelected.TryGetValue(row.Key, out var slot))
                continue;
            var points = AtlasRitualPresentation.ResolveRoutePoints(row, centers);
            if (points.Length < 2)
                continue;
            var colorHex = AtlasRitualPalette[slot % AtlasRitualPalette.Length];
            var color = ColorU32(colorHex, 0.95f);
            DrawAtlasNodePath(dl, points, color, MathF.Max(2f, 2.5f * uiScale), uiScale,
                spacingMul: 24f, chevrons: true, phaseIndex: slot);

            dl.ChannelsSetCurrent(AtlasChannelLabels);
            for (var index = 1; index < points.Length && index - 1 < row.Rewards.Count; index++)
            {
                var label = row.Rewards[index - 1];
                var size = ImGui.CalcTextSize(label);
                var padding = new NumVec2(4f, 2f) * uiScale;
                var position = points[index] - new NumVec2(size.X * 0.5f, size.Y + 12f * uiScale);
                dl.AddRectFilled(position - padding, position + size + padding, ColorU32("#080908", 0.98f), 3f * uiScale);
                dl.AddRect(position - padding, position + size + padding, color, 3f * uiScale);
                dl.AddText(position, ColorU32("#F2E9D2", 1f), label);
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
