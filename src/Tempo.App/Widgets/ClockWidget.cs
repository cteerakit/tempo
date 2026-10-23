using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.App.Shell;

namespace Tempo.App.Widgets;

internal sealed class ClockSettings
{
    [DisplayName("Time format"), Description("A .NET date format, e.g. HH:mm, h:mm tt or HH:mm:ss")]
    public string TimeFormat { get; set; } = "HH:mm";

    [DisplayName("Show date")]
    public bool ShowDate { get; set; } = true;

    [DisplayName("Date format"), Description("e.g. ddd d MMM, dd/MM/yyyy")]
    public string DateFormat { get; set; } = "ddd d MMM";

    [DisplayName("Stack time over date")]
    public bool Stacked { get; set; }
}

internal sealed record CalendarDay(int Day, bool IsToday, bool IsCurrentMonth);

internal sealed partial class ClockWidget(WidgetContext context, ClockSettings settings) : WidgetViewModelBase(context)
{
    private DateTime _displayedMonth = FirstOfMonth(DateTime.Today);

    public ClockSettings Settings { get; } = settings;

    [ObservableProperty]
    public partial string Time { get; set; } = "";

    [ObservableProperty]
    public partial string Date { get; set; } = "";

    [ObservableProperty]
    public partial string LongDate { get; set; } = "";

    [ObservableProperty]
    public partial string MonthTitle { get; set; } = "";

    public ObservableCollection<CalendarDay> Days { get; } = [];

    public IReadOnlyList<string> WeekdayNames { get; } = GetWeekdayNames();

    public override void Start()
    {
        // Seconds formats need a fast tick; minute formats only need to notice the minute change.
        var interval = Settings.TimeFormat.Contains('s') ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(2);
        Every(interval, Tick);
    }

    protected override void OnFlyoutOpenChanged(bool isOpen)
    {
        if (!isOpen)
            return;
        _displayedMonth = FirstOfMonth(DateTime.Today);
        BuildMonth();
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        _displayedMonth = _displayedMonth.AddMonths(-1);
        BuildMonth();
    }

    [RelayCommand]
    private void NextMonth()
    {
        _displayedMonth = _displayedMonth.AddMonths(1);
        BuildMonth();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        Time = Format(now, Settings.TimeFormat, "HH:mm");
        Date = Settings.ShowDate ? Format(now, Settings.DateFormat, "ddd d MMM") : "";
        LongDate = now.ToString("D", CultureInfo.CurrentCulture);
    }

    private void BuildMonth()
    {
        var culture = CultureInfo.CurrentCulture;
        MonthTitle = _displayedMonth.ToString("MMMM yyyy", culture);

        var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)_displayedMonth.DayOfWeek - (int)firstDay + 7) % 7;
        var start = _displayedMonth.AddDays(-offset);

        Days.Clear();
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            Days.Add(new CalendarDay(day.Day, day == DateTime.Today, day.Month == _displayedMonth.Month));
        }
    }

    private static string Format(DateTime value, string format, string fallback)
    {
        try
        {
            return value.ToString(string.IsNullOrWhiteSpace(format) ? fallback : format, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return value.ToString(fallback, CultureInfo.CurrentCulture);
        }
    }

    private static DateTime FirstOfMonth(DateTime d) => new(d.Year, d.Month, 1);

    private static IReadOnlyList<string> GetWeekdayNames()
    {
        var format = CultureInfo.CurrentCulture.DateTimeFormat;
        var first = (int)format.FirstDayOfWeek;
        return Enumerable.Range(0, 7).Select(i => format.ShortestDayNames[(first + i) % 7]).ToList();
    }
}
