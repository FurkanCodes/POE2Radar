using System.Runtime.InteropServices;

namespace POE2Radar.Overlay.Input;

/// <summary>
/// Minimal keyboard input via Win32 <c>SendInput</c>, scancode-based (KEYEVENTF_SCANCODE) which
/// games read more reliably than virtual-key events. Used by explicitly enabled QoL features; all
/// firing is gated by <see cref="RadarApp"/> (foreground + UI/game state + cooldown + kill-switch).
/// </summary>
internal static class SendInputNative
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    /// <summary>Press and release a virtual key (e.g. 0x31 = '1') as a scancode keystroke.</summary>
    public static void Tap(ushort vk)
    {
        var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        if (scan == 0) return;

        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].U.ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE };
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].U.ki = new KEYBDINPUT { wScan = scan, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP };

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Move to a validated screen-space point and issue one mouse click.</summary>
    public static bool Click(int screenX, int screenY, bool rightButton)
    {
        if (!SetCursorPos(screenX, screenY)) return false;
        var down = rightButton ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
        var up = rightButton ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].U.mi = new MOUSEINPUT { dwFlags = down };
        inputs[1].type = INPUT_MOUSE;
        inputs[1].U.mi = new MOUSEINPUT { dwFlags = up };
        return SendInput(2, inputs, Marshal.SizeOf<INPUT>()) == 2;
    }

}
