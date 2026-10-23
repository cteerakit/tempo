using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.App.Services;
using Tempo.App.Shell;
using Tempo.App.Widgets;
using Tempo.Core.Layout;
using Tempo.Core.Settings;

namespace Tempo.App.Settings;

internal sealed record Option<T>(T Value, string Label, string Description = "")
{
    public override string ToString() => Label;
}

internal sealed record ThemeCard(string Id, string Name, string Description, Brush Background, Brush Foreground, Brush Accent, Brush Border)
{
    public override string ToString() => Name;
}

/// <summary>
/// Every property reads from and writes straight to the live settings, so changes show on the bar immediately.
/// </summary>
internal sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Themes = ThemeService.Themes.Select(t =>
        {
            var d = services.Theme.GetDictionary(t.Id);
            return new ThemeCard(t.Id, t.DisplayName, t.Description,
                (Brush)d["Bar.Background"], (Brush)d["Bar.Foreground"], (Brush)d["Accent"], (Brush)d["Bar.Border"]);
        }).ToList();
        Monitors = [new(null, "Primary monitor"), new(BarSettings.AllMonitors, "All monitors"), .. Shell.Monitors.GetAll().Select(m => new Option<string?>(m.DeviceName, m.DisplayName))];
        FontFamilies = [new(null, "Theme default"), .. System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source).Order().Select(f => new Option<string?>(f, f))];

        services.Settings.Changed += OnSettingsChanged;
    }

    private TempoSettings S => _services.Settings.Current;

    private void Update(Action<TempoSettings> edit) => _services.Settings.Update(edit);

    // ───────────── Layout ─────────────

    public IReadOnlyList<WidgetDescriptor> Library => _services.Widgets.All;

    [ObservableProperty]
    public partial string WidgetStatus { get; set; } = "";

    [RelayCommand]
    private void AddWidget(WidgetDescriptor descriptor)
    {
        Update(s => s.Layout.Add(descriptor.TypeId, WidgetZone.Right));
        WidgetStatus = $"{descriptor.DisplayName} added to your bar.";
    }

    public IReadOnlyList<ThemeCard> Themes { get; }

    public string ThemeId
    {
        get => S.Appearance.ThemeId;
        set => Update(s => s.Appearance.ThemeId = value);
    }

    public IReadOnlyList<Option<string?>> AccentPresets { get; } =
    [
        new(null, "Theme"), new("#8AB4FF", "Sky"), new("#A78BFA", "Violet"), new("#F472B6", "Pink"),
        new("#FB7185", "Rose"), new("#FB923C", "Orange"), new("#FACC15", "Sun"), new("#4ADE80", "Mint"),
        new("#22D3EE", "Cyan"), new("#FFFFFF", "White"),
    ];

    public string? AccentColor
    {
        get => S.Appearance.AccentColor;
        set => Update(s => s.Appearance.AccentColor = ThemeService.TryParseColor(value) is null ? null : value!.Trim());
    }

    [RelayCommand]
    private void SetAccent(string? color) => AccentColor = color;

    public double TintOpacity
    {
        get => S.Appearance.TintOpacity ?? ThemeColor().A / 255.0;
        set => Update(s => s.Appearance.TintOpacity = Math.Round(value, 2));
    }

    public double BlurStrength
    {
        get => S.Appearance.BlurStrength;
        set => Update(s => s.Appearance.BlurStrength = Math.Round(value));
    }

    public bool IsBlurEnabled => _services.Theme.Current.Backdrop != BackdropKind.None;

    public double CornerRadius
    {
        get => S.Appearance.CornerRadius ?? _services.Theme.Current.CornerRadius;
        set => Update(s => s.Appearance.CornerRadius = Math.Round(value));
    }

    public string? FontFamily
    {
        get => S.Appearance.FontFamily;
        set => Update(s => s.Appearance.FontFamily = value);
    }

    public IReadOnlyList<Option<string?>> FontFamilies { get; }

    public double FontSize
    {
        get => S.Appearance.FontSize ?? 13;
        set => Update(s => s.Appearance.FontSize = Math.Round(value, 1));
    }

    public IReadOnlyList<Option<BackdropKind?>> Backdrops { get; } =
    [
        new(null, "Theme default"), new(BackdropKind.Acrylic, "Acrylic"), new(BackdropKind.Blur, "Blur"), new(BackdropKind.None, "None"),
    ];

    public BackdropKind? Backdrop
    {
        get => S.Appearance.Backdrop;
        set => Update(s => s.Appearance.Backdrop = value);
    }

    [RelayCommand]
    private void ResetAppearance(string? which) => Update(s =>
    {
        var a = s.Appearance;
        switch (which)
        {
            case "tint": a.TintOpacity = null; break;
            case "blurStrength": a.BlurStrength = 100; break;
            case "radius": a.CornerRadius = null; break;
            case "font": a.FontSize = null; a.FontFamily = null; break;
            default: s.Appearance = new AppearanceSettings { ThemeId = a.ThemeId }; break;
        }
    });

    private Color ThemeColor() => (Color)_services.Theme.GetDictionary(S.Appearance.ThemeId)["Bar.BackgroundColor"];

    // ───────────── Behavior ─────────────

    public IReadOnlyList<Option<BarEdge>> Edges { get; } = [new(BarEdge.Bottom, "Bottom"), new(BarEdge.Top, "Top")];

    public IReadOnlyList<Option<BarMode>> Modes { get; } =
    [
        new(BarMode.Full, "Full width", "Spans the whole screen edge"),
        new(BarMode.Pill, "Floating pill", "A rounded bar that hugs its content"),
    ];

    public IReadOnlyList<Option<TaskbarMode>> TaskbarModes { get; } =
    [
        new(TaskbarMode.None, "Leave it alone", "Tempo sits next to the Windows taskbar"),
        new(TaskbarMode.AutoHide, "Auto-hide", "The Windows taskbar slides in when you point at the screen edge"),
        new(TaskbarMode.Hidden, "Hide completely", "Only Tempo is shown; tray icons are reachable via the Windows taskbar widget"),
    ];

    public IReadOnlyList<Option<string?>> Monitors { get; }

    public BarEdge Edge
    {
        get => S.Bar.Edge;
        set => Update(s => s.Bar.Edge = value);
    }

    public BarMode Mode
    {
        get => S.Bar.Mode;
        set => Update(s => s.Bar.Mode = value);
    }

    public bool IsPill => S.Bar.Mode == BarMode.Pill;

    public IReadOnlyList<Option<BarSize>> Sizes { get; } = [.. Enum.GetValues<BarSize>().Select(size =>
    {
        var m = BarMetrics.For(size);
        return new Option<BarSize>(size, size.ToString(), $"{m.Height:0} px bar, {m.IconSize:0} px icons");
    })];

    public BarSize BarSize
    {
        get => S.Bar.Size;
        set => Update(s => s.Bar.Size = value);
    }

    public double BarMargin
    {
        get => S.Bar.Margin;
        set => Update(s => s.Bar.Margin = Math.Round(value));
    }

    public bool ReserveSpaceInPill
    {
        get => S.Bar.ReserveSpaceInPill;
        set => Update(s => s.Bar.ReserveSpaceInPill = value);
    }

    public string? Monitor
    {
        get => S.Bar.Monitor;
        set => Update(s => s.Bar.Monitor = value);
    }

    public TaskbarMode TaskbarMode
    {
        get => S.Behavior.TaskbarMode;
        set => Update(s => s.Behavior.TaskbarMode = value);
    }

    public bool HideOnFullscreen
    {
        get => S.Behavior.HideOnFullscreen;
        set => Update(s => s.Behavior.HideOnFullscreen = value);
    }

    public bool StartWithWindows
    {
        get => S.Behavior.StartWithWindows;
        set => Update(s => s.Behavior.StartWithWindows = value);
    }

    public string SettingsPath => _services.Settings.FilePath;

    [RelayCommand]
    private void OpenSettingsFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SettingsPath}\"") { UseShellExecute = true })?.Dispose();

    [RelayCommand]
    private void OpenSettingsFile() => ShellActions.Launch("notepad.exe", $"\"{SettingsPath}\"");

    [RelayCommand]
    private void OpenLog() => ShellActions.Launch("notepad.exe", $"\"{Path.Combine(Path.GetDirectoryName(SettingsPath)!, "tempo.log")}\"");

    // ───────────── Plumbing ─────────────

    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(Sets));
        OnPropertyChanged(nameof(SelectedSet));
        OnPropertyChanged(nameof(SetIcon));
        OnPropertyChanged(nameof(SetIconOnly));
        // Refresh every simple property; unchanged values are no-ops for the bound controls.
        foreach (var name in RefreshedProperties)
            OnPropertyChanged(name);
    }

    private static readonly string[] RefreshedProperties =
    [
        nameof(ThemeId), nameof(AccentColor), nameof(TintOpacity), nameof(BlurStrength), nameof(IsBlurEnabled), nameof(CornerRadius), nameof(FontFamily), nameof(FontSize),
        nameof(Backdrop), nameof(Edge), nameof(Mode), nameof(IsPill), nameof(BarSize), nameof(BarMargin),
        nameof(ReserveSpaceInPill), nameof(Monitor), nameof(TaskbarMode), nameof(HideOnFullscreen), nameof(StartWithWindows),
    ];

    public void Dispose() => _services.Settings.Changed -= OnSettingsChanged;
}
