using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class UiElementProjectionTests
{
    [Fact]
    public void ScalePair_MatchesGameHelperScaleIndexMapping()
    {
        var widthScale = UiElementProjection.ScalePair(0, 1f, 1920, 1080);
        var heightScale = UiElementProjection.ScalePair(1, 1f, 1920, 1080);
        var minScale = UiElementProjection.ScalePair(2, 1f, 1920, 1080);
        var nonUniform = UiElementProjection.ScalePair(3, 2f, 1920, 1080);

        Assert.Equal(0.75f, widthScale.X, 4);
        Assert.Equal(0.75f, widthScale.Y, 4);
        Assert.Equal(0.675f, heightScale.X, 4);
        Assert.Equal(0.675f, heightScale.Y, 4);
        Assert.Equal(0.675f, minScale.X, 4);
        Assert.Equal(0.675f, minScale.Y, 4);
        Assert.Equal(1.5f, nonUniform.X, 4);
        Assert.Equal(1.35f, nonUniform.Y, 4);
    }

    [Fact]
    public void TryGetRect_AccumulatesAncestorPositionsWithEachElementScale()
    {
        const uint visible = 1u << Poe2.UiElement.FlagVisibleBit;
        var parent = new UiElementProjection.Element(1, 0, visible, 100, 50, 0, 0, 1f, 0, 400, 300);
        var child = new UiElementProjection.Element(2, 1, visible, 20, 10, 0, 0, 1f, 1, 40, 40);
        var elements = new Dictionary<nint, UiElementProjection.Element>
        {
            [1] = parent,
            [2] = child,
        };

        var ok = UiElementProjection.TryGetRect(2, ReadElement, 1920, 1080, new Dictionary<nint, UiElementProjection.Element>(), new Dictionary<nint, UiElementProjection.Point>(), out var rect);

        Assert.True(ok);
        Assert.Equal(88.5f, rect.X, 3);
        Assert.Equal(44.25f, rect.Y, 3);
        Assert.Equal(27f, rect.W, 3);
        Assert.Equal(27f, rect.H, 3);

        bool ReadElement(nint address, out UiElementProjection.Element element)
            => elements.TryGetValue(address, out element);
    }

    [Fact]
    public void TryGetRect_AppliesPositionModifierWhenFlagIsSet()
    {
        const uint modifyPos = 1u << Poe2.UiElement.FlagModifyPosBit;
        var child = new UiElementProjection.Element(2, 0, modifyPos, 20, 10, 10, -4, 1f, 1, 40, 40);
        var elements = new Dictionary<nint, UiElementProjection.Element> { [2] = child };

        var ok = UiElementProjection.TryGetRect(2, ReadElement, 1920, 1080, null, null, out var rect);

        Assert.True(ok);
        Assert.Equal(20.25f, rect.X, 3);
        Assert.Equal(4.05f, rect.Y, 3);

        bool ReadElement(nint address, out UiElementProjection.Element element)
            => elements.TryGetValue(address, out element);
    }
}
