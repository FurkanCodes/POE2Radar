using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Settings;

namespace POE2Radar.Overlay.Input;

/// <summary>Single registry for bindable overlay actions — keyboard and gamepad parity.</summary>
public sealed record InputAction(
    string Id,
    string Label,
    int DefaultBinding,
    Func<RadarSettings, int> GetBinding,
    Action<RadarSettings, int> SetBinding,
    string Hint);

public static class InputActionCatalog
{
    public static IReadOnlyList<InputAction> All { get; } = BuildAll();

    public static bool UsesGamepadBindings(RadarSettings settings)
    {
        foreach (var action in All)
        {
            if (HotkeyCodes.IsGamepad(action.GetBinding(settings)))
                return true;
        }
        return false;
    }

    public static InputAction? TryGet(string id)
    {
        foreach (var action in All)
            if (string.Equals(action.Id, id, StringComparison.OrdinalIgnoreCase))
                return action;
        return null;
    }

    private static IReadOnlyList<InputAction> BuildAll() =>
    [
        new("hideEntityHotkey", "Never show under cursor", 0x74,
            s => s.HideEntityHotkey, (s, v) => s.HideEntityHotkey = v, SettingHints.Hotkeys.HideEntity),
        new("trackEntityHotkey", "Inspect under cursor", 0x73,
            s => s.TrackEntityHotkey, (s, v) => s.TrackEntityHotkey = v, SettingHints.Hotkeys.TrackEntity),
        new("autoPathToggleHotkey", "Auto-path toggle", 0x72,
            s => s.AutoPathToggleHotkey, (s, v) => s.AutoPathToggleHotkey = v, SettingHints.Hotkeys.AutoPathToggle),
        new("addNearestPathHotkey", "Add nearest path", 0x75,
            s => s.AddNearestPathHotkey, (s, v) => s.AddNearestPathHotkey = v, SettingHints.Hotkeys.AddNearestPath),
        new("clearPathsHotkey", "Clear paths", 0x76,
            s => s.ClearPathsHotkey, (s, v) => s.ClearPathsHotkey = v, SettingHints.Hotkeys.ClearPaths),
        new("autoFlaskToggleHotkey", "Auto-flask toggle", 0x77,
            s => s.AutoFlaskToggleHotkey, (s, v) => s.AutoFlaskToggleHotkey = v, SettingHints.Hotkeys.AutoFlaskToggle),
        new("atlasPickHotkey", "Atlas tile pick", 0x79,
            s => s.AtlasPickHotkey, (s, v) => s.AtlasPickHotkey = v, SettingHints.Hotkeys.AtlasPick),
        new("toggleSettingsHotkey", "Overlay settings", 0x7A,
            s => s.ToggleSettingsHotkey, (s, v) => s.ToggleSettingsHotkey = v, SettingHints.Hotkeys.ToggleSettings),
        new("openDashboardHotkey", "Open dashboard", 0x7B,
            s => s.OpenDashboardHotkey, (s, v) => s.OpenDashboardHotkey = v, SettingHints.Hotkeys.OpenDashboard),
        new("quitHotkey", "Quit overlay", 0x78,
            s => s.QuitHotkey, (s, v) => s.QuitHotkey = v, SettingHints.Hotkeys.Quit),
    ];
}
