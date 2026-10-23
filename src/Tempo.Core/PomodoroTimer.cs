namespace Tempo.Core;

public enum PomodoroPhase { Focus, ShortBreak, LongBreak }

public sealed class PomodoroTimer(int focusMinutes = 25, int shortBreakMinutes = 5,
    int longBreakMinutes = 15, int sessionsBeforeLongBreak = 4, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTimeOffset _deadline;
    private TimeSpan _remaining = TimeSpan.FromMinutes(Math.Clamp(focusMinutes, 1, 180));

    public PomodoroPhase Phase { get; private set; }
    public int CompletedSessions { get; private set; }
    public bool IsRunning { get; private set; }
    public TimeSpan Remaining => IsRunning
        ? TimeSpan.FromTicks(Math.Max(0, (_deadline - _clock.GetUtcNow()).Ticks)) : _remaining;

    public void Toggle()
    {
        Update();
        if (IsRunning)
            _remaining = Remaining;
        else
            _deadline = _clock.GetUtcNow() + _remaining;
        IsRunning = !IsRunning;
    }

    public bool Update()
    {
        if (!IsRunning || Remaining > TimeSpan.Zero)
            return false;
        Advance(true);
        return true;
    }

    public void Skip() => Advance(false);

    public void Reset()
    {
        IsRunning = false;
        _remaining = Duration;
    }

    private TimeSpan Duration => TimeSpan.FromMinutes(Phase switch
    {
        PomodoroPhase.Focus => Math.Clamp(focusMinutes, 1, 180),
        PomodoroPhase.ShortBreak => Math.Clamp(shortBreakMinutes, 1, 60),
        _ => Math.Clamp(longBreakMinutes, 1, 120),
    });

    private void Advance(bool completed)
    {
        if (Phase == PomodoroPhase.Focus)
        {
            if (completed)
                CompletedSessions++;
            Phase = completed && CompletedSessions % Math.Clamp(sessionsBeforeLongBreak, 1, 12) == 0
                ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else
            Phase = PomodoroPhase.Focus;
        Reset();
    }
}
