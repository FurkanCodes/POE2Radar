using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Native;
using POE2Radar.Overlay.Pickup;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private readonly PickupEngine _pickupEngine;
    private PickupView _pickupView = PickupView.Disabled;
    private PickupTarget[] _pickupNearbyTargets = [];
    private long _pickupNextLabelScanStamp;
    private uint _pickupTargetArea;

    private void RefreshPickup(
        LiveFrameState live,
        WorldSnapshot snapshot,
        int windowWidth,
        int windowHeight,
        bool gameFocused)
    {
        var settings = _settings.PickupHelper;
        var mode = (PickupMode)Math.Clamp(settings.Mode, 0, 3);
        var controllerTriggered = HotkeyCodes.IsGamepad(settings.ActivationHotkey);
        var activationControllerConnected =
            !controllerTriggered ||
            GamepadInput.IsConnected(_settings.GamepadUserIndex);
        var activationDown = settings.ActivationHotkey > 0 && HotkeyPoll.IsDown(settings.ActivationHotkey);
        var emergencyDown = settings.EmergencyStopHotkey > 0 && HotkeyPoll.IsDown(settings.EmergencyStopHotkey);
        var ground = snapshot.Entities
            .Where(IsGroundItem)
            .ToArray();
        var groundAddresses = ground.Select(x => x.Address).ToHashSet();

        IReadOnlyList<PickupTarget> targets;
        if (!settings.Enabled || !live.InGame)
        {
            _pickupNearbyTargets = [];
            targets = [];
        }
        else if (mode == PickupMode.HoverHold)
        {
            targets = BuildHoveredPickupTarget(live, ground, settings.MaxPickupDistance);
        }
        else if (mode is PickupMode.NearbyHold or PickupMode.AutoNearby &&
                 !(mode == PickupMode.NearbyHold
                     ? activationDown
                     : _pickupView.Running || activationDown))
        {
            _pickupNearbyTargets = [];
            targets = [];
        }
        else
        {
            RefreshNearbyPickupTargets(
                live,
                snapshot,
                ground,
                windowWidth,
                windowHeight,
                Math.Clamp(settings.MaxPickupDistance, 5, 100));
            targets = _pickupNearbyTargets
                .Where(x => groundAddresses.Contains(x.Address))
                .ToArray();
        }

        var showHiddenDown =
            settings.ShowHiddenItemsHotkey > 0 &&
            HotkeyPoll.IsDown(settings.ShowHiddenItemsHotkey);
        var filterOverrideHeld = ShouldPausePickupForShowHidden(settings, showHiddenDown);
        var uiClear = live.InGame &&
                      !_atlasOpen &&
                      _hpPct > 0f &&
                      _imguiOverlay?.IsSettingsOpen != true &&
                      _live.IsWorldInteractionUiClear(live.InGameState);
        var frame = new PickupFrame(
            settings,
            activationDown,
            emergencyDown,
            gameFocused,
            live.InGame,
            uiClear,
            filterOverrideHeld,
            controllerTriggered,
            activationControllerConnected,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            targets,
            groundAddresses);
        _pickupView = _pickupEngine.Tick(frame);
    }

    internal static bool ShouldPausePickupForShowHidden(
        PickupHelperSettings settings,
        bool showHiddenDown)
        => settings.PauseWhileShowHiddenHeld &&
           settings.ShowHiddenItemsHotkey > 0 &&
           settings.ActivationHotkey != settings.ShowHiddenItemsHotkey &&
           showHiddenDown;

    private IReadOnlyList<PickupTarget> BuildHoveredPickupTarget(
        LiveFrameState live,
        IReadOnlyList<Poe2Live.EntityDot> ground,
        int maxDistance)
    {
        var hovered = _live.MouseOverEntity(live.InGameState);
        if (hovered == 0) return [];
        var item = ground.FirstOrDefault(x => x.Address == hovered);
        if (item.Address == 0) return [];
        var distance = NumVec2.Distance(item.Grid, live.PlayerGrid);
        if (maxDistance > 0 && distance > maxDistance) return [];
        if (!TryGetCursorClient(out var cursor)) return [];
        var label = ItemDisplayName(item);
        return
        [
            new PickupTarget(
                item.Id,
                item.Address,
                label,
                distance,
                cursor.X - 2f,
                cursor.Y - 2f,
                4f,
                4f),
        ];
    }

    private void RefreshNearbyPickupTargets(
        LiveFrameState live,
        WorldSnapshot snapshot,
        IReadOnlyList<Poe2Live.EntityDot> ground,
        int windowWidth,
        int windowHeight,
        int maxDistance)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_pickupTargetArea != snapshot.AreaHash)
        {
            _pickupTargetArea = snapshot.AreaHash;
            _pickupNextLabelScanStamp = 0;
            _pickupNearbyTargets = [];
        }
        if (now < _pickupNextLabelScanStamp) return;
        var scanDelayMs = _settings.PickupHelper.Mode == (int)PickupMode.Assist ? 250 : 160;
        _pickupNextLabelScanStamp = now + MillisecondsToTicks(scanDelayMs);

        // Confirmation only needs the cheap world-entity snapshot; avoid walking the UI tree again
        // while the game is processing the current click.
        if (_pickupView.Status.StartsWith("PICKING", StringComparison.Ordinal)) return;

        if (live.CameraMatrix is not { Length: >= 16 } matrix)
        {
            _pickupNearbyTargets = [];
            return;
        }

        var labels = new List<VisiblePickupLabel>();
        foreach (var (element, firstLine) in _live.ScanLootLabels(live.InGameState, maxNodes: 6000))
        {
            if (!_live.TryUiElementRect(
                    element,
                    windowWidth,
                    windowHeight,
                    out var x,
                    out var y,
                    out var w,
                    out var h,
                    requireFirstLine: firstLine))
                continue;
            if (w < 8f || h < 5f || w > windowWidth * 0.8f || h > 160f) continue;
            var fullText = _live.UiElementText(element);
            var lines = fullText
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePickupLabel)
                .Where(s => s.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (lines.Count == 0) continue;
            labels.Add(new VisiblePickupLabel(element, firstLine.Trim(), x, y, w, h, lines));
        }

        var usedLabels = new HashSet<int>();
        var targets = new List<PickupTarget>();
        foreach (var item in ground
                     .Select(x => (Item: x, Distance: NumVec2.Distance(x.Grid, live.PlayerGrid)))
                     .Where(x => x.Distance <= maxDistance)
                     .OrderBy(x => x.Distance))
        {
            var itemName = NormalizePickupLabel(item.Item.ItemName);
            if (itemName.Length == 0) continue;
            var worldScreen = ProjectPickupWorld(item.Item.World, matrix, windowWidth, windowHeight);
            var bestIndex = -1;
            var bestScreenDistance = float.MaxValue;
            for (var i = 0; i < labels.Count; i++)
            {
                if (usedLabels.Contains(i) || !labels[i].Lines.Contains(itemName)) continue;
                var center = new NumVec2(
                    labels[i].X + labels[i].Width * 0.5f,
                    labels[i].Y + labels[i].Height * 0.5f);
                var screenDistance = NumVec2.DistanceSquared(center, worldScreen);
                if (screenDistance >= bestScreenDistance) continue;
                bestScreenDistance = screenDistance;
                bestIndex = i;
            }
            if (bestIndex < 0) continue;

            usedLabels.Add(bestIndex);
            var label = labels[bestIndex];
            targets.Add(new PickupTarget(
                item.Item.Id,
                item.Item.Address,
                label.FirstLine.Length > 0 ? label.FirstLine : ItemDisplayName(item.Item),
                item.Distance,
                label.X,
                label.Y,
                label.Width,
                label.Height,
                label.Element));
        }

        _pickupNearbyTargets = targets
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private PickupClickResult ClickPickupTarget(PickupTarget target)
    {
        if (_gameHwnd == 0 || !OverlayNative.IsGameFocused(_gameHwnd, _process.ProcessId))
            return PickupClickResult.InputFailed;
        if (!TryResolvePickupTargetScreenPoint(target, out var point))
            return target.LabelElement != 0
                ? PickupClickResult.TargetUnavailable
                : PickupClickResult.InputFailed;

        // Give the standalone client one frame slice to commit the SetCursorPos hover before the
        // click. The input adapter deliberately emits no second cursor movement.
        return SendInputNative.Click(point.X, point.Y, rightButton: false, settleMs: 12)
            ? PickupClickResult.Sent
            : PickupClickResult.InputFailed;
    }

    private bool TryResolvePickupTargetScreenPoint(
        PickupTarget target,
        out OverlayNative.POINT point)
    {
        var x = target.X;
        var y = target.Y;
        var width = target.Width;
        var height = target.Height;
        if (target.LabelElement != 0)
        {
            ResolveGameClientSize(out var windowWidth, out var windowHeight);
            if (!_live.TryUiElementRect(
                    target.LabelElement,
                    windowWidth,
                    windowHeight,
                    out x,
                    out y,
                    out width,
                    out height,
                    requireFirstLine: target.Label))
            {
                point = default;
                return false;
            }
        }
        point = new OverlayNative.POINT
        {
            X = (int)MathF.Round(x + width * 0.5f),
            Y = (int)MathF.Round(y + height * 0.5f),
        };
        return OverlayNative.ClientToScreen(_gameHwnd, ref point);
    }

    private static bool IsGroundItem(Poe2Live.EntityDot item)
        => item.Address != 0 &&
           item.Metadata.Contains("WorldItem", StringComparison.Ordinal);

    private static string ItemDisplayName(Poe2Live.EntityDot item)
        => !string.IsNullOrWhiteSpace(item.ItemName)
            ? item.ItemName
            : !string.IsNullOrWhiteSpace(item.ItemArt)
                ? item.ItemArt
                : "Visible item";

    private static string NormalizePickupLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var span = value.AsSpan().Trim();
        var start = 0;
        while (start < span.Length && (char.IsDigit(span[start]) || span[start] is 'x' or '×' or ' ' or '\t'))
            start++;
        return span[start..].ToString().Trim();
    }

    private static NumVec2 ProjectPickupWorld(
        POE2Radar.Core.Game.Vector3 world,
        float[] matrix,
        float width,
        float height)
    {
        var cw = world.X * matrix[3] + world.Y * matrix[7] + world.Z * matrix[11] + matrix[15];
        if (cw <= 0.0001f) return new NumVec2(width * 0.5f, height * 0.5f);
        var cx = world.X * matrix[0] + world.Y * matrix[4] + world.Z * matrix[8] + matrix[12];
        var cy = world.X * matrix[1] + world.Y * matrix[5] + world.Z * matrix[9] + matrix[13];
        return new NumVec2((cx / cw / 2f + 0.5f) * width, (0.5f - cy / cw / 2f) * height);
    }

    private readonly record struct VisiblePickupLabel(
        nint Element,
        string FirstLine,
        float X,
        float Y,
        float Width,
        float Height,
        HashSet<string> Lines);
}
