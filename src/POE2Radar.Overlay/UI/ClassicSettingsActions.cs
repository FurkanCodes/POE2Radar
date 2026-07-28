namespace POE2Radar.Overlay.UI;

internal sealed record ClassicSettingsActions(
    Action ToggleRendering,
    Action AddNearestRoute,
    Action ClearRoutes,
    Action NewLootSession,
    Action StartWaystoneCrafting,
    Action StopWaystoneCrafting,
    Action PlanExpedition,
    Action CompleteCampaignObjective,
    Action BackCampaignObjective,
    Action RestoreCampaignWidget,
    Action ResetCampaignCharacter,
    Action ExportCampaignProgress,
    Action ImportCampaignProgress)
{
    public ClassicSettingsActions(
        Action toggleRendering,
        Action addNearestRoute,
        Action clearRoutes,
        Action newLootSession,
        Action startWaystoneCrafting,
        Action stopWaystoneCrafting,
        Action planExpedition)
        : this(
            toggleRendering,
            addNearestRoute,
            clearRoutes,
            newLootSession,
            startWaystoneCrafting,
            stopWaystoneCrafting,
            planExpedition,
            static () => { },
            static () => { },
            static () => { },
            static () => { },
            static () => { },
            static () => { })
    {
    }
}

internal sealed class ClassicActionControl : UserControl
{
    public ClassicActionControl(
        string warning,
        params (string Label, string Description, Action Run)[] actions)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Font = ClassicUiPalette.UiFont;

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12),
        };
        panel.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 640,
            Height = 42,
            ForeColor = SystemColors.GrayText,
            Text = warning,
        });

        foreach (var action in actions)
        {
            var row = new Panel { Width = 660, Height = 48 };
            var button = new Button
            {
                Location = new Point(0, 7),
                Size = new Size(165, 29),
                Text = action.Label,
            };
            button.Click += (_, _) => action.Run();
            row.Controls.Add(button);
            row.Controls.Add(new Label
            {
                Location = new Point(178, 4),
                Size = new Size(470, 38),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = action.Description,
            });
            panel.Controls.Add(row);
        }
        Controls.Add(panel);
    }
}
