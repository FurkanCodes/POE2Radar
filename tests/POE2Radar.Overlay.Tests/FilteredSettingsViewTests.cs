using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.UI;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class FilteredSettingsViewTests
{
    [Theory]
    [InlineData("FpsCap", "Fps Cap")]
    [InlineData("HideFromScreenCapture", "Hide From Screen Capture")]
    [InlineData("Atlas2Qol", "Atlas 2 Qol")]
    public void FriendlyName_SplitsPascalCaseAndDigits(string source, string expected)
    {
        Assert.Equal(expected, FilteredSettingsView.FriendlyName(source));
    }

    [Fact]
    public void FilteredProperties_WriteThroughToLiveSettings()
    {
        var settings = new RadarSettings { FpsCap = 45, ShowTerrain = true };
        var view = new FilteredSettingsView(
            settings,
            "Performance",
            property => property.Name == nameof(RadarSettings.FpsCap));

        var properties = view.GetProperties();

        var fps = Assert.Single(properties.Cast<System.ComponentModel.PropertyDescriptor>());
        Assert.Equal("Fps Cap", fps.DisplayName);
        fps.SetValue(view, 72);
        Assert.Equal(72, settings.FpsCap);
        Assert.Null(properties.Find(nameof(RadarSettings.ShowTerrain), ignoreCase: false));
    }

    [Fact]
    public void PickupProperties_AreGroupedInConfigurationOrder()
    {
        var settings = new PickupHelperSettings();
        var properties = new FilteredSettingsView(settings, "Pickup Helper").GetProperties();

        Assert.Equal("01 · Operation", properties[nameof(PickupHelperSettings.Enabled)]!.Category);
        Assert.Equal("02 · Controls", properties[nameof(PickupHelperSettings.ActivationHotkey)]!.Category);
        Assert.Equal("03 · Targeting", properties[nameof(PickupHelperSettings.MaxPickupDistance)]!.Category);
        Assert.Equal("04 · Display", properties[nameof(PickupHelperSettings.ShowTargetHighlight)]!.Category);
        Assert.Equal("05 · Safety", properties[nameof(PickupHelperSettings.PauseWhileShowHiddenHeld)]!.Category);
        Assert.Equal("06 · Timing", properties[nameof(PickupHelperSettings.MinPickupDelayMs)]!.Category);
    }

    [Fact]
    public void CodedIntegerModes_AppearAsNamedDropdownChoices()
    {
        var settings = new PickupHelperSettings { Mode = 3 };
        var properties = new FilteredSettingsView(settings, "Pickup Helper").GetProperties();
        var mode = properties[nameof(PickupHelperSettings.Mode)]!;

        Assert.True(mode.Converter.GetStandardValuesSupported());
        Assert.True(mode.Converter.GetStandardValuesExclusive());
        Assert.Equal("Nearby items · automatic toggle", mode.Converter.ConvertToString(3));
        Assert.Equal(0, mode.Converter.ConvertFromString("Assist only"));
        Assert.DoesNotContain("Live value", mode.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hotkeys_ShowNamesWithoutChangingStoredInteger()
    {
        var settings = new PickupHelperSettings { EmergencyStopHotkey = 0x77 };
        var properties = new FilteredSettingsView(settings, "Pickup Helper").GetProperties();
        var hotkey = properties[nameof(PickupHelperSettings.EmergencyStopHotkey)]!;

        Assert.Equal("F8 (119)", hotkey.Converter.ConvertToString(0x77));
        Assert.Equal(0x77, hotkey.Converter.ConvertFromString("F8 (119)"));
        hotkey.SetValue(settings, hotkey.Converter.ConvertFromString("F9"));
        Assert.Equal(0x78, settings.EmergencyStopHotkey);
        Assert.Equal("Alt (18)", hotkey.Converter.ConvertToString(0x12));
    }

    [Fact]
    public void LootTrackerCurrencyChoices_IncludeAutomaticAndCorrectStoredValues()
    {
        var settings = new LootTrackerSettings();
        var properties = new FilteredSettingsView(settings, "Loot Tracker").GetProperties();
        var currency = properties[nameof(LootTrackerSettings.DisplayCurrency)]!;

        Assert.Equal("Automatic", currency.Converter.ConvertToString(LootTrackerSettings.CurrencyAuto));
        Assert.Equal("Divine", currency.Converter.ConvertToString(LootTrackerSettings.CurrencyDivine));
        Assert.Equal("Exalted", currency.Converter.ConvertToString(LootTrackerSettings.CurrencyExalted));
        Assert.Equal("Chaos", currency.Converter.ConvertToString(LootTrackerSettings.CurrencyChaos));
    }
}
