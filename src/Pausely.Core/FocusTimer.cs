namespace Pausely.Core;

public enum TimerPhase { Idle, Focus, Paused, Locking, Break, Ready }

public enum ActivityKind { Focus, Break }

/// <summary>Time actually spent focusing (counting down, not paused or away) or on a break.</summary>
public sealed record ActivityEntry(ActivityKind Kind, DateTimeOffset Start, TimeSpan Duration);

/// <summary>All calls belong to the UI thread. No Windows dependencies; clock is injectable.</summary>
public sealed class FocusTimer(TimeProvider clock, AppSettings settings)
{
    private AppSettings _settings = settings;
    private DateTimeOffset _deadline;
    private DateTimeOffset? _awaySince;
    private TimeSpan _held;
    private bool _warned;
    public TimerPhase Phase { get; private set; } = TimerPhase.Idle;
    public bool IsAway => _awaySince.HasValue;
    public int CompletedBreaks { get; private set; }
    public string? Error { get; private set; }
    private readonly List<ActivityEntry> _activity = [];
    private ActivityKind? _activeKind;
    private DateTimeOffset _activeSince;
    public event Action? LockRequested;
    public event Action? ReminderRequested;
    public event Action? Changed;
    public AppSettings Settings => _settings;
    public TimeSpan IntervalDuration { get; private set; }
    public long IntervalId { get; private set; }
    public bool CanAdjustTime => !IsAway && Phase is TimerPhase.Focus or TimerPhase.Paused or TimerPhase.Break;
    public TimeSpan MaximumRemaining => TimeSpan.FromMinutes(Phase == TimerPhase.Break ? 120 : 480);
    public TimeSpan Remaining => Phase switch
    {
        TimerPhase.Focus when IsAway => _held,
        TimerPhase.Focus or TimerPhase.Break or TimerPhase.Locking => Max(_deadline - clock.GetUtcNow()),
        TimerPhase.Paused => _held,
        _ => TimeSpan.Zero
    };

    /// <summary>Focus and break entries this session; focus time between two breaks is one entry. Includes the live entry.</summary>
    public IReadOnlyList<ActivityEntry> Activity
    {
        get
        {
            var live = _activeKind is null ? TimeSpan.Zero : Max(clock.GetUtcNow() - _activeSince);
            var result = _activity.ToList();
            if (live > TimeSpan.Zero && result.Count > 0) result[^1] = result[^1] with { Duration = result[^1].Duration + live };
            result.RemoveAll(entry => entry.Duration < TimeSpan.FromSeconds(1));
            return result;
        }
    }
    public TimeSpan TotalFocus => Total(ActivityKind.Focus);
    public TimeSpan TotalBreak => Total(ActivityKind.Break);

    /// <summary>Clears the focus/break totals. A running focus or break keeps counting from zero.</summary>
    public void ResetActivity()
    {
        _activity.Clear();
        _activeKind = null;
        Notify();
    }

    public void Configure(AppSettings value)
    {
        if (value.Validate() is { } error) throw new ArgumentException(error);
        _settings = value;
        Notify(); // Active interval retains its deadline; next interval uses new settings.
    }

    public void Start()
    {
        Error = null;
        _warned = false;
        _held = TimeSpan.FromMinutes(_settings.FocusMinutes);
        IntervalDuration = _held;
        IntervalId++;
        _deadline = clock.GetUtcNow() + _held;
        Phase = TimerPhase.Focus;
        Notify();
    }

    public void PauseOrResume()
    {
        if (Phase == TimerPhase.Focus)
        {
            _held = Remaining;
            Phase = TimerPhase.Paused;
        }
        else if (Phase == TimerPhase.Paused)
        {
            _deadline = clock.GetUtcNow() + _held;
            Phase = TimerPhase.Focus;
            Error = null;
        }
        Notify();
    }

    public void Reset()
        => SetRemaining(TimeSpan.FromMinutes(Phase == TimerPhase.Break ? _settings.BreakMinutes : _settings.FocusMinutes));

    /// <summary>Adjust only this interval. A deliberately paused timer remains paused.</summary>
    public void SetRemaining(TimeSpan remaining)
    {
        if (!CanAdjustTime) throw new InvalidOperationException("There is no editable countdown right now.");
        if (remaining < TimeSpan.FromSeconds(1) || remaining > MaximumRemaining)
            throw new ArgumentOutOfRangeException(nameof(remaining), $"Choose between one second and {MaximumRemaining.TotalMinutes:g} minutes.");
        _held = remaining;
        _deadline = clock.GetUtcNow() + remaining;
        IntervalDuration = remaining;
        IntervalId++;
        _warned = false;
        Error = null;
        Notify();
    }

    public void Stop()
    {
        Phase = TimerPhase.Idle;
        Error = null;
        Notify();
    }

    public void TakeBreak()
    {
        if (Phase is TimerPhase.Break or TimerPhase.Locking or TimerPhase.Ready) return;
        if (IsAway) { BeginBreak(); return; }
        _held = Remaining > TimeSpan.Zero ? Remaining : TimeSpan.FromMinutes(_settings.FocusMinutes);
        Phase = TimerPhase.Locking;
        _deadline = clock.GetUtcNow().AddSeconds(8);
        Error = null;
        Notify();
        LockRequested?.Invoke();
    }

    public void LockFailed(string error)
    {
        if (Phase == TimerPhase.Locking) Phase = TimerPhase.Paused;
        Error = error;
        Notify();
    }

    public void SetAway(bool away)
    {
        if (away == IsAway) return;
        if (away)
        {
            _held = Remaining;
            _awaySince = clock.GetUtcNow();
            if (Phase == TimerPhase.Locking) BeginBreak();
        }
        else
        {
            var absence = clock.GetUtcNow() - _awaySince!.Value;
            _awaySince = null;
            if (Phase == TimerPhase.Ready) Start();
            else if (Phase == TimerPhase.Focus)
            {
                if (absence >= TimeSpan.FromMinutes(_settings.BreakMinutes)) Start();
                else _deadline = clock.GetUtcNow() + _held;
            }
            Tick();
        }
        Notify();
    }

    public void Tick()
    {
        if (Phase == TimerPhase.Locking && Remaining == TimeSpan.Zero)
            LockFailed("Windows did not confirm the lock. Resume to try again, or take a break manually.");
        else if (Phase == TimerPhase.Break && Remaining == TimeSpan.Zero)
        {
            CompletedBreaks++;
            if (IsAway) Phase = TimerPhase.Ready;
            else Start();
        }
        else if (Phase == TimerPhase.Focus && !IsAway)
        {
            if (Remaining == TimeSpan.Zero) TakeBreak();
            else if (!_warned && _settings.WarningSeconds > 0 && Remaining.TotalSeconds <= _settings.WarningSeconds)
            {
                _warned = true;
                ReminderRequested?.Invoke();
            }
        }
        Notify();
    }

    private void BeginBreak()
    {
        Phase = TimerPhase.Break;
        IntervalDuration = TimeSpan.FromMinutes(_settings.BreakMinutes);
        IntervalId++;
        _deadline = clock.GetUtcNow() + IntervalDuration;
        Notify();
    }

    private TimeSpan Total(ActivityKind kind)
        => Activity.Where(entry => entry.Kind == kind).Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Duration);

    private void Notify()
    {
        TrackActivity();
        Changed?.Invoke();
    }

    /// <summary>Accrues elapsed time into the open entry, then opens a new entry when the activity kind changes.</summary>
    private void TrackActivity()
    {
        var now = clock.GetUtcNow();
        if (_activeKind is not null && _activity.Count > 0)
            _activity[^1] = _activity[^1] with { Duration = _activity[^1].Duration + Max(now - _activeSince) };
        _activeSince = now;
        _activeKind = Phase == TimerPhase.Break ? ActivityKind.Break : Phase == TimerPhase.Focus && !IsAway ? ActivityKind.Focus : null;
        if (_activeKind is not { } kind) return;
        if (_activity.Count > 0 && _activity[^1].Kind != kind && _activity[^1].Duration < TimeSpan.FromSeconds(1)) _activity.RemoveAt(_activity.Count - 1);
        if (_activity.Count == 0 || _activity[^1].Kind != kind) _activity.Add(new ActivityEntry(kind, now, TimeSpan.Zero));
    }

    private static TimeSpan Max(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
