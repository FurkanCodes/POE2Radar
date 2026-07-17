using POE2Radar.Core.Game;
using Xunit;

namespace POE2Radar.Core.Tests;

public sealed class UiElementProjectionTests
{
    [Fact]
    public void ScalePair_MatchesGameHelperScaleIndexMapping()
    {
        var unscaled = UiElementProjection.ScalePair(0, 1f, 1920, 1080);
        var widthScale = UiElementProjection.ScalePair(1, 1f, 1920, 1080);
        var heightScale = UiElementProjection.ScalePair(2, 1f, 1920, 1080);
        var nonUniform = UiElementProjection.ScalePair(3, 2f, 1920, 1080);

        Assert.Equal(1f, unscaled.X, 4);
        Assert.Equal(1f, unscaled.Y, 4);
        Assert.Equal(0.675f, widthScale.X, 4);
        Assert.Equal(0.675f, widthScale.Y, 4);
        Assert.Equal(0.675f, heightScale.X, 4);
        Assert.Equal(0.675f, heightScale.Y, 4);
        Assert.Equal(1.35f, nonUniform.X, 4);
        Assert.Equal(1.35f, nonUniform.Y, 4);
        Assert.Equal(96f, UiElementProjection.HorizontalCull(1920, 1080), 4);
    }

    [Fact]
    public void TryGetRect_AccumulatesAncestorPositionsWithEachElementScale()
    {
        const uint visible = 1u << Poe2.UiElement.FlagVisibleBit;
        var parent = new UiElementProjection.Element(1, 0, visible, 100, 50, 0, 0, 1f, 1, 400, 300);
        var child = new UiElementProjection.Element(2, 1, visible, 20, 10, 0, 0, 1f, 2, 40, 40);
        var elements = new Dictionary<nint, UiElementProjection.Element>
        {
            [1] = parent,
            [2] = child,
        };

        var ok = UiElementProjection.TryGetRect(2, ReadElement, 1920, 1080, new Dictionary<nint, UiElementProjection.Element>(), new Dictionary<nint, UiElementProjection.Point>(), out var rect);

        Assert.True(ok);
        Assert.Equal(177f, rect.X, 3);
        Assert.Equal(40.5f, rect.Y, 3);
        Assert.Equal(27f, rect.W, 3);
        Assert.Equal(27f, rect.H, 3);

        bool ReadElement(nint address, out UiElementProjection.Element element)
            => elements.TryGetValue(address, out element);
    }

    [Fact]
    public void TryGetRect_AppliesPositionModifierWhenFlagIsSet()
    {
        const uint visible = 1u << Poe2.UiElement.FlagVisibleBit;
        const uint modifyPos = 1u << Poe2.UiElement.FlagModifyPosBit;
        var parent = new UiElementProjection.Element(1, 0, visible, 100, 50, 10, -4, 1f, 1, 400, 300);
        var child = new UiElementProjection.Element(2, 1, visible | modifyPos, 20, 10, 0, 0, 1f, 2, 40, 40);
        var elements = new Dictionary<nint, UiElementProjection.Element>
        {
            [1] = parent,
            [2] = child,
        };

        var ok = UiElementProjection.TryGetRect(2, ReadElement, 1920, 1080, null, null, out var rect);

        Assert.True(ok);
        Assert.Equal(183.75f, rect.X, 3);
        Assert.Equal(37.8f, rect.Y, 3);

        bool ReadElement(nint address, out UiElementProjection.Element element)
            => elements.TryGetValue(address, out element);
    }

    [Fact]
    public void TryGetRect_UsesLiveCullOverride_OnUltrawideClient()
    {
        const uint visible = 1u << Poe2.UiElement.FlagVisibleBit;
        var parent = new UiElementProjection.Element(1, 0, visible, 100, 50, 0, 0, 1f, 2, 400, 300);
        var child = new UiElementProjection.Element(2, 1, visible, 20, 10, 0, 0, 1f, 2, 40, 40);
        var elements = new Dictionary<nint, UiElementProjection.Element>
        {
            [1] = parent,
            [2] = child,
        };

        var ok = UiElementProjection.TryGetRect(
            2,
            ReadElement,
            3440,
            1440,
            null,
            null,
            out var rect,
            horizontalCull: 0f);

        Assert.True(ok);
        Assert.Equal(108f, rect.X, 3);
        Assert.Equal(54f, rect.Y, 3);

        bool ReadElement(nint address, out UiElementProjection.Element element)
            => elements.TryGetValue(address, out element);
    }
}
