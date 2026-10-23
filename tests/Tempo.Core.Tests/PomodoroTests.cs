using Tempo.Core;

namespace Tempo.Core.Tests;

public sealed class PomodoroTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int seconds) => Now = Now.AddSeconds(seconds);
    }

    [Fact]
    public void PauseAndResumePreserveRemainingTime()
    {
        var clock = new Clock();
        var timer = new PomodoroTimer(clock: clock);
        timer.Toggle();
        clock.Advance(90);
        timer.Toggle();
        clock.Advance(600);
        Assert.Equal(TimeSpan.FromSeconds(1410), timer.Remaining);
        timer.Toggle();
        clock.Advance(10);
        Assert.Equal(TimeSpan.FromSeconds(1400), timer.Remaining);
        timer.Reset();
        Assert.False(timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining);
    }

    [Fact]
    public void DelayedTickCompletesOnlyOneSessionAndWaitsForUser()
    {
        var clock = new Clock();
        var timer = new PomodoroTimer(sessionsBeforeLongBreak: 2, clock: clock);
        timer.Toggle();
        clock.Advance(7200);
        Assert.True(timer.Update());
        Assert.False(timer.Update());
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(1, timer.CompletedSessions);
        Assert.False(timer.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Remaining);
        timer.Skip();
        timer.Toggle();
        clock.Advance(1500);
        timer.Update();
        Assert.Equal(PomodoroPhase.LongBreak, timer.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), timer.Remaining);
        timer.Skip();
        Assert.Equal(PomodoroPhase.Focus, timer.Phase);
    }

    [Fact]
    public void SkipDoesNotCountAsCompletedAndInvalidDurationsAreBounded()
    {
        var timer = new PomodoroTimer(focusMinutes: -10, shortBreakMinutes: 999);
        Assert.Equal(TimeSpan.FromMinutes(1), timer.Remaining);
        timer.Skip();
        Assert.Equal(0, timer.CompletedSessions);
        Assert.Equal(TimeSpan.FromMinutes(60), timer.Remaining);
    }
}
