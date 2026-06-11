namespace POE2Radar.Overlay.Input;

/// <summary>Encoded input bindings: keyboard VK (1–0xFF) or gamepad (0x10000 | XINPUT button mask).</summary>
public static class HotkeyCodes
{
    public const int GamepadFlag = 0x10000;

    public static bool IsGamepad(int code) => code >= GamepadFlag;

    public static ushort GamepadButtonMask(int code) => (ushort)(code & 0xFFFF);

    public static int EncodeGamepad(ushort buttonMask) => GamepadFlag | buttonMask;

    public static string DisplayName(int code)
    {
        if (code <= 0) return "(none)";
        if (IsGamepad(code))
        {
            var mask = GamepadButtonMask(code);
            return GamepadInput.ButtonName(mask);
        }
        return Config.VirtualKeyHelper.Name(code);
    }

    public static bool IsMouseButton(int code)
        => !IsGamepad(code) && Config.VirtualKeyHelper.IsMouseButton(code);
}
