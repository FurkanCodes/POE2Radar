using POE2Radar.Core.Game;
using POE2Radar.Overlay.Input;
using POE2Radar.Overlay.Native;
using POE2Radar.Overlay.StashUtility;
using NumVec2 = System.Numerics.Vector2;

namespace POE2Radar.Overlay;

public sealed partial class RadarApp
{
    private enum AlchemyInputStage : byte { Idle, TargetClickPending, WaitingForChange }

    private readonly record struct AlchemyAction(
        Poe2Live.StashValueSlot Target,
        Poe2Live.StashValueSlot Currency,
        string Name,
        string CurrencyToken);

    private WaystoneAlchemyHint[] _waystoneAlchemyHints = [];
    private string _waystoneAlchemyStatus = "Disabled";
    private bool _waystoneAlchemyRunning;
    private bool _waystoneAlchemyRunWasDown;
    private bool _waystoneAlchemyStopWasDown;
    private bool _waystoneAlchemyControllerStarted;
    private bool _waystoneAlchemyEmergencyLatched;
    private AlchemyInputStage _waystoneAlchemyStage;
    private AlchemyAction _waystoneAlchemyAction;
    private long _waystoneAlchemyNextStamp;
    private long _waystoneAlchemyTimeoutStamp;
    private long _waystoneAlchemyBeforeSignature;
    private readonly HashSet<nint> _waystoneAlchemyProcessed = [];
    private string? _waystoneAlchemyChoiceFailure;

    private void RefreshWaystoneAlchemy(LiveFrameState live, bool gameFocused)
    {
        var s = _settings.WaystoneAlchemy;
        if (!s.Enabled)
        {
            _waystoneAlchemyEmergencyLatched = false;
            StopWaystoneAlchemy("Disabled", clearHints: true);
            return;
        }

        var stopDown = s.EmergencyStopHotkey > 0 && HotkeyPoll.IsDown(s.EmergencyStopHotkey);
        if (stopDown && !_waystoneAlchemyStopWasDown)
        {
            _waystoneAlchemyEmergencyLatched = true;
            StopWaystoneAlchemy("EMERGENCY STOP", clearHints: false);
        }
        _waystoneAlchemyStopWasDown = stopDown;

        var runDown = s.RunHotkey > 0 && HotkeyPoll.IsDown(s.RunHotkey);
        if (runDown && !_waystoneAlchemyRunWasDown)
        {
            if (_waystoneAlchemyRunning)
                StopWaystoneAlchemy("Stopped by user", clearHints: false);
            else if (s.Mode == 1 && s.AutoModeAcknowledged)
            {
                _waystoneAlchemyEmergencyLatched = false;
                _waystoneAlchemyRunning = true;
                _waystoneAlchemyProcessed.Clear();
                _waystoneAlchemyControllerStarted = HotkeyCodes.IsGamepad(s.RunHotkey);
                _waystoneAlchemyStage = AlchemyInputStage.Idle;
                _waystoneAlchemyStatus = "AUTO running";
            }
        }
        _waystoneAlchemyRunWasDown = runDown;

        BuildWaystoneAlchemyHints(s);
        if (s.Mode == 0)
        {
            if (_waystoneAlchemyRunning) StopWaystoneAlchemy("MANUAL guidance", clearHints: false);
            _waystoneAlchemyStatus = _waystoneAlchemyHints.Length == 0
                ? "MANUAL · no eligible Waystones"
                : $"MANUAL · {_waystoneAlchemyHints.Length} next actions";
            return;
        }

        if (!s.AutoModeAcknowledged)
        {
            StopWaystoneAlchemy("AUTO requires safety acknowledgement", clearHints: false);
            return;
        }
        if (!_waystoneAlchemyRunning)
        {
            if (!_waystoneAlchemyEmergencyLatched)
                _waystoneAlchemyStatus = $"AUTO armed · press {HotkeyCodes.DisplayName(s.RunHotkey)}";
            return;
        }

        if (!gameFocused || !live.InGame || _gameHwnd == 0)
        {
            StopWaystoneAlchemy("Stopped: PoE2 lost focus", clearHints: false);
            return;
        }
        if (_waystoneAlchemyControllerStarted && !GamepadInput.IsConnected(_settings.GamepadUserIndex))
        {
            StopWaystoneAlchemy("Stopped: controller disconnected", clearHints: false);
            return;
        }
        if (_stashInventoryEntities.Count == 0 || !_stashValueSlots.Any(IsInventoryWaystone))
        {
            StopWaystoneAlchemy("Stopped: inventory UI closed or unreadable", clearHints: false);
            return;
        }
        if (s.Recipe == 2)
        {
            StopWaystoneAlchemy("Paranoia is guided-only until its controller panel is mapped", clearHints: false);
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        switch (_waystoneAlchemyStage)
        {
            case AlchemyInputStage.Idle:
                if (now < _waystoneAlchemyNextStamp) return;
                if (!TryChooseAlchemyAction(s, out _waystoneAlchemyAction))
                {
                    StopWaystoneAlchemy(_waystoneAlchemyChoiceFailure ?? "Complete · no remaining eligible actions", clearHints: false);
                    return;
                }
                if (!ClickAlchemySlot(_waystoneAlchemyAction.Currency, rightButton: true))
                {
                    StopWaystoneAlchemy("Stopped: currency click failed", clearHints: false);
                    return;
                }
                _waystoneAlchemyBeforeSignature = SlotSignature(_waystoneAlchemyAction.Target);
                _waystoneAlchemyNextStamp = now + MillisecondsToTicks(Math.Clamp(s.ActionDelayMs, 150, 1500));
                _waystoneAlchemyStage = AlchemyInputStage.TargetClickPending;
                _waystoneAlchemyStatus = $"AUTO · {_waystoneAlchemyAction.Name}";
                break;

            case AlchemyInputStage.TargetClickPending:
                if (now < _waystoneAlchemyNextStamp) return;
                if (!ClickAlchemySlot(_waystoneAlchemyAction.Target, rightButton: false))
                {
                    StopWaystoneAlchemy("Stopped: target click failed", clearHints: false);
                    return;
                }
                _waystoneAlchemyTimeoutStamp = now + MillisecondsToTicks(2500);
                _waystoneAlchemyStage = AlchemyInputStage.WaitingForChange;
                break;

            case AlchemyInputStage.WaitingForChange:
                var current = _stashValueSlots.FirstOrDefault(x => x.ItemEntity == _waystoneAlchemyAction.Target.ItemEntity);
                if (current.ItemEntity == 0 || SlotSignature(current) != _waystoneAlchemyBeforeSignature)
                {
                    if (s.Recipe == 1)
                        _waystoneAlchemyProcessed.Add(_waystoneAlchemyAction.Target.ItemEntity);
                    _waystoneAlchemyStage = AlchemyInputStage.Idle;
                    _waystoneAlchemyNextStamp = now + MillisecondsToTicks(Math.Clamp(s.ActionDelayMs, 150, 1500));
                    return;
                }
                if (now >= _waystoneAlchemyTimeoutStamp)
                    StopWaystoneAlchemy("Stopped: game state did not change", clearHints: false);
                break;
        }
    }

    private void BuildWaystoneAlchemyHints(Config.WaystoneAlchemySettings settings)
    {
        var hints = new List<WaystoneAlchemyHint>();
        foreach (var target in _stashValueSlots.Where(IsInventoryWaystone).OrderBy(x => x.Rect.Y).ThenBy(x => x.Rect.X))
        {
            if (settings.Recipe == 1 && _waystoneAlchemyProcessed.Contains(target.ItemEntity)) continue;
            var action = DetermineAlchemyAction(target, settings);
            if (action is null) continue;
            var hasCurrency = FindAlchemyCurrency(action.Value.CurrencyToken).ItemEntity != 0;
            hints.Add(new WaystoneAlchemyHint(
                new NumVec2(target.Rect.X, target.Rect.Y),
                new NumVec2(target.Rect.W, target.Rect.H),
                hasCurrency ? action.Value.Name : $"NEED {action.Value.Name}",
                hasCurrency ? 0xFF40E0FFu : 0xFF4040FFu,
                _waystoneAlchemyRunning && target.ItemEntity == _waystoneAlchemyAction.Target.ItemEntity));
        }
        _waystoneAlchemyHints = hints.Count == 0 ? [] : hints.ToArray();
    }

    private bool TryChooseAlchemyAction(Config.WaystoneAlchemySettings settings, out AlchemyAction action)
    {
        _waystoneAlchemyChoiceFailure = null;
        foreach (var target in _stashValueSlots.Where(IsInventoryWaystone).OrderBy(x => x.Rect.Y).ThenBy(x => x.Rect.X))
        {
            if (settings.Recipe == 1 && _waystoneAlchemyProcessed.Contains(target.ItemEntity)) continue;
            var next = DetermineAlchemyAction(target, settings);
            if (next is null) continue;
            var currency = FindAlchemyCurrency(next.Value.CurrencyToken);
            if (currency.ItemEntity == 0)
            {
                _waystoneAlchemyChoiceFailure = $"Stopped: missing {next.Value.Name}";
                break;
            }
            action = new AlchemyAction(target, currency, next.Value.Name, next.Value.CurrencyToken);
            return true;
        }
        action = default;
        return false;
    }

    internal static (string Name, string CurrencyToken)? DetermineAlchemyAction(
        Poe2Live.StashValueSlot target,
        Config.WaystoneAlchemySettings settings)
    {
        if (StashUtilityRules.ParseTier($"{target.BaseItemName}|{target.FullItemPath}") < Math.Clamp(settings.MinimumTier, 1, 16))
            return null;
        if (settings.Recipe == 1)
            return target.Rarity == Poe2Live.Rarity.Rare && !target.Corrupted
                ? ("CORRUPT", "CurrencyCorrupt")
                : null;
        if (settings.Recipe == 2)
            return target.Rarity == Poe2Live.Rarity.Rare
                ? ("PARANOIA ×3", "DistilledParanoia")
                : null;

        return target.Rarity switch
        {
            Poe2Live.Rarity.Normal => ("ALCHEMY", "CurrencyUpgradeToRare"),
            Poe2Live.Rarity.Magic when !target.Identified => ("IDENTIFY", "CurrencyIdentification"),
            Poe2Live.Rarity.Magic when settings.UseRegalOnMagic => ("REGAL", "CurrencyUpgradeMagicToRare"),
            Poe2Live.Rarity.Rare when !target.Identified => ("IDENTIFY", "CurrencyIdentification"),
            Poe2Live.Rarity.Rare when settings.ApplyExaltedToRare &&
                target.Mods.Count(m => m.Explicit) < Math.Clamp(settings.DesiredExplicitMods, 3, 6)
                => ("EXALTED", "CurrencyAddModToRare"),
            _ => null,
        };
    }

    private Poe2Live.StashValueSlot FindAlchemyCurrency(string token)
        => _stashValueSlots.FirstOrDefault(x =>
            _stashInventoryEntities.Contains(x.ItemEntity) &&
            x.StackCount > 0 &&
            (x.FullItemPath.Contains(token, StringComparison.OrdinalIgnoreCase) ||
             x.InternalName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
             (token == "DistilledParanoia" && x.BaseItemName.Contains("Distilled Paranoia", StringComparison.OrdinalIgnoreCase))));

    private bool IsInventoryWaystone(Poe2Live.StashValueSlot slot)
        => _stashInventoryEntities.Contains(slot.ItemEntity) &&
           (slot.FullItemPath.Contains("Waystone", StringComparison.OrdinalIgnoreCase) ||
            slot.FullItemPath.Contains("MapKey", StringComparison.OrdinalIgnoreCase) ||
            slot.BaseItemName.Contains("Waystone", StringComparison.OrdinalIgnoreCase));

    private bool ClickAlchemySlot(Poe2Live.StashValueSlot slot, bool rightButton)
    {
        if (!OverlayNative.IsGameFocused(_gameHwnd, _process.ProcessId)) return false;
        var point = new OverlayNative.POINT
        {
            X = (int)MathF.Round(slot.Rect.X + slot.Rect.W * 0.5f),
            Y = (int)MathF.Round(slot.Rect.Y + slot.Rect.H * 0.5f),
        };
        if (!OverlayNative.ClientToScreen(_gameHwnd, ref point)) return false;
        return SendInputNative.Click(point.X, point.Y, rightButton);
    }

    private void StopWaystoneAlchemy(string status, bool clearHints)
    {
        _waystoneAlchemyRunning = false;
        _waystoneAlchemyStage = AlchemyInputStage.Idle;
        _waystoneAlchemyAction = default;
        _waystoneAlchemyStatus = status;
        if (clearHints) _waystoneAlchemyHints = [];
    }

    private static long SlotSignature(Poe2Live.StashValueSlot slot)
    {
        var hash = new HashCode();
        hash.Add(slot.ItemEntity);
        hash.Add(slot.Rarity);
        hash.Add(slot.Identified);
        hash.Add(slot.Corrupted);
        hash.Add(slot.StackCount);
        foreach (var mod in slot.Mods) { hash.Add(mod.Id); hash.Add(mod.Value0); hash.Add(mod.Value1); }
        return hash.ToHashCode();
    }

    private static long MillisecondsToTicks(int milliseconds)
        => System.Diagnostics.Stopwatch.Frequency * milliseconds / 1000;
}
