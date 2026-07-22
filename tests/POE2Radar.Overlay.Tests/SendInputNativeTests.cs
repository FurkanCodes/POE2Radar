using POE2Radar.Overlay.Input;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class SendInputNativeTests
{
    [Fact]
    public void ClickAfterSetCursorPosition_DoesNotMoveCursorASecondTime()
    {
        var flags = SendInputNative.PostCursorClickFlags(rightButton: false);

        Assert.Equal(2, flags.Length);
        Assert.All(flags, flag => Assert.Equal(0u, flag & SendInputNative.CursorMovementFlags));
    }
}
