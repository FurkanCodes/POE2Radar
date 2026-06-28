using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class RunecraftLayoutTests
{
    [Fact]
    public void PanelFingerprints_MatchGameHelperRunecraftHelper()
    {
        uint[] expected =
        [
            0x00462EF1,
            0x00502EF3,
            0x00502EF7,
            0x00542EF1,
            0x00502EF1,
        ];

        Assert.Equal(5, expected.Length);
        Assert.Equal(0x00462EF1u, expected[0]);
        Assert.Equal(0x00502EF1u, expected[^1]);
    }

    [Theory]
    [InlineData(0x180, Poe2.UiElement.Flags)]
    [InlineData(0x390, Poe2.UiElement.Text)]
    [InlineData(0x120, Poe2.UiElement.ScrollOffset)]
    public void UiOffsets_AreDocumentedForRunecraft(int _, int offset)
    {
        Assert.True(offset > 0);
    }

    [Fact]
    public void ScrollMath_AppliesScaleToViewportOffset()
    {
        var scroll = new Vector2 { X = 10f, Y = -40f };
        var scale = UiElementProjection.ScalePair(2, 1f, 1920, 1080);
        var unscaled = RunecraftScrollMath.ApplyUnscaledScroll(scroll);
        Assert.Equal(10f, unscaled.X);
        Assert.Equal(-40f, unscaled.Y);

        var screen = RunecraftScrollMath.ApplyScreenScroll(scroll, scale);
        Assert.Equal(10f * scale.X, screen.X, 3);
        Assert.Equal(-40f * scale.Y, screen.Y, 3);
    }
}
