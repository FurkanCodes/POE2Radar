// Sekhema-specific behavior is derived from MordWraith/Gamehelper's GPL-3.0 SekhemaHelper.
// Upstream snapshot: 7e7a23571c494090cbc6a7faafa633e17762a78d. See the bundled notice/license.
using System.Numerics;
using ImGuiNET;
using POE2Radar.Overlay.Config;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class ImGuiRadarOverlay
{
    private int _sekhemaDrawCrashLogged;

    private void DrawSekhemaMapSafe(
        ImDrawListPtr drawList,
        RenderContext context,
        MapFrame frame,
        NumVec2 center,
        float scale)
    {
        try
        {
            DrawSekhemaMap(drawList, context, frame, center, scale);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _sekhemaDrawCrashLogged, 1) == 0)
                Diagnostics.CrashLog.Write("Sekhema map draw error (base radar kept alive)", ex);
        }
    }

    private void DrawSekhemaScreenOverlaySafe(ImDrawListPtr drawList, RenderContext context)
    {
        try
        {
            DrawSekhemaScreenOverlay(drawList, context);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _sekhemaDrawCrashLogged, 1) == 0)
                Diagnostics.CrashLog.Write("Sekhema screen draw error (base radar kept alive)", ex);
        }
    }

    private void DrawSekhemaMap(
        ImDrawListPtr drawList,
        RenderContext context,
        MapFrame frame,
        NumVec2 center,
        float scale)
    {
        var view = context.Sekhema;
        var settings = _settings.Sekhema;
        if (!settings.Enabled || !view.InTrial || !context.Map.IsVisible) return;

        if (settings.Debug && settings.HazardDebugDrawWalkable && context.Terrain is { } terrain)
            DrawSekhemaWalkableDebug(drawList, context, terrain, center, scale, settings);

        if (settings.Debug)
        {
            var activeColor = ColorU32("#FFE633", 1f);
            var collectedColor = ColorU32("#8C8C8C", 1f);
            foreach (var crystal in view.Crystals)
            {
                var point = Project(
                    crystal.Grid,
                    MapProjectionMotion.PlayerReference(context),
                    center,
                    scale,
                    crystal.TerrainHeight - frame.PlayerTerrainHeight);
                var id = crystal.Id.ToString();
                var size = ImGui.CalcTextSize(id);
                var at = point - size * 0.5f - new NumVec2(0f, 14f);
                var pad = new NumVec2(3f, 1f);
                drawList.AddRectFilled(at - pad, at + size + pad, 0x8C000000u, 2f);
                drawList.AddText(at + NumVec2.One, 0xFF000000u, id);
                drawList.AddText(at, crystal.Active ? activeColor : collectedColor, id);
            }
        }

        if (settings.DrawHazardRoute)
        {
            var routeColor = ColorU32(settings.HazardRouteColor, 1f);
            var markerColor = ColorU32(settings.HazardMarkerColor, 1f);
            for (var legIndex = 0; legIndex < view.CrystalRoute.Length; legIndex++)
            {
                var leg = view.CrystalRoute[legIndex];
                if (leg.Points.Length == 0) continue;
                NumVec2? previous = null;
                for (var pointIndex = 0; pointIndex < leg.Points.Length; pointIndex++)
                {
                    var progress = leg.Points.Length <= 1 ? 1f : pointIndex / (float)(leg.Points.Length - 1);
                    var height = leg.StartTerrainHeight +
                                 (leg.EndTerrainHeight - leg.StartTerrainHeight) * progress;
                    var projected = Project(
                        leg.Points[pointIndex],
                        MapProjectionMotion.PlayerReference(context),
                        center,
                        scale,
                        height - frame.PlayerTerrainHeight);
                    if (previous is { } prior)
                        drawList.AddLine(prior, projected, routeColor, settings.HazardRouteThickness);
                    previous = projected;
                }

                var endpoint = Project(
                    leg.Points[^1],
                    MapProjectionMotion.PlayerReference(context),
                    center,
                    scale,
                    leg.EndTerrainHeight - frame.PlayerTerrainHeight);
                drawList.AddCircleFilled(endpoint, settings.HazardMarkerRadius, markerColor, 16);
                drawList.AddCircle(endpoint, settings.HazardMarkerRadius, routeColor, 16, 1.5f);
                var number = (legIndex + 1).ToString();
                var numberSize = ImGui.CalcTextSize(number);
                drawList.AddText(endpoint - numberSize * 0.5f, 0xFF000000u, number);

                if (settings.Debug)
                {
                    var mode = leg.Walkable ? "A*" : "straight";
                    var info = $"{leg.StraightDistance:0} {mode}";
                    var midpointIndex = leg.Points.Length / 2;
                    var midpointProgress = leg.Points.Length <= 1
                        ? 1f
                        : midpointIndex / (float)(leg.Points.Length - 1);
                    var midpointHeight = leg.StartTerrainHeight +
                                         (leg.EndTerrainHeight - leg.StartTerrainHeight) * midpointProgress;
                    var midpoint = Project(
                        leg.Points[midpointIndex],
                        MapProjectionMotion.PlayerReference(context),
                        center,
                        scale,
                        midpointHeight - frame.PlayerTerrainHeight);
                    var textSize = ImGui.CalcTextSize(info);
                    var at = midpoint - textSize * 0.5f;
                    drawList.AddText(at + NumVec2.One, 0xFF000000u, info);
                    drawList.AddText(at, routeColor, info);
                }
            }
        }

        foreach (var marker in view.Markers)
        {
            var point = Project(
                marker.Grid,
                MapProjectionMotion.PlayerReference(context),
                center,
                scale,
                marker.TerrainHeight - frame.PlayerTerrainHeight);
            var (fill, outline, radius) = marker.Kind switch
            {
                SekhemaMarkerKind.Portal => (
                    ColorU32(settings.PortalColor, 0.9f),
                    0xFFFFFFFFu,
                    settings.RoomObjectMarkerRadius),
                SekhemaMarkerKind.Lever => (
                    ColorU32(settings.LeverColor, 0.9f),
                    0xFFFFFFFFu,
                    settings.RoomObjectMarkerRadius),
                SekhemaMarkerKind.ChestGold => (
                    ColorU32("#FFD619", 0.95f),
                    ColorU32(settings.ChestMarkerColor, 0.95f),
                    settings.ChestMarkerRadius),
                SekhemaMarkerKind.ChestSilver => (
                    ColorU32("#D1D9F2", 0.95f),
                    ColorU32(settings.ChestMarkerColor, 0.95f),
                    settings.ChestMarkerRadius),
                _ => (
                    ColorU32("#D98C40", 0.95f),
                    ColorU32(settings.ChestMarkerColor, 0.95f),
                    settings.ChestMarkerRadius),
            };
            drawList.AddCircleFilled(point, radius, fill, 18);
            drawList.AddCircle(point, radius, outline, 18, marker.Kind is SekhemaMarkerKind.Portal or SekhemaMarkerKind.Lever ? 1.5f : 2f);
            DrawSekhemaLabel(drawList, point, radius, marker.Label);
        }
    }

    private static void DrawSekhemaLabel(ImDrawListPtr drawList, NumVec2 point, float radius, string label)
    {
        if (label.Length == 0) return;
        var size = ImGui.CalcTextSize(label);
        var at = new NumVec2(point.X - size.X * 0.5f, point.Y - radius - size.Y - 3f);
        var pad = new NumVec2(3f, 1f);
        drawList.AddRectFilled(at - pad, at + size + pad, 0xA6000000u, 2f);
        drawList.AddText(at, 0xFFFFFFFFu, label);
    }

    private static void DrawSekhemaWalkableDebug(
        ImDrawListPtr drawList,
        RenderContext context,
        Core.Game.Poe2Live.TerrainData terrain,
        NumVec2 center,
        float scale,
        SekhemaSettings settings)
    {
        var radius = (int)Math.Clamp(settings.HazardDebugWalkableRadius, 50, 1200);
        var stride = Math.Max(2, radius / 120);
        var player = MapProjectionMotion.PlayerReference(context);
        var playerX = (int)player.X;
        var playerY = (int)player.Y;
        var color = ColorU32("#33FF4D", 0.28f);
        var cellSize = Math.Max(1f, scale * stride * 0.6f);
        var half = new NumVec2(cellSize);
        for (var y = playerY - radius; y <= playerY + radius; y += stride)
        for (var x = playerX - radius; x <= playerX + radius; x += stride)
        {
            if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height ||
                terrain.Walkable[y * terrain.Width + x] == 0)
                continue;
            var point = Project(new NumVec2(x, y), player, center, scale);
            drawList.AddRectFilled(point - half, point + half, color);
        }
    }

    private void DrawSekhemaScreenOverlay(ImDrawListPtr drawList, RenderContext context)
    {
        var view = context.Sekhema;
        var settings = _settings.Sekhema;
        if (!settings.Enabled || !view.InTrial) return;

        var bestColor = ColorU32(settings.BestPathColor, 1f);
        var debugColor = ColorU32(settings.DebugTextColor, 1f);
        foreach (var room in view.Rooms)
        {
            var min = new NumVec2(room.Rect.X, room.Rect.Y);
            var max = new NumVec2(room.Rect.X + room.Rect.W, room.Rect.Y + room.Rect.H);
            if (room.BestPath)
                drawList.AddRect(min, max, bestColor, 0f, ImDrawFlags.None, settings.FrameThickness);
            if (settings.Debug && room.DebugText.Length > 0)
            {
                var size = ImGui.CalcTextSize(room.DebugText);
                drawList.AddRectFilled(min - new NumVec2(3f, 1f), min + size + new NumVec2(3f, 1f),
                    ColorU32(settings.DebugBackgroundColor, 0.75f), 2f);
                drawList.AddText(min, debugColor, room.DebugText);
            }
        }

        if (!settings.Debug) return;
        var resources = view.Resources;
        var text =
            $"Sekhema: {view.Status}\nwater {Resource(resources.Water)}, honour {Percent(resources.HonourPercent)}, " +
            $"keys {Resource(resources.BronzeKeys)}/{Resource(resources.SilverKeys)}/{Resource(resources.GoldKeys)} (B/S/G)";
        var at = new NumVec2(20f, 120f);
        var textSize = ImGui.CalcTextSize(text);
        drawList.AddRectFilled(at - new NumVec2(4f, 2f), at + textSize + new NumVec2(4f, 2f),
            ColorU32(settings.DebugBackgroundColor, 0.8f), 2f);
        drawList.AddText(at, debugColor, text);

        static string Resource(int value) => value >= 0 ? value.ToString() : "?";
        static string Percent(double value) => value >= 0 ? $"{value:F0}%" : "?";
    }

    private void DrawSekhemaTab(RadarSettings root, RenderContext? context)
    {
        var settings = root.Sekhema;
        SekhemaCheckbox("Enable Sekhema Helper", settings.Enabled, value => settings.Enabled = value);
        if (!settings.Enabled) return;

        if (context is { Sekhema.InTrial: true } ctx)
        {
            var resources = ctx.Sekhema.Resources;
            ImGui.TextDisabled(
                $"Live: water {Display(resources.Water)} · honour {DisplayPct(resources.HonourPercent)} · " +
                $"keys {Display(resources.BronzeKeys)}/{Display(resources.SilverKeys)}/{Display(resources.GoldKeys)} B/S/G");
            ImGui.TextDisabled(ctx.Sekhema.Status);
        }
        else
        {
            ImGui.TextDisabled("Enter the Trial of the Sekhemas to see live data.");
        }

        ImGui.SeparatorText("Profile");
        if (ImGui.BeginCombo("Active Profile", settings.CurrentProfile))
        {
            foreach (var name in settings.Profiles.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                var selected = name == settings.CurrentProfile;
                if (ImGui.Selectable(name, selected)) settings.CurrentProfile = name;
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (ImGui.Button("Reset this profile to defaults"))
            settings.Profiles[settings.CurrentProfile] = settings.CurrentProfile == "No-Hit"
                ? SekhemaProfileSettings.CreateNoHit()
                : SekhemaProfileSettings.CreateDefault();

        var profile = settings.Profiles.TryGetValue(settings.CurrentProfile, out var selectedProfile)
            ? selectedProfile
            : SekhemaProfileSettings.CreateDefault();
        settings.Profiles[settings.CurrentProfile] = profile;
        DrawSekhemaWeights("Room types", "rooms", profile.RoomTypeWeights);

        SekhemaCheckbox("Avoid Merchant when water below", settings.SuppressMerchantLowWater,
            value => settings.SuppressMerchantLowWater = value);
        if (settings.SuppressMerchantLowWater)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            SekhemaSliderInt("##SekhemaMerchantWater", settings.MerchantWaterThreshold, 100, 1000,
                value => settings.MerchantWaterThreshold = value);
        }
        SekhemaCheckbox("Avoid honour restore when honour above", settings.SuppressHonourRestoreHighPct,
            value => settings.SuppressHonourRestoreHighPct = value);
        if (settings.SuppressHonourRestoreHighPct)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            SekhemaSliderInt("##SekhemaHonourPct", settings.HonourRestoreThresholdPct, 30, 100,
                value => settings.HonourRestoreThresholdPct = value, "%d%%");
        }
        DrawSekhemaWeights("Afflictions", "afflictions", profile.AfflictionWeights);
        DrawSekhemaWeights("Rewards", "rewards", profile.RewardWeights);

        ImGui.SeparatorText("Room map");
        SekhemaCheckbox("Draw Best Path", settings.DrawBestPath, value => settings.DrawBestPath = value);
        if (settings.DrawBestPath)
        {
            SekhemaSliderFloat("Frame Thickness", settings.FrameThickness, 1f, 10f,
                value => settings.FrameThickness = value);
            SekhemaColor("Best Path Color", settings.BestPathColor, value => settings.BestPathColor = value);
        }

        if (ImGui.CollapsingHeader("Overlay POI", ImGuiTreeNodeFlags.DefaultOpen))
        {
            SekhemaCheckbox("Show Portals (Ritual)", settings.ShowPortals, value => settings.ShowPortals = value);
            if (settings.ShowPortals)
                SekhemaColor("Portal Color", settings.PortalColor, value => settings.PortalColor = value);
            SekhemaCheckbox("Show Levers (Gauntlet)", settings.ShowLevers, value => settings.ShowLevers = value);
            if (settings.ShowLevers)
                SekhemaColor("Lever Color", settings.LeverColor, value => settings.LeverColor = value);
            if (settings.ShowPortals || settings.ShowLevers)
                SekhemaSliderFloat("POI Marker Radius", settings.RoomObjectMarkerRadius, 4f, 20f,
                    value => settings.RoomObjectMarkerRadius = value);

            SekhemaCheckbox("Show Crystals (Escape)", settings.DrawHazardRoute,
                value => settings.DrawHazardRoute = value);
            if (settings.DrawHazardRoute)
            {
                SekhemaCheckbox("Follow Walkable Terrain (A*)", settings.HazardWalkableRoute,
                    value => settings.HazardWalkableRoute = value);
                SekhemaColor("Route Line Color", settings.HazardRouteColor, value => settings.HazardRouteColor = value);
                SekhemaColor("Crystal Marker Color", settings.HazardMarkerColor,
                    value => settings.HazardMarkerColor = value);
                SekhemaSliderFloat("Route Thickness", settings.HazardRouteThickness, 1f, 8f,
                    value => settings.HazardRouteThickness = value);
                SekhemaSliderFloat("Marker Radius", settings.HazardMarkerRadius, 3f, 20f,
                    value => settings.HazardMarkerRadius = value);
                SekhemaSliderInt("Room ID Gap (0 = off)", settings.HazardIdGroupGap, 0, 200,
                    value => settings.HazardIdGroupGap = value);
                SekhemaSliderFloat("Room Margin (in-room gate)", settings.HazardRoomMargin, 0f, 800f,
                    value => settings.HazardRoomMargin = value, "%.0f");
            }
        }

        ImGui.SeparatorText("Reward room");
        SekhemaCheckbox("Mark Best Chests by Keys", settings.DrawChestPriority,
            value => settings.DrawChestPriority = value);
        if (settings.DrawChestPriority)
        {
            SekhemaColor("Selected Marker Color", settings.ChestMarkerColor,
                value => settings.ChestMarkerColor = value);
            SekhemaSliderFloat("Chest Marker Radius", settings.ChestMarkerRadius, 4f, 24f,
                value => settings.ChestMarkerRadius = value);
            DrawSekhemaChestPriority(settings);
        }

        ImGui.SeparatorText("Diagnostics");
        SekhemaCheckbox("Debug (show weights and live reads)", settings.Debug, value => settings.Debug = value);
        if (!settings.Debug) return;
        SekhemaColor("Debug Text Color", settings.DebugTextColor, value => settings.DebugTextColor = value);
        SekhemaColor("Debug Background", settings.DebugBackgroundColor,
            value => settings.DebugBackgroundColor = value);
        var ids = settings.HazardDebugCrystalIds;
        if (ImGui.InputText("Force Crystal IDs", ref ids, 128))
            settings.HazardDebugCrystalIds = ids;
        SekhemaCheckbox("Paint Walkable Grid", settings.HazardDebugDrawWalkable,
            value => settings.HazardDebugDrawWalkable = value);
        if (settings.HazardDebugDrawWalkable)
            SekhemaSliderFloat("Walkable Paint Radius", settings.HazardDebugWalkableRadius, 50f, 1200f,
                value => settings.HazardDebugWalkableRadius = value, "%.0f");

        static string Display(int value) => value >= 0 ? value.ToString() : "?";
        static string DisplayPct(double value) => value >= 0 ? $"{value:F0}%" : "?";
    }

    private static void DrawSekhemaWeights(
        string title,
        string id,
        Dictionary<string, float> weights)
    {
        if (!ImGui.CollapsingHeader($"{title} ({weights.Count})##Sekhema{id}")) return;
        ImGui.PushID($"SekhemaWeights{id}");
        foreach (var name in weights.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var value = weights[name];
            if (ImGui.DragFloat(name, ref value, 10f, -1_000_000f, 1_000_000f, "%.0f"))
                weights[name] = value;
        }
        ImGui.PopID();
    }

    private static void DrawSekhemaChestPriority(SekhemaSettings settings)
    {
        if (!ImGui.TreeNode("Content priority (top = best)")) return;
        ImGui.TextDisabled("Tick content to track it. Higher rows win; quality and distance break ties.");
        var moveFrom = -1;
        var moveTo = -1;
        for (var i = 0; i < settings.ChestPriorityOrder.Count; i++)
        {
            ImGui.PushID($"SekhemaChest{i}");
            if (ImGui.ArrowButton("up", ImGuiDir.Up) && i > 0)
            {
                moveFrom = i;
                moveTo = i - 1;
            }
            ImGui.SameLine();
            if (ImGui.ArrowButton("down", ImGuiDir.Down) && i + 1 < settings.ChestPriorityOrder.Count)
            {
                moveFrom = i;
                moveTo = i + 1;
            }
            ImGui.SameLine();
            var content = settings.ChestPriorityOrder[i];
            var enabled = !settings.ChestDisabledContent.Contains(content);
            if (ImGui.Checkbox($"{i + 1}. {content}", ref enabled))
            {
                if (enabled) settings.ChestDisabledContent.Remove(content);
                else settings.ChestDisabledContent.Add(content);
            }
            ImGui.PopID();
        }
        if (moveFrom >= 0)
        {
            var item = settings.ChestPriorityOrder[moveFrom];
            settings.ChestPriorityOrder.RemoveAt(moveFrom);
            settings.ChestPriorityOrder.Insert(moveTo, item);
        }
        if (ImGui.Button("Reset chest priority order"))
        {
            settings.ChestPriorityOrder =
            [
                "GrandSpectrum", "RadiusJewels", "LargeRelic", "Jewels", "Currency",
                "MediumRelic", "SmallRelic", "Maps", "Generic",
            ];
            settings.ChestDisabledContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Currency", "MediumRelic", "SmallRelic", "Maps", "Generic",
            };
        }
        ImGui.TreePop();
    }

    private static void SekhemaCheckbox(string label, bool value, Action<bool> setter)
    {
        if (ImGui.Checkbox(label, ref value)) setter(value);
    }

    private static void SekhemaSliderInt(
        string label,
        int value,
        int minimum,
        int maximum,
        Action<int> setter,
        string format = "%d")
    {
        if (ImGui.SliderInt(label, ref value, minimum, maximum, format)) setter(value);
    }

    private static void SekhemaSliderFloat(
        string label,
        float value,
        float minimum,
        float maximum,
        Action<float> setter,
        string format = "%.3f")
    {
        if (ImGui.SliderFloat(label, ref value, minimum, maximum, format)) setter(value);
    }

    private static void SekhemaColor(string label, string value, Action<string> setter)
    {
        var color = ParseHexColor(value);
        if (ImGui.ColorEdit3(label, ref color, ImGuiColorEditFlags.NoInputs))
            setter(FormatHexColor3(color));
    }
}
