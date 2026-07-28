using System.Numerics;
using ImGuiNET;
using POE2Radar.Core.Campaign;
using POE2Radar.Overlay.Campaign;
using POE2Radar.Overlay.Config;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class ImGuiRadarOverlay
{
    private static class CampaignColors
    {
        internal static readonly Vector4 Base = Rgb(0x18, 0x14, 0x10);
        internal static readonly Vector4 Surface = Rgb(0x22, 0x1c, 0x15);
        internal static readonly Vector4 Elevated = Rgb(0x2a, 0x22, 0x18);
        internal static readonly Vector4 Border = Rgb(0x3a, 0x2f, 0x20);
        internal static readonly Vector4 BorderAccent = Rgb(0x5a, 0x48, 0x30);
        internal static readonly Vector4 Text = Rgb(0xb8, 0xa8, 0x88);
        internal static readonly Vector4 TextStrong = Rgb(0xd8, 0xca, 0xb0);
        internal static readonly Vector4 TextMuted = Rgb(0x8a, 0x7c, 0x64);
        internal static readonly Vector4 TextDim = Rgb(0x5a, 0x4f, 0x3e);
        internal static readonly Vector4 Gold = Rgb(0xc8, 0x98, 0x60);
        internal static readonly Vector4 GoldBright = Rgb(0xe8, 0xb8, 0x78);
        internal static readonly Vector4 Sage = Rgb(0x9b, 0xb8, 0x7a);
        internal static readonly Vector4 Steel = Rgb(0x7e, 0x9e, 0xb8);

        private static Vector4 Rgb(byte red, byte green, byte blue, float alpha = 1f)
            => new(red / 255f, green / 255f, blue / 255f, alpha);
    }

    private void DrawCampaignWidget(RenderContext ctx)
    {
        var view = ctx.Campaign;
        if (!view.Available || !view.Visible) return;

        var campaignSettings = _settings.Campaign;
        var scale = Math.Clamp(campaignSettings.WidgetScale, 0.75f, 1.50f);
        if (!_campaignPositionInitialized)
        {
            var defaultX = _settings.NavTaskbarX >= 0f ? _settings.NavTaskbarX : 10f;
            var defaultY = _settings.NavTaskbarY >= 0f ? _settings.NavTaskbarY + 44f : 56f;
            var x = campaignSettings.WidgetX >= 0f ? campaignSettings.WidgetX : defaultX;
            var y = campaignSettings.WidgetY >= 0f ? campaignSettings.WidgetY : defaultY;
            ImGui.SetNextWindowPos(
                new NumVec2(
                    Math.Clamp(x, 0f, Math.Max(0f, ctx.WindowWidth - 260f)),
                    Math.Clamp(y, 0f, Math.Max(0f, ctx.WindowHeight - 120f))),
                ImGuiCond.Always);
            _campaignPositionInitialized = true;
        }

        ImGui.SetNextWindowBgAlpha(Math.Clamp(campaignSettings.WidgetOpacity, 0.35f, 1f));
        var widgetWidth = 360f * scale;
        ImGui.SetNextWindowSizeConstraints(
            new NumVec2(widgetWidth, 0f),
            new NumVec2(widgetWidth, float.MaxValue));
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NumVec2(10f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new NumVec2(6f, 3f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new NumVec2(4f, 2f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 1f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, CampaignColors.Base);
        ImGui.PushStyleColor(ImGuiCol.Border, CampaignColors.Border);
        ImGui.PushStyleColor(ImGuiCol.Text, CampaignColors.Text);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, CampaignColors.Elevated);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, CampaignColors.Border);

        if (!ImGui.Begin("##CampaignHelper", flags))
        {
            ImGui.End();
            ImGui.PopStyleColor(6);
            ImGui.PopStyleVar(5);
            return;
        }
        ImGui.SetWindowFontScale(scale);

        DrawCampaignDragHandle(ctx, CampaignColors.Gold);
        ImGui.SameLine(0f, 7f);
        TextColoredUnformatted(CampaignColors.Gold, "PoE Overlay II · Campaign Guide");
        var right = ImGui.GetWindowWidth() - 76f * scale;
        ImGui.SameLine(right);
        if (ImGui.SmallButton(campaignSettings.WidgetCollapsed ? "⌄##CampaignCollapse" : "⌃##CampaignCollapse"))
            campaignSettings.WidgetCollapsed = !campaignSettings.WidgetCollapsed;
        ImGui.SameLine();
        if (ImGui.SmallButton("⚙##CampaignGuide"))
        {
            _campaignGuideOpen = true;
            _campaignGuideChapter = view.Current?.Chapter ?? "act1";
            _campaignGuideZone = view.Current is null
                ? ""
                : CampaignCatalog.Shared.SectionContaining(view.Current.Id)?.Id ?? "";
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Open the complete act and zone guide.");
        ImGui.SameLine();
        if (ImGui.SmallButton("×##DismissCampaign"))
            _enqueue(() => _setCampaignDismissed(true));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Dismiss for this character; re-enable it in Campaign Helper settings.");

        if (!campaignSettings.WidgetCollapsed)
        {
            var zoneTitle = view.Current?.AreaName ?? view.TargetStatus;
            var zoneCursorY = ImGui.GetCursorPosY();
            var zoneTop = ImGui.GetCursorScreenPos();
            var zoneWidth = ImGui.GetContentRegionAvail().X;
            var zoneHeight = 31f * scale;
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(
                zoneTop,
                zoneTop + new NumVec2(zoneWidth, zoneHeight),
                ImGui.ColorConvertFloat4ToU32(CampaignColors.Surface));
            drawList.AddRectFilled(
                zoneTop,
                zoneTop + new NumVec2(3f * scale, zoneHeight),
                ImGui.ColorConvertFloat4ToU32(CampaignColors.Gold));
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 6f * scale);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 9f * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, CampaignColors.TextStrong);
            ImGui.TextUnformatted(zoneTitle);
            ImGui.PopStyleColor();
            if (view.Current is not null)
            {
                var requiredInZone = view.ZoneObjectives
                    .Where(x => !x.Objective.Optional)
                    .ToArray();
                var requiredDone = requiredInZone.Length > 0 && requiredInZone.All(x => x.Completed);
                var nextRequiredDone = requiredDone;
                ImGui.SameLine(ImGui.GetWindowWidth() - 83f * scale);
                if (CampaignCheckbox(
                        "##CompactZoneRequired",
                        ref nextRequiredDone,
                        scale,
                        "Mark every required objective in this zone"))
                {
                    var ids = requiredInZone.Select(x => x.Objective.Id).ToArray();
                    _enqueue(() => _setCampaignObjectivesComplete(ids, nextRequiredDone));
                }
            }
            ImGui.SameLine(ImGui.GetWindowWidth() - 59f * scale);
            CampaignTag(
                $"{view.AreaCompleted}/{view.AreaTotal}",
                new Vector4(CampaignColors.Sage.X, CampaignColors.Sage.Y, CampaignColors.Sage.Z, 0.14f),
                CampaignColors.Sage);
            ImGui.SetCursorPosY(zoneCursorY + zoneHeight + 4f * scale);

            if (view.Current is null)
            {
                TextColoredUnformatted(CampaignColors.Gold, view.TargetStatus.ToUpperInvariant());
                ImGui.TextDisabled(
                    campaignSettings.GuideMode == CampaignGuideMode.Required
                        ? "Switch to Full Clear to continue optional objectives."
                        : "Every Act and Interlude objective is checked.");
            }
            else
            {
                foreach (var state in view.ZoneObjectives.Where(x =>
                             campaignSettings.ShowCompletedObjectives || !x.Completed))
                    DrawCampaignObjectiveRow(state, view, scale);
            }
        }

        ImGui.End();
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(5);
    }

    private void DrawCampaignObjectiveRow(
        CampaignObjectiveState state,
        CampaignView view,
        float scale)
    {
        var objective = state.Objective;
        ImGui.PushID(objective.Id);
        var rowStart = ImGui.GetCursorScreenPos();

        var completed = state.Completed;
        if (CampaignCheckbox("##done", ref completed, scale, "Check or uncheck this objective"))
            _enqueue(() => _setCampaignObjectivesComplete([objective.Id], completed));
        ImGui.SameLine(0f, 8f * scale);
        var textColor = state.Completed
            ? new Vector4(CampaignColors.TextDim.X, CampaignColors.TextDim.Y, CampaignColors.TextDim.Z, 0.72f)
            : state.Current ? CampaignColors.TextStrong : CampaignColors.Text;
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(180f, ImGui.GetContentRegionAvail().X - 4f));
        ImGui.TextUnformatted(objective.Text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        if (state.Completed)
        {
            var textMin = ImGui.GetItemRectMin();
            var textMax = ImGui.GetItemRectMax();
            var lineY = textMin.Y + ImGui.GetTextLineHeight() * 0.54f;
            ImGui.GetWindowDrawList().AddLine(
                new NumVec2(textMin.X, lineY),
                new NumVec2(textMax.X, lineY),
                ImGui.ColorConvertFloat4ToU32(CampaignColors.TextDim),
                1f);
        }

        if (objective.Optional || objective.Rewards.Length > 0)
        {
            if (objective.Optional)
            {
                PlaceCampaignTag("OPTIONAL", scale);
                CampaignTag(
                    "OPTIONAL",
                    new Vector4(CampaignColors.TextMuted.X, CampaignColors.TextMuted.Y, CampaignColors.TextMuted.Z, 0.14f),
                    CampaignColors.TextMuted);
            }
            foreach (var reward in objective.Rewards)
            {
                PlaceCampaignTag(reward, scale);
                CampaignTag(
                    reward,
                    new Vector4(CampaignColors.Sage.X, CampaignColors.Sage.Y, CampaignColors.Sage.Z, 0.13f),
                    CampaignColors.Sage);
            }
        }

        var rowBottom = Math.Max(
            ImGui.GetCursorScreenPos().Y,
            rowStart.Y + ImGui.GetTextLineHeightWithSpacing());
        if (state.Current)
            ImGui.GetWindowDrawList().AddRectFilled(
                rowStart - new NumVec2(6f * scale, 1f),
                new NumVec2(rowStart.X - 3f * scale, rowBottom),
                ImGui.ColorConvertFloat4ToU32(CampaignColors.Gold));

        if (state.Current && view.TargetStatus.Length > 0
            && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip($"{view.CompletionBadge}\n{view.TargetStatus}");
        ImGui.Dummy(new NumVec2(0f, 1f * scale));
        ImGui.PopID();
    }

    private static void PlaceCampaignTag(string text, float scale)
    {
        if (CampaignTagFitsOnCurrentLine(text))
        {
            ImGui.SameLine(0f, 5f * scale);
            return;
        }
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 21f * scale);
    }

    private static bool CampaignCheckbox(
        string id,
        ref bool value,
        float scale,
        string? tooltip = null)
    {
        var side = 13f * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        var verticalOffset = Math.Max(0f, (lineHeight - side) * 0.5f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + verticalOffset);
        ImGui.InvisibleButton(id, new NumVec2(side, side));
        var hovered = ImGui.IsItemHovered();
        var pressed = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        if (pressed) value = !value;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        var border = hovered ? CampaignColors.Gold : CampaignColors.BorderAccent;
        if (value)
        {
            drawList.AddRectFilled(
                min,
                max,
                ImGui.ColorConvertFloat4ToU32(CampaignColors.Gold),
                2f * scale);
            var ink = ImGui.ColorConvertFloat4ToU32(CampaignColors.Base);
            drawList.AddLine(
                min + new NumVec2(3f, 6.8f) * scale,
                min + new NumVec2(5.4f, 9.2f) * scale,
                ink,
                1.7f * scale);
            drawList.AddLine(
                min + new NumVec2(5.4f, 9.2f) * scale,
                min + new NumVec2(10.4f, 3.8f) * scale,
                ink,
                1.7f * scale);
        }
        else
        {
            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(CampaignColors.Base), 2f * scale);
            drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(border), 2f * scale, default, 1.25f * scale);
        }
        if (hovered && !string.IsNullOrWhiteSpace(tooltip))
            ImGui.SetTooltip(tooltip);
        if (verticalOffset > 0f)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - verticalOffset);
        return pressed;
    }

    private static void CampaignTag(string text, Vector4 background, Vector4 foreground)
    {
        var padding = new NumVec2(5f, 1f);
        var textSize = ImGui.CalcTextSize(text);
        var size = textSize + padding * 2f;
        var at = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(
            at,
            at + size,
            ImGui.ColorConvertFloat4ToU32(background),
            3f);
        drawList.AddRect(
            at,
            at + size,
            ImGui.ColorConvertFloat4ToU32(
                new Vector4(foreground.X, foreground.Y, foreground.Z, 0.30f)),
            3f);
        drawList.AddText(
            at + padding,
            ImGui.ColorConvertFloat4ToU32(foreground),
            text);
        ImGui.Dummy(size);
    }

    private static bool CampaignTagFitsOnCurrentLine(string text)
    {
        var contentRight =
            ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X;
        var remaining = contentRight - ImGui.GetItemRectMax().X - 5f;
        return ImGui.CalcTextSize(text).X + 10f <= remaining;
    }

    private static void PushCampaignBrowserStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new NumVec2(8f, 5f));
        ImGui.PushStyleColor(ImGuiCol.Text, CampaignColors.Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, CampaignColors.TextMuted);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, CampaignColors.Base);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CampaignColors.Surface);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, CampaignColors.Elevated);
        ImGui.PushStyleColor(ImGuiCol.Border, CampaignColors.Border);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, CampaignColors.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, CampaignColors.Elevated);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, CampaignColors.Border);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, CampaignColors.Gold);
        ImGui.PushStyleColor(ImGuiCol.Button, CampaignColors.Elevated);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, CampaignColors.Border);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, CampaignColors.BorderAccent);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(CampaignColors.Gold.X, CampaignColors.Gold.Y, CampaignColors.Gold.Z, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(CampaignColors.Gold.X, CampaignColors.Gold.Y, CampaignColors.Gold.Z, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(CampaignColors.Gold.X, CampaignColors.Gold.Y, CampaignColors.Gold.Z, 0.24f));
        ImGui.PushStyleColor(ImGuiCol.Separator, CampaignColors.Border);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, CampaignColors.Base);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, CampaignColors.Surface);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, CampaignColors.Gold);
    }

    private static void PopCampaignBrowserStyle()
    {
        ImGui.PopStyleColor(20);
        ImGui.PopStyleVar(4);
    }

    private void DrawCampaignGuideBrowser(RenderContext ctx)
    {
        if (!_campaignGuideOpen) return;

        PushCampaignBrowserStyle();
        ImGui.SetNextWindowSize(new NumVec2(920f, 650f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new NumVec2(700f, 480f), new NumVec2(1400f, 1000f));
        var open = _campaignGuideOpen;
        if (!ImGui.Begin("Campaign Guide##FullCampaignGuide", ref open, ImGuiWindowFlags.NoCollapse))
        {
            _campaignGuideOpen = open;
            ImGui.End();
            PopCampaignBrowserStyle();
            return;
        }
        _campaignGuideOpen = open;

        var view = ctx.Campaign;
        if (!view.Available)
        {
            ImGui.TextDisabled("Enter the game with a character to load campaign progress.");
            ImGui.End();
            PopCampaignBrowserStyle();
            return;
        }

        var campaign = _settings.Campaign;
        TextColoredUnformatted(CampaignColors.GoldBright, "POE 2 CAMPAIGN");
        ImGui.SameLine();
        ImGui.TextDisabled(
            $"Required {view.RequiredCompleted}/{view.RequiredTotal}  ·  "
            + $"Full clear {view.FullCompleted}/{view.FullTotal}");
        ImGui.SameLine(ImGui.GetWindowWidth() - 245f);
        var requiredMode = campaign.GuideMode == CampaignGuideMode.Required;
        if (ImGui.RadioButton("Required only##GuideMode", requiredMode))
            campaign.GuideMode = CampaignGuideMode.Required;
        ImGui.SameLine();
        if (ImGui.RadioButton("Full clear##GuideMode", !requiredMode))
            campaign.GuideMode = CampaignGuideMode.FullClear;

        ImGui.Separator();
        if (_campaignGuideChapter.Length == 0)
            _campaignGuideChapter = view.Current?.Chapter ?? "act1";
        foreach (var (chapterId, label) in CampaignChapters)
        {
            if (chapterId != CampaignChapters[0].Id) ImGui.SameLine();
            var selected = string.Equals(_campaignGuideChapter, chapterId, StringComparison.Ordinal);
            if (selected)
                ImGui.PushStyleColor(ImGuiCol.Button, CampaignColors.BorderAccent);
            if (ImGui.Button($"{label}##CampaignChapter-{chapterId}"))
            {
                _campaignGuideChapter = chapterId;
                _campaignGuideZone = "";
            }
            if (selected) ImGui.PopStyleColor();
        }

        var catalog = CampaignCatalog.Shared;
        var chapterRewards = catalog.ForChapter(_campaignGuideChapter)
            .SelectMany(x => x.Rewards)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (chapterRewards.Length > 0
            && ImGui.CollapsingHeader(
                $"ACT REWARDS & UNLOCKS ({chapterRewards.Length})##CampaignRewards"))
        {
            for (var index = 0; index < chapterRewards.Length; index++)
            {
                if (index > 0 && CampaignTagFitsOnCurrentLine(chapterRewards[index]))
                    ImGui.SameLine(0f, 5f);
                CampaignTag(
                    chapterRewards[index],
                    new Vector4(CampaignColors.Sage.X, CampaignColors.Sage.Y, CampaignColors.Sage.Z, 0.13f),
                    CampaignColors.Sage);
            }
        }
        var sections = catalog.SectionsForChapter(_campaignGuideChapter)
            .Select(section => new
            {
                Section = section,
                Objectives = section.Objectives
                    .Where(x => campaign.GuideMode == CampaignGuideMode.FullClear || !x.Optional)
                    .ToArray(),
            })
            .Where(x => x.Objectives.Length > 0)
            .ToArray();
        if (sections.Length == 0)
        {
            ImGui.TextDisabled("No objectives are available in this chapter for the selected mode.");
            ImGui.End();
            PopCampaignBrowserStyle();
            return;
        }
        if (!sections.Any(x => string.Equals(x.Section.Id, _campaignGuideZone, StringComparison.Ordinal)))
            _campaignGuideZone = sections[0].Section.Id;

        var footerHeight = 82f;
        var contentHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y - footerHeight);
        if (ImGui.BeginChild("##CampaignZones", new NumVec2(245f, contentHeight)))
        {
            ImGui.TextDisabled("ZONES");
            for (var index = 0; index < sections.Length; index++)
            {
                var entry = sections[index];
                var completed = entry.Objectives.Count(x => view.CompletedObjectiveIds.Contains(x.Id));
                var selected = string.Equals(
                    entry.Section.Id,
                    _campaignGuideZone,
                    StringComparison.Ordinal);
                var label = $"{entry.Section.AreaName}  {completed}/{entry.Objectives.Length}";
                if (ImGui.Selectable($"{label}##{entry.Section.Id}", selected))
                    _campaignGuideZone = entry.Section.Id;
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        var selectedSection = sections.First(x =>
            string.Equals(x.Section.Id, _campaignGuideZone, StringComparison.Ordinal));
        if (ImGui.BeginChild("##CampaignObjectives", new NumVec2(0f, contentHeight)))
        {
            var zoneCompleted = selectedSection.Objectives.Count(x =>
                view.CompletedObjectiveIds.Contains(x.Id));
            TextColoredUnformatted(
                CampaignColors.TextStrong,
                selectedSection.Section.AreaName);
            ImGui.SameLine();
            ImGui.TextDisabled($"{zoneCompleted}/{selectedSection.Objectives.Length}");
            var requiredObjectives = selectedSection.Objectives.Where(x => !x.Optional).ToArray();
            if (requiredObjectives.Length > 0)
            {
                ImGui.SameLine(ImGui.GetWindowWidth() - 145f);
                var requiredDone = requiredObjectives.All(x =>
                    view.CompletedObjectiveIds.Contains(x.Id));
                var nextRequiredDone = requiredDone;
                if (CampaignCheckbox(
                        "##BrowserZoneRequired",
                        ref nextRequiredDone,
                        1f,
                        "Mark every required objective in this zone"))
                {
                    var ids = requiredObjectives.Select(x => x.Id).ToArray();
                    _enqueue(() => _setCampaignObjectivesComplete(ids, nextRequiredDone));
                }
                ImGui.SameLine(0f, 6f);
                ImGui.TextUnformatted("Mark required");
            }
            ImGui.Separator();
            foreach (var objective in selectedSection.Objectives)
                DrawCampaignBrowserObjectiveRow(
                    objective,
                    view.CompletedObjectiveIds.Contains(objective.Id),
                    string.Equals(view.Current?.Id, objective.Id, StringComparison.Ordinal));
        }
        ImGui.EndChild();

        if (ImGui.Button("Export progress"))
        {
            var code = _exportCampaignProgress();
            if (code.Length > 0)
            {
                ImGui.SetClipboardText(code);
                _campaignTransferStatus = "Progress code copied to clipboard.";
            }
            else
            {
                _campaignTransferStatus = "Enter the game before exporting progress.";
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Import progress"))
        {
            _campaignImportCode = ImGui.GetClipboardText() ?? "";
            ImGui.OpenPopup("Import campaign progress");
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset chapter"))
        {
            _campaignResetChapterOnly = true;
            ImGui.OpenPopup("Reset campaign progress");
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset all"))
        {
            _campaignResetChapterOnly = false;
            ImGui.OpenPopup("Reset campaign progress");
        }
        ImGui.SameLine();
        var showCompleted = campaign.ShowCompletedObjectives;
        if (CampaignCheckbox(
                "##ShowCompletedCompact",
                ref showCompleted,
                1f,
                "Show checked objectives in the compact widget"))
            campaign.ShowCompletedObjectives = showCompleted;
        ImGui.SameLine(0f, 6f);
        ImGui.TextUnformatted("Show completed in compact widget");
        if (_campaignTransferStatus.Length > 0)
            ImGui.TextDisabled(_campaignTransferStatus);

        DrawCampaignImportPopup();
        DrawCampaignResetPopup();
        ImGui.End();
        PopCampaignBrowserStyle();
    }

    private void DrawCampaignBrowserObjectiveRow(
        CampaignObjective objective,
        bool completed,
        bool current)
    {
        ImGui.PushID($"browser-{objective.Id}");
        var rowStart = ImGui.GetCursorScreenPos();
        if (current)
        {
            ImGui.GetWindowDrawList().AddRectFilled(
                rowStart - new NumVec2(6f, 1f),
                rowStart + new NumVec2(-3f, ImGui.GetTextLineHeightWithSpacing()),
                ImGui.ColorConvertFloat4ToU32(CampaignColors.Gold));
        }
        var nextValue = completed;
        if (CampaignCheckbox("##complete", ref nextValue, 1f, "Check or uncheck this objective"))
            _enqueue(() => _setCampaignObjectivesComplete([objective.Id], nextValue));
        ImGui.SameLine(0f, 8f);
        ImGui.PushStyleColor(
            ImGuiCol.Text,
            completed
                ? new Vector4(CampaignColors.TextDim.X, CampaignColors.TextDim.Y, CampaignColors.TextDim.Z, 0.72f)
                : current ? CampaignColors.TextStrong : CampaignColors.Text);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(220f, ImGui.GetContentRegionAvail().X - 8f));
        ImGui.TextUnformatted(objective.Text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        if (completed)
        {
            var textMin = ImGui.GetItemRectMin();
            var textMax = ImGui.GetItemRectMax();
            var lineY = textMin.Y + ImGui.GetTextLineHeight() * 0.54f;
            ImGui.GetWindowDrawList().AddLine(
                new NumVec2(textMin.X, lineY),
                new NumVec2(textMax.X, lineY),
                ImGui.ColorConvertFloat4ToU32(CampaignColors.TextDim),
                1f);
        }
        if (objective.Optional || objective.Rewards.Length > 0)
        {
            if (objective.Optional)
            {
                PlaceCampaignTag("OPTIONAL", 1f);
                CampaignTag(
                    "OPTIONAL",
                    new Vector4(CampaignColors.TextMuted.X, CampaignColors.TextMuted.Y, CampaignColors.TextMuted.Z, 0.14f),
                    CampaignColors.TextMuted);
            }
            foreach (var reward in objective.Rewards)
            {
                PlaceCampaignTag(reward, 1f);
                CampaignTag(
                    reward,
                    new Vector4(CampaignColors.Sage.X, CampaignColors.Sage.Y, CampaignColors.Sage.Z, 0.13f),
                    CampaignColors.Sage);
            }
        }
        if (!string.IsNullOrWhiteSpace(objective.Note))
        {
            ImGui.Indent(27f);
            var noteStart = ImGui.GetCursorScreenPos();
            ImGui.PushStyleColor(ImGuiCol.Text, CampaignColors.TextMuted);
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(objective.Note);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.GetWindowDrawList().AddRectFilled(
                noteStart - new NumVec2(7f, 0f),
                new NumVec2(noteStart.X - 5f, ImGui.GetItemRectMax().Y),
                ImGui.ColorConvertFloat4ToU32(CampaignColors.Steel));
            ImGui.Unindent(27f);
        }
        ImGui.Spacing();
        ImGui.PopID();
    }

    private void DrawCampaignImportPopup()
    {
        if (!ImGui.BeginPopupModal(
                "Import campaign progress",
                ImGuiWindowFlags.AlwaysAutoResize))
            return;
        ImGui.TextWrapped(
            "Paste a POE2Radar progress code or a PoE2 Leveling website export. Unknown objectives "
            + "are ignored and character names are never included in POE2Radar codes.");
        ImGui.InputTextMultiline(
            "##CampaignImportCode",
            ref _campaignImportCode,
            128 * 1024,
            new NumVec2(520f, 110f));
        if (ImGui.Button("Import"))
        {
            var imported = _importCampaignProgress(_campaignImportCode);
            _campaignTransferStatus = imported
                ? "Campaign progress imported."
                : "That progress code is invalid.";
            if (imported) ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawCampaignResetPopup()
    {
        if (!ImGui.BeginPopupModal(
                "Reset campaign progress",
                ImGuiWindowFlags.AlwaysAutoResize))
            return;
        ImGui.TextUnformatted(
            _campaignResetChapterOnly
                ? $"Reset all {_campaignGuideChapter.ToUpperInvariant()} objectives for the current character?"
                : "Reset every campaign objective for the current character?");
        if (ImGui.Button("Reset"))
        {
            if (_campaignResetChapterOnly)
            {
                var ids = CampaignCatalog.Shared.ForChapter(_campaignGuideChapter)
                    .Select(x => x.Id)
                    .ToArray();
                _enqueue(() => _setCampaignObjectivesComplete(ids, false));
                _campaignTransferStatus = $"{_campaignGuideChapter.ToUpperInvariant()} progress reset.";
            }
            else
            {
                _enqueue(_resetCampaignCharacter);
                _campaignTransferStatus = "All campaign progress reset.";
            }
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawCampaignDragHandle(RenderContext ctx, Vector4 gold)
    {
        ImGui.InvisibleButton("##CampaignDrag", new NumVec2(13f, 18f));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var color = ImGui.ColorConvertFloat4ToU32(
            new Vector4(gold.X, gold.Y, gold.Z, ImGui.IsItemHovered() ? 1f : 0.65f));
        var drawList = ImGui.GetWindowDrawList();
        for (var row = -1; row <= 1; row++)
            for (var column = 0; column < 2; column++)
                drawList.AddCircleFilled(
                    new NumVec2(min.X + 4f + column * 5f, (min.Y + max.Y) * 0.5f + row * 4f),
                    1.15f,
                    color,
                    8);

        var dragging = ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left);
        if (dragging)
        {
            var position = ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta;
            var size = ImGui.GetWindowSize();
            position.X = Math.Clamp(position.X, 0f, Math.Max(0f, ctx.WindowWidth - size.X));
            position.Y = Math.Clamp(position.Y, 0f, Math.Max(0f, ctx.WindowHeight - size.Y));
            ImGui.SetWindowPos(position, ImGuiCond.Always);
            _campaignWasDragging = true;
        }
        else if (_campaignWasDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var position = ImGui.GetWindowPos();
            _campaignWasDragging = false;
            _enqueue(() => _setCampaignPosition(position.X, position.Y));
        }
    }
}
