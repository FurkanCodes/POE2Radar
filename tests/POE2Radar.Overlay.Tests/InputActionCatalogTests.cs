using POE2Radar.Core.Game;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Input;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class InputActionCatalogTests
{
    [Fact]
    public void AllActions_HaveHintsAndDefaults()
    {
        Assert.NotEmpty(InputActionCatalog.All);
        foreach (var action in InputActionCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Id));
            Assert.False(string.IsNullOrWhiteSpace(action.Label));
            Assert.False(string.IsNullOrWhiteSpace(action.Hint));
            Assert.True(action.DefaultBinding > 0);
            Assert.NotNull(action.GetBinding);
            Assert.NotNull(action.SetBinding);
        }
    }

    [Fact]
    public void AllActions_AreDisplayableViaHotkeyCodes()
    {
        var settings = new RadarSettings();
        foreach (var action in InputActionCatalog.All)
        {
            var binding = action.GetBinding(settings);
            Assert.False(string.IsNullOrWhiteSpace(HotkeyCodes.DisplayName(binding)));
            action.SetBinding(settings, action.DefaultBinding);
            Assert.Equal(action.DefaultBinding, action.GetBinding(settings));
        }
    }

    [Fact]
    public void UsesGamepadBindings_DetectsEncodedPadMask()
    {
        var settings = new RadarSettings { HideEntityHotkey = HotkeyCodes.EncodeGamepad(GamepadInput.A) };
        Assert.True(InputActionCatalog.UsesGamepadBindings(settings));
        Assert.True(HotkeyPoll.UsesGamepadBindings(settings));
    }
}
