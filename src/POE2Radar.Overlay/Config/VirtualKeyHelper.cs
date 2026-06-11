namespace POE2Radar.Overlay.Config;

/// <summary>Display names for Win32 virtual-key codes used in settings hotkey binds.</summary>
public static class VirtualKeyHelper
{
    /// <summary>Win32 VK codes for mouse buttons (excludes VK_CANCEL 0x03).</summary>
    public static readonly int[] MouseButtonVks = [0x01, 0x02, 0x04, 0x05, 0x06];

    public static bool IsMouseButton(int vk)
        => vk is 0x01 or 0x02 or 0x04 or 0x05 or 0x06;

    public static string Name(int vk, bool requireShift = false)
    {
        if (vk <= 0) return "(none)";
        var name = vk is >= 0x30 and <= 0x39 ? ((char)vk).ToString()
            : vk is >= 0x41 and <= 0x5A ? ((char)vk).ToString()
            : VkNames.TryGetValue(vk, out var n) ? n : $"0x{vk:X}";
        return requireShift ? $"Shift+{name}" : name;
    }

    private static readonly Dictionary<int, string> VkNames = new()
    {
        [0x01] = "Mouse Left",
        [0x02] = "Mouse Right",
        [0x04] = "Mouse Middle",
        [0x05] = "Mouse 4",
        [0x06] = "Mouse 5",
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x1B] = "Esc",
        [0x20] = "Space",
        [0x21] = "Page Up",
        [0x22] = "Page Down",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "Left",
        [0x26] = "Up",
        [0x27] = "Right",
        [0x28] = "Down",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x70] = "F1",
        [0x71] = "F2",
        [0x72] = "F3",
        [0x73] = "F4",
        [0x74] = "F5",
        [0x75] = "F6",
        [0x76] = "F7",
        [0x77] = "F8",
        [0x78] = "F9",
        [0x79] = "F10",
        [0x7A] = "F11",
        [0x7B] = "F12",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'",
    };
}
