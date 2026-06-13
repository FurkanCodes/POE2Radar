using POE2Radar.Overlay.Input;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class HotkeyCodesTests
{
    [Fact]
    public void EncodedGamepadBinding_IsDisplayableAndNotMouse()
    {
        var code = HotkeyCodes.EncodeGamepad(GamepadInput.A);

        Assert.True(HotkeyCodes.IsGamepad(code));
        Assert.Equal(GamepadInput.A, HotkeyCodes.GamepadButtonMask(code));
        Assert.Equal("Pad A", HotkeyCodes.DisplayName(code));
        Assert.False(HotkeyCodes.IsMouseButton(code));
    }

    [Fact]
    public void UnknownGamepadMask_StillDisplaysAsPadHex()
    {
        var code = HotkeyCodes.EncodeGamepad(0x0400);

        Assert.Equal("Pad 0x400", HotkeyCodes.DisplayName(code));
    }
}
