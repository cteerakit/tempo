using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.Core;

namespace Tempo.App.Widgets;

internal sealed class PomodoroSettings
{
    [DisplayName("Focus minutes"), Range(1, 180)]
    public int FocusMinutes { get; set; } = 25;

    [DisplayName("Short break minutes"), Range(1, 60)]
    public int ShortBreakMinutes { get; set; } = 5;

    [DisplayName("Long break minutes"), Range(1, 120)]
    public int LongBreakMinutes { get; set; } = 15;

    [DisplayName("Focus sessions before a long break"), Range(1, 12)]
    public int SessionsBeforeLongBreak { get; set; } = 4;

    [DisplayName("Play sound when a session ends")]
    public bool PlaySound { get; set; } = true;
}

internal sealed partial class PomodoroWidget(WidgetContext context, PomodoroSettings settings) : WidgetViewModelBase(context)
{
    private readonly PomodoroTimer _timer = new(settings.FocusMinutes, settings.ShortBreakMinutes,
        settings.LongBreakMinutes, settings.SessionsBeforeLongBreak);

    [ObservableProperty] public partial string Countdown { get; set; } = "";
    [ObservableProperty] public partial string Phase { get; set; } = "Focus";
    [ObservableProperty] public partial string ActionLabel { get; set; } = "Start";
    [ObservableProperty] public partial string Status { get; set; } = "";

    public override void Start() => Every(TimeSpan.FromMilliseconds(250), Tick);

    [RelayCommand]
    private void Toggle() { _timer.Toggle(); Refresh(); }

    [RelayCommand]
    private void Reset() { _timer.Reset(); Refresh(); }

    [RelayCommand]
    private void Skip() { _timer.Skip(); Refresh(); }

    private void Tick()
    {
        if (_timer.Update())
        {
            IsFlyoutOpen = true;
            if (settings.PlaySound)
                System.Media.SystemSounds.Asterisk.Play();
        }
        Refresh();
    }

    private void Refresh()
    {
        var seconds = (int)Math.Ceiling(_timer.Remaining.TotalSeconds);
        Countdown = $"{seconds / 60:00}:{seconds % 60:00}";
        Phase = _timer.Phase switch
        {
            PomodoroPhase.Focus => "Focus",
            PomodoroPhase.ShortBreak => "Short break",
            _ => "Long break",
        };
        ActionLabel = _timer.IsRunning ? "Pause" : "Start";
        Status = $"{_timer.CompletedSessions} focus sessions completed · {(_timer.IsRunning ? "Running" : "Ready / paused")}";
    }
}
