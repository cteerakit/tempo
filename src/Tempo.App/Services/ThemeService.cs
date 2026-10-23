using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Tempo.Core.Settings;

namespace Tempo.App.Services;

internal sealed record ThemeInfo(string Id, string DisplayName, string Description);

/// <summary>What the bar window needs from the active theme beyond its brushes.</summary>
internal sealed record EffectiveTheme(string Id, bool IsDark, BackdropKind Backdrop, double CornerRadius);

/// <summary>
/// Swaps the theme dictionary in application resources and layers the user's overrides on top.
/// All bar XAML uses DynamicResource, so a change restyles everything live.
/// </summary>
internal sealed class ThemeService
{
    public const string AutoId = "Auto";

    public static IReadOnlyList<ThemeInfo> Themes { get; } =
    [
        new(AutoId, "Auto", "Glass in dark mode, White in light mode"),
        new("Glass", "Glass", "Smoky translucent glass"),
        new("Frost", "Frost", "Bright frosted glass over the wallpaper"),
        new("Midnight", "Midnight", "Deep navy with a violet accent"),
        new("White", "White", "Light, clean and airy"),
        new("Black", "Black", "Solid black, no blur"),
    ];

    private static readonly string[] OverrideKeys = ["Accent", "Bar.Background", "Bar.FontFamily", "Bar.FontSize"];

    private readonly Dictionary<string, ResourceDictionary> _cache = [];
    private readonly ResourceDictionary _slot = [];
    private AppearanceSettings? _last;

    public ThemeService()
    {
        // Index 0 is reserved for the theme so that later dictionaries (shared styles) can reference it.
        Application.Current.Resources.MergedDictionaries.Insert(0, _slot);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _last?.ThemeId == AutoId)
                Application.Current.Dispatcher.BeginInvoke(() => Apply(_last));
        };
    }

    public EffectiveTheme Current { get; private set; } = new("Glass", true, BackdropKind.Acrylic, 14);

    public event Action? Changed;

    public void Apply(AppearanceSettings appearance)
    {
        _last = appearance;
        var id = Resolve(appearance.ThemeId);
        var theme = Load(id);

        if (_slot.MergedDictionaries.FirstOrDefault() != theme)
        {
            _slot.MergedDictionaries.Clear();
            _slot.MergedDictionaries.Add(theme);
        }

        var resources = Application.Current.Resources;
        foreach (var key in OverrideKeys)
            resources.Remove(key);

        if (TryParseColor(appearance.AccentColor) is { } accent)
            resources["Accent"] = Frozen(new SolidColorBrush(accent));
        if (appearance.TintOpacity is { } opacity)
        {
            var color = (Color)theme["Bar.BackgroundColor"];
            color.A = (byte)Math.Round(opacity * 255);
            resources["Bar.Background"] = Frozen(new SolidColorBrush(color));
        }
        if (appearance.FontFamily is { } font)
            resources["Bar.FontFamily"] = new FontFamily(font);
        if (appearance.FontSize is { } size)
            resources["Bar.FontSize"] = size;

        var backdrop = appearance.Backdrop
                       ?? (Enum.TryParse<BackdropKind>((string)theme["Theme.Backdrop"], out var b) ? b : BackdropKind.Acrylic);
        Current = new EffectiveTheme(id, (bool)theme["Theme.IsDark"], backdrop, appearance.CornerRadius ?? (double)theme["Theme.CornerRadius"]);
        Changed?.Invoke();
    }

    /// <summary>The raw dictionary for a theme, used by the settings gallery to draw previews.</summary>
    public ResourceDictionary GetDictionary(string themeId) => Load(Resolve(themeId));

    public static string Resolve(string themeId)
    {
        if (themeId == AutoId)
            return SystemUsesLightTheme() ? "White" : "Glass";
        return Themes.Any(t => t.Id == themeId && t.Id != AutoId) ? themeId : "Glass";
    }

    public static Color? TryParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            return (Color)ColorConverter.ConvertFromString(value.Trim());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private ResourceDictionary Load(string id)
    {
        if (!_cache.TryGetValue(id, out var dict))
        {
            dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Tempo;component/Themes/{id}.xaml") };
            _cache[id] = dict;
        }
        return dict;
    }

    private static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
