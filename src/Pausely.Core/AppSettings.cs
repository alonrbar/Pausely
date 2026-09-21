namespace Pausely.Core;

public sealed record AppSettings
{
    public double FocusMinutes { get; init; } = 25;
    public double BreakMinutes { get; init; } = 5;
    public double WarningSeconds { get; init; } = 60;
    public double NotificationSeconds { get; init; } = 6;
    public bool AutoStartTimer { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public bool PlaySound { get; init; }
    public string ReminderMessage { get; init; } = "Quick break in {time}";
    public string BreakMessage { get; init; } = "Break not over yet";

    public string? Validate()
    {
        if (!double.IsFinite(FocusMinutes) || FocusMinutes < 0.1 || FocusMinutes > 480)
            return "Focus time must be between 0.1 and 480 minutes.";
        if (!double.IsFinite(BreakMinutes) || BreakMinutes < 0.1 || BreakMinutes > 120)
            return "Break time must be between 0.1 and 120 minutes.";
        if (!double.IsFinite(WarningSeconds) || WarningSeconds < 0 || WarningSeconds >= FocusMinutes * 60)
            return "Reminder lead time must be zero (off), or shorter than the focus time.";
        if (!double.IsFinite(NotificationSeconds) || NotificationSeconds < 1 || NotificationSeconds > 60)
            return "Reminder visibility must be between 1 and 60 seconds.";
        if (string.IsNullOrWhiteSpace(ReminderMessage) || ReminderMessage.Length > 160 ||
            string.IsNullOrWhiteSpace(BreakMessage) || BreakMessage.Length > 160)
            return "Messages must contain between 1 and 160 characters.";
        return null;
    }
}
