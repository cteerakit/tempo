using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tempo.App.Shell;
using Tempo.Core.Shell;
using static Tempo.App.Interop.NativeMethods;

namespace Tempo.App.Widgets;

internal sealed class AppsSettings
{
    public Dictionary<string, AppPinSet> PinSets { get; set; } = [];
    [DisplayName("Show apps pinned to the Windows taskbar")]
    public bool ShowTaskbarPins { get; set; } = true;

    [DisplayName("Show app names")]
    public bool ShowLabels { get; set; }

    [DisplayName("Show separator between pinned and unpinned apps")]
    public bool ShowSeparator { get; set; } = true;

    /// <summary>Executables or <c>shell:AppsFolder\AUMID</c> targets pinned from Tempo.</summary>
    public List<string> Pins { get; set; } = [];

    /// <summary>Taskbar shortcuts (by .lnk path) the user unpinned from Tempo.</summary>
    public List<string> HiddenPins { get; set; } = [];

    /// <summary>Button order from drag-and-drop, as group keys; unlisted apps go after these.</summary>
    public List<string> Order { get; set; } = [];
}

internal sealed class AppPinSet
{
    public List<string> Pins { get; set; } = [];
    public List<string> HiddenPins { get; set; } = [];
    public List<string> Order { get; set; } = [];
}

internal sealed partial class AppWindowItem(nint hwnd) : ObservableObject
{
    public nint Hwnd { get; } = hwnd;

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsMinimized { get; set; }

    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }
}

internal sealed partial class AppEntry(string key, AppsWidget owner) : ObservableObject
{
    public string Key { get; } = key;

    public AppsWidget Owner { get; } = owner;

    public ObservableCollection<AppWindowItem> Windows { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPinned))]
    public partial PinnedApp? Pin { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsFlashing { get; set; }

    [ObservableProperty]
    public partial bool IsWindowListOpen { get; set; }

    /// <summary>True while the hover preview (live window thumbnails) is showing.</summary>
    [ObservableProperty]
    public partial bool IsPreviewOpen { get; set; }

    /// <summary>True while this button is being dragged to a new position.</summary>
    [ObservableProperty]
    public partial bool IsDragging { get; set; }

    partial void OnIsWindowListOpenChanged(bool value)
    {
        if (value)
        {
            IsPreviewOpen = false;
            Services.OutsideClickWatcher.RegisterFlyout(this, () => IsWindowListOpen = false);
        }
        else
            Services.OutsideClickWatcher.Unregister(this);
    }

    [ObservableProperty]
    public partial string ToolTip { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowSeparatorBefore { get; set; }

    public bool IsPinned => Pin is not null;

    /// <summary>What to start for "launch" and "new window": the pin, else the running app's identity.</summary>
    public string? LaunchTarget { get; set; }

    /// <summary>What "Pin to Tempo" stores for an unpinned running app.</summary>
    public string? PinTarget { get; set; }
}

/// <summary>Pinned apps plus running windows, grouped per app like the Windows taskbar.</summary>
internal sealed partial class AppsWidget(WidgetContext context, AppsSettings settings) : WidgetViewModelBase(context)
{
    private WindowTracker? _tracker;
    private List<PinnedApp> _taskbarPins = [];
    private FileSystemWatcher? _pinWatcher;
    private readonly HashSet<nint> _flashing = [];
    private bool _refreshQueued;
    private int _iconPixels;
    private AppEntry? _dragged;
    private string? _dragSetId;

    public AppsSettings Settings { get; } = settings;

    private AppPinSet? ActiveSet
    {
        get
        {
            var id = Services.Settings.Current.PinSets.ActiveId;
            if (id == "default")
                return null;
            if (!Settings.PinSets.TryGetValue(id, out var set))
                Settings.PinSets[id] = set = new();
            return set;
        }
    }

    private List<string> Pins => ActiveSet?.Pins ?? Settings.Pins;
    private List<string> HiddenPins => ActiveSet?.HiddenPins ?? Settings.HiddenPins;
    private List<string> Order
    {
        get => ActiveSet?.Order ?? Settings.Order;
        set
        {
            if (ActiveSet is { } set) set.Order = value;
            else Settings.Order = value;
        }
    }

    public ObservableCollection<AppEntry> Entries { get; } = [];

    public override void Start()
    {
        Services.Settings.Changed += QueueRefresh;
        Own(() => Services.Settings.Changed -= QueueRefresh);
        _tracker = new WindowTracker();
        _tracker.WindowsChanged += QueueRefresh;
        _tracker.WindowFlashed += hwnd =>
        {
            _flashing.Add(hwnd);
            QueueRefresh();
        };
        Own(_tracker);

        if (Settings.ShowTaskbarPins)
        {
            _taskbarPins = PinnedApps.LoadTaskbarPins();
            if (Directory.Exists(PinnedApps.TaskbarPinFolder))
            {
                _pinWatcher = new FileSystemWatcher(PinnedApps.TaskbarPinFolder, "*.lnk") { EnableRaisingEvents = true };
                FileSystemEventHandler reload = (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    _taskbarPins = PinnedApps.LoadTaskbarPins();
                    QueueRefresh();
                });
                _pinWatcher.Created += reload;
                _pinWatcher.Deleted += reload;
                _pinWatcher.Renamed += (s, e) => reload(s, e);
                Own(_pinWatcher);
            }
        }

        // Shell hooks cover most changes; polling catches the rest (e.g. windows that uncloak later).
        Every(TimeSpan.FromSeconds(1), Refresh);
    }

    [RelayCommand]
    private void Click(AppEntry entry)
    {
        entry.IsPreviewOpen = false;
        switch (entry.Windows.Count)
        {
            case 0:
                Launch(entry);
                break;
            case 1:
                ToggleWindow(entry.Windows[0]);
                break;
            default:
                entry.IsWindowListOpen = !entry.IsWindowListOpen;
                break;
        }
    }

    [RelayCommand]
    private void Launch(AppEntry entry)
    {
        if (entry.LaunchTarget is { } target)
            ShellActions.Launch(target);
    }

    [RelayCommand]
    private void ActivateWindow(AppWindowItem window)
    {
        foreach (var entry in Entries)
        {
            entry.IsWindowListOpen = false;
            entry.IsPreviewOpen = false;
        }
        ShellActions.Activate(window.Hwnd);
    }

    [RelayCommand]
    private void CloseWindow(AppWindowItem window) => ShellActions.Close(window.Hwnd);

    [RelayCommand]
    private void CloseAll(AppEntry entry)
    {
        foreach (var window in entry.Windows)
            ShellActions.Close(window.Hwnd);
    }

    [RelayCommand]
    private void PinToTempo(AppEntry entry)
    {
        if (entry.PinTarget is not { } target || Pins.Contains(target, StringComparer.OrdinalIgnoreCase))
            return;
        Pins.Add(target);
        Context.SaveSettings(Settings);
    }

    [RelayCommand]
    private void Unpin(AppEntry entry)
    {
        if (entry.Pin is not { } pin)
            return;
        if (Pins.RemoveAll(p => string.Equals(p, pin.Id, StringComparison.OrdinalIgnoreCase)) == 0)
            HiddenPins.Add(pin.Id);
        Context.SaveSettings(Settings);
    }

    private static void ToggleWindow(AppWindowItem window)
    {
        if (GetForegroundWindow() == window.Hwnd && !IsIconic(window.Hwnd))
            ShellActions.Minimize(window.Hwnd);
        else
            ShellActions.Activate(window.Hwnd);
    }

    private void QueueRefresh()
    {
        if (_dragged is not null && _dragSetId != Services.Settings.Current.PinSets.ActiveId)
        {
            _dragged.IsDragging = false;
            _dragged = null;
        }
        if (_refreshQueued)
            return;
        _refreshQueued = true;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            _refreshQueued = false;
            Refresh();
        });
    }

    // ───────────── Drag to reorder ─────────────

    public void BeginReorder(AppEntry entry)
    {
        foreach (var e in Entries)
        {
            e.IsWindowListOpen = false;
            e.IsPreviewOpen = false;
        }
        _dragged = entry;
        _dragSetId = Services.Settings.Current.PinSets.ActiveId;
        entry.IsDragging = true;
    }

    /// <summary>Moves the dragged button live; refreshes are paused meanwhile so it doesn't jump back.</summary>
    public void MoveDragged(int index)
    {
        if (_dragged is null)
            return;
        var from = Entries.IndexOf(_dragged);
        var first = Entries.ToList().FindIndex(e => e.IsPinned == _dragged.IsPinned);
        var last = Entries.ToList().FindLastIndex(e => e.IsPinned == _dragged.IsPinned);
        index = Math.Clamp(index, first, last);
        if (from >= 0 && from != index)
        {
            Entries.Move(from, index);
            UpdateSeparator();
        }
    }

    public void EndReorder()
    {
        if (_dragged is null)
            return;
        _dragged.IsDragging = false;
        _dragged = null;

        var order = AppOrdering.Merge(Entries.Select(e => e.Key), Order);
        if (!order.SequenceEqual(Order, StringComparer.OrdinalIgnoreCase))
        {
            Order = order;
            Context.SaveSettings(Settings);
        }
    }

    private void Refresh()
    {
        if (_tracker is null || _dragged is not null)
            return;

        // Icons are requested at exactly the size they're drawn in device pixels: letting WPF scale
        // a larger bitmap down by a non-integer factor (e.g. 48 → 22) visibly blurs them.
        var iconPixels = (int)Math.Round(Bar.IconSize * Bar.Scale);
        if (iconPixels != _iconPixels)
        {
            _iconPixels = iconPixels;
            foreach (var entry in Entries)
                entry.Icon = null;
        }

        var foreground = GetForegroundWindow();
        _flashing.Remove(foreground);
        var windows = _tracker.GetWindows();
        _flashing.IntersectWith(windows.Select(w => w.Hwnd));

        // 1. Pinned items, in order.
        var hidden = new HashSet<string>(HiddenPins, StringComparer.OrdinalIgnoreCase);
        var pins = (Settings.ShowTaskbarPins && ActiveSet is null ? _taskbarPins.Where(p => !hidden.Contains(p.Id)) : [])
            .Concat(Pins.Select(PinnedApps.FromTarget))
            .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase);

        var groups = new List<(string Key, PinnedApp? Pin, List<TrackedWindow> Windows)>();
        foreach (var pin in pins)
            groups.Add(("pin:" + pin.Id.ToLowerInvariant(), pin, []));

        // 2. Running windows join their pin, or a group of their own.
        foreach (var window in windows)
        {
            var index = groups.FindIndex(g => g.Pin is { } p && AppGrouping.MatchesPin(p.Aumid, p.TargetPath, window.Aumid, window.ExePath, p.Arguments));
            if (index < 0)
            {
                var key = AppGrouping.GetGroupKey(window.Aumid, window.ExePath, window.Hwnd);
                index = groups.FindIndex(g => g.Key == key);
                if (index < 0)
                {
                    groups.Add((key, null, []));
                    index = groups.Count - 1;
                }
            }
            groups[index].Windows.Add(window);
        }

        Sync(AppOrdering.Apply(groups, g => g.Key, Order, g => g.Pin is not null), foreground);
    }

    private void Sync(List<(string Key, PinnedApp? Pin, List<TrackedWindow> Windows)> groups, nint foreground)
    {
        var existing = Entries.ToDictionary(e => e.Key);
        for (var i = 0; i < groups.Count; i++)
        {
            var (key, pin, windows) = groups[i];
            if (!existing.TryGetValue(key, out var entry))
            {
                entry = new AppEntry(key, this);
                Entries.Insert(i, entry);
            }
            else
            {
                var current = Entries.IndexOf(entry);
                if (current != i)
                    Entries.Move(current, i);
            }
            Update(entry, pin, windows, foreground);
        }
        while (Entries.Count > groups.Count)
            Entries.RemoveAt(Entries.Count - 1);
        UpdateSeparator();
    }

    private void UpdateSeparator()
    {
        var firstUnpinned = Settings.ShowSeparator && Entries.Any(e => e.IsPinned)
            ? Entries.FirstOrDefault(e => !e.IsPinned) : null;
        foreach (var entry in Entries)
            entry.ShowSeparatorBefore = entry == firstUnpinned;
    }

    private void Update(AppEntry entry, PinnedApp? pin, List<TrackedWindow> windows, nint foreground)
    {
        var first = windows.FirstOrDefault();
        entry.Pin = pin;
        entry.IsRunning = windows.Count > 0;
        entry.IsActive = windows.Any(w => w.Hwnd == foreground);
        entry.IsFlashing = windows.Any(w => _flashing.Contains(w.Hwnd));

        var isWebApp = AppGrouping.IsBrowserWebApp(first?.Aumid);
        var appsFolderPath = first?.Aumid is { } aumid && (first.IsPackaged || isWebApp) ? $@"shell:AppsFolder\{aumid}" : null;
        var profileShortcut = first?.Aumid is { } browserId && AppGrouping.IsBrowserIdentity(browserId)
            ? PinnedApps.FindProfileShortcut(browserId, first.Hwnd) : null;
        var fallbackTarget = AppGrouping.IsBrowserIdentity(first?.Aumid) && !isWebApp ? null : first?.ExePath;
        entry.LaunchTarget = pin?.LaunchPath ?? profileShortcut ?? appsFolderPath ?? fallbackTarget;
        entry.PinTarget = profileShortcut ?? appsFolderPath ?? fallbackTarget;

        if (string.IsNullOrEmpty(entry.Name))
            entry.Name = pin?.DisplayName ?? (isWebApp ? first?.Title : DescribeExe(first?.ExePath)) ?? first?.Title ?? "App";

        var isBrowserProfile = AppGrouping.IsBrowserIdentity(first?.Aumid ?? pin?.Aumid)
                               && !AppGrouping.IsBrowserWebApp(first?.Aumid ?? pin?.Aumid);
        if (entry.Icon is null || isBrowserProfile)
        {
            var iconPath = pin?.LaunchPath ?? profileShortcut ?? appsFolderPath ?? first?.ExePath;
            var webAppId = isWebApp ? first?.Aumid : pin?.Aumid;
            entry.Icon = (AppGrouping.IsBrowserWebApp(webAppId) ? ShellIcons.ForWebApp(webAppId!, _iconPixels, first?.Hwnd ?? 0) : null)
                         ?? (isBrowserProfile ? ShellIcons.ForBrowserProfile(first?.Hwnd ?? 0, pin?.LaunchPath ?? profileShortcut, _iconPixels) : null)
                         ?? (iconPath is null ? null : ShellIcons.ForPath(iconPath, _iconPixels))
                         ?? (first is null ? null : ShellIcons.ForWindow(first.Hwnd));
        }

        // Window list, kept in place so open flyouts don't flicker.
        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var item = entry.Windows.FirstOrDefault(x => x.Hwnd == w.Hwnd);
            if (item is null)
            {
                item = new AppWindowItem(w.Hwnd) { Icon = isWebApp ? entry.Icon : ShellIcons.ForWindow(w.Hwnd) ?? entry.Icon };
                entry.Windows.Insert(Math.Min(i, entry.Windows.Count), item);
            }
            else if (entry.Windows.IndexOf(item) is var at && at != i)
            {
                entry.Windows.Move(at, i);
            }
            item.Title = w.Title;
            item.IsActive = w.Hwnd == foreground;
            item.IsMinimized = w.IsMinimized;
            if (isBrowserProfile) item.Icon = entry.Icon;
        }
        while (entry.Windows.Count > windows.Count)
            entry.Windows.RemoveAt(entry.Windows.Count - 1);

        if (entry.Windows.Count < 2)
            entry.IsWindowListOpen = false;
        if (entry.Windows.Count == 0)
            entry.IsPreviewOpen = false;

        entry.ToolTip = windows.Count switch
        {
            0 => entry.Name,
            1 => windows[0].Title,
            _ => $"{entry.Name} — {windows.Count} windows",
        };
    }

    private static string? DescribeExe(string? exe)
    {
        if (exe is null || !File.Exists(exe))
            return null;
        try
        {
            var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(exe) : description;
        }
        catch (FileNotFoundException)
        {
            return Path.GetFileNameWithoutExtension(exe);
        }
    }
}

