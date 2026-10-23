using System.Text.Json.Serialization;
using Tempo.Core.Layout;

namespace Tempo.Core.Settings;

public enum BarEdge { Top, Bottom }

public enum BarMode { Full, Pill }

/// <summary>One knob for the bar's scale: height, app icon size and glyph size move together.</summary>
public enum BarSize { Small, Medium, Large }

public sealed record BarMetrics(double Height, double IconSize, double GlyphSize)
{
    public static BarMetrics For(BarSize size) => size switch
    {
        BarSize.Small => new(40, 24, 15),
        BarSize.Large => new(56, 32, 20),
        _ => new(48, 28, 18),
    };
}

public enum TaskbarMode { None, AutoHide, Hidden }

/// <summary>
/// Blur effect drawn behind the bar. Mica is intentionally absent: Windows renders
/// system backdrops as solid colours on inactive windows, and the bar is never active.
/// </summary>
public enum BackdropKind { None, Blur, Acrylic }

public sealed class TempoSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public BarSettings Bar { get; set; } = new();
    public BehaviorSettings Behavior { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public LayoutSettings Layout { get; set; } = LayoutSettings.CreateDefault();
    public PinSetCatalog PinSets { get; set; } = new();

    public static TempoSettings CreateDefault() => new();
}

public sealed class PinSetDefinition
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "Default";
    public string Icon { get; set; } = "\uE8F1";
    public bool IconOnly { get; set; }
}

public sealed class PinSetCatalog
{
    public string ActiveId { get; set; } = "default";
    public List<PinSetDefinition> Sets { get; set; } = [new()];

    public void Normalize()
    {
        Sets ??= [];
        var ids = new HashSet<string>();
        Sets.RemoveAll(s => s is null || string.IsNullOrWhiteSpace(s.Id) || !ids.Add(s.Id));
        if (!Sets.Any(s => s.Id == "default"))
            Sets.Insert(0, new());
        foreach (var set in Sets)
            set.Name = string.IsNullOrWhiteSpace(set.Name) ? "Untitled set" : set.Name.Trim();
        if (!Sets.Any(s => s.Id == ActiveId))
            ActiveId = "default";
    }
}

public sealed class BarSettings
{
    public const string AllMonitors = "all";

    public const double MinMargin = 0, MaxMargin = 32;

    public BarEdge Edge { get; set; } = BarEdge.Bottom;
    public BarMode Mode { get; set; } = BarMode.Pill;
    public BarSize Size { get; set; } = BarSize.Medium;
    public bool IsLocked { get; set; }

    [JsonIgnore]
    public BarMetrics Metrics => BarMetrics.For(Size);

    [JsonIgnore]
    public double Height => Metrics.Height;

    /// <summary>Gap between the pill and the screen edge (pill mode only).</summary>
    public double Margin { get; set; } = 10;

    /// <summary>Monitor device name (e.g. <c>\\.\DISPLAY1</c>); null means the primary monitor; "all" shows a bar on every monitor.</summary>
    public string? Monitor { get; set; } = AllMonitors;

    /// <summary>In pill mode, whether maximized windows should stop short of the pill.</summary>
    public bool ReserveSpaceInPill { get; set; } = true;
}

public sealed class BehaviorSettings
{
    public TaskbarMode TaskbarMode { get; set; } = TaskbarMode.Hidden;
    public bool StartWithWindows { get; set; } = true;
    public bool HideOnFullscreen { get; set; } = true;
}

/// <summary>Theme selection plus optional overrides; a null override means "use the theme's value".</summary>
public sealed class AppearanceSettings
{
    public const double MinCornerRadius = 0, MaxCornerRadius = 36;
    public const double MinFontSize = 10, MaxFontSize = 20;

    public string ThemeId { get; set; } = "Auto";
    public string? AccentColor { get; set; }
    public double? TintOpacity { get; set; }
    public double BlurStrength { get; set; } = 100;
    public double? CornerRadius { get; set; }
    public string? FontFamily { get; set; }
    public double? FontSize { get; set; }
    public BackdropKind? Backdrop { get; set; }
}
