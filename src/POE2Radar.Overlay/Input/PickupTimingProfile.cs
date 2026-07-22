namespace POE2Radar.Overlay.Input;

/// <summary>
/// Bounded timing for the fast pickup path. Safety remains owned by <c>PickupEngine</c>.
/// </summary>
internal readonly record struct PickupTimingProfile(
    int ScanIntervalMs,
    int ReactionMinMs,
    int ReactionMaxMs,
    int CursorSettleMs,
    int MouseDownMs,
    int PostClickMs,
    int ClickCooldownMs)
{
    internal static readonly PickupTimingProfile HumanSpeed = new(
        ScanIntervalMs: 16,
        ReactionMinMs: 0,
        ReactionMaxMs: 0,
        CursorSettleMs: 12,
        MouseDownMs: 0,
        PostClickMs: 0,
        ClickCooldownMs: 50);

    internal int InputPathDelayMs => CursorSettleMs + MouseDownMs + PostClickMs;
    internal int DetectionToClickBudgetMs => ScanIntervalMs + ReactionMaxMs + InputPathDelayMs;
}
