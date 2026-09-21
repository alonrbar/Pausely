using System.ComponentModel;
using System.Windows.Media;

namespace Pausely.Views;

public partial class MainWindow : Window
{
    private readonly AppController _controller;
    public MainWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        controller.Timer.Changed += Refresh;
        Closing += OnClosing;
        Refresh();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_controller.IsExiting) { _controller.Timer.Changed -= Refresh; return; }
        e.Cancel = true;
        Hide();
    }

    private void Refresh()
    {
        var timer = _controller.Timer;
        var phase = timer.Phase;
        Countdown.Text = AppController.FormatTime(timer.Remaining);
        PhaseLabel.Text = phase switch
        {
            TimerPhase.Focus => "TIME TO FOCUS", TimerPhase.Paused => "A MOMENT OF FLEXIBILITY",
            TimerPhase.Break => "ROOM TO BREATHE", TimerPhase.Ready => "READY WHEN YOU ARE",
            TimerPhase.Locking => "MAKING SPACE", _ => "FIND YOUR RHYTHM"
        };
        CountdownCaption.Text = phase == TimerPhase.Break ? "left in your break" : phase == TimerPhase.Idle ? "ready to begin" : "until your next pause";
        StatusText.Text = phase switch
        {
            TimerPhase.Focus => "Settle in. We'll take care of the pauses.",
            TimerPhase.Paused => "Your timer is paused. Pick up when you're ready.",
            TimerPhase.Break => "Look away, stretch a little, take a breath.",
            TimerPhase.Locking => "Waiting for Windows to lock your screen…",
            TimerPhase.Ready => "A fresh focus session starts when you return.",
            _ => "A little structure for a gentler working day."
        };
        PrimaryButton.Content = phase switch { TimerPhase.Focus => "Pause timer", TimerPhase.Paused => "Resume focus", TimerPhase.Break => "Override break", _ => "Start focusing" };
        PrimaryButton.IsEnabled = phase is not (TimerPhase.Locking or TimerPhase.Ready);
        BreakButton.IsEnabled = phase is TimerPhase.Focus or TimerPhase.Paused or TimerPhase.Idle;
        ResetButton.IsEnabled = SetTimeButton.IsEnabled = timer.CanAdjustTime;
        Rhythm.Text = $"{timer.Settings.FocusMinutes:g}m focus / {timer.Settings.BreakMinutes:g}m break";
        BreakCount.Text = $"{timer.CompletedBreaks} little reset{(timer.CompletedBreaks == 1 ? "" : "s")}";
        ErrorText.Text = timer.Error;
        ErrorText.Visibility = timer.Error is null ? Visibility.Collapsed : Visibility.Visible;
        var total = timer.IntervalDuration.TotalSeconds;
        var fraction = total > 0 ? Math.Clamp(timer.Remaining.TotalSeconds / total, 0, 0.99999) : 0;
        if (fraction <= 0) { ProgressArc.Data = null; return; }
        var angle = fraction * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(114, 10), IsClosed = false };
        figure.Segments.Add(new ArcSegment(new Point(114 + 104 * Math.Sin(angle), 114 - 104 * Math.Cos(angle)), new Size(104, 104), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        ProgressArc.Data = new PathGeometry([figure]);
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Timer.Phase is TimerPhase.Focus or TimerPhase.Paused) _controller.Timer.PauseOrResume();
        else _controller.Timer.Start();
    }
    private void Break_Click(object sender, RoutedEventArgs e) => _controller.Timer.TakeBreak();
    private void Settings_Click(object sender, RoutedEventArgs e) => _controller.ShowSettings();
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_controller.Timer.CanAdjustTime) _controller.Timer.Reset();
    }
    private void SetTime_Click(object sender, RoutedEventArgs e)
    {
        if (!_controller.Timer.CanAdjustTime) return;
        new SetTimeWindow(_controller.Timer) { Owner = this }.ShowDialog();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
