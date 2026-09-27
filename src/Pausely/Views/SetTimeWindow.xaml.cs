using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;

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
        SecondsInput.Text = "00";
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

    private void MinutesStep_Click(object sender, RoutedEventArgs e)
        => StepInput(MinutesInput, ((FrameworkElement)sender).Tag is "1" ? 1 : -1);

    private void SecondsStep_Click(object sender, RoutedEventArgs e)
        => StepInput(SecondsInput, ((FrameworkElement)sender).Tag is "1" ? 1 : -1);

    private void TimeInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;
        StepInput((TextBox)sender, e.Key == Key.Up ? 1 : -1);
        e.Handled = true;
    }

    private void StepInput(TextBox input, int direction)
    {
        var isMinutes = input == MinutesInput;
        var maximumMinutes = (int)_timer.MaximumRemaining.TotalMinutes;
        var atMaximum = int.TryParse(MinutesInput.Text, out var minutes) && minutes >= maximumMinutes;
        var maximum = isMinutes ? maximumMinutes : atMaximum ? 0 : 59;
        int.TryParse(input.Text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out var value);
        value = Math.Clamp(Math.Clamp(value, 0, maximum) + direction, 0, maximum);
        input.Text = value.ToString(isMinutes ? "0" : "00", CultureInfo.CurrentCulture);
        if (isMinutes && value == maximumMinutes) SecondsInput.Text = "00";
        ValidationText.Text = "";
        input.Focus();
        input.SelectAll();
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
