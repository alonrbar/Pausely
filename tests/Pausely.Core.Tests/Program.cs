using Pausely.Core;

var tests = new (string Name, Action Test)[]
{
    ("Defaults are valid", () => Equal<string?>(null, new AppSettings().Validate())),
    ("Rejects non-finite and out-of-range settings", () =>
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1, 0, 481 })
            True((new AppSettings { FocusMinutes = value }).Validate() is not null);
        True((new AppSettings { BreakMinutes = 0 }).Validate() is not null);
        True((new AppSettings { WarningSeconds = 1500 }).Validate() is not null);
        True((new AppSettings { NotificationSeconds = 0 }).Validate() is not null);
        True((new AppSettings { BreakMessage = null! }).Validate() is not null);
    }),
    ("One reminder at configured threshold", () =>
    {
        var (timer, clock) = Create(); var count = 0; timer.ReminderRequested += () => count++;
        timer.Start(); clock.Advance(1439); timer.Tick(); Equal(0, count);
        clock.Advance(1); timer.Tick(); timer.Tick(); Equal(1, count);
    }),
    ("Reminder can be disabled", () =>
    {
        var (timer, clock) = Create(new AppSettings { WarningSeconds = 0 }); var count = 0;
        timer.ReminderRequested += () => count++; timer.Start(); clock.Advance(1499); timer.Tick(); Equal(0, count);
    }),
    ("Lock is requested once and confirmed before break begins", () =>
    {
        var (timer, clock) = Create(); var count = 0; timer.LockRequested += () => count++;
        timer.Start(); clock.Advance(1500); timer.Tick(); timer.Tick(); Equal(1, count); Equal(TimerPhase.Locking, timer.Phase);
        timer.SetAway(true); Equal(TimerPhase.Break, timer.Phase); Equal(300d, timer.Remaining.TotalSeconds);
    }),
    ("Failed native lock pauses with error", () =>
    {
        var (timer, _) = Create(); timer.Start(); timer.TakeBreak(); timer.LockFailed("Denied");
        Equal(TimerPhase.Paused, timer.Phase); Equal("Denied", timer.Error);
    }),
    ("Unconfirmed asynchronous lock times out", () =>
    {
        var (timer, clock) = Create(); timer.Start(); timer.TakeBreak(); clock.Advance(8); timer.Tick();
        Equal(TimerPhase.Paused, timer.Phase); True(timer.Error is not null);
    }),
    ("Early unlock retains remaining break", () =>
    {
        var (timer, clock) = Create(); timer.Start(); timer.TakeBreak(); timer.SetAway(true); clock.Advance(70); timer.SetAway(false);
        Equal(TimerPhase.Break, timer.Phase); Equal(230d, timer.Remaining.TotalSeconds);
    }),
    ("Override starts fresh focus without counting completed break", () =>
    {
        var (timer, clock) = Create(); timer.TakeBreak(); timer.SetAway(true); clock.Advance(30); timer.SetAway(false); timer.Start();
        Equal(TimerPhase.Focus, timer.Phase); Equal(1500d, timer.Remaining.TotalSeconds); Equal(0, timer.CompletedBreaks);
    }),
    ("Break completion while locked waits for unlock", () =>
    {
        var (timer, clock) = Create(); timer.TakeBreak(); timer.SetAway(true); clock.Advance(600); timer.Tick();
        Equal(TimerPhase.Ready, timer.Phase); Equal(1, timer.CompletedBreaks); clock.Advance(900); timer.Tick();
        Equal(1, timer.CompletedBreaks); timer.SetAway(false); Equal(TimerPhase.Focus, timer.Phase); Equal(1500d, timer.Remaining.TotalSeconds);
    }),
    ("Unlock after break without intermediate ticks starts focus", () =>
    {
        var (timer, clock) = Create(); timer.TakeBreak(); timer.SetAway(true); clock.Advance(301); timer.SetAway(false);
        Equal(TimerPhase.Focus, timer.Phase); Equal(1, timer.CompletedBreaks);
    }),
    ("Break completes while early-unlock prompt is visible", () =>
    {
        var (timer, clock) = Create(); timer.TakeBreak(); timer.SetAway(true); clock.Advance(10); timer.SetAway(false);
        clock.Advance(290); timer.Tick(); Equal(TimerPhase.Focus, timer.Phase); Equal(1, timer.CompletedBreaks);
    }),
    ("Relocking preserves break deadline", () =>
    {
        var (timer, clock) = Create(); timer.TakeBreak(); timer.SetAway(true); clock.Advance(60); timer.SetAway(false);
        clock.Advance(20); timer.SetAway(true); Equal(220d, timer.Remaining.TotalSeconds);
    }),
    ("Short manual lock freezes focus", () =>
    {
        var (timer, clock) = Create(); timer.Start(); clock.Advance(100); timer.SetAway(true);
        clock.Advance(60); timer.Tick(); timer.SetAway(false); Equal(1400d, timer.Remaining.TotalSeconds);
    }),
    ("Long manual lock or sleep resets focus", () =>
    {
        var (timer, clock) = Create(); timer.Start(); clock.Advance(1400); timer.SetAway(true);
        clock.Advance(300); timer.Tick(); timer.SetAway(false); Equal(1500d, timer.Remaining.TotalSeconds);
    }),
    ("Pause preserves time and resume continues", () =>
    {
        var (timer, clock) = Create(); timer.Start(); clock.Advance(300); timer.PauseOrResume(); clock.Advance(900);
        Equal(1200d, timer.Remaining.TotalSeconds); timer.PauseOrResume(); clock.Advance(1); Equal(1199d, timer.Remaining.TotalSeconds);
    }),
    ("Manual lock does not unpause user pause", () =>
    {
        var (timer, clock) = Create(); timer.Start(); timer.PauseOrResume(); timer.SetAway(true); clock.Advance(600); timer.SetAway(false);
        Equal(TimerPhase.Paused, timer.Phase);
    }),
    ("Settings preserve current focus and apply next interval", () =>
    {
        var (timer, clock) = Create(); timer.Start(); clock.Advance(100); timer.Configure(new AppSettings { FocusMinutes = 40 });
        Equal(1400d, timer.Remaining.TotalSeconds); timer.Start(); Equal(2400d, timer.Remaining.TotalSeconds);
    }),
    ("Settings preserve current break", () =>
    {
        var (timer, _) = Create(); timer.TakeBreak(); timer.SetAway(true); timer.Configure(new AppSettings { BreakMinutes = 10 });
        Equal(300d, timer.Remaining.TotalSeconds);
    }),
    ("Duplicate away events do not restart absence or break", () =>
    {
        var (timer, clock) = Create(); timer.TakeBreak(); timer.SetAway(true); clock.Advance(100); timer.SetAway(true);
        Equal(200d, timer.Remaining.TotalSeconds);
    }),
    ("Stop cancels pending lock timeout and reminders", () =>
    {
        var (timer, clock) = Create(); timer.Start(); timer.TakeBreak(); timer.Stop(); clock.Advance(1600); timer.Tick();
        Equal(TimerPhase.Idle, timer.Phase); Equal<string?>(null, timer.Error);
    }),
    ("Starting while away does not consume focus time", () =>
    {
        var (timer, clock) = Create(); timer.SetAway(true); timer.Start(); clock.Advance(60); timer.Tick();
        Equal(1500d, timer.Remaining.TotalSeconds); timer.SetAway(false); Equal(1500d, timer.Remaining.TotalSeconds);
    }),
    ("One-off focus time leaves settings and next focus unchanged", () =>
    {
        var (timer, clock) = Create(); var original = timer.Settings;
        timer.Start(); clock.Advance(100); timer.SetRemaining(TimeSpan.FromMinutes(7));
        Equal(420d, timer.Remaining.TotalSeconds); Equal(420d, timer.IntervalDuration.TotalSeconds);
        Equal(TimerPhase.Focus, timer.Phase); Equal(original, timer.Settings);
        clock.Advance(420); timer.Tick(); Equal(TimerPhase.Locking, timer.Phase);
        timer.SetAway(true); clock.Advance(300); timer.SetAway(false);
        Equal(1500d, timer.Remaining.TotalSeconds); Equal(original, timer.Settings);
    }),
    ("Reset restores configured focus after a one-off time", () =>
    {
        var (timer, clock) = Create(); timer.Start(); timer.SetRemaining(TimeSpan.FromMinutes(40));
        clock.Advance(120); timer.Reset(); Equal(1500d, timer.Remaining.TotalSeconds); Equal(0, timer.CompletedBreaks);
        Equal(TimerPhase.Focus, timer.Phase);
    }),
    ("Adjust and reset preserve deliberate pause", () =>
    {
        var (timer, clock) = Create(); timer.Start(); timer.PauseOrResume(); timer.SetRemaining(TimeSpan.FromSeconds(95));
        clock.Advance(600); Equal(95d, timer.Remaining.TotalSeconds); Equal(TimerPhase.Paused, timer.Phase);
        timer.Reset(); clock.Advance(60); Equal(1500d, timer.Remaining.TotalSeconds); Equal(TimerPhase.Paused, timer.Phase);
        timer.PauseOrResume(); clock.Advance(1); Equal(1499d, timer.Remaining.TotalSeconds);
    }),
    ("One-off break time completes normally without changing settings", () =>
    {
        var (timer, clock) = Create(); var original = timer.Settings;
        timer.TakeBreak(); timer.SetAway(true); clock.Advance(1); timer.SetAway(false);
        timer.SetRemaining(TimeSpan.FromSeconds(45)); Equal(TimerPhase.Break, timer.Phase); Equal(original, timer.Settings);
        clock.Advance(45); timer.Tick(); Equal(TimerPhase.Focus, timer.Phase); Equal(1, timer.CompletedBreaks);
    }),
    ("Reset restores configured break without completing it", () =>
    {
        var (timer, clock) = Create(); timer.TakeBreak(); timer.SetAway(true); clock.Advance(10); timer.SetAway(false);
        timer.SetRemaining(TimeSpan.FromSeconds(20)); timer.Reset(); Equal(300d, timer.Remaining.TotalSeconds);
        Equal(TimerPhase.Break, timer.Phase); Equal(0, timer.CompletedBreaks);
    }),
    ("Adjustment rearms reminders and replaces the old deadline", () =>
    {
        var (timer, clock) = Create(); var warnings = 0; var locks = 0;
        timer.ReminderRequested += () => warnings++; timer.LockRequested += () => locks++;
        timer.Start(); clock.Advance(1440); timer.Tick(); Equal(1, warnings);
        timer.SetRemaining(TimeSpan.FromMinutes(3)); clock.Advance(60); timer.Tick(); Equal(0, locks); Equal(1, warnings);
        clock.Advance(60); timer.Tick(); timer.Tick(); Equal(2, warnings);
    }),
    ("Invalid time does not mutate the current interval", () =>
    {
        var (timer, _) = Create(); timer.Start(); var interval = timer.IntervalId;
        foreach (var value in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromMilliseconds(500), TimeSpan.FromMinutes(481) })
        {
            Throws<ArgumentOutOfRangeException>(() => timer.SetRemaining(value));
            Equal(1500d, timer.Remaining.TotalSeconds); Equal(interval, timer.IntervalId);
        }
    }),
    ("Inactive, locking, and away timers reject adjustments", () =>
    {
        var (timer, _) = Create(); True(!timer.CanAdjustTime);
        Throws<InvalidOperationException>(() => timer.Reset());
        timer.Start(); timer.TakeBreak(); True(!timer.CanAdjustTime);
        Throws<InvalidOperationException>(() => timer.SetRemaining(TimeSpan.FromMinutes(2)));
        timer.SetAway(true); True(!timer.CanAdjustTime);
        Throws<InvalidOperationException>(() => timer.Reset());
    }),
    ("Interval identity changes on reset, adjustment and transition", () =>
    {
        var (timer, _) = Create(); timer.Start(); var id = timer.IntervalId;
        timer.Reset(); True(timer.IntervalId != id); id = timer.IntervalId;
        timer.SetRemaining(TimeSpan.FromMinutes(2)); True(timer.IntervalId != id); id = timer.IntervalId;
        timer.TakeBreak(); timer.SetAway(true); True(timer.IntervalId != id);
    }),
    ("New cycle rearms reminder", () =>
    {
        var (timer, clock) = Create(); var count = 0; timer.ReminderRequested += () => count++;
        timer.Start(); clock.Advance(1440); timer.Tick(); timer.Start(); clock.Advance(1440); timer.Tick(); Equal(2, count);
    })
};
var failures = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static (FocusTimer, FakeClock) Create(AppSettings? settings = null)
{
    var clock = new FakeClock();
    return (new FocusTimer(clock, settings ?? new AppSettings()), clock);
}
static void True(bool value) { if (!value) throw new Exception("Expected true."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
}
sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(double seconds) => _now = _now.AddSeconds(seconds);
}
