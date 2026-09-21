using System.Globalization;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Pausely.Views;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly AppSettings _savedSettings;
    public SettingsWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        _savedSettings = controller.Timer.Settings;
        Populate(_savedSettings);
        AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateSaveState()));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => UpdateSaveState()));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => UpdateSaveState()));
        UpdateSaveState();
    }
    private void Populate(AppSettings s)
    {
        FocusInput.Text = s.FocusMinutes.ToString(CultureInfo.CurrentCulture);
        BreakInput.Text = s.BreakMinutes.ToString(CultureInfo.CurrentCulture);
        WarningInput.Text = s.WarningSeconds.ToString(CultureInfo.CurrentCulture);
        DurationInput.Text = s.NotificationSeconds.ToString(CultureInfo.CurrentCulture);
        ReminderInput.Text = s.ReminderMessage;
        BreakMessageInput.Text = s.BreakMessage;
        AutoStartInput.IsChecked = s.AutoStartTimer;
        StartupInput.IsChecked = s.StartWithWindows;
        SoundInput.IsChecked = s.PlaySound;
    }
    private void UpdateSaveState() => SaveButton.IsEnabled = Read(showValidation: false) != _savedSettings;

    private AppSettings? Read(bool showValidation = true)
    {
        if (!double.TryParse(FocusInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var focus) ||
            !double.TryParse(BreakInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var rest) ||
            !double.TryParse(WarningInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var warning) ||
            !double.TryParse(DurationInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var duration))
        {
            if (showValidation) ValidationText.Text = "Please enter a number in each timing field.";
            return null;
        }
        var value = new AppSettings
        {
            FocusMinutes = focus, BreakMinutes = rest, WarningSeconds = warning, NotificationSeconds = duration,
            ReminderMessage = ReminderInput.Text.Trim(), BreakMessage = BreakMessageInput.Text.Trim(),
            AutoStartTimer = AutoStartInput.IsChecked == true, StartWithWindows = StartupInput.IsChecked == true, PlaySound = SoundInput.IsChecked == true
        };
        if (showValidation) ValidationText.Text = value.Validate() ?? "";
        return value.Validate() is null ? value : null;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveButton.IsEnabled) return;
        if (Read() is not { } settings) return;
        try { _controller.SaveSettings(settings); Close(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        { ValidationText.Text = $"Couldn't save settings: {ex.Message}"; }
    }
    private void Preview_Click(object sender, RoutedEventArgs e) { if (Read() is { } settings) _controller.ShowReminder(settings, TimeSpan.FromSeconds(settings.WarningSeconds)); }
    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
            "Are you sure?\n\nThis replaces your current edits with Pausely's default settings. Nothing is saved until you choose Save Settings.",
            "Reset to Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Populate(new AppSettings());
        UpdateSaveState();
        ValidationText.Text = SaveButton.IsEnabled ? "Defaults restored. Save Settings to apply." : "Default settings are already saved.";
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
