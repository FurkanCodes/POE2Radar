using System.Runtime.InteropServices;
using System.Threading;

namespace POE2Radar.Overlay.Input;

/// <summary>
/// Minimal keyboard/mouse input via Win32 <c>SendInput</c>. Keyboard uses scancodes
/// (KEYEVENTF_SCANCODE). Mouse moves absolutely then clicks — PoE2 often ignores a bare
/// button event if the cursor has not settled after <see cref="SetCursorPos"/>.
/// </summary>
internal static class SendInputNative
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;
    internal const uint CursorMovementFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;

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

    /// <summary>
    /// Move to a screen-space point and issue one mouse click.
    /// <paramref name="settleMs"/> lets the game register the cursor move before the button event
    /// (needed for inventory currency apply — right-click would select the orb but left-click miss).
    /// </summary>
    public static bool Click(int screenX, int screenY, bool rightButton, int settleMs = 0)
    {
        if (!SetCursorPos(screenX, screenY)) return false;
        if (settleMs > 0)
            Thread.Sleep(Math.Clamp(settleMs, 1, 250));

        // SetCursorPos already moved the cursor in the caller's screen coordinate space. Sending
        // another absolute MOVE here can be interpreted in a different DPI/desktop coordinate
        // space and shift the cursor off the loot label immediately before the button event.
        var flags = PostCursorClickFlags(rightButton);
        var inputs = new INPUT[flags.Length];
        for (var i = 0; i < flags.Length; i++)
        {
            inputs[i].type = INPUT_MOUSE;
            inputs[i].U.mi = new MOUSEINPUT { dwFlags = flags[i] };
        }
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    internal static uint[] PostCursorClickFlags(bool rightButton)
        => rightButton
            ? [MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP]
            : [MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP];
}
