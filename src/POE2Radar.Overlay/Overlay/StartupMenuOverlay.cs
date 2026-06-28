using System.Numerics;
using System.Threading.Tasks;
using ImGuiNET;
using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Native;
using POE2Radar.Overlay.Settings;

namespace POE2Radar.Overlay;

/// <summary>Launcher — find PoE2 on disk, browse if needed, start the game, then attach the overlay.</summary>
internal sealed class StartupMenuOverlay : ClickableTransparentOverlay.Overlay
{
    private readonly RadarSettings _settings;
    private readonly object _boundsLock = new();
    private System.Drawing.Point _position;
    private System.Drawing.Size _size;
    private bool _boundsReady;

    private AttachResult? _attach;
    private AttachResult? _started;
    private DateTime _nextProbeUtc = DateTime.MinValue;
    private bool _closeRequested;

    private GameInstallLocator.LocateResult _install;
    private string? _launchError;
    private string? _launchNote;

    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1.5);

    public bool Started { get; private set; }
    public AttachResult? Result => _started;

    public StartupMenuOverlay(RadarSettings settings)
        : base("POE2Radar Startup", true, 3840, 2160)
    {
        _settings = settings;
        VSync = true;
        _install = GameInstallLocator.Discover(_settings.GameExePath);
    }

    protected override Task PostInitialized()
    {
        ImGuiTheme.Apply();
        OverlayFonts.Apply(this, _settings);

        lock (_boundsLock)
        {
            _position = System.Drawing.Point.Empty;
            _size = new System.Drawing.Size(
                OverlayNative.GetSystemMetrics(OverlayNative.SM_CXSCREEN),
                OverlayNative.GetSystemMetrics(OverlayNative.SM_CYSCREEN));
            _boundsReady = true;
        }

        RememberDiscoveredPath();
        ProbeNow();
        return base.PostInitialized();
    }

    protected override void Render()
    {
        if (_closeRequested) { Close(); return; }

        if (_boundsReady)
        {
            lock (_boundsLock)
            {
                Position = _position;
                Size = _size;
            }
        }

        MaybeAutoProbe();

        var io = ImGui.GetIO();
        const float menuW = 620f;

        ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(menuW, 0f), ImGuiCond.Always);

        var open = true;
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;

        if (!ImGui.Begin("POE2Radar", ref open, flags))
        {
            ImGui.End();
            return;
        }

        if (!open)
            RequestQuit();

        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Accent);
        ImGui.TextUnformatted("POE2Radar — map/radar overlay");
        ImGui.PopStyleColor();
        ImGui.TextDisabled(new string('=', 29));
        ImGui.Spacing();

        DrawInstallSection();
        DrawAttachSection();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var hasGame = _install.Found;
        if (!hasGame) ImGui.BeginDisabled();
        if (ImGui.Button("Start Game", new Vector2(UiW(11f), 0f)))
            LaunchGame();
        ImGuiTheme.Tooltip(SettingHints.Startup.StartGame);
        if (!hasGame) ImGui.EndDisabled();

        ImGui.SameLine();
        var canRadar = _attach?.CanStart == true;
        if (!canRadar) ImGui.BeginDisabled();
        if (ImGui.Button("Start Radar", new Vector2(UiW(11f), 0f)))
            StartRadar();
        ImGuiTheme.Tooltip(SettingHints.Startup.StartRadar);
        if (!canRadar) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Quit", new Vector2(UiW(8f), 0f)))
            RequestQuit();
        ImGuiTheme.Tooltip(SettingHints.Startup.Quit);

        ImGui.End();
    }

    private void DrawInstallSection()
    {
        ImGuiTheme.SectionHeader("Game install");

        if (_install.Found)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.25f, 0.92f, 0.45f, 1f));
            ImGui.TextWrapped("Found Path of Exile 2");
            ImGui.PopStyleColor();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
            ImGui.TextWrapped(_install.Path!);
            ImGui.TextWrapped(_install.Message);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.78f, 0.20f, 1f));
            ImGui.TextWrapped("Path of Exile 2 not found on this PC.");
            ImGui.PopStyleColor();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
            ImGui.TextWrapped(_install.Message);
            ImGui.PopStyleColor();
        }

        if (!string.IsNullOrEmpty(_launchNote))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Accent);
            ImGui.TextWrapped(_launchNote);
            ImGui.PopStyleColor();
        }

        if (!string.IsNullOrEmpty(_launchError))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.35f, 0.35f, 1f));
            ImGui.TextWrapped(_launchError);
            ImGui.PopStyleColor();
        }

        if (ImGui.Button("Browse…", new Vector2(UiW(10f), 0f)))
            BrowseForGame();
        ImGuiTheme.Tooltip(SettingHints.Startup.BrowseGame);
    }

    private void DrawAttachSection()
    {
        ImGui.Spacing();
        ImGuiTheme.SectionHeader("Overlay attach");

        var attach = _attach;
        if (attach is null)
        {
            ImGui.TextWrapped("Checking for a running game…");
            return;
        }

        ImGui.TextWrapped(attach.StatusTitle);
        if (!string.IsNullOrEmpty(attach.StatusDetail))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
            ImGui.TextWrapped(attach.StatusDetail);
            ImGui.PopStyleColor();
        }

        if (attach.Status == AttachStatus.NotInZone)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.78f, 0.20f, 1f));
            ImGui.TextWrapped("Game is running — load into a zone, then Start Radar (or wait for auto-start).");
            ImGui.PopStyleColor();
        }

        if (attach.CanStart)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.25f, 0.92f, 0.45f, 1f));
            ImGui.TextWrapped("Ready — click Start Radar or wait a moment for auto-start.");
            ImGui.PopStyleColor();
        }
    }

    private void RememberDiscoveredPath()
    {
        if (!_install.Found || _install.Path is null) return;
        if (string.Equals(_settings.GameExePath, _install.Path, StringComparison.OrdinalIgnoreCase)) return;
        GameInstallLocator.SavePath(_settings, _install.Path);
    }

    private void BrowseForGame()
    {
        var initial = _install.Found ? Path.GetDirectoryName(_install.Path) : null;
        var picked = GameExeBrowseDialog.PickExistingExe(initial);
        if (picked is null) return;

        if (!GameInstallLocator.IsValidGameExe(picked))
        {
            _launchError = "That file does not look like a PoE2 client. Pick PathOfExile.exe or PathOfExileSteam.exe.";
            return;
        }

        _install = new GameInstallLocator.LocateResult(
            Path.GetFullPath(picked),
            GameInstallLocator.LocateSource.Saved,
            "Using path you selected.");
        GameInstallLocator.SavePath(_settings, _install.Path!);
        _launchError = null;
        _launchNote = null;
    }

    private void LaunchGame()
    {
        if (!_install.Found || _install.Path is null)
        {
            _launchError = "Browse to the game executable first.";
            return;
        }

        if (!GameInstallLocator.TryLaunch(_install.Path, out var error))
        {
            _launchError = error;
            return;
        }

        _launchError = null;
        _launchNote = "Launching Path of Exile 2… load into a zone, then Start Radar.";
        _nextProbeUtc = DateTime.MinValue;
        ProbeNow();
    }

    private void MaybeAutoProbe()
    {
        if (_attach?.CanStart == true) return;
        if (DateTime.UtcNow < _nextProbeUtc) return;
        ProbeNow();
    }

    private void ProbeNow()
    {
        _nextProbeUtc = DateTime.UtcNow + ProbeInterval;
        _attach?.Dispose();
        _attach = AttachResult.Probe();
        if (_attach.CanStart)
            StartRadar();
    }

    private void StartRadar()
    {
        if (_attach?.CanStart != true) return;

        _started = _attach;
        _attach = null;
        Started = true;
        _closeRequested = true;
    }

    private void RequestQuit() => _closeRequested = true;

    private static float UiW(float factor) => ImGuiTheme.ControlWidth(factor);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _attach?.Dispose();
        base.Dispose(disposing);
    }
}
