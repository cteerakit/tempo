using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows.Controls.Primitives;
using CommunityToolkit.Mvvm.ComponentModel;
using Tempo.App.Services;
using Tempo.App.Shell;
using Tempo.Core.Settings;

namespace Tempo.App.Widgets;

/// <summary>Everything a widget may need from the host application.</summary>
internal sealed class AppServices
{
    public required SettingsService Settings { get; init; }
    public required ThemeService Theme { get; init; }
    public required PollingScheduler Scheduler { get; init; }
    public required TaskbarHider Taskbar { get; init; }
    public required WidgetRegistry Widgets { get; init; }
    public required BarState Bar { get; init; }
    public required Action OpenSettings { get; init; }
    public TrayIconService TrayIcons { get; init; } = new();
}

/// <summary>Bar-wide state that widget templates bind to (e.g. which way flyouts open).</summary>
internal sealed partial class BarState : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlyoutPlacement), nameof(FlyoutOffset))]
    public partial BarEdge Edge { get; set; } = BarEdge.Bottom;

    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    public bool IsOnPrimaryMonitor { get; set; } = true;

    public double Scale { get; set; } = 1;

    /// <summary>App icon size in DIPs for the current bar size.</summary>
    [ObservableProperty]
    public partial double IconSize { get; set; } = 24;

    [ObservableProperty]
    public partial double GlyphSize { get; set; } = 15;

    public PlacementMode FlyoutPlacement => Edge == BarEdge.Bottom ? PlacementMode.Top : PlacementMode.Bottom;

    public double FlyoutOffset => Edge == BarEdge.Bottom ? -10 : 10;
}

internal sealed record WidgetContext(string InstanceId, AppServices Services, Action<object> SaveSettings);

internal static class WidgetJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

internal sealed class WidgetDescriptor
{
    public required string TypeId { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string Glyph { get; init; }
    public required Type SettingsType { get; init; }
    public required Func<WidgetContext, object, WidgetViewModelBase> Factory { get; init; }

    public static WidgetDescriptor Create<TSettings>(
        string typeId, string displayName, string description, string category, string glyph,
        Func<WidgetContext, TSettings, WidgetViewModelBase> factory) where TSettings : class, new() => new()
        {
            TypeId = typeId,
            DisplayName = displayName,
            Description = description,
            Category = category,
            Glyph = glyph,
            SettingsType = typeof(TSettings),
            Factory = (context, settings) => factory(context, (TSettings)settings),
        };

    /// <summary>Reads stored settings; anything unreadable falls back to defaults rather than breaking the bar.</summary>
    public object ReadSettings(JsonObject? json)
    {
        try
        {
            if (json is not null && json.Deserialize(SettingsType, WidgetJson.Options) is { } settings)
                return settings;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            Log.Error($"Invalid settings for widget {TypeId}; using defaults", ex);
        }
        return Activator.CreateInstance(SettingsType)!;
    }

    public override string ToString() => DisplayName;

    public JsonObject WriteSettings(object settings) =>
        JsonSerializer.SerializeToNode(settings, SettingsType, WidgetJson.Options) as JsonObject ?? [];
}

internal sealed class WidgetRegistry
{
    private readonly Dictionary<string, WidgetDescriptor> _byId = [];

    public ObservableCollection<WidgetDescriptor> All { get; } = [];

    public void Register(WidgetDescriptor descriptor)
    {
        _byId.Add(descriptor.TypeId, descriptor);
        All.Add(descriptor);
    }

    public WidgetDescriptor? Find(string typeId) => _byId.GetValueOrDefault(typeId);
}

/// <summary>
/// Base class for every widget. The bar creates one per placed instance, calls <see cref="Start"/>,
/// and disposes it when the instance is removed or its settings change.
/// </summary>
internal abstract partial class WidgetViewModelBase(WidgetContext context) : ObservableObject, IDisposable
{
    private readonly List<IDisposable> _resources = [];

    public WidgetContext Context { get; } = context;

    public string InstanceId => Context.InstanceId;

    public bool IsDragging { get; set; }

    public BarState Bar => Context.Services.Bar;

    protected AppServices Services => Context.Services;

    /// <summary>Serialized settings this instance was created with; the bar compares it to detect edits.</summary>
    public string SettingsSnapshot { get; set; } = "";

    [ObservableProperty]
    public partial bool IsFlyoutOpen { get; set; }

    partial void OnIsFlyoutOpenChanged(bool value)
    {
        if (value)
            OutsideClickWatcher.RegisterFlyout(this, () => IsFlyoutOpen = false);
        else
            OutsideClickWatcher.Unregister(this);
        OnFlyoutOpenChanged(value);
    }

    protected virtual void OnFlyoutOpenChanged(bool isOpen)
    {
    }

    /// <summary>Widgets with nothing to show (e.g. media with no player) can collapse themselves.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    public virtual void Start()
    {
    }

    /// <summary>Polls on the shared scheduler for the widget's lifetime.</summary>
    protected void Every(TimeSpan interval, Action action) => Own(Services.Scheduler.Every(interval, action));

    protected void Own(IDisposable resource) => _resources.Add(resource);

    protected void Own(Action cleanup) => _resources.Add(new Cleanup(cleanup));

    public virtual void Dispose()
    {
        foreach (var resource in _resources)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error($"Error disposing a resource of widget {InstanceId}", ex);
            }
        }
        _resources.Clear();
        GC.SuppressFinalize(this);
    }

    private sealed class Cleanup(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
