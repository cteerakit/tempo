using System.Text.Json.Nodes;

namespace Tempo.Core.Layout;

public enum WidgetZone { Left, Center, Right }

/// <summary>One placed widget. <see cref="Settings"/> is the widget's own settings object, stored opaquely.</summary>
public sealed class WidgetInstance
{
    public string InstanceId { get; set; } = NewId();
    public string TypeId { get; set; } = "";
    public JsonObject Settings { get; set; } = [];

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}

/// <summary>Well-known widget type ids, shared by the default layout and the app's registry.</summary>
public static class WidgetTypes
{
    public const string Start = "tempo.start";
    public const string Apps = "tempo.apps";
    public const string PinSetSwitcher = "tempo.pin-set-switcher";
    public const string Clock = "tempo.clock";
    public const string Pomodoro = "tempo.pomodoro";
    public const string Media = "tempo.media";
    public const string System = "tempo.system";
    // The id predates the rename from "Volume"; it stays so existing saved layouts keep their widget.
    public const string Sound = "tempo.volume";
    public const string Notifications = "tempo.notifications";
    public const string Network = "tempo.network";
    public const string Taskbar = "tempo.taskbar";
    public const string HiddenIcons = "tempo.hidden-icons";
    public const string Spacer = "tempo.spacer";
    public const string Separator = "tempo.separator";
}

public sealed class LayoutSettings
{
    public List<WidgetInstance> Left { get; set; } = [];
    public List<WidgetInstance> Center { get; set; } = [];
    public List<WidgetInstance> Right { get; set; } = [];

    public static LayoutSettings CreateDefault()
    {
        var layout = new LayoutSettings();
        layout.Add(WidgetTypes.Start, WidgetZone.Left);
        layout.Add(WidgetTypes.Media, WidgetZone.Left);
        layout.Add(WidgetTypes.Apps, WidgetZone.Center);
        layout.Add(WidgetTypes.System, WidgetZone.Right);
        layout.Add(WidgetTypes.Network, WidgetZone.Right);
        layout.Add(WidgetTypes.Sound, WidgetZone.Right);
        layout.Add(WidgetTypes.Taskbar, WidgetZone.Right);
        layout.Add(WidgetTypes.Clock, WidgetZone.Right);
        layout.Add(WidgetTypes.Notifications, WidgetZone.Right);
        return layout;
    }

    public List<WidgetInstance> GetZone(WidgetZone zone) => zone switch
    {
        WidgetZone.Left => Left,
        WidgetZone.Center => Center,
        WidgetZone.Right => Right,
        _ => throw new ArgumentOutOfRangeException(nameof(zone)),
    };

    public IEnumerable<(WidgetZone Zone, WidgetInstance Instance)> EnumerateAll()
    {
        foreach (var zone in Enum.GetValues<WidgetZone>())
            foreach (var instance in GetZone(zone))
                yield return (zone, instance);
    }

    public WidgetInstance? Find(string instanceId) =>
        TryFind(instanceId, out var zone, out var index) ? GetZone(zone)[index] : null;

    public bool TryFind(string instanceId, out WidgetZone zone, out int index)
    {
        foreach (var z in Enum.GetValues<WidgetZone>())
        {
            index = GetZone(z).FindIndex(w => w.InstanceId == instanceId);
            if (index >= 0)
            {
                zone = z;
                return true;
            }
        }
        zone = default;
        index = -1;
        return false;
    }

    public WidgetInstance Add(string typeId, WidgetZone zone, int? index = null, JsonObject? settings = null)
    {
        var instance = new WidgetInstance { TypeId = typeId, Settings = settings ?? [] };
        var list = GetZone(zone);
        list.Insert(Math.Clamp(index ?? list.Count, 0, list.Count), instance);
        return instance;
    }

    public bool Remove(string instanceId)
    {
        if (!TryFind(instanceId, out var zone, out var index))
            return false;
        GetZone(zone).RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Moves a widget to <paramref name="toIndex"/> in <paramref name="toZone"/>, where the index is the
    /// insertion point as seen before the move (i.e. what a drop indicator shows).
    /// </summary>
    public bool Move(string instanceId, WidgetZone toZone, int toIndex)
    {
        if (!TryFind(instanceId, out var fromZone, out var fromIndex))
            return false;

        var source = GetZone(fromZone);
        var instance = source[fromIndex];
        source.RemoveAt(fromIndex);

        if (fromZone == toZone && fromIndex < toIndex)
            toIndex--;

        var target = GetZone(toZone);
        target.Insert(Math.Clamp(toIndex, 0, target.Count), instance);
        return fromZone != toZone || fromIndex != toIndex;
    }
}
