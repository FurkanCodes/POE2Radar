using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Native;

namespace POE2Radar.Overlay.Input;

/// <summary>Unified keyboard + gamepad binding poll (Win32 VK or encoded gamepad mask).</summary>
public static class HotkeyPoll
{
    public static void BeginTick(RadarSettings settings)
    {
        var pollPad = settings.GamepadHotkeysEnabled || InputActionCatalog.UsesGamepadBindings(settings);
        GamepadInput.Configure(pollPad, settings.GamepadUserIndex);
        if (pollPad) GamepadInput.Poll();
    }

    public static bool UsesGamepadBindings(RadarSettings settings)
        => InputActionCatalog.UsesGamepadBindings(settings);

    public static bool IsDown(int binding)
    {
        if (binding <= 0) return false;
        if (HotkeyCodes.IsGamepad(binding))
            return GamepadInput.IsButtonDown(HotkeyCodes.GamepadButtonMask(binding));
        return (OverlayNative.GetAsyncKeyState(binding) & 0x8000) != 0;
    }
}
