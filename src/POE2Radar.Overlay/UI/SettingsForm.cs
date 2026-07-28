using System.ComponentModel;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Native;
using POE2Radar.Overlay.Web;

namespace POE2Radar.Overlay.UI;

internal readonly record struct SettingsUiStatus(
    bool InGame,
    bool GameActive,
    bool RenderingEnabled,
    string AreaCode,
    float HpPct,
    float ManaPct,
    float EsPct,
    string PickupStatus);

/// <summary>Classic property-sheet shell for the live RadarSettings object.</summary>
internal sealed class SettingsForm : Form
{
    private static readonly HashSet<string> PerformanceNames =
    [
        nameof(RadarSettings.LowImpactMode),
        nameof(RadarSettings.FpsCap),
        nameof(RadarSettings.LiveRefreshHz),
        nameof(RadarSettings.WorldRefreshHz),
        nameof(RadarSettings.InactiveRefreshHz),
        nameof(RadarSettings.HpBarRefreshHz),
        nameof(RadarSettings.MaxLiveHpBars),
        nameof(RadarSettings.MetricsRefreshHz),
        nameof(RadarSettings.GpuMetricsRefreshSeconds),
        nameof(RadarSettings.SmoothOverlayMotion),
        nameof(RadarSettings.OverlaySmoothingMs),
        nameof(RadarSettings.ChipSmoothingMs),
        nameof(RadarSettings.PixelSnapLabels),
        nameof(RadarSettings.OverlayVSync),
        nameof(RadarSettings.ShowPerfStats),
        nameof(RadarSettings.ShowFpsOverlay),
    ];

    private static readonly HashSet<string> FlaskNames =
    [
        nameof(RadarSettings.LifeFlaskMode),
        nameof(RadarSettings.LifeThresholdPct),
        nameof(RadarSettings.EsThresholdPct),
        nameof(RadarSettings.ManaThresholdPct),
        nameof(RadarSettings.LifeCooldownMs),
        nameof(RadarSettings.ManaCooldownMs),
        nameof(RadarSettings.LifeKey),
        nameof(RadarSettings.ManaKey),
        nameof(RadarSettings.AutoFlaskToggleHotkey),
    ];

    private static readonly HashSet<string> SystemNames =
    [
        nameof(RadarSettings.HideFromScreenCapture),
        nameof(RadarSettings.NavMenuCorner),
        nameof(RadarSettings.NavTaskbarX),
        nameof(RadarSettings.NavTaskbarY),
        nameof(RadarSettings.UiFontPath),
        nameof(RadarSettings.UiFontSize),
        nameof(RadarSettings.UiFontGlyphRange),
        nameof(RadarSettings.ApiPort),
        nameof(RadarSettings.GameExePath),
        nameof(RadarSettings.InterfaceStyle),
    ];

    private readonly RadarSettings _settings;
    private readonly Action _switchToModern;
    private readonly Action<bool> _visibilityChanged;
    private readonly TreeView _navigation;
    private readonly PropertyGrid _propertyGrid;
    private readonly Panel _pageHost;
    private readonly ClassicRadarDetailsControl? _radarDetails;
    private readonly ClassicSettingsActions? _actions;
    private readonly Label _pageTitle;
    private readonly Label _pageNote;
    private readonly Label _linkLamp;
    private readonly ToolStripStatusLabel _linkStatus;
    private readonly ToolStripStatusLabel _areaStatus;
    private readonly ToolStripStatusLabel _vitalsStatus;
    private readonly ToolStripStatusLabel _pickupStatus;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private bool _allowClose;

    public SettingsForm(
        RadarSettings settings,
        Action switchToModern,
        Action<bool> visibilityChanged,
        DisplayRules? displayRules = null,
        HiddenEntities? hiddenEntities = null,
        ClassicSettingsActions? actions = null)
    {
        _settings = settings;
        _switchToModern = switchToModern;
        _visibilityChanged = visibilityChanged;
        _actions = actions;
        if (displayRules is not null && hiddenEntities is not null)
            _radarDetails = new ClassicRadarDetailsControl(displayRules, hiddenEntities);

        Text = "POE2Radar Settings";
        Font = ClassicUiPalette.UiFont;
        BackColor = SystemColors.Control;
        MinimumSize = new Size(850, 600);
        ClientSize = new Size(980, 690);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        Controls.Add(ClassicUiPalette.CreateHeader(
            "POE2Radar Settings",
            "Live configuration · changes are saved automatically"));

        var footer = new StatusStrip { SizingGrip = true };
        _linkLamp = ClassicUiPalette.CreateLamp();
        var lampHost = new ToolStripControlHost(_linkLamp) { Margin = new Padding(6, 3, 4, 0) };
        footer.Items.Add(lampHost);
        _linkStatus = new ToolStripStatusLabel("POE2 LINK: OFFLINE")
        {
            Font = ClassicUiPalette.SmallBoldFont,
            BorderSides = ToolStripStatusLabelBorderSides.Right,
        };
        _areaStatus = new ToolStripStatusLabel("AREA: —")
        {
            BorderSides = ToolStripStatusLabelBorderSides.Right,
        };
        _vitalsStatus = new ToolStripStatusLabel("HP —  MP —  ES —")
        {
            BorderSides = ToolStripStatusLabelBorderSides.Right,
        };
        _pickupStatus = new ToolStripStatusLabel("Pickup: waiting")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        footer.Items.AddRange([_linkStatus, _areaStatus, _vitalsStatus, _pickupStatus]);
        Controls.Add(footer);

        var commandPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8, 8, 8, 8),
        };
        var closeButton = new Button
        {
            Dock = DockStyle.Right,
            Width = 92,
            Text = "&Close",
        };
        closeButton.Click += (_, _) => HideSettings();
        commandPanel.Controls.Add(closeButton);
        var saveButton = new Button
        {
            Dock = DockStyle.Right,
            Width = 92,
            Margin = new Padding(0, 0, 8, 0),
            Text = "&Save Now",
        };
        saveButton.Click += (_, _) => SaveNow();
        commandPanel.Controls.Add(saveButton);
        var legacyButton = new Button
        {
            Dock = DockStyle.Left,
            Width = 170,
            Text = "Open &Modern Settings",
        };
        legacyButton.Click += (_, _) =>
        {
            SaveNow();
            HideSettings();
            SetInterfaceStyle("Modern");
        };
        commandPanel.Controls.Add(legacyButton);
        commandPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(186, 15),
            ForeColor = SystemColors.GrayText,
            Text = "Switch appearance without changing radar behavior.",
        });
        Controls.Add(commandPanel);

        var body = new SplitContainer
        {
            Size = new Size(950, 570),
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            IsSplitterFixed = false,
            SplitterWidth = 5,
            Panel1MinSize = 170,
            Panel2MinSize = 430,
            SplitterDistance = 218,
            BorderStyle = BorderStyle.Fixed3D,
        };
        body.Panel1.Padding = new Padding(7);
        body.Panel2.Padding = new Padding(8);
        Controls.Add(body);
        body.BringToFront();

        body.Panel1.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Font = ClassicUiPalette.SmallBoldFont,
            Text = "CONTROL PAGES",
            TextAlign = ContentAlignment.MiddleLeft,
        });
        body.Panel1.Controls.Add(CreateAppearanceSelector());
        _navigation = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            HotTracking = false,
            FullRowSelect = true,
            ShowLines = true,
            ShowPlusMinus = true,
            BorderStyle = BorderStyle.Fixed3D,
        };
        _navigation.AfterSelect += (_, args) =>
        {
            if (args.Node is { } node)
                SelectPage(node);
        };
        body.Panel1.Controls.Add(_navigation);
        _navigation.BringToFront();

        var infoPanel = new Panel { Dock = DockStyle.Top, Height = 58 };
        _pageTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font(ClassicUiPalette.UiFont, FontStyle.Bold),
            Text = "Settings",
        };
        _pageNote = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = SystemColors.GrayText,
        };
        infoPanel.Controls.Add(_pageNote);
        infoPanel.Controls.Add(_pageTitle);
        body.Panel2.Controls.Add(infoPanel);

        _pageHost = new Panel { Dock = DockStyle.Fill };
        body.Panel2.Controls.Add(_pageHost);
        _pageHost.BringToFront();

        _propertyGrid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            ToolbarVisible = false,
            HelpVisible = true,
            CommandsVisibleIfAvailable = true,
            PropertySort = PropertySort.CategorizedAlphabetical,
            LineColor = SystemColors.ControlLight,
            ViewBackColor = SystemColors.Window,
            ViewForeColor = SystemColors.WindowText,
        };
        _propertyGrid.PropertyValueChanged += (_, _) => QueueSave();
        _pageHost.Controls.Add(_propertyGrid);
        _propertyGrid.BringToFront();

        _saveTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _saveTimer.Tick += (_, _) => SaveNow();

        PopulatePages();
        FormClosing += OnFormClosing;
        VisibleChanged += (_, _) =>
        {
            _visibilityChanged(Visible);
            if (!Visible)
                SaveNow();
        };
    }

    private GroupBox CreateAppearanceSelector()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            Height = 55,
            Text = " INTERFACE STYLE ",
            Padding = new Padding(8, 4, 8, 4),
        };
        var modern = new RadioButton
        {
            AutoSize = true,
            Location = new Point(12, 22),
            Text = "Modern",
            Checked = string.Equals(_settings.InterfaceStyle, "Modern", StringComparison.OrdinalIgnoreCase),
        };
        var old = new RadioButton
        {
            AutoSize = true,
            Location = new Point(98, 22),
            Text = "Old",
            Checked = !modern.Checked,
        };
        modern.CheckedChanged += (_, _) =>
        {
            if (modern.Checked)
                SetInterfaceStyle("Modern");
        };
        old.CheckedChanged += (_, _) =>
        {
            if (!old.Checked) return;
            _settings.InterfaceStyle = "Old";
            SaveNow();
        };
        group.Controls.Add(modern);
        group.Controls.Add(old);
        return group;
    }

    private void SetInterfaceStyle(string style)
    {
        _settings.InterfaceStyle = style;
        SaveNow();
        if (!string.Equals(style, "Modern", StringComparison.OrdinalIgnoreCase)) return;
        Hide();
        _switchToModern();
    }

    private void PopulatePages()
    {
        _navigation.BeginUpdate();
        _navigation.Nodes.Clear();

        AddPage(null, "All Settings", _settings,
            "Complete top-level configuration surface. Migration bookkeeping is intentionally hidden.",
            property => IsUserSetting(property.Name));

        var overlay = _navigation.Nodes.Add("OVERLAY");
        AddPage(overlay, "Radar", _settings,
            "Map layers, paths, entity visibility, projection, and navigation presentation.",
            property => IsRadarProperty(property.Name));
        if (_radarDetails is not null)
            AddControlPage(
                overlay,
                "Radar Details",
                "Ordered display rules, live entities in the current zone, and Never Show patterns.",
                _radarDetails);
        AddCollectionPage(
            overlay,
            "Radar Lists",
            new { _settings.AutoNavPatterns },
            "Auto-navigation pattern lists.");
        if (_actions is not null)
            AddControlPage(
                overlay,
                "Radar Controls",
                "Immediate radar and navigation commands.",
                new ClassicActionControl(
                    "These commands affect the live overlay immediately.",
                    ("Toggle Rendering", "Show or hide radar content without closing POE2Radar.", _actions.ToggleRendering),
                    ("Add Nearest Route", "Add the nearest eligible navigation target.", _actions.AddNearestRoute),
                    ("Clear Routes", "Remove all currently selected navigation routes.", _actions.ClearRoutes)));
        AddPage(overlay, "Performance", _settings,
            "Read cadence, frame pacing, smoothing, and diagnostics.", property => PerformanceNames.Contains(property.Name));
        AddPage(overlay, "HP Bars", _settings.HpBars,
            "World-space monster life-bar geometry and colors.");
        AddPage(overlay, "Terrain", _settings.Terrain,
            "Walkable terrain fill, edge, and opacity settings.");
        var styles = AddPage(overlay, "Display Styles", _settings.Styles,
            "Category defaults. Use the legacy advanced editor for ordered display rules and icon pickers.");
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(_settings.Styles))
        {
            if (property.GetValue(_settings.Styles) is not IconStyle iconStyle) continue;
            var styleName = FilteredSettingsView.FriendlyName(property.Name);
            AddPage(styles, styleName, iconStyle,
                $"Shape, color, opacity, and size for {styleName.ToLowerInvariant()} markers.");
        }
        AddPage(overlay, "Flasks", _settings,
            "Auto-flask thresholds, cooldowns, and key bindings.", property => FlaskNames.Contains(property.Name));

        var plugins = _navigation.Nodes.Add("PLUGINS");
        var pickup = AddPage(plugins, "Pickup Helper", _settings.PickupHelper,
            "Foreground-gated item pickup assistance and item policy.",
            property => property.Name != nameof(PickupHelperSettings.Policy));
        AddPage(pickup, "Item Policy", _settings.PickupHelper.Policy,
            "Equipment gate plus allow, deny, and ordered priority match patterns.");
        AddCollectionPage(
            pickup,
            "Policy Tables",
            _settings.PickupHelper.Policy,
            "Allow, deny, and ordered priority match tables.");
        var lootTracker = AddPage(plugins, "Loot Tracker", _settings.LootTracker,
            "Session tracking, value display, and detail-window controls.");
        if (_actions is not null)
            AddControlPage(
                lootTracker,
                "Session Control",
                "Start a fresh loot-tracking session.",
                new ClassicActionControl(
                    "Starting a new session resets the active session totals.",
                    ("New Loot Session", "Archive the current run in memory and start fresh totals.", _actions.NewLootSession)));
        AddPage(plugins, "Stash Value", _settings.StashValue,
            "Stash pricing overlay and scan behavior.");
        var stashUtility = AddPage(plugins, "Stash Utility", _settings.StashUtility,
            "Stash highlighting and utility behavior.");
        AddCollectionPage(
            stashUtility,
            "Mod Tables",
            _settings.StashUtility,
            "Waystone and tablet good, great, bad, god, and minimum-roll tables.");
        var crafting = AddPage(plugins, "Crafting Assistant", _settings.WaystoneAlchemy,
            "Opt-in waystone currency automation and safety limits.");
        if (_actions is not null)
            AddControlPage(
                crafting,
                "Crafting Control",
                "Start or stop foreground-gated waystone crafting.",
                new ClassicActionControl(
                    "Automation remains opt-in, foreground-gated, cooldown-limited, and stops on invalid state.",
                    ("Start Crafting", "Begin with the configured currency sequence and safety limits.", _actions.StartWaystoneCrafting),
                    ("STOP Crafting", "Immediately stop the crafting assistant.", _actions.StopWaystoneCrafting)));
        AddPage(plugins, "Amanamu Alert", _settings.Amanamu,
            "Amanamu proximity and cloud-state warnings.");
        AddPage(plugins, "Ritual", _settings.Ritual,
            "Ritual reward pricing, alerts, and overlay layout.");
        var runecraft = AddPage(plugins, "Runecraft", _settings.Runecraft,
            "Runecraft pricing, monolith display, and expedition guidance.");
        AddCollectionPage(
            runecraft,
            "Expedition Tables",
            _settings.Runecraft,
            "Reward weights plus preferred and dangerous relic modifiers.");
        if (_actions is not null)
            AddControlPage(
                runecraft,
                "Expedition Control",
                "Run the current expedition placement planner.",
                new ClassicActionControl(
                    "Planning reads the current encounter and does not send input.",
                    ("Plan Expedition", "Calculate a route using the configured weights and relic policies.", _actions.PlanExpedition)));
        var sekhema = AddPage(plugins, "Sekhema", _settings.Sekhema,
            "Trial path planning, profiles, and debug display.");
        AddCollectionPage(
            sekhema,
            "Profiles & Weights",
            _settings.Sekhema,
            "Profiles, chest priorities, disabled content, and room/reward/affliction weights.");

        var world = _navigation.Nodes.Add("WORLD");
        var atlas = AddPage(world, "Atlas", _settings,
            "Atlas nodes, routes, search, content, and visual presentation.",
            property => property.Name.StartsWith("Atlas", StringComparison.Ordinal));
        AddCollectionPage(
            atlas,
            "Atlas Tables",
            new
            {
                _settings.AtlasHighlightTags,
                _settings.AtlasArrowTags,
                _settings.AtlasHighlightColors,
                _settings.AtlasRitualRewardWeights,
                _settings.AtlasContentGroups,
                _settings.AtlasMapGroups,
                _settings.AtlasRouteGroups,
            },
            "Highlight tags and colors, ritual weights, content groups, map groups, and route entries.");

        var system = _navigation.Nodes.Add("SYSTEM");
        AddPage(system, "Hotkeys", _settings,
            "Win32 virtual-key values. Use the legacy editor when you prefer press-to-bind capture.",
            property => property.Name.EndsWith("Hotkey", StringComparison.Ordinal)
                        || property.Name is nameof(RadarSettings.LifeKey) or nameof(RadarSettings.ManaKey)
                        || property.Name.StartsWith("Gamepad", StringComparison.Ordinal));
        AddPage(system, "Application", _settings,
            "Window capture, API, font, navigation taskbar, and install path.",
            property => SystemNames.Contains(property.Name));

        overlay.Expand();
        plugins.Expand();
        world.Expand();
        system.Expand();
        _navigation.EndUpdate();

        var selected = FindPage(_settings.LastSettingsTab) ?? FindPage("Radar") ?? _navigation.Nodes[0];
        _navigation.SelectedNode = selected;
        selected.EnsureVisible();
    }

    private TreeNode AddPage(
        TreeNode? parent,
        string name,
        object target,
        string note,
        Func<PropertyDescriptor, bool>? include = null)
    {
        var node = parent is null ? _navigation.Nodes.Add(name) : parent.Nodes.Add(name);
        node.Tag = new SettingsPage(name, note, new FilteredSettingsView(target, name, include));
        return node;
    }

    private TreeNode AddControlPage(TreeNode parent, string name, string note, Control content)
    {
        var node = parent.Nodes.Add(name);
        node.Tag = new SettingsPage(name, note, null, content);
        return node;
    }

    private void AddCollectionPage(TreeNode parent, string name, object target, string note)
    {
        var editor = new ClassicCollectionEditorControl(target, name, SaveNow);
        if (editor.CollectionCount > 0)
            AddControlPage(parent, name, note, editor);
        else
            editor.Dispose();
    }

    private TreeNode? FindPage(string name)
    {
        foreach (TreeNode root in _navigation.Nodes)
        {
            var match = FindPage(root, name);
            if (match is not null) return match;
        }
        return null;
    }

    internal bool SelectPageForPreview(string name)
    {
        var node = FindPage(name);
        if (node is null) return false;
        _navigation.SelectedNode = node;
        node.EnsureVisible();
        return true;
    }

    private static TreeNode? FindPage(TreeNode node, string name)
    {
        if (string.Equals(node.Text, name, StringComparison.OrdinalIgnoreCase) && node.Tag is SettingsPage)
            return node;
        foreach (TreeNode child in node.Nodes)
        {
            var match = FindPage(child, name);
            if (match is not null) return match;
        }
        return null;
    }

    private void SelectPage(TreeNode node)
    {
        if (node.Tag is not SettingsPage page)
        {
            if (node.Nodes.Count > 0)
                _navigation.SelectedNode = node.Nodes[0];
            return;
        }

        _pageTitle.Text = page.Name;
        _pageNote.Text = page.Content is null
            ? $"{page.Note} Expand a section; select a setting for an explanation below."
            : page.Note;
        foreach (Control control in _pageHost.Controls)
            control.Visible = false;
        if (page.Content is { } content)
        {
            if (content.Parent != _pageHost)
                _pageHost.Controls.Add(content);
            content.Visible = true;
            content.BringToFront();
        }
        else
        {
            _propertyGrid.SelectedObject = page.View;
            _propertyGrid.Visible = true;
            _propertyGrid.BringToFront();
            ResetPropertyGridSections();
        }
        _settings.LastSettingsTab = page.Name;
        QueueSave();
    }

    private void ResetPropertyGridSections()
    {
        _propertyGrid.CollapseAllGridItems();
        var selected = _propertyGrid.SelectedGridItem;
        if (selected is null) return;
        while (selected.Parent is { } parent)
            selected = parent;

        if (selected.GridItems.Count > 0)
        {
            var first = selected.GridItems[0];
            first.Expanded = true;
            _propertyGrid.SelectedGridItem = first;
        }
    }

    private static bool IsUserSetting(string name)
        => !name.EndsWith("Migrated", StringComparison.Ordinal)
           && name != nameof(RadarSettings.AnyPathLayerEnabled);

    private static bool IsRadarProperty(string name)
    {
        if (!IsUserSetting(name) || PerformanceNames.Contains(name) || FlaskNames.Contains(name)
            || SystemNames.Contains(name) || name.StartsWith("Atlas", StringComparison.Ordinal)
            || name.EndsWith("Hotkey", StringComparison.Ordinal)
            || name.StartsWith("Gamepad", StringComparison.Ordinal))
            return false;

        return name is not nameof(RadarSettings.Ritual)
            and not nameof(RadarSettings.Amanamu)
            and not nameof(RadarSettings.Runecraft)
            and not nameof(RadarSettings.Sekhema)
            and not nameof(RadarSettings.StashValue)
            and not nameof(RadarSettings.StashUtility)
            and not nameof(RadarSettings.WaystoneAlchemy)
            and not nameof(RadarSettings.PickupHelper)
            and not nameof(RadarSettings.LootTracker)
            and not nameof(RadarSettings.Styles)
            and not nameof(RadarSettings.HpBars)
            and not nameof(RadarSettings.Terrain);
    }

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        _settings.Save();
    }

    public void ToggleSettings()
    {
        if (Visible)
        {
            HideSettings();
            return;
        }

        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    private void HideSettings()
    {
        SaveNow();
        Hide();
    }

    public void UpdateStatus(SettingsUiStatus status)
    {
        _linkLamp.BackColor = status.InGame
            ? ClassicUiPalette.LinkOn
            : ClassicUiPalette.LinkWait;
        _linkStatus.Text = status.InGame
            ? $"POE2 LINK: {(status.GameActive ? "ACTIVE" : "BACKGROUND")}"
            : "POE2 LINK: WAITING";
        _areaStatus.Text = $"AREA: {(string.IsNullOrWhiteSpace(status.AreaCode) ? "—" : status.AreaCode)}";
        _vitalsStatus.Text = $"HP {status.HpPct:0}%  MP {status.ManaPct:0}%  ES {status.EsPct:0}%";
        _pickupStatus.Text = $"Render {(status.RenderingEnabled ? "ON" : "OFF")} · Pickup {status.PickupStatus}";
    }

    public void UpdateRenderContext(RenderContext context)
        => _radarDetails?.UpdateContext(context);

    public void RequestClose()
    {
        _allowClose = true;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveNow();
        if (_allowClose
            || e.CloseReason is CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing
                or CloseReason.ApplicationExitCall)
            return;
        e.Cancel = true;
        Hide();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        OverlayNative.ApplyCaptureExclusion(Handle, _settings.HideFromScreenCapture);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _saveTimer.Dispose();
        base.Dispose(disposing);
    }

    private sealed record SettingsPage(
        string Name,
        string Note,
        FilteredSettingsView? View,
        Control? Content = null);
}
