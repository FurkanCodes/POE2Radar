using ImGuiNET;

namespace POE2Radar.Overlay;

internal enum LootTrackerBarMode : byte
{
    None,
    Map,
    Compact,
}

internal readonly record struct LootTrackerWindowPolicy(
    ImGuiCond PositionCondition,
    ImGuiWindowFlags Flags,
    bool ForceSize,
    bool ShowLootButton);

internal static class LootTrackerDrawPolicy
{
    internal static LootTrackerWindowPolicy CompactWindow { get; } = new(
        ImGuiCond.FirstUseEver,
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoFocusOnAppearing,
        ForceSize: false,
        ShowLootButton: true);

    internal static LootTrackerWindowPolicy BreakdownWindow { get; } = new(
        ImGuiCond.FirstUseEver,
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoResize |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.AlwaysAutoResize |
        ImGuiWindowFlags.NoFocusOnAppearing,
        ForceSize: false,
        ShowLootButton: false);

    internal static LootTrackerBarMode BarMode(
        bool viewEnabled,
        bool settingsEnabled,
        bool onMap,
        bool keepVisibleAfterRun,
        bool hasSession)
    {
        if (!viewEnabled || !settingsEnabled)
            return LootTrackerBarMode.None;
        return onMap || (keepVisibleAfterRun && hasSession)
            ? LootTrackerBarMode.Compact
            : LootTrackerBarMode.None;
    }

    internal static bool HasRecoveryControl(
        bool hidden,
        LootTrackerBarMode barMode,
        bool settingsOpen)
        => hidden && (barMode != LootTrackerBarMode.None || settingsOpen);
}
