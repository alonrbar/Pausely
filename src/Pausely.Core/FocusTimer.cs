namespace Pausely.Core;

public enum TimerPhase { Idle, Focus, Paused, Locking, Break, Ready }

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

    public void Configure(AppSettings value)
    {
        if (value.Validate() is { } error) throw new ArgumentException(error);
        _settings = value;
        Changed?.Invoke(); // Active interval retains its deadline; next interval uses new settings.
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
        Changed?.Invoke();
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
        Changed?.Invoke();
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
        Changed?.Invoke();
    }

    public void Stop()
    {
        Phase = TimerPhase.Idle;
        Error = null;
        Changed?.Invoke();
    }

    public void TakeBreak()
    {
        if (Phase is TimerPhase.Break or TimerPhase.Locking or TimerPhase.Ready) return;
        if (IsAway) { BeginBreak(); return; }
        _held = Remaining > TimeSpan.Zero ? Remaining : TimeSpan.FromMinutes(_settings.FocusMinutes);
        Phase = TimerPhase.Locking;
        _deadline = clock.GetUtcNow().AddSeconds(8);
        Error = null;
        Changed?.Invoke();
        LockRequested?.Invoke();
    }

    public void LockFailed(string error)
    {
        if (Phase == TimerPhase.Locking) Phase = TimerPhase.Paused;
        Error = error;
        Changed?.Invoke();
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
        Changed?.Invoke();
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
        Changed?.Invoke();
    }

    private void BeginBreak()
    {
        Phase = TimerPhase.Break;
        IntervalDuration = TimeSpan.FromMinutes(_settings.BreakMinutes);
        IntervalId++;
        _deadline = clock.GetUtcNow() + IntervalDuration;
        Changed?.Invoke();
    }

    private static TimeSpan Max(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
