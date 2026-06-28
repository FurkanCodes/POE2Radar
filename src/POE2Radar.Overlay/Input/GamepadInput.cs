using System.Runtime.InteropServices;

namespace POE2Radar.Overlay.Input;

/// <summary>XInput polling for Xbox / compatible controllers (Win32 xinput1_4 or xinput9_1_0).</summary>
public static class GamepadInput
{
    // XINPUT_GAMEPAD button masks (ushort wButtons).
    public const ushort A = 0x1000;
    public const ushort B = 0x2000;
    public const ushort X = 0x4000;
    public const ushort Y = 0x8000;
    public const ushort DPadUp = 0x0001;
    public const ushort DPadDown = 0x0002;
    public const ushort DPadLeft = 0x0004;
    public const ushort DPadRight = 0x0008;
    public const ushort Start = 0x0010;
    public const ushort Back = 0x0020;
    public const ushort LeftThumb = 0x0040;
    public const ushort RightThumb = 0x0080;
    public const ushort LeftShoulder = 0x0100;
    public const ushort RightShoulder = 0x0200;

    private static readonly (ushort Mask, string Name)[] BindOrder =
    [
        (A, "Pad A"),
        (B, "Pad B"),
        (X, "Pad X"),
        (Y, "Pad Y"),
        (LeftShoulder, "Pad LB"),
        (RightShoulder, "Pad RB"),
        (Back, "Pad Back"),
        (Start, "Pad Start"),
        (LeftThumb, "Pad L3"),
        (RightThumb, "Pad R3"),
        (DPadUp, "Pad D-Up"),
        (DPadDown, "Pad D-Down"),
        (DPadLeft, "Pad D-Left"),
        (DPadRight, "Pad D-Right"),
    ];

    private static ushort _buttons;
    private static ushort _bindBaseline;
    private static bool _enabled;
    private static uint _userIndex;

    public static void Configure(bool enabled, int userIndex)
    {
        _enabled = enabled;
        _userIndex = (uint)Math.Clamp(userIndex, 0, 3);
    }

    /// <summary>True when an Xbox-compatible controller is connected on the given slot.</summary>
    public static bool IsConnected(int userIndex = 0)
        => TryReadButtons((uint)Math.Clamp(userIndex, 0, 3), out _);

    public static string ButtonName(ushort mask)
    {
        foreach (var (m, name) in BindOrder)
            if (m == mask) return name;
        return $"Pad 0x{mask:X}";
    }

    public static IReadOnlyList<(ushort Mask, string Name)> BindableButtons => BindOrder;

    public static void Poll()
    {
        if (!_enabled) { _buttons = 0; return; }
        if (!TryReadButtons(out var cur)) { _buttons = 0; return; }
        _buttons = cur;
    }

    public static bool IsButtonDown(ushort mask)
        => mask != 0 && (_buttons & mask) != 0;

    /// <summary>Call when bind UI arms — ignores buttons already held.</summary>
    public static void ArmBindCapture()
    {
        _bindBaseline = TryReadButtons(out var cur) ? cur : (ushort)0;
    }

    /// <summary>New button press since <see cref="ArmBindCapture"/> (binding UI).</summary>
    public static bool TryGetBindPress(out ushort mask)
    {
        mask = 0;
        if (!_enabled || !TryReadButtons(out var cur)) return false;
        var edge = (ushort)(cur & ~_bindBaseline);
        if (edge == 0) return false;
        foreach (var (m, _) in BindOrder)
        {
            if ((edge & m) != 0)
            {
                mask = m;
                return true;
            }
        }
        mask = edge;
        return true;
    }

    private static bool TryReadButtons(out ushort buttons)
        => TryReadButtons(_userIndex, out buttons);

    private static bool TryReadButtons(uint userIndex, out ushort buttons)
    {
        var state = new XINPUT_STATE();
        if (XInputGetState(userIndex, ref state) != 0)
        {
            buttons = 0;
            return false;
        }
        buttons = state.Gamepad.wButtons;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    private static int XInputGetState(uint index, ref XINPUT_STATE state)
    {
        var r = XInputGetState14(index, ref state);
        if (r == 0) return 0;
        return XInputGetState9(index, ref state);
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState14(uint dwUserIndex, ref XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState9(uint dwUserIndex, ref XINPUT_STATE pState);
}
