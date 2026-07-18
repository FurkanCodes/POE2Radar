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

    /// <summary>Test-facing next-action choice before currency slot resolution.</summary>
    internal readonly record struct AlchemyChoice(
        nint TargetEntity,
        string Name,
        string CurrencyToken);

    private const int WaystoneAlchemyFocusGraceMs = 2000;

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
    private long _waystoneAlchemyFocusGraceUntil;
    private readonly HashSet<nint> _waystoneAlchemyProcessed = [];
    private readonly HashSet<nint> _waystoneAlchemyFailed = [];
    private string? _waystoneAlchemyChoiceFailure;

    internal void StartWaystoneAlchemyFromUi()
    {
        var s = _settings.WaystoneAlchemy;
        if (!s.Enabled)
        {
            _waystoneAlchemyStatus = "Enable Crafting Assistant first";
            return;
        }
        if (s.Mode != 1)
        {
            _waystoneAlchemyStatus = "Switch to AUTO to start crafting";
            return;
        }
        if (!s.AutoModeAcknowledged)
        {
            _waystoneAlchemyStatus = "AUTO requires safety acknowledgement";
            return;
        }
        if (s.TargetType == 0 && s.Recipe == 2)
        {
            _waystoneAlchemyStatus = "Paranoia is guided-only until its controller panel is mapped";
            return;
        }

        BeginWaystoneAlchemyRun(controllerStarted: false);
        if (_gameHwnd != 0)
            OverlayNative.SetForegroundWindow(_gameHwnd);
    }

    internal void StopWaystoneAlchemyFromUi()
        => StopWaystoneAlchemy("Stopped by user", clearHints: false);

    private void BeginWaystoneAlchemyRun(bool controllerStarted)
    {
        _waystoneAlchemyEmergencyLatched = false;
        _waystoneAlchemyRunning = true;
        _waystoneAlchemyProcessed.Clear();
        _waystoneAlchemyFailed.Clear();
        _waystoneAlchemyControllerStarted = controllerStarted;
        _waystoneAlchemyStage = AlchemyInputStage.Idle;
        _waystoneAlchemyAction = default;
        _waystoneAlchemyFocusGraceUntil =
            System.Diagnostics.Stopwatch.GetTimestamp() + MillisecondsToTicks(WaystoneAlchemyFocusGraceMs);
        _waystoneAlchemyNextStamp = 0;
        _waystoneAlchemyStatus = "AUTO starting…";
    }

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
                BeginWaystoneAlchemyRun(controllerStarted: HotkeyCodes.IsGamepad(s.RunHotkey));
        }
        _waystoneAlchemyRunWasDown = runDown;

        BuildWaystoneAlchemyHints(s);
        if (s.Mode == 0)
        {
            if (_waystoneAlchemyRunning) StopWaystoneAlchemy("MANUAL guidance", clearHints: false);
            var targetLabel = s.TargetType == 1 ? "Tablets" : "Waystones";
            _waystoneAlchemyStatus = _waystoneAlchemyHints.Length == 0
                ? $"MANUAL · no eligible {targetLabel}"
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
            {
                var startHint = s.RunHotkey > 0
                    ? $"or press {HotkeyCodes.DisplayName(s.RunHotkey)}"
                    : "from Crafting Assistant";
                _waystoneAlchemyStatus = $"AUTO armed · Start {startHint}";
            }
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!gameFocused || !live.InGame || _gameHwnd == 0)
        {
            if (now < _waystoneAlchemyFocusGraceUntil)
            {
                _waystoneAlchemyStatus = "AUTO · waiting for PoE2 focus…";
                return;
            }
            StopWaystoneAlchemy("Stopped: PoE2 lost focus", clearHints: false);
            return;
        }
        if (_waystoneAlchemyControllerStarted && !GamepadInput.IsConnected(_settings.GamepadUserIndex))
        {
            StopWaystoneAlchemy("Stopped: controller disconnected", clearHints: false);
            return;
        }
        if (_stashInventoryEntities.Count == 0 ||
            !_stashValueSlots.Any(slot => IsInventoryAlchemyTarget(slot, s)))
        {
            StopWaystoneAlchemy("Stopped: inventory UI closed or unreadable", clearHints: false);
            return;
        }
        if (s.TargetType == 0 && s.Recipe == 2)
        {
            StopWaystoneAlchemy("Paranoia is guided-only until its controller panel is mapped", clearHints: false);
            return;
        }

        switch (_waystoneAlchemyStage)
        {
            case AlchemyInputStage.Idle:
                if (now < _waystoneAlchemyNextStamp) return;
                if (!TryChooseAlchemyAction(s, out _waystoneAlchemyAction))
                {
                    StopWaystoneAlchemy(_waystoneAlchemyChoiceFailure ?? "Complete · no remaining eligible actions", clearHints: false);
                    return;
                }
                // Re-resolve currency from the latest scan so we don't click a stale/wrong neighbor cell
                // (Alchemy beside Chance was producing "Item cannot increase in rarity").
                var freshCurrency = FindAlchemyCurrency(_waystoneAlchemyAction.CurrencyToken);
                if (freshCurrency.ItemEntity == 0)
                {
                    StopWaystoneAlchemy($"Stopped: missing {_waystoneAlchemyAction.Name}", clearHints: false);
                    return;
                }
                _waystoneAlchemyAction = _waystoneAlchemyAction with { Currency = freshCurrency };
                if (!ClickAlchemySlot(freshCurrency, rightButton: true))
                {
                    StopWaystoneAlchemy("Stopped: currency click failed", clearHints: false);
                    return;
                }
                // Give the inventory scan a beat to refresh target rects after picking up currency.
                var delayMs = Math.Max(250, Math.Clamp(s.ActionDelayMs, 150, 1500));
                _waystoneAlchemyNextStamp = now + MillisecondsToTicks(delayMs);
                _waystoneAlchemyTimeoutStamp = now + MillisecondsToTicks(delayMs + 2000);
                _waystoneAlchemyStage = AlchemyInputStage.TargetClickPending;
                _waystoneAlchemyStatus = $"AUTO · {_waystoneAlchemyAction.Name}";
                _stashUtilityForceScan = true;
                break;

            case AlchemyInputStage.TargetClickPending:
                if (now < _waystoneAlchemyNextStamp) return;
                // Re-resolve target from the latest inventory scan — stale rects after picking up
                // currency are why Transmute/Alchemy appear selected but never apply.
                if (!TryResolveAlchemyTarget(_waystoneAlchemyAction.Target.ItemEntity, out var freshTarget))
                {
                    if (now >= _waystoneAlchemyTimeoutStamp)
                    {
                        ClearAlchemyCursor();
                        _waystoneAlchemyFailed.Add(_waystoneAlchemyAction.Target.ItemEntity);
                        _waystoneAlchemyStage = AlchemyInputStage.Idle;
                        _waystoneAlchemyStatus = "AUTO · target slot lost — skipped";
                    }
                    return;
                }
                _waystoneAlchemyAction = _waystoneAlchemyAction with { Target = freshTarget };
                _waystoneAlchemyBeforeSignature = SlotSignature(freshTarget);
                if (!ClickAlchemySlot(freshTarget, rightButton: false))
                {
                    ClearAlchemyCursor();
                    StopWaystoneAlchemy("Stopped: target click failed", clearHints: false);
                    return;
                }
                _waystoneAlchemyTimeoutStamp = now + MillisecondsToTicks(2500);
                _waystoneAlchemyStage = AlchemyInputStage.WaitingForChange;
                _waystoneAlchemyStatus = $"AUTO · apply {_waystoneAlchemyAction.Name}";
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
                {
                    // Missed click or game rejected currency — clear orb-on-cursor and continue.
                    ClearAlchemyCursor();
                    _waystoneAlchemyFailed.Add(_waystoneAlchemyAction.Target.ItemEntity);
                    _waystoneAlchemyStage = AlchemyInputStage.Idle;
                    _waystoneAlchemyNextStamp = now + MillisecondsToTicks(Math.Clamp(s.ActionDelayMs, 150, 1500));
                    _waystoneAlchemyStatus = $"AUTO · skipped rejected ({_waystoneAlchemyAction.Name})";
                }
                break;
        }
    }

    private void BuildWaystoneAlchemyHints(Config.WaystoneAlchemySettings settings)
    {
        var hints = new List<WaystoneAlchemyHint>();
        foreach (var target in _stashValueSlots
                     .Where(slot => IsInventoryAlchemyTarget(slot, settings))
                     .OrderBy(x => x.Rect.Y)
                     .ThenBy(x => x.Rect.X))
        {
            if (_waystoneAlchemyFailed.Contains(target.ItemEntity)) continue;
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
        var available = CollectAvailableAlchemyCurrencyTokens();
        if (!TrySelectNextAlchemyChoice(
                _stashValueSlots,
                settings,
                _waystoneAlchemyProcessed,
                _waystoneAlchemyFailed,
                available,
                IsInventoryAlchemyTarget,
                out var choice,
                out _waystoneAlchemyChoiceFailure))
        {
            action = default;
            return false;
        }

        var currency = FindAlchemyCurrency(choice.CurrencyToken);
        var target = _stashValueSlots.First(x => x.ItemEntity == choice.TargetEntity);
        action = new AlchemyAction(target, currency, choice.Name, choice.CurrencyToken);
        return true;
    }

    private HashSet<string> CollectAvailableAlchemyCurrencyTokens()
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in _stashValueSlots)
        {
            if (!_stashInventoryEntities.Contains(slot.ItemEntity) || slot.StackCount <= 0)
                continue;
            if (MatchesAlchemyCurrencyToken(slot, "CurrencyUpgradeToRare")) tokens.Add("CurrencyUpgradeToRare");
            if (MatchesAlchemyCurrencyToken(slot, "CurrencyUpgradeToMagic")) tokens.Add("CurrencyUpgradeToMagic");
            if (MatchesAlchemyCurrencyToken(slot, "CurrencyAddModToMagic")) tokens.Add("CurrencyAddModToMagic");
            if (MatchesAlchemyCurrencyToken(slot, "CurrencyUpgradeMagicToRare")) tokens.Add("CurrencyUpgradeMagicToRare");
            if (MatchesAlchemyCurrencyToken(slot, "CurrencyAddModToRare")) tokens.Add("CurrencyAddModToRare");
            if (MatchesAlchemyCurrencyToken(slot, "CurrencyIdentification")) tokens.Add("CurrencyIdentification");
            if (MatchesAlchemyCurrencyToken(slot, "CurrencyCorrupt")) tokens.Add("CurrencyCorrupt");
            if (MatchesAlchemyCurrencyToken(slot, "DistilledParanoia")) tokens.Add("DistilledParanoia");
            if (MatchesAlchemyCurrencyToken(slot, "CurrencyIncursionCorruptTablet"))
                tokens.Add("CurrencyIncursionCorruptTablet");
        }
        return tokens;
    }

    /// <summary>
    /// Picks the next craftable inventory target, skipping finished/failed items and actions
    /// whose currency is not currently available.
    /// </summary>
    internal static bool TrySelectNextAlchemyChoice(
        IReadOnlyList<Poe2Live.StashValueSlot> slots,
        Config.WaystoneAlchemySettings settings,
        IReadOnlySet<nint> processed,
        IReadOnlySet<nint> failed,
        IReadOnlySet<string> availableCurrencyTokens,
        Func<Poe2Live.StashValueSlot, Config.WaystoneAlchemySettings, bool> isTarget,
        out AlchemyChoice choice,
        out string? failureReason)
    {
        failureReason = null;
        string? missingCurrency = null;
        var eligibleCount = 0;

        foreach (var target in slots
                     .Where(slot => isTarget(slot, settings))
                     .OrderBy(x => x.Rect.Y)
                     .ThenBy(x => x.Rect.X))
        {
            if (failed.Contains(target.ItemEntity)) continue;
            if (settings.Recipe == 1 && processed.Contains(target.ItemEntity)) continue;

            var next = DetermineAlchemyAction(target, settings);
            if (next is null) continue;
            eligibleCount++;

            if (!availableCurrencyTokens.Contains(next.Value.CurrencyToken))
            {
                missingCurrency ??= next.Value.Name;
                continue;
            }

            choice = new AlchemyChoice(target.ItemEntity, next.Value.Name, next.Value.CurrencyToken);
            return true;
        }

        choice = default;
        if (missingCurrency is not null)
            failureReason = $"Stopped: missing {missingCurrency}";
        else if (eligibleCount == 0)
            failureReason = "Complete · no remaining eligible actions";
        return false;
    }

    internal static (string Name, string CurrencyToken)? DetermineAlchemyAction(
        Poe2Live.StashValueSlot target,
        Config.WaystoneAlchemySettings settings)
    {
        if (settings.TargetType == 1)
            return DetermineTabletAlchemyAction(target, settings);

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

        // PoE2 0.3.1+: Alchemy works on Normal and Magic (rerolls blues to 4 mods). Regal is opt-in
        // via UseRegalOnMagic when you want to keep existing Magic affixes.
        return target.Rarity switch
        {
            Poe2Live.Rarity.Normal => ("ALCHEMY", "CurrencyUpgradeToRare"),
            Poe2Live.Rarity.Magic when !target.Identified => ("IDENTIFY", "CurrencyIdentification"),
            Poe2Live.Rarity.Magic when settings.UseRegalOnMagic => ("REGAL", "CurrencyUpgradeMagicToRare"),
            Poe2Live.Rarity.Magic => ("ALCHEMY", "CurrencyUpgradeToRare"),
            Poe2Live.Rarity.Rare when !target.Identified => ("IDENTIFY", "CurrencyIdentification"),
            Poe2Live.Rarity.Rare when settings.ApplyExaltedToRare &&
                target.Mods.Count(m => m.Explicit) < Math.Clamp(settings.DesiredExplicitMods, 3, 6)
                => ("EXALTED", "CurrencyAddModToRare"),
            _ => null,
        };
    }

    private static (string Name, string CurrencyToken)? DetermineTabletAlchemyAction(
        Poe2Live.StashValueSlot target,
        Config.WaystoneAlchemySettings settings)
    {
        if (!IsTablet(target) ||
            target.Rarity == Poe2Live.Rarity.Unique ||
            target.Corrupted)
        {
            return null;
        }

        var explicitMods = target.Mods.Count(mod => mod.Explicit);
        var desiredMods = Math.Clamp(settings.DesiredTabletExplicitMods, 2, 4);

        if (settings.Recipe == 1)
        {
            return explicitMods >= desiredMods
                ? ("ANCIENT INFUSER", "CurrencyIncursionCorruptTablet")
                : null;
        }

        if (settings.Recipe == 2)
        {
            // Same as waystones: Alchemy on Normal/Magic → Rare with 4 random mods (blues are rerolled).
            // If Partial Translation is missing the game rejects the click; we skip that tablet and continue.
            return target.Rarity is Poe2Live.Rarity.Normal or Poe2Live.Rarity.Magic
                ? ("ALCHEMY", "CurrencyUpgradeToRare")
                : null;
        }

        if (settings.Recipe != 0)
            return null;

        // Magic items cap at 2 explicits. Never Augment at 2+ — that is the "cannot be increased
        // any further" failure. Waystones avoid this by Regaling magic directly; tablets need
        // Augment only while still short of 2.
        const int magicExplicitCap = 2;
        return target.Rarity switch
        {
            Poe2Live.Rarity.Normal => ("TRANSMUTE", "CurrencyUpgradeToMagic"),
            Poe2Live.Rarity.Magic when !target.Identified => ("IDENTIFY", "CurrencyIdentification"),
            Poe2Live.Rarity.Magic when explicitMods < magicExplicitCap
                => ("AUGMENT", "CurrencyAddModToMagic"),
            Poe2Live.Rarity.Magic when desiredMods >= 3
                => ("REGAL", "CurrencyUpgradeMagicToRare"),
            Poe2Live.Rarity.Rare when !target.Identified => ("IDENTIFY", "CurrencyIdentification"),
            Poe2Live.Rarity.Rare when explicitMods < desiredMods
                => ("EXALTED", "CurrencyAddModToRare"),
            _ => null,
        };
    }

    private Poe2Live.StashValueSlot FindAlchemyCurrency(string token)
    {
        Poe2Live.StashValueSlot best = default;
        foreach (var slot in _stashValueSlots)
        {
            if (!_stashInventoryEntities.Contains(slot.ItemEntity) || slot.StackCount <= 0)
                continue;
            if (!MatchesAlchemyCurrencyToken(slot, token)) continue;
            // Prefer a slot with a usable on-screen rect; keep scanning for a better hit.
            if (best.ItemEntity == 0 || (slot.Rect.W > 8f && slot.Rect.H > 8f && best.Rect.W <= 8f))
                best = slot;
        }
        return best;
    }

    /// <summary>
    /// Exact currency id / display-name match. Loose <c>Contains(token)</c> is unsafe:
    /// e.g. Binding Orb (<c>CurrencyUpgradeToRareAndSetSockets</c>) and mis-clicks onto
    /// Orb of Chance produce "Item cannot increase in rarity" on waystones.
    /// </summary>
    internal static bool MatchesAlchemyCurrencyToken(Poe2Live.StashValueSlot slot, string token)
    {
        var name = slot.BaseItemName ?? "";
        if (name.Contains("Orb of Chance", StringComparison.OrdinalIgnoreCase) ||
            PathIdEquals(slot, "CurrencyUpgradeRandomly"))
        {
            return false;
        }

        if (PathIdEquals(slot, token))
            return true;

        return token switch
        {
            "CurrencyUpgradeToRare" =>
                NameIs(name, "Orb of Alchemy"),
            "CurrencyUpgradeToMagic" =>
                NameIs(name, "Orb of Transmutation"),
            "CurrencyAddModToMagic" =>
                NameIs(name, "Orb of Augmentation"),
            "CurrencyUpgradeMagicToRare" =>
                NameIs(name, "Regal Orb"),
            "CurrencyAddModToRare" =>
                NameIs(name, "Exalted Orb"),
            "CurrencyIdentification" =>
                NameIs(name, "Scroll of Wisdom"),
            "CurrencyCorrupt" =>
                NameIs(name, "Vaal Orb"),
            "DistilledParanoia" =>
                NameIs(name, "Distilled Paranoia"),
            "CurrencyIncursionCorruptTablet" =>
                NameIs(name, "Ancient Infuser"),
            _ => false,
        };

        static bool NameIs(string displayName, string expected)
            => displayName.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
               displayName.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathIdEquals(Poe2Live.StashValueSlot slot, string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var basename = slot.InternalName ?? "";
        if (basename.Equals(token, StringComparison.OrdinalIgnoreCase))
            return true;

        // Allow numbered variants (CurrencyUpgradeToRare2) but not longer prefixes
        // like CurrencyUpgradeToRareAndSetSockets.
        if (basename.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            var rest = basename[token.Length..];
            if (rest.Length == 0) return true;
            if (rest.Length <= 2 && rest.All(char.IsDigit)) return true;
        }

        var path = slot.FullItemPath ?? "";
        return path.EndsWith("/" + token, StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("\\" + token, StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("/" + token + ".dds", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInventoryWaystone(Poe2Live.StashValueSlot slot)
        => _stashInventoryEntities.Contains(slot.ItemEntity) &&
           (slot.FullItemPath.Contains("Waystone", StringComparison.OrdinalIgnoreCase) ||
            slot.FullItemPath.Contains("MapKey", StringComparison.OrdinalIgnoreCase) ||
            slot.BaseItemName.Contains("Waystone", StringComparison.OrdinalIgnoreCase));

    private bool IsInventoryTablet(Poe2Live.StashValueSlot slot)
        => _stashInventoryEntities.Contains(slot.ItemEntity) && IsTablet(slot);

    private bool IsInventoryAlchemyTarget(
        Poe2Live.StashValueSlot slot,
        Config.WaystoneAlchemySettings settings)
        => settings.TargetType == 1
            ? IsInventoryTablet(slot)
            : IsInventoryWaystone(slot);

    private static bool IsTablet(Poe2Live.StashValueSlot slot)
    {
        var identity = $"{slot.BaseItemName}|{slot.InternalName}|{slot.FullItemPath}";
        return identity.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ||
               identity.Contains("TowerAugment", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveAlchemyTarget(nint itemEntity, out Poe2Live.StashValueSlot target)
    {
        target = _stashValueSlots.FirstOrDefault(x => x.ItemEntity == itemEntity);
        return target.ItemEntity != 0 && target.Rect.W >= 8f && target.Rect.H >= 8f;
    }

    private bool ClickAlchemySlot(Poe2Live.StashValueSlot slot, bool rightButton)
    {
        if (!OverlayNative.IsGameFocused(_gameHwnd, _process.ProcessId)) return false;
        if (slot.Rect.W < 8f || slot.Rect.H < 8f) return false;
        var point = new OverlayNative.POINT
        {
            X = (int)MathF.Round(slot.Rect.X + slot.Rect.W * 0.5f),
            Y = (int)MathF.Round(slot.Rect.Y + slot.Rect.H * 0.5f),
        };
        if (!OverlayNative.ClientToScreen(_gameHwnd, ref point)) return false;
        // Settle so PoE2 registers the move before the button — without this, currency is picked
        // up but the follow-up apply click often misses (Upgrade Transmute path).
        return SendInputNative.Click(point.X, point.Y, rightButton, settleMs: 80);
    }

    private void ClearAlchemyCursor()
    {
        // Escape drops an active currency from the cursor so the next action can start clean.
        if (!OverlayNative.IsGameFocused(_gameHwnd, _process.ProcessId)) return;
        SendInputNative.Tap(0x1B); // VK_ESCAPE
    }

    private void StopWaystoneAlchemy(string status, bool clearHints)
    {
        _waystoneAlchemyRunning = false;
        _waystoneAlchemyStage = AlchemyInputStage.Idle;
        _waystoneAlchemyAction = default;
        _waystoneAlchemyFocusGraceUntil = 0;
        _waystoneAlchemyStatus = status;
        if (clearHints)
        {
            _waystoneAlchemyHints = [];
            _waystoneAlchemyProcessed.Clear();
            _waystoneAlchemyFailed.Clear();
        }
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
