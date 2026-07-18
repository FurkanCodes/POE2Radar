namespace POE2Radar.Overlay;

internal enum LootTrackerBarMode : byte
{
    None,
    Map,
    Compact,
}

internal static class LootTrackerDrawPolicy
{
    internal static LootTrackerBarMode BarMode(
        bool viewEnabled,
        bool settingsEnabled,
        bool onMap)
    {
        if (!viewEnabled || !settingsEnabled)
            return LootTrackerBarMode.None;
        return onMap ? LootTrackerBarMode.Map : LootTrackerBarMode.Compact;
    }
}
