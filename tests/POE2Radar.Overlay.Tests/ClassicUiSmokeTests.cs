using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.UI;
using POE2Radar.Overlay.Web;
using System.Windows.Forms;
using Xunit;

namespace POE2Radar.Overlay.Tests;

public sealed class ClassicUiSmokeTests
{
    [Fact]
    public void Forms_ConstructAndRenderOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new RadarSettings();
                var temp = Path.Combine(Path.GetTempPath(), $"poe2radar-ui-{Guid.NewGuid():N}");
                Directory.CreateDirectory(temp);
                var rules = new DisplayRules(Path.Combine(temp, "rules.json"));
                rules.Add(new DisplayRule
                {
                    Name = "Preview rare monsters",
                    Categories = ["Monster"],
                    Rarity = "Rare",
                    Navigable = true,
                    Color = "#FFD33D",
                });
                var hidden = new HiddenEntities(Path.Combine(temp, "hidden.json"));
                var actions = new ClassicSettingsActions(
                    () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
                using var startup = new StartupForm(settings, autoProbe: false);
                using var settingsForm = new SettingsForm(settings, () => { }, _ => { }, rules, hidden, actions);
                Render(startup, "startup");
                Render(settingsForm, "settings");
                Assert.True(settingsForm.SelectPageForPreview("Radar Details"));
                Render(settingsForm, "settings-radar-details");
                Assert.True(settingsForm.SelectPageForPreview("Pickup Helper"));
                Render(settingsForm, "settings-pickup-helper");
                Assert.True(settingsForm.SelectPageForPreview("Campaign Helper"));
                Render(settingsForm, "settings-campaign-helper");
                Assert.True(settingsForm.SelectPageForPreview("Character Progress"));
                Render(settingsForm, "settings-campaign-progress");
                Assert.True(settingsForm.SelectPageForPreview("Stash Utility"));
                Render(settingsForm, "settings-stash-utility");
                Assert.True(settingsForm.SelectPageForPreview("Mod Tables"));
                Render(settingsForm, "settings-stash-mod-tables");
                Assert.True(settingsForm.SelectPageForPreview("Atlas Tables"));
                Render(settingsForm, "settings-atlas-tables");
                Assert.True(settingsForm.SelectPageForPreview("Crafting Control"));
                Render(settingsForm, "settings-crafting-control");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI smoke-test thread timed out.");
        Assert.Null(failure);
    }

    private static void Render(Form form, string name)
    {
        var output = Environment.GetEnvironmentVariable("POE2RADAR_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(output))
        {
            form.CreateControl();
            form.PerformLayout();
            return;
        }

        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));

        Directory.CreateDirectory(output);
        bitmap.Save(
            Path.Combine(output, $"winforms-{name}-preview.png"),
            System.Drawing.Imaging.ImageFormat.Png);
    }
}
