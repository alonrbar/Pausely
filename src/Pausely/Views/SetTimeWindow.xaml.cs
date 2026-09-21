using System.Globalization;

namespace Pausely.Views;

public partial class SetTimeWindow : Window
{
    private readonly FocusTimer _timer;
    private readonly long _intervalId;
    private bool _applying;

    public SetTimeWindow(FocusTimer timer)
    {
        InitializeComponent();
        _timer = timer;
        _intervalId = timer.IntervalId;
        Topmost = timer.Phase == TimerPhase.Break;
        var seconds = (int)Math.Ceiling(timer.Remaining.TotalSeconds);
        MinutesInput.Text = (seconds / 60).ToString(CultureInfo.CurrentCulture);
        SecondsInput.Text = (seconds % 60).ToString(CultureInfo.CurrentCulture);
        RangeHint.Text = $"Choose 1 second to {timer.MaximumRemaining.TotalMinutes:g} minutes. A paused timer stays paused.";
        Loaded += (_, _) => { MinutesInput.Focus(); MinutesInput.SelectAll(); };
        timer.Changed += OnTimerChanged;
        Closed += (_, _) => timer.Changed -= OnTimerChanged;
    }

    private void OnTimerChanged()
    {
        // Do not apply a stale focus edit to the break that follows it, or vice versa.
        if (!_applying && (_timer.IntervalId != _intervalId || !_timer.CanAdjustTime)) Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MinutesInput.Text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var minutes) ||
            !int.TryParse(SecondsInput.Text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var seconds) || seconds > 59)
        { ValidationText.Text = "Enter whole minutes and seconds (0–59)."; return; }
        var duration = TimeSpan.FromSeconds((double)minutes * 60 + seconds);
        if (duration < TimeSpan.FromSeconds(1) || duration > _timer.MaximumRemaining)
        { ValidationText.Text = $"Choose between 1 second and {_timer.MaximumRemaining.TotalMinutes:g} minutes."; return; }
        if (_timer.IntervalId != _intervalId || !_timer.CanAdjustTime) { Close(); return; }
        _applying = true;
        _timer.SetRemaining(duration);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
