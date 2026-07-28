using POE2Radar.Overlay.Config;
using POE2Radar.Overlay.Web;

namespace POE2Radar.Overlay.UI;

/// <summary>Owns the modeless WinForms settings message loop on a dedicated STA thread.</summary>
internal sealed class SettingsUiHost : IDisposable
{
    private readonly RadarSettings _settings;
    private readonly Action _switchToModern;
    private readonly DisplayRules _displayRules;
    private readonly HiddenEntities _hiddenEntities;
    private readonly ClassicSettingsActions _actions;
    private readonly ManualResetEventSlim _started = new(false);
    private Thread? _thread;
    private volatile SettingsForm? _form;
    private Exception? _startupError;
    private long _lastStatusTick;
    private int _isOpen;
    private int _disposed;

    public SettingsUiHost(
        RadarSettings settings,
        Action switchToModern,
        DisplayRules displayRules,
        HiddenEntities hiddenEntities,
        ClassicSettingsActions actions)
    {
        _settings = settings;
        _switchToModern = switchToModern;
        _displayRules = displayRules;
        _hiddenEntities = hiddenEntities;
        _actions = actions;
    }

    public bool IsOpen => Volatile.Read(ref _isOpen) != 0;

    public void Start()
    {
        if (_thread is not null) return;

        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "WinFormsSettings",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("WinForms settings UI did not initialize within five seconds.");
        if (_startupError is { } error)
            throw new InvalidOperationException("WinForms settings UI failed to initialize.", error);
    }

    private void RunMessageLoop()
    {
        try
        {
            using var form = new SettingsForm(
                _settings,
                _switchToModern,
                visible => Volatile.Write(ref _isOpen, visible ? 1 : 0),
                _displayRules,
                _hiddenEntities,
                _actions);
            using var context = new ApplicationContext();
            form.FormClosed += (_, _) => context.ExitThread();
            _form = form;
            _ = form.Handle;
            _started.Set();
            Application.Run(context);
        }
        catch (Exception ex)
        {
            _startupError = ex;
        }
        finally
        {
            Volatile.Write(ref _isOpen, 0);
            _form = null;
            _started.Set();
        }
    }

    public void Toggle()
    {
        var form = _form;
        if (form is null || form.IsDisposed) return;
        try
        {
            form.BeginInvoke(form.ToggleSettings);
        }
        catch (InvalidOperationException)
        {
            // Window is closing during shutdown.
        }
    }

    public void Show()
    {
        if (IsOpen) return;
        Toggle();
    }

    public void Hide()
    {
        if (!IsOpen) return;
        Toggle();
    }

    public void UpdateStatus(RenderContext context, bool renderingEnabled)
    {
        var now = Environment.TickCount64;
        var previous = Interlocked.Read(ref _lastStatusTick);
        if (now - previous < 250 || Interlocked.CompareExchange(ref _lastStatusTick, now, previous) != previous)
            return;

        var status = new SettingsUiStatus(
            context.InGame,
            context.Active,
            renderingEnabled,
            context.AreaCode,
            context.HpPct,
            context.ManaPct,
            context.EsPct,
            context.PickupStatus);

        var form = _form;
        if (form is null || form.IsDisposed) return;
        try
        {
            form.BeginInvoke(() =>
            {
                form.UpdateStatus(status);
                form.UpdateRenderContext(context);
            });
        }
        catch (InvalidOperationException)
        {
            // Window is closing during shutdown.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var form = _form;
        if (form is not null && !form.IsDisposed)
        {
            try { form.BeginInvoke(form.RequestClose); }
            catch (InvalidOperationException) { }
        }
        try { _thread?.Join(1500); } catch { }
        _started.Dispose();
    }
}
