using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Native;

namespace POE2Radar.Overlay.UI;

/// <summary>Old-school Windows launcher with the same probe/attach lifecycle as the former ImGui menu.</summary>
internal sealed class StartupForm : Form
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1.5);

    private readonly RadarSettings _settings;
    private readonly bool _autoProbe;
    private readonly TextBox _pathBox;
    private readonly Label _installNote;
    private readonly Label _attachTitle;
    private readonly Label _attachDetail;
    private readonly Label _linkLamp;
    private readonly ToolStripStatusLabel _statusText;
    private readonly Button _startGameButton;
    private readonly Button _startRadarButton;
    private readonly System.Windows.Forms.Timer _probeTimer;

    private GameInstallLocator.LocateResult _install;
    private AttachResult? _attach;
    private bool _transferred;

    public AttachResult? Result { get; private set; }

    public StartupForm(RadarSettings settings, bool autoProbe = true)
    {
        _settings = settings;
        _autoProbe = autoProbe;
        _install = GameInstallLocator.Discover(settings.GameExePath);

        Text = "POE2Radar Control Station";
        Font = ClassicUiPalette.UiFont;
        BackColor = SystemColors.Control;
        ClientSize = new Size(660, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;

        Controls.Add(ClassicUiPalette.CreateHeader(
            "POE2Radar",
            "External map/radar instrument · Path of Exile 2"));

        var installGroup = new GroupBox
        {
            Text = " GAME INSTALL ",
            Location = new Point(14, 86),
            Size = new Size(632, 112),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        installGroup.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(12, 26),
            Text = "Client executable:",
        });
        _pathBox = new TextBox
        {
            Location = new Point(110, 22),
            Size = new Size(418, 21),
            ReadOnly = true,
            BackColor = SystemColors.Window,
        };
        installGroup.Controls.Add(_pathBox);
        var browseButton = new Button
        {
            Location = new Point(536, 20),
            Size = new Size(82, 25),
            Text = "Browse...",
        };
        browseButton.Click += (_, _) => BrowseForGame();
        installGroup.Controls.Add(browseButton);
        _installNote = new Label
        {
            AutoEllipsis = true,
            Location = new Point(12, 55),
            Size = new Size(606, 42),
            ForeColor = SystemColors.GrayText,
        };
        installGroup.Controls.Add(_installNote);
        Controls.Add(installGroup);

        var attachGroup = new GroupBox
        {
            Text = " OVERLAY ATTACH ",
            Location = new Point(14, 207),
            Size = new Size(632, 111),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _linkLamp = ClassicUiPalette.CreateLamp();
        _linkLamp.Location = new Point(14, 26);
        attachGroup.Controls.Add(_linkLamp);
        _attachTitle = new Label
        {
            AutoEllipsis = true,
            Font = ClassicUiPalette.SmallBoldFont,
            Location = new Point(36, 24),
            Size = new Size(580, 18),
            Text = "Checking for a running game...",
        };
        attachGroup.Controls.Add(_attachTitle);
        _attachDetail = new Label
        {
            AutoEllipsis = true,
            Location = new Point(14, 50),
            Size = new Size(602, 47),
            ForeColor = SystemColors.GrayText,
        };
        attachGroup.Controls.Add(_attachDetail);
        Controls.Add(attachGroup);

        _startGameButton = new Button
        {
            Location = new Point(14, 331),
            Size = new Size(108, 30),
            Text = "&Start Game",
        };
        _startGameButton.Click += (_, _) => LaunchGame();
        Controls.Add(_startGameButton);

        _startRadarButton = new Button
        {
            Location = new Point(130, 331),
            Size = new Size(108, 30),
            Text = "Start &Radar",
            Enabled = false,
        };
        _startRadarButton.Click += (_, _) => StartRadar();
        Controls.Add(_startRadarButton);

        var retryButton = new Button
        {
            Location = new Point(246, 331),
            Size = new Size(108, 30),
            Text = "&Check Now",
        };
        retryButton.Click += (_, _) => ProbeNow();
        Controls.Add(retryButton);

        var quitButton = new Button
        {
            Location = new Point(538, 331),
            Size = new Size(108, 30),
            Text = "&Quit",
            DialogResult = DialogResult.Cancel,
        };
        quitButton.Click += (_, _) => Close();
        Controls.Add(quitButton);
        CancelButton = quitButton;
        AcceptButton = _startRadarButton;

        var status = new StatusStrip { SizingGrip = false };
        status.Items.Add(new ToolStripStatusLabel("POE2 LINK") { Font = ClassicUiPalette.SmallBoldFont });
        _statusText = new ToolStripStatusLabel("OFFLINE") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        status.Items.Add(_statusText);
        status.Items.Add(new ToolStripStatusLabel("External read-only attach"));
        Controls.Add(status);

        _probeTimer = new System.Windows.Forms.Timer { Interval = (int)ProbeInterval.TotalMilliseconds };
        _probeTimer.Tick += (_, _) =>
        {
            if (_attach?.CanStart != true)
                ProbeNow();
        };

        UpdateInstallDisplay();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        OverlayNative.ApplyCaptureExclusion(Handle, _settings.HideFromScreenCapture);
        if (!_autoProbe) return;
        RememberDiscoveredPath();
        ProbeNow();
        _probeTimer.Start();
    }

    private void UpdateInstallDisplay(string? note = null, bool error = false)
    {
        _pathBox.Text = _install.Path ?? "(not found)";
        _installNote.Text = note ?? _install.Message;
        _installNote.ForeColor = error ? ClassicUiPalette.LinkOff : SystemColors.GrayText;
        _startGameButton.Enabled = _install.Found;
    }

    private void RememberDiscoveredPath()
    {
        if (!_install.Found || _install.Path is null) return;
        if (string.Equals(_settings.GameExePath, _install.Path, StringComparison.OrdinalIgnoreCase)) return;
        GameInstallLocator.SavePath(_settings, _install.Path);
    }

    private void BrowseForGame()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the Path of Exile 2 client",
            Filter = "Path of Exile client (*.exe)|PathOfExile*.exe|Programs (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = _install.Found ? Path.GetDirectoryName(_install.Path) : null,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (!GameInstallLocator.IsValidGameExe(dialog.FileName))
        {
            UpdateInstallDisplay(
                "That file does not look like a PoE2 client. Select PathOfExile.exe or PathOfExileSteam.exe.",
                error: true);
            return;
        }

        _install = new GameInstallLocator.LocateResult(
            Path.GetFullPath(dialog.FileName),
            GameInstallLocator.LocateSource.Saved,
            "Using the path you selected.");
        GameInstallLocator.SavePath(_settings, _install.Path!);
        UpdateInstallDisplay();
    }

    private void LaunchGame()
    {
        if (!_install.Found || _install.Path is null)
        {
            UpdateInstallDisplay("Browse to the game executable first.", error: true);
            return;
        }

        if (!GameInstallLocator.TryLaunch(_install.Path, out var error))
        {
            UpdateInstallDisplay(error, error: true);
            return;
        }

        UpdateInstallDisplay("Launching Path of Exile 2. Load into a zone; the radar will attach automatically.");
        ProbeNow();
    }

    private void ProbeNow()
    {
        _attach?.Dispose();
        _attach = AttachResult.Probe();
        _attachTitle.Text = _attach.StatusTitle;
        _attachDetail.Text = _attach.StatusDetail;
        _startRadarButton.Enabled = _attach.CanStart;

        (_linkLamp.BackColor, _statusText.Text) = _attach.Status switch
        {
            AttachStatus.Ready => (ClassicUiPalette.LinkOn, "READY"),
            AttachStatus.NotInZone => (ClassicUiPalette.LinkWait, "GAME DETECTED · WAITING FOR ZONE"),
            AttachStatus.PoENotRunning => (ClassicUiPalette.LinkOff, "OFFLINE"),
            AttachStatus.AccessDenied => (ClassicUiPalette.LinkOff, "ACCESS DENIED"),
            _ => (ClassicUiPalette.LinkOff, "ATTACH ERROR"),
        };

        if (_attach.CanStart)
            StartRadar();
    }

    private void StartRadar()
    {
        if (_attach?.CanStart != true) return;
        _probeTimer.Stop();
        Result = _attach;
        _attach = null;
        _transferred = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _probeTimer.Dispose();
            if (!_transferred)
                _attach?.Dispose();
        }
        base.Dispose(disposing);
    }
}
