using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Pickup;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class PickupEngineTests
{
    private static readonly PickupTarget Target = new(7, (nint)0x1234, "Exalted Orb", 12f, 10f, 20f, 80f, 24f);

    [Fact]
    public void ActivationBindingAlsoUsedForShowHidden_DoesNotCancelPickup()
    {
        var settings = Settings(mode: PickupMode.NearbyHold);
        settings.ActivationHotkey = 0x12;
        settings.ShowHiddenItemsHotkey = 0x12;

        var paused = RadarApp.ShouldPausePickupForShowHidden(settings, showHiddenDown: true);

        Assert.False(paused);
    }

    [Fact]
    public void NearbyModeRequiresAcknowledgement()
    {
        var clicks = 0;
        var engine = new PickupEngine(_ => { clicks++; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.NearbyHold);
        settings.AutoModeAcknowledged = false;

        var view = engine.Tick(Frame(settings, targets: [Target]));

        Assert.Equal(0, clicks);
        Assert.Contains("acknowledgement", view.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClicksOnceThenWaitsForGroundEntityToDisappear()
    {
        var clicks = 0;
        var engine = new PickupEngine(_ => { clicks++; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.HoverHold);

        _ = engine.Tick(Frame(settings, targets: [Target], ground: Set(Target.Address)));
        var clicking = engine.Tick(Frame(settings, targets: [Target], ground: Set(Target.Address)));
        var waiting = engine.Tick(Frame(settings, targets: [Target], ground: Set(Target.Address)));
        var confirmed = engine.Tick(Frame(settings, targets: [], ground: Set()));

        Assert.Equal(1, clicks);
        Assert.Contains("PICKING", clicking.Status);
        Assert.Contains("PICKING", waiting.Status);
        Assert.Contains("PICKED", confirmed.Status);
    }

    [Fact]
    public void ConfirmationTimeoutBlocksUntilActivationIsReleased()
    {
        var clicks = 0;
        var engine = new PickupEngine(_ => { clicks++; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.HoverHold);
        settings.ConfirmationTimeoutMs = 500;

        _ = engine.Tick(Frame(settings, now: 1, targets: [Target], ground: Set(Target.Address)));
        _ = engine.Tick(Frame(settings, now: 1, targets: [Target], ground: Set(Target.Address)));
        var timedOut = engine.Tick(Frame(
            settings,
            now: 1 + System.Diagnostics.Stopwatch.Frequency,
            targets: [Target],
            ground: Set(Target.Address)));
        var stillBlocked = engine.Tick(Frame(
            settings,
            now: 1 + System.Diagnostics.Stopwatch.Frequency * 2,
            targets: [Target],
            ground: Set(Target.Address)));
        _ = engine.Tick(Frame(settings, activation: false, targets: [Target], ground: Set(Target.Address)));
        _ = engine.Tick(Frame(settings, targets: [Target], ground: Set(Target.Address)));
        _ = engine.Tick(Frame(settings, targets: [Target], ground: Set(Target.Address)));

        Assert.Contains("not confirmed", timedOut.Status);
        Assert.Contains("not confirmed", stillBlocked.Status);
        Assert.Equal(2, clicks);
    }

    [Fact]
    public void ControllerDisconnectAndEmergencyStopPreventClicks()
    {
        var clicks = 0;
        var engine = new PickupEngine(_ => { clicks++; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.HoverHold);

        var disconnected = engine.Tick(Frame(
            settings,
            controllerTriggered: true,
            controllerConnected: false,
            targets: [Target]));
        _ = engine.Tick(Frame(settings, activation: false));
        var emergency = engine.Tick(Frame(settings, emergency: true, targets: [Target]));

        Assert.Equal(0, clicks);
        Assert.Contains("disconnected", disconnected.Status);
        Assert.Contains("EMERGENCY", emergency.Status);
    }

    [Fact]
    public void ShowHiddenItemsBindingStopsPickup()
    {
        var clicks = 0;
        var engine = new PickupEngine(_ => { clicks++; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.HoverHold);

        var view = engine.Tick(Frame(settings, filterOverride: true, targets: [Target]));

        Assert.Equal(0, clicks);
        Assert.Contains("show-hidden-items", view.Status);
    }

    [Fact]
    public void AutomaticModeStartsOnOnePressAndKeepsRunningAfterRelease()
    {
        var clicks = 0;
        var engine = new PickupEngine(_ => { clicks++; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.AutoNearby);

        var armed = engine.Tick(Frame(settings, activation: false, targets: [Target], ground: Set(Target.Address)));
        _ = engine.Tick(Frame(settings, activation: true, targets: [Target], ground: Set(Target.Address)));
        var clicking = engine.Tick(Frame(settings, activation: false, targets: [Target], ground: Set(Target.Address)));
        var confirmed = engine.Tick(Frame(settings, activation: false, targets: [], ground: Set()));

        Assert.Contains("AUTO armed", armed.Status);
        Assert.Equal(1, clicks);
        Assert.True(clicking.Running);
        Assert.Contains("PICKING", clicking.Status);
        Assert.Contains("PICKED", confirmed.Status);
    }

    [Fact]
    public void AutomaticModeSecondPressStopsWithoutRequiringHold()
    {
        var clicks = 0;
        var engine = new PickupEngine(_ => { clicks++; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.AutoNearby);
        settings.MinPickupDelayMs = 500;
        settings.MaxPickupDelayMs = 500;

        _ = engine.Tick(Frame(settings, activation: false, targets: [Target], ground: Set(Target.Address)));
        _ = engine.Tick(Frame(settings, activation: true, targets: [Target], ground: Set(Target.Address)));
        _ = engine.Tick(Frame(settings, activation: false, targets: [Target], ground: Set(Target.Address)));
        var stopped = engine.Tick(Frame(settings, activation: true, targets: [Target], ground: Set(Target.Address)));

        Assert.Equal(0, clicks);
        Assert.False(stopped.Running);
        Assert.Contains("AUTO armed", stopped.Status);
    }

    [Fact]
    public void AutomaticModeStopsWhenStartingControllerDisconnects()
    {
        var engine = new PickupEngine(_ => PickupClickResult.Sent);
        var settings = Settings(mode: PickupMode.AutoNearby);

        _ = engine.Tick(Frame(settings, activation: false));
        _ = engine.Tick(Frame(
            settings,
            activation: true,
            controllerTriggered: true,
            controllerConnected: true,
            targets: [Target],
            ground: Set(Target.Address)));
        var stopped = engine.Tick(Frame(
            settings,
            activation: false,
            controllerTriggered: true,
            controllerConnected: false,
            targets: [Target],
            ground: Set(Target.Address)));

        Assert.False(stopped.Running);
        Assert.Contains("controller disconnected", stopped.Status);
    }

    [Fact]
    public void AutomaticModeTreatsOneUnconfirmedMovingClickAsRecoverableMiss()
    {
        var clicks = 0;
        var engine = new PickupEngine(_ => { clicks++; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.AutoNearby);
        settings.ConfirmationTimeoutMs = 500;

        _ = engine.Tick(Frame(settings, activation: false));
        _ = engine.Tick(Frame(
            settings,
            activation: true,
            now: 1,
            targets: [Target],
            ground: Set(Target.Address)));
        _ = engine.Tick(Frame(
            settings,
            activation: false,
            now: 1,
            targets: [Target],
            ground: Set(Target.Address)));
        var recovered = engine.Tick(Frame(
            settings,
            activation: false,
            now: 1 + System.Diagnostics.Stopwatch.Frequency,
            targets: [Target],
            ground: Set(Target.Address)));

        Assert.Equal(1, clicks);
        Assert.True(recovered.Running);
        Assert.Contains("MISSED", recovered.Status);
        Assert.DoesNotContain("Stopped", recovered.Status);
    }

    [Fact]
    public void PendingPickupUsesReacquiredMovingLabelRectangle()
    {
        PickupTarget clicked = default;
        var engine = new PickupEngine(target => { clicked = target; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.HoverHold);
        var moved = Target with { X = 440f, Y = 260f };

        _ = engine.Tick(Frame(settings, targets: [Target], ground: Set(Target.Address)));
        _ = engine.Tick(Frame(settings, targets: [moved], ground: Set(Target.Address)));

        Assert.Equal(moved.X, clicked.X);
        Assert.Equal(moved.Y, clicked.Y);
    }

    [Fact]
    public void AutomaticMissBackoffPreventsImmediateSameItemClickSpam()
    {
        var clicks = 0;
        var engine = new PickupEngine(_ => { clicks++; return PickupClickResult.Sent; });
        var settings = Settings(mode: PickupMode.AutoNearby);
        settings.ConfirmationTimeoutMs = 500;
        settings.MissRetryDelayMs = 300;

        _ = engine.Tick(Frame(settings, activation: false));
        _ = engine.Tick(Frame(settings, activation: true, now: 1, targets: [Target], ground: Set(Target.Address)));
        _ = engine.Tick(Frame(settings, activation: false, now: 1, targets: [Target], ground: Set(Target.Address)));
        var missAt = 1 + System.Diagnostics.Stopwatch.Frequency;
        _ = engine.Tick(Frame(settings, activation: false, now: missAt, targets: [Target], ground: Set(Target.Address)));
        var backingOff = engine.Tick(Frame(
            settings,
            activation: false,
            now: missAt + System.Diagnostics.Stopwatch.Frequency / 10,
            targets: [Target],
            ground: Set(Target.Address)));

        Assert.Equal(1, clicks);
        Assert.True(backingOff.Running);
        Assert.Contains("reacquiring", backingOff.Status);
    }

    [Fact]
    public void AutomaticModeRecoversWhenLabelElementMovedBeforeClick()
    {
        var engine = new PickupEngine(_ => PickupClickResult.TargetUnavailable);
        var settings = Settings(mode: PickupMode.AutoNearby);

        _ = engine.Tick(Frame(settings, activation: false));
        _ = engine.Tick(Frame(settings, activation: true, targets: [Target], ground: Set(Target.Address)));
        var recovered = engine.Tick(Frame(settings, activation: false, targets: [Target], ground: Set(Target.Address)));

        Assert.True(recovered.Running);
        Assert.Contains("MISSED", recovered.Status);
    }

    [Fact]
    public void AutomaticModeStillStopsOnRealInputFailure()
    {
        var engine = new PickupEngine(_ => PickupClickResult.InputFailed);
        var settings = Settings(mode: PickupMode.AutoNearby);

        _ = engine.Tick(Frame(settings, activation: false));
        _ = engine.Tick(Frame(settings, activation: true, targets: [Target], ground: Set(Target.Address)));
        var stopped = engine.Tick(Frame(settings, activation: false, targets: [Target], ground: Set(Target.Address)));

        Assert.False(stopped.Running);
        Assert.Contains("click failed", stopped.Status);
    }

    private static PickupHelperSettings Settings(PickupMode mode)
        => new()
        {
            Enabled = true,
            Mode = (int)mode,
            AutoModeAcknowledged = true,
            MinPickupDelayMs = 0,
            MaxPickupDelayMs = 0,
            ClickCooldownMs = 100,
            ConfirmationTimeoutMs = 500,
        };

    private static PickupFrame Frame(
        PickupHelperSettings settings,
        bool activation = true,
        bool emergency = false,
        bool filterOverride = false,
        bool controllerTriggered = false,
        bool controllerConnected = true,
        long now = 1,
        IReadOnlyList<PickupTarget>? targets = null,
        IReadOnlySet<nint>? ground = null)
        => new(
            settings,
            activation,
            emergency,
            GameFocused: true,
            InGame: true,
            UiClear: true,
            FilterOverrideHeld: filterOverride,
            ControllerTriggered: controllerTriggered,
            ControllerConnected: controllerConnected,
            Now: now,
            Targets: targets ?? [],
            GroundItems: ground ?? new HashSet<nint>());

    private static IReadOnlySet<nint> Set(params nint[] addresses)
        => addresses.ToHashSet();

    private static long MillisecondsToTicks(int milliseconds)
        => System.Diagnostics.Stopwatch.Frequency * milliseconds / 1000;
}
