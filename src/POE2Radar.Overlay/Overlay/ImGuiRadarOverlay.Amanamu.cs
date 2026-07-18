using ImGuiNET;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Settings;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class ImGuiRadarOverlay
{
    private void DrawAmanamuWorldOverlay(ImDrawListPtr dl, RenderContext ctx)
    {
        if (ctx.Amanamu is not { } view
            || !view.Enabled
            || !view.ShowWorldOverlay
            || view.Alerts.Length == 0
            || ctx.CameraMatrix is not { Length: >= 16 } matrix
            || ShouldDrawLargeMapOverlay(ctx.Map))
            return;

        var width = (float)ctx.WindowWidth;
        var height = (float)ctx.WindowHeight;
        var center = new NumVec2(width * 0.5f, height * 0.5f);

        foreach (var alert in view.Alerts)
        {
            var color = ColorU32(
                alert.InsideCloud ? view.InsideCloudColor : view.OutsideCloudColor,
                0.98f);
            var projected = TryProjectAmanamu(alert.World, matrix, width, height, out var screen, out var direction);
            var onScreen = projected
                           && screen.X >= 0f && screen.X <= width
                           && screen.Y >= 0f && screen.Y <= height;

            if (onScreen)
            {
                screen = SmoothScreenPoint(
                    $"amanamu:{alert.Id}",
                    screen,
                    ctx.OverlaySmoothingMs,
                    ctx.SmoothOverlayMotion);

                if (view.DrawCircle)
                {
                    dl.AddCircle(screen, view.CircleRadius + 2f, ColorU32(0, 0, 0, 0.9f), 48, 5f);
                    dl.AddCircle(screen, view.CircleRadius, color, 48, 3f);
                }

                if (view.DrawLabels)
                {
                    var state = alert.InsideCloud ? "INSIDE CLOUD" : "OUTSIDE CLOUD";
                    var label = $"AMANAMU VOID\n{state}\n{alert.DistanceGrid:0}g";
                    var textSize = ImGui.CalcTextSize(label);
                    var textAt = new NumVec2(
                        screen.X - textSize.X * 0.5f,
                        screen.Y - view.LabelYOffset - textSize.Y);
                    dl.AddText(textAt + NumVec2.One, ColorU32(0, 0, 0, 0.95f), label);
                    dl.AddText(textAt, color, label);
                }
            }

            if (!onScreen && view.DrawOffscreenArrows)
            {
                if (direction.LengthSquared() < 0.0001f)
                    direction = alert.Grid - ctx.PlayerGrid;
                if (direction.LengthSquared() < 0.0001f)
                    direction = new NumVec2(0f, -1f);
                direction = NumVec2.Normalize(direction);

                var arrowAt = ClampAmanamuArrowToEdge(
                    center,
                    direction,
                    width,
                    height,
                    view.ArrowEdgeMargin);
                DrawAmanamuArrow(dl, arrowAt, direction, color);

                var edgeLabel = alert.InsideCloud
                    ? $"VOID {alert.DistanceGrid:0}g IN"
                    : $"VOID {alert.DistanceGrid:0}g OUT";
                var edgeSize = ImGui.CalcTextSize(edgeLabel);
                var edgeAt = new NumVec2(
                    Math.Clamp(arrowAt.X - edgeSize.X * 0.5f, 4f, width - edgeSize.X - 4f),
                    Math.Clamp(arrowAt.Y + 18f, 4f, height - edgeSize.Y - 4f));
                dl.AddText(edgeAt + NumVec2.One, ColorU32(0, 0, 0, 0.95f), edgeLabel);
                dl.AddText(edgeAt, color, edgeLabel);
            }
        }
    }

    private static bool TryProjectAmanamu(
        System.Numerics.Vector3 world,
        float[] matrix,
        float width,
        float height,
        out NumVec2 screen,
        out NumVec2 direction)
    {
        var cw = world.X * matrix[3] + world.Y * matrix[7] + world.Z * matrix[11] + matrix[15];
        var cx = world.X * matrix[0] + world.Y * matrix[4] + world.Z * matrix[8] + matrix[12];
        var cy = world.X * matrix[1] + world.Y * matrix[5] + world.Z * matrix[9] + matrix[13];

        if (!float.IsFinite(cw) || !float.IsFinite(cx) || !float.IsFinite(cy))
        {
            screen = new NumVec2(width * 0.5f, height * 0.5f);
            direction = NumVec2.Zero;
            return false;
        }

        if (cw <= 0.0001f)
        {
            screen = new NumVec2(width * 0.5f, height * 0.5f);
            direction = new NumVec2(-cx, cy);
            return false;
        }

        screen = new NumVec2(
            (cx / cw / 2f + 0.5f) * width,
            (0.5f - cy / cw / 2f) * height);
        direction = screen - new NumVec2(width * 0.5f, height * 0.5f);
        return float.IsFinite(screen.X) && float.IsFinite(screen.Y);
    }

    private static NumVec2 ClampAmanamuArrowToEdge(
        NumVec2 center,
        NumVec2 direction,
        float width,
        float height,
        float margin)
    {
        var halfW = MathF.Max(1f, width * 0.5f - margin);
        var halfH = MathF.Max(1f, height * 0.5f - margin);
        var tx = MathF.Abs(direction.X) > 0.0001f ? halfW / MathF.Abs(direction.X) : float.MaxValue;
        var ty = MathF.Abs(direction.Y) > 0.0001f ? halfH / MathF.Abs(direction.Y) : float.MaxValue;
        return center + direction * MathF.Min(tx, ty);
    }

    private static void DrawAmanamuArrow(ImDrawListPtr dl, NumVec2 at, NumVec2 direction, uint color)
    {
        var perpendicular = new NumVec2(-direction.Y, direction.X);
        var tip = at + direction * 13f;
        var baseCenter = at - direction * 8f;
        dl.AddTriangleFilled(tip, baseCenter + perpendicular * 9f, baseCenter - perpendicular * 9f, color);
        dl.AddTriangle(tip, baseCenter + perpendicular * 9f, baseCenter - perpendicular * 9f,
            ColorU32(0, 0, 0, 0.9f), 2f);
    }

    private static void DrawAmanamuMapMarkers(
        ImDrawListPtr dl,
        RenderContext ctx,
        NumVec2 player,
        NumVec2 center,
        float scale,
        List<MapLabelCandidate> labels,
        float clipL,
        float clipT,
        float clipR,
        float clipB)
    {
        if (ctx.Amanamu is not { } view
            || !view.Enabled
            || !view.ShowMapMarkers
            || view.Alerts.Length == 0)
            return;

        foreach (var alert in view.Alerts)
        {
            var at = Project(alert.Grid, player, center, scale);
            if (at.X < clipL - 40f || at.Y < clipT - 40f || at.X > clipR + 40f || at.Y > clipB + 40f)
                continue;

            var color = ColorU32(
                alert.InsideCloud ? view.InsideCloudColor : view.OutsideCloudColor,
                0.98f);
            dl.AddCircleFilled(at, 7f, ColorU32(0, 0, 0, 0.9f), 20);
            dl.AddCircleFilled(at, 5f, color, 20);
            dl.AddCircle(at, 10f, color, 24, 2.5f);
            var label = alert.InsideCloud ? "VOID IN" : "VOID OUT";
            if (!MapLabelAlreadyPresent(labels, label))
                labels.Add(new MapLabelCandidate($"amanamu:{alert.Id}", at, label, color, color));
        }
    }

    private void DrawAmanamuTab(RadarSettings settings, RenderContext? ctx)
    {
        var s = settings.Amanamu;
        var enabled = s.Enabled;
        if (ImGui.Checkbox("Enable Amanamu Void Alert", ref enabled)) s.Enabled = enabled;
        ImGuiTheme.Tooltip(SettingHints.Amanamu.Enabled);

        if (ctx?.Amanamu is { } live)
        {
            ImGui.SameLine();
            ImGui.TextColored(
                live.Alerts.Length > 0 ? new System.Numerics.Vector4(0.31f, 1f, 0.47f, 1f) : ImGuiTheme.TextMuted,
                $"{live.Alerts.Length} detected");
        }

        var detectionOpen = ImGuiTheme.BeginAccordionSection(
            "AmanamuDetection",
            "Detection and performance",
            defaultOpen: true);
        if (detectionOpen)
        {
            var rareOnly = s.OnlyRareOrUnique;
            if (ImGui.Checkbox("Only rare / unique monsters", ref rareOnly)) s.OnlyRareOrUnique = rareOnly;
            ImGuiTheme.Tooltip(SettingHints.Amanamu.RareOnly);

            var distance = Math.Clamp(s.MaxDistanceGrid, 0, 2000);
            ImGui.SetNextItemWidth(UiW(9f));
            if (ImGui.SliderInt("Detection distance (grid)", ref distance, 0, 1000))
                s.MaxDistanceGrid = distance;
            ImGuiTheme.Tooltip(SettingHints.Amanamu.Distance);

            ImGui.TextWrapped("Discovery is capped at 4 unknown candidates per world tick and runs only when cached Abyss signals are present. Confirmed monsters alone receive live buff polls.");
        }
        ImGuiTheme.EndAccordionSection(detectionOpen);

        var displayOpen = ImGuiTheme.BeginAccordionSection(
            "AmanamuDisplay",
            "Display",
            defaultOpen: true);
        if (displayOpen)
        {
            var world = s.ShowWorldOverlay;
            if (ImGui.Checkbox("World labels and arrows", ref world)) s.ShowWorldOverlay = world;
            var map = s.ShowMapMarkers;
            if (ImGui.Checkbox("Map markers", ref map)) s.ShowMapMarkers = map;
            var labels = s.DrawLabels;
            if (ImGui.Checkbox("On-screen labels", ref labels)) s.DrawLabels = labels;
            var arrows = s.DrawOffscreenArrows;
            if (ImGui.Checkbox("Off-screen arrows", ref arrows)) s.DrawOffscreenArrows = arrows;
            var circle = s.DrawCircle;
            if (ImGui.Checkbox("Target circle", ref circle)) s.DrawCircle = circle;

            var radius = Math.Clamp(s.CircleRadius, 8f, 160f);
            ImGui.SetNextItemWidth(UiW(9f));
            if (ImGui.SliderFloat("Circle radius", ref radius, 8f, 120f, "%.0f px"))
                s.CircleRadius = radius;

            var inside = s.InsideCloudColor ?? "#B450FF";
            ImGui.SetNextItemWidth(UiW(9f));
            if (ImGui.InputText("Inside-cloud color", ref inside, 16))
                s.InsideCloudColor = inside;
            var outside = s.OutsideCloudColor ?? "#50FF78";
            ImGui.SetNextItemWidth(UiW(9f));
            if (ImGui.InputText("Outside-cloud color", ref outside, 16))
                s.OutsideCloudColor = outside;
        }
        ImGuiTheme.EndAccordionSection(displayOpen);
    }
}
