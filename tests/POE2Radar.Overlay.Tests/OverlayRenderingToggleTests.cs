using POE2Radar.Overlay.Native;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class OverlayRenderingToggleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenderingToggle_KeepsNativeOverlayWindowAlive(bool renderingEnabled)
    {
        var command = ImGuiRadarOverlay.WindowShowCommandForDrawEnabled(renderingEnabled);

        Assert.Equal(OverlayNative.SW_SHOWNOACTIVATE, command);
        Assert.NotEqual(OverlayNative.SW_HIDE, command);
    }
}
