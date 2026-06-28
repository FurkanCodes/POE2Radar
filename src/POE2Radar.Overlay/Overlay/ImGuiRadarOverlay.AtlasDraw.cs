using System.Diagnostics;
using System.Globalization;
using ImGuiNET;
using POE2Radar.Core.Game;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class ImGuiRadarOverlay
{
    private const int AtlasChannelLines = 1;
    private const int AtlasChannelDots = 2;
    private const int AtlasChannelLabels = 3;

    private string? _atlasHoverTooltipName;
    private string? _atlasHoverTooltipDesc;

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

        dl.ChannelsMerge();

        if (_atlasHoverTooltipName is { Length: > 0 })
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
        if (ctx.AtlasShowNodeSprites)
            DrawAtlasNodeSprite(dl, ctx, n, c);

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

        if (string.IsNullOrEmpty(mapName)) return;

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
        var chevron = MathF.Max(7f * uiScale, thickness * 2.2f);
        var spacing = chevron * MathF.Max(1.5f, spacingMul);
        var guide = MathF.Max(1f, thickness * 0.5f);
        const int phases = 3;
        var carryStart = spacing * (0.15f + (phaseIndex % phases) / (float)phases);
        var carry = carryStart;

        dl.ChannelsSetCurrent(AtlasChannelLines);
        NumVec2? prev = null;
        for (var i = 0; i < pts.Count; i++)
        {
            var c = pts[i];
            if (prev is { } p)
            {
                dl.AddLine(p, c, col, guide);
                if (chevrons)
                    DrawAtlasPathChevrons(dl, p, c, col, chevron, spacing, ref carry);
            }
            prev = c;
        }

        dl.ChannelsSetCurrent(AtlasChannelDots);
        foreach (var c in pts)
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
