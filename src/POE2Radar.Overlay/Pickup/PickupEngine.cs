using POE2Radar.Overlay.Config;

namespace POE2Radar.Overlay.Pickup;

internal enum PickupMode : byte
{
    Assist = 0,
    HoverHold = 1,
    NearbyHold = 2,
    AutoNearby = 3,
}

internal readonly record struct PickupTarget(
    uint Id,
    nint Address,
    string Label,
    float Distance,
    float X,
    float Y,
    float Width,
    float Height,
    nint LabelElement = 0);

internal enum PickupClickResult : byte
{
    Sent,
    TargetUnavailable,
    InputFailed,
}

internal readonly record struct PickupFrame(
    PickupHelperSettings Settings,
    bool ActivationDown,
    bool EmergencyDown,
    bool GameFocused,
    bool InGame,
    bool UiClear,
    bool FilterOverrideHeld,
    bool ControllerTriggered,
    bool ControllerConnected,
    long Now,
    IReadOnlyList<PickupTarget> Targets,
    IReadOnlySet<nint> GroundItems);

internal readonly record struct PickupView(string Status, bool Running, PickupTarget? Target)
{
    public static readonly PickupView Disabled = new("Disabled", false, null);
}

/// <summary>
/// Stateful pickup module. Its one interface hides delay scheduling, safety stops, click rate limits,
/// and post-click entity confirmation. Target discovery and the concrete input action are adapters.
/// </summary>
internal sealed class PickupEngine(Func<PickupTarget, PickupClickResult> click)
{
    private enum Stage : byte { Idle, Delay, WaitingForConfirmation }

    private readonly Func<PickupTarget, PickupClickResult> _click = click;
    private Stage _stage;
    private PickupTarget _target;
    private long _nextActionStamp;
    private long _confirmationTimeoutStamp;
    private bool _blockedUntilRelease;
    private string _blockedStatus = "";
    private bool _activationWasDown;
    private bool _autoRunning;
    private bool _autoControllerStarted;
    private readonly Dictionary<nint, MissState> _misses = [];

    private readonly record struct MissState(int Count, long RetryAfter);

    public PickupView Tick(in PickupFrame frame)
    {
        var settings = frame.Settings;
        var activationPressed = frame.ActivationDown && !_activationWasDown;
        _activationWasDown = frame.ActivationDown;
        if (!settings.Enabled)
        {
            _autoRunning = false;
            ResetAction();
            return PickupView.Disabled;
        }

        var mode = (PickupMode)Math.Clamp(settings.Mode, 0, 3);
        if (frame.EmergencyDown)
            return Block("EMERGENCY STOP");

        if (mode == PickupMode.Assist)
        {
            _autoRunning = false;
            ResetAction();
            var assist = frame.Targets.Count > 0 ? frame.Targets[0] : (PickupTarget?)null;
            return new PickupView(
                assist is { } target ? $"ASSIST · {target.Label}" : "ASSIST · no visible loot in range",
                false,
                assist);
        }

        if (mode is PickupMode.NearbyHold or PickupMode.AutoNearby && !settings.AutoModeAcknowledged)
        {
            _autoRunning = false;
            ResetAction();
            return new PickupView("Automatic label clicks require safety acknowledgement", false, null);
        }

        if (mode == PickupMode.AutoNearby && activationPressed)
        {
            if (_autoRunning)
            {
                _autoRunning = false;
                ResetAction();
                return new PickupView("AUTO armed · press activation", false, null);
            }

            _autoRunning = true;
            _autoControllerStarted = frame.ControllerTriggered;
            ResetAction();
        }
        else if (mode != PickupMode.AutoNearby)
        {
            _autoRunning = false;
            _autoControllerStarted = false;
        }

        var active = mode == PickupMode.AutoNearby ? _autoRunning : frame.ActivationDown;
        var controllerTriggered = mode == PickupMode.AutoNearby
            ? _autoControllerStarted
            : frame.ControllerTriggered;
        if (!active)
        {
            ResetAction();
            var status = mode == PickupMode.AutoNearby
                ? "AUTO armed · press activation"
                : "READY · hold activation";
            return new PickupView(status, false, frame.Targets.Count > 0 ? frame.Targets[0] : null);
        }

        if (_blockedUntilRelease)
            return new PickupView(_blockedStatus, false, _target.Address != 0 ? _target : null);

        if (!frame.GameFocused || !frame.InGame)
            return Block("Stopped: PoE2 lost focus");
        if (frame.FilterOverrideHeld)
            return Block("Stopped: show-hidden-items key is held");
        if (!frame.UiClear)
            return Block("Stopped: blocking game panel is open");
        if (controllerTriggered && !frame.ControllerConnected)
            return Block("Stopped: controller disconnected");

        PruneMisses(frame.GroundItems);
        if (_stage == Stage.WaitingForConfirmation)
        {
            if (!frame.GroundItems.Contains(_target.Address))
            {
                var completed = _target.Label;
                _misses.Remove(_target.Address);
                _stage = Stage.Idle;
                _target = default;
                _nextActionStamp = frame.Now + MillisecondsToTicks(Math.Clamp(settings.ClickCooldownMs, 100, 1000));
                return new PickupView($"PICKED · {completed}", true, null);
            }

            if (frame.Now >= _confirmationTimeoutStamp && mode == PickupMode.AutoNearby)
                return RecoverAutomaticMiss(frame.Now, settings);
            if (frame.Now >= _confirmationTimeoutStamp)
                return Block("Stopped: pickup was not confirmed");

            return new PickupView($"PICKING · {_target.Label}", true, _target);
        }

        var next = ChooseTarget(frame.Targets, frame.Now);
        if (next.Address == 0)
        {
            _stage = Stage.Idle;
            _target = default;
            var status = frame.Targets.Count == 0
                ? "READY · no visible loot in range"
                : "READY · reacquiring moved loot";
            return new PickupView(status, true, null);
        }

        if (_stage != Stage.Delay || next.Address != _target.Address)
        {
            _target = next;
            _stage = Stage.Delay;
            var min = Math.Clamp(settings.MinPickupDelayMs, 0, 500);
            var max = Math.Clamp(settings.MaxPickupDelayMs, min, 750);
            var delay = min == max ? min : Random.Shared.Next(min, max + 1);
            _nextActionStamp = Math.Max(_nextActionStamp, frame.Now + MillisecondsToTicks(delay));
            return new PickupView($"TARGET · {_target.Label}", true, _target);
        }

        // Keep the scheduled target and timer, but use its newest validated moving-label rectangle.
        _target = next;
        if (frame.Now < _nextActionStamp)
            return new PickupView($"TARGET · {_target.Label}", true, _target);

        var clickResult = _click(_target);
        if (clickResult == PickupClickResult.TargetUnavailable && mode == PickupMode.AutoNearby)
            return RecoverAutomaticMiss(frame.Now, settings);
        if (clickResult != PickupClickResult.Sent)
            return Block("Stopped: label click failed");

        _stage = Stage.WaitingForConfirmation;
        _confirmationTimeoutStamp = frame.Now +
            MillisecondsToTicks(Math.Clamp(settings.ConfirmationTimeoutMs, 500, 4000));
        return new PickupView($"PICKING · {_target.Label}", true, _target);
    }

    private PickupTarget ChooseTarget(IReadOnlyList<PickupTarget> targets, long now)
    {
        if (_stage == Stage.Delay && _target.Address != 0)
        {
            foreach (var candidate in targets)
                if (candidate.Address == _target.Address && CanRetry(candidate.Address, now))
                    return candidate;
        }

        foreach (var candidate in targets)
            if (CanRetry(candidate.Address, now))
                return candidate;
        return default;
    }

    private bool CanRetry(nint address, long now)
        => !_misses.TryGetValue(address, out var miss) || now >= miss.RetryAfter;

    private PickupView RecoverAutomaticMiss(long now, PickupHelperSettings settings)
    {
        var missed = _target;
        _misses.TryGetValue(missed.Address, out var prior);
        var count = prior.Count + 1;
        var maxMisses = Math.Clamp(settings.MaxMissesBeforeCooldown, 1, 8);
        var delayMs = count >= maxMisses
            ? Math.Clamp(settings.MissedItemCooldownMs, 500, 10_000)
            : Math.Clamp(settings.MissRetryDelayMs, 100, 2000);
        _misses[missed.Address] = new MissState(
            count >= maxMisses ? 0 : count,
            now + MillisecondsToTicks(delayMs));
        _stage = Stage.Idle;
        _target = default;
        _confirmationTimeoutStamp = 0;
        _nextActionStamp = now + MillisecondsToTicks(Math.Clamp(settings.ClickCooldownMs, 100, 1000));
        return new PickupView($"MISSED · reacquiring {missed.Label}", true, null);
    }

    private void PruneMisses(IReadOnlySet<nint> groundItems)
    {
        if (_misses.Count == 0) return;
        foreach (var address in _misses.Keys.Where(x => !groundItems.Contains(x)).ToArray())
            _misses.Remove(address);
    }

    private PickupView Block(string status)
    {
        _autoRunning = false;
        _blockedUntilRelease = true;
        _blockedStatus = status;
        _stage = Stage.Idle;
        return new PickupView(status, false, _target.Address != 0 ? _target : null);
    }

    private void ResetAction()
    {
        _stage = Stage.Idle;
        _target = default;
        _nextActionStamp = 0;
        _confirmationTimeoutStamp = 0;
        _blockedUntilRelease = false;
        _blockedStatus = "";
        _misses.Clear();
    }

    private static long MillisecondsToTicks(int milliseconds)
        => System.Diagnostics.Stopwatch.Frequency * milliseconds / 1000;
}
