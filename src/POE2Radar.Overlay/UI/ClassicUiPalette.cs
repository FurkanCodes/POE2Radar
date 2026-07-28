using System.Drawing;

namespace POE2Radar.Overlay.UI;

internal static class ClassicUiPalette
{
    public static readonly Font UiFont = new("Tahoma", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font HeaderFont = new("Tahoma", 13f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font SmallBoldFont = new("Tahoma", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Color HeaderBack = Color.FromArgb(8, 35, 66);
    public static readonly Color HeaderText = Color.White;
    public static readonly Color Accent = Color.FromArgb(226, 168, 42);
    public static readonly Color LinkOn = Color.FromArgb(22, 145, 68);
    public static readonly Color LinkWait = Color.FromArgb(205, 132, 0);
    public static readonly Color LinkOff = Color.FromArgb(166, 42, 42);

    public static Label CreateLamp()
        => new()
        {
            AutoSize = false,
            BackColor = LinkOff,
            BorderStyle = BorderStyle.Fixed3D,
            Size = new Size(13, 13),
            Margin = new Padding(0, 1, 6, 0),
        };

    public static Panel CreateHeader(string title, string subtitle)
    {
        var panel = new Panel
        {
            BackColor = HeaderBack,
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(16, 10, 16, 8),
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = HeaderText,
            Font = HeaderFont,
            Location = new Point(15, 10),
            Text = title,
        });
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(205, 217, 230),
            Location = new Point(17, 39),
            Text = subtitle,
        });
        panel.Controls.Add(new Panel
        {
            BackColor = Accent,
            Dock = DockStyle.Bottom,
            Height = 3,
        });
        return panel;
    }
}
