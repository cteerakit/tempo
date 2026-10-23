using System.Collections.ObjectModel;
using Tempo.App.Widgets;
using Tempo.Core.Layout;

namespace Tempo.App.Bar;

/// <summary>
/// Keeps the three zone collections in step with the layout settings. Widgets whose instance and
/// settings are unchanged are reused, so editing one widget doesn't restart the others.
/// </summary>
internal sealed class BarViewModel(AppServices services) : IDisposable
{
    private Dictionary<string, WidgetViewModelBase> _live = [];
    private readonly HashSet<string> _reportedUnknownTypes = [];

    public BarState Bar => services.Bar;

    public ObservableCollection<WidgetViewModelBase> Left { get; } = [];
    public ObservableCollection<WidgetViewModelBase> Center { get; } = [];
    public ObservableCollection<WidgetViewModelBase> Right { get; } = [];

    public void Reconcile(LayoutSettings layout)
    {
        var next = new Dictionary<string, WidgetViewModelBase>();
        foreach (var zone in Enum.GetValues<WidgetZone>())
        {
            var desired = new List<WidgetViewModelBase>();
            foreach (var instance in layout.GetZone(zone))
            {
                if (Resolve(instance) is { } widget)
                {
                    next[instance.InstanceId] = widget;
                    desired.Add(widget);
                }
            }
            Apply(GetZone(zone), desired);
        }

        foreach (var (id, widget) in _live)
        {
            if (!next.TryGetValue(id, out var kept) || !ReferenceEquals(kept, widget))
                widget.Dispose();
        }
        _live = next;
    }

    public void PreviewMove(WidgetViewModelBase widget, WidgetZone zone, int index)
    {
        var source = Enum.GetValues<WidgetZone>().Select(GetZone).FirstOrDefault(items => items.Contains(widget));
        if (source is null)
            return;
        var target = GetZone(zone);
        var from = source.IndexOf(widget);
        if (source == target)
        {
            if (from < index)
                index--;
            index = Math.Clamp(index, 0, source.Count - 1);
            if (from != index)
                source.Move(from, index);
        }
        else
        {
            source.Remove(widget);
            target.Insert(Math.Clamp(index, 0, target.Count), widget);
        }
    }

    private WidgetViewModelBase? Resolve(WidgetInstance instance)
    {
        var descriptor = services.Widgets.Find(instance.TypeId);
        if (descriptor is null)
        {
            if (_reportedUnknownTypes.Add(instance.TypeId))
                Log.Error($"Unknown widget type '{instance.TypeId}' in layout; skipping it.");
            return null;
        }

        var snapshot = instance.TypeId + instance.Settings.ToJsonString();
        if (_live.TryGetValue(instance.InstanceId, out var existing) && existing.SettingsSnapshot == snapshot)
            return existing;

        try
        {
            var id = instance.InstanceId;
            WidgetViewModelBase? widget = null;
            var context = new WidgetContext(id, services, settings => services.Settings.Update(s =>
            {
                if (s.Layout.Find(id) is { } target)
                {
                    target.Settings = descriptor.WriteSettings(settings);
                    // The saving widget already owns this state. Preserve its view and animations
                    // when Changed synchronously reconciles the layout; other bars still reload.
                    if (widget is not null && _live.TryGetValue(id, out var live) && ReferenceEquals(live, widget))
                        widget.SettingsSnapshot = target.TypeId + target.Settings.ToJsonString();
                }
            }));
            widget = descriptor.Factory(context, descriptor.ReadSettings(instance.Settings));
            widget.SettingsSnapshot = snapshot;
            widget.Start();
            return widget;
        }
        catch (Exception ex)
        {
            Log.Error($"Widget '{instance.TypeId}' failed to start", ex);
            return null;
        }
    }

    private ObservableCollection<WidgetViewModelBase> GetZone(WidgetZone zone) => zone switch
    {
        WidgetZone.Left => Left,
        WidgetZone.Center => Center,
        _ => Right,
    };

    private static void Apply(ObservableCollection<WidgetViewModelBase> target, List<WidgetViewModelBase> desired)
    {
        if (target.SequenceEqual(desired))
            return;
        for (var i = target.Count - 1; i >= 0; i--)
            if (!desired.Contains(target[i]))
                target.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            var existing = target.IndexOf(desired[i]);
            if (existing < 0)
                target.Insert(i, desired[i]);
            else if (existing != i)
                target.Move(existing, i);
        }
    }

    public void Dispose()
    {
        foreach (var widget in _live.Values)
            widget.Dispose();
        _live.Clear();
        Left.Clear();
        Center.Clear();
        Right.Clear();
    }
}
