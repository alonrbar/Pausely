using Pausely.Services;
using Pausely.Views;
using System.Drawing;
using System.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Pausely;

public sealed class AppController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly bool _preview;
    private readonly DispatcherTimer _pulse;
    private readonly WindowsSession? _session;
    private readonly Forms.NotifyIcon? _tray;
    private readonly Icon? _icon;
    private Forms.ToolStripMenuItem? _statusItem;
    private Forms.ToolStripMenuItem? _pauseItem;
    private Forms.ToolStripMenuItem? _breakItem;
    private Forms.ToolStripMenuItem? _overrideItem;
    private MainWindow? _main;
    private SettingsWindow? _settings;
    private ReminderWindow? _reminder;
    private BreakWindow? _break;
    private bool _suspended;
    private int _syncTicks;
    public FocusTimer Timer { get; }
    public bool IsExiting { get; private set; }

    public AppController(SettingsStore store, bool preview = false, TimeProvider? clock = null)
    {
        _store = store;
        _preview = preview;
        Timer = new FocusTimer(clock ?? TimeProvider.System, preview ? new AppSettings() : store.Load());
        _pulse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pulse.Tick += Pulse;
        if (!preview)
        {
            _session = new WindowsSession();
            _session.AwayChanged += OnAwayChanged;
            _session.SuspensionChanged += OnSuspensionChanged;
            Timer.SetAway(WindowsSession.IsAway());
            using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Pausely.ico"))!.Stream;
            _icon = new Icon(resource);
            _tray = new Forms.NotifyIcon { Icon = _icon, Text = "Pausely · Ready when you are", Visible = true };
            _tray.DoubleClick += (_, _) => ShowMain();
            _tray.ContextMenuStrip = BuildTrayMenu();
        }
        Timer.LockRequested += RequestLock;
        Timer.ReminderRequested += OnReminder;
        Timer.Changed += Refresh;
    }

    public void Run(bool background)
    {
        if (Timer.Settings.AutoStartTimer) Timer.Start();
        if (!_preview) _pulse.Start();
        if (!background) ShowMain();
        if (_store.LoadWarning is { } warning) MessageBox.Show(warning, "Pausely · Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public MainWindow ShowMain()
    {
        _main ??= new MainWindow(this);
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
        return _main;
    }

    public void ShowSettings()
    {
        if (_settings is not null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(this);
        if (_main?.IsVisible == true) _settings.Owner = _main;
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    public void SaveSettings(AppSettings settings)
    {
        if (!_preview)
        {
            // Restore the old startup choice if writing settings fails.
            StartupRegistration.Apply(settings.StartWithWindows);
            try { _store.Save(settings); }
            catch
            {
                try { StartupRegistration.Apply(Timer.Settings.StartWithWindows); } catch (Exception ex) { App.Log(ex); }
                throw;
            }
        }
        Timer.Configure(settings);
    }

    public void ShowReminder(AppSettings settings, TimeSpan remaining)
    {
        _reminder?.Close();
        var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        var time = seconds is >= 59 and <= 60 ? "one minute" : seconds % 60 == 0 ? $"{seconds / 60} minutes" : $"{seconds} seconds";
        var window = new ReminderWindow(settings.ReminderMessage.Replace("{time}", time, StringComparison.OrdinalIgnoreCase), settings.NotificationSeconds);
        _reminder = window;
        window.Closed += (_, _) => { if (_reminder == window) _reminder = null; };
        window.Show();
        if (settings.PlaySound && !_preview) SystemSounds.Asterisk.Play();
    }

    public void LockAgain() => RequestLock();
    private void RequestLock()
    {
        if (_preview) return;
        if (WindowsSession.TryLock() is { } error)
        {
            Timer.LockFailed(error);
            _tray?.ShowBalloonTip(6000, "Pausely couldn't lock Windows", error, Forms.ToolTipIcon.Warning);
        }
    }
    private void OnReminder() => ShowReminder(Timer.Settings, Timer.Remaining);
    private void OnAwayChanged(bool away) => Timer.SetAway(away || _suspended);
    private void OnSuspensionChanged(bool suspended)
    {
        if (IsExiting) return;
        _suspended = suspended;
        Timer.SetAway(suspended || WindowsSession.IsAway());
        Timer.Tick();
    }
    private void Pulse(object? sender, EventArgs e)
    {
        // Reconcile missed session events (including sign-in startup and resume).
        if (++_syncTicks >= 8) { _syncTicks = 0; Timer.SetAway(_suspended || WindowsSession.IsAway()); }
        Timer.Tick();
    }

    private void Refresh()
    {
        if (IsExiting) return;
        if (Timer.Phase != TimerPhase.Focus || Timer.IsAway) _reminder?.Close();
        if (Timer.Phase == TimerPhase.Break && !Timer.IsAway)
        {
            if (_break is null) { _break = new BreakWindow(this); _break.Show(); _break.Activate(); }
            _break.Refresh();
        }
        else if (_break is not null) { var window = _break; _break = null; window.Dismiss(); }
        var label = $"{Timer.Phase} · {FormatTime(Timer.Remaining)}";
        if (_tray is not null) _tray.Text = $"Pausely · {label}";
        if (_statusItem is not null) _statusItem.Text = label;
        if (_pauseItem is not null)
        {
            _pauseItem.Text = Timer.Phase == TimerPhase.Paused ? "Resume focus" : Timer.Phase == TimerPhase.Idle ? "Start focusing" : "Pause timer";
            _pauseItem.Enabled = Timer.Phase is TimerPhase.Focus or TimerPhase.Paused or TimerPhase.Idle;
        }
        if (_breakItem is not null) _breakItem.Enabled = Timer.Phase is TimerPhase.Focus or TimerPhase.Paused or TimerPhase.Idle;
        if (_overrideItem is not null) _overrideItem.Visible = Timer.Phase == TimerPhase.Break;
    }
    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        _statusItem = new Forms.ToolStripMenuItem("Ready when you are") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add("Open Pausely", null, (_, _) => ShowMain());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _pauseItem = new Forms.ToolStripMenuItem("Pause timer", null, (_, _) => { if (Timer.Phase == TimerPhase.Idle) Timer.Start(); else Timer.PauseOrResume(); });
        menu.Items.Add(_pauseItem);
        _breakItem = new Forms.ToolStripMenuItem("Take a break now", null, (_, _) => Timer.TakeBreak());
        menu.Items.Add(_breakItem);
        _overrideItem = new Forms.ToolStripMenuItem("Override break", null, (_, _) => Timer.Start()) { Visible = false };
        menu.Items.Add(_overrideItem);
        menu.Items.Add("Stop timer", null, (_, _) => Timer.Stop());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Quit Pausely", null, (_, _) => Application.Current.Shutdown());
        return menu;
    }
    public static string FormatTime(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
    public void Dispose()
    {
        IsExiting = true;
        _pulse.Stop();
        _pulse.Tick -= Pulse;
        _session?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _icon?.Dispose();
        Timer.Changed -= Refresh;
        Timer.LockRequested -= RequestLock;
        Timer.ReminderRequested -= OnReminder;
        _reminder?.Close();
        _break?.Dismiss();
        _settings?.Close();
        _main?.Close();
    }
}
