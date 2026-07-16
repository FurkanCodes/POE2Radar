using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Native;

namespace POE2Radar.Overlay.Input;

/// <summary>Unified keyboard + gamepad binding poll (Win32 VK or encoded gamepad mask).</summary>
public static class HotkeyPoll
{
    public static void BeginTick(RadarSettings settings)
    {
        var pollPad = settings.GamepadHotkeysEnabled || UsesGamepadBindings(settings);
        var probePad = pollPad || settings.Runecraft.AutoShowMonolithWithGamepad;
        GamepadInput.Configure(pollPad, settings.GamepadUserIndex);
        if (pollPad)
            GamepadInput.Poll();
        else if (probePad)
            GamepadInput.ProbeConnection(settings.GamepadUserIndex);
    }

    private static bool UsesGamepadBindings(RadarSettings s)
        => HotkeyCodes.IsGamepad(s.HideEntityHotkey)
        || HotkeyCodes.IsGamepad(s.TrackEntityHotkey)
        || HotkeyCodes.IsGamepad(s.AutoPathToggleHotkey)
        || HotkeyCodes.IsGamepad(s.ToggleRenderingHotkey)
        || HotkeyCodes.IsGamepad(s.AddNearestPathHotkey)
        || HotkeyCodes.IsGamepad(s.ClearPathsHotkey)
        || HotkeyCodes.IsGamepad(s.AutoFlaskToggleHotkey)
        || HotkeyCodes.IsGamepad(s.QuitHotkey)
        || HotkeyCodes.IsGamepad(s.AtlasPickHotkey)
        || HotkeyCodes.IsGamepad(s.ToggleSettingsHotkey)
        || HotkeyCodes.IsGamepad(s.OpenDashboardHotkey)
        || HotkeyCodes.IsGamepad(s.WaystoneAlchemy.RunHotkey)
        || HotkeyCodes.IsGamepad(s.WaystoneAlchemy.EmergencyStopHotkey);

    public static bool IsDown(int binding)
    {
        if (binding <= 0) return false;
        if (HotkeyCodes.IsGamepad(binding))
            return GamepadInput.IsButtonDown(HotkeyCodes.GamepadButtonMask(binding));
        return (OverlayNative.GetAsyncKeyState(binding) & 0x8000) != 0;
    }
}
