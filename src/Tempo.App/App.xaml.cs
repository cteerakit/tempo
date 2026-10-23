using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using Tempo.App.Bar;
using Tempo.App.Services;
using Tempo.App.Shell;
using Tempo.App.Widgets;

namespace Tempo.App;

public partial class App : Application
{
    private const string MutexName = @"Local\Tempo.SingleInstance";
    private const string OpenSettingsEventName = @"Local\Tempo.OpenSettings";
    private const string ExitEventName = @"Local\Tempo.Exit";

    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tempo");

    private Mutex? _instanceMutex;
    private EventWaitHandle? _openSettingsSignal, _exitSignal;
    private TaskbarHider? _taskbar;
    private SettingsService? _settings;
    private AppServices? _services;
    private readonly Dictionary<string, BarWindow> _bars = new(StringComparer.OrdinalIgnoreCase);
    private TaskbarIcon? _tray;
    private Settings.SettingsWindow? _settingsWindow;
    private IDisposable? _taskbarWatch, _updateWatch;
    private UpdateService? _updates;
    private MenuItem? _updateTrayItem;
    private bool _skipUpdateOnExit;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(DataDirectory);
        Log.Initialize(Path.Combine(DataDirectory, "tempo.log"));

        // Emergency switch: always restores the Windows taskbar, even if Tempo is wedged.
        if (e.Args.Contains("--restore-taskbar", StringComparer.OrdinalIgnoreCase))
        {
            Signal(ExitEventName);
            RestoreTaskbar();
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Launching Tempo again opens the running instance's settings.
            Signal(OpenSettingsEventName);
            Shutdown();
            return;
        }

        _taskbar = new TaskbarHider(Path.Combine(DataDirectory, "taskbar-state.json"));

        // Created up front so a second launch during startup still gets through (the events stay signalled).
        _openSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsEventName);
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);

        InstallSafetyNets();
        OutsideClickWatcher.Initialize(Dispatcher);
        _taskbar.RecoverFromPreviousRun();

        _settings = new SettingsService(Path.Combine(DataDirectory, "settings.json"), Dispatcher);
        _updates = new UpdateService();
        var registry = new WidgetRegistry();
        BuiltInWidgets.RegisterAll(registry);
        _services = new AppServices
        {
            Settings = _settings,
            Theme = new ThemeService(),
            Scheduler = new PollingScheduler(Dispatcher),
            Taskbar = _taskbar,
            Widgets = registry,
            Bar = new BarState(),
            OpenSettings = OpenSettings,
            Updates = _updates,
        };

        ReconcileBars();
        _settings.Changed += ReconcileBars;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ApplyBehavior();
        _settings.Changed += ApplyBehavior;
        _taskbarWatch = _services.Scheduler.Every(TimeSpan.FromSeconds(1), _taskbar.Reassert, runNow: false);

        CreateTrayIcon();
        ListenForSignals();
        _updates.CheckSoon();
        _updateWatch = _services.Scheduler.Every(UpdateService.CheckInterval, () => _ = _updates.CheckAsync(), runNow: false);
        Log.Info($"Tempo {typeof(App).Assembly.GetName().Version} started.");
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ReconcileBars);

    private void ReconcileBars()
    {
        var services = _services!;
        var selected = _settings!.Current.Bar.Monitor;
        var monitors = Monitors.GetAll();
        var targets = selected == Core.Settings.BarSettings.AllMonitors
            ? monitors
            : monitors.Where(m => m.DeviceName == Monitors.Find(selected).DeviceName).ToList();
        var names = targets.Select(m => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _bars.Keys.Where(name => !names.Contains(name)).ToArray())
        {
            _bars[name].Close();
            _bars.Remove(name);
        }
        foreach (var monitor in targets)
        {
            if (_bars.ContainsKey(monitor.DeviceName))
                continue;
            var barServices = new AppServices
            {
                Settings = services.Settings,
                Theme = services.Theme,
                Scheduler = new PollingScheduler(Dispatcher),
                Taskbar = services.Taskbar,
                Widgets = services.Widgets,
                Bar = new BarState(),
                OpenSettings = services.OpenSettings,
                Updates = services.Updates,
                TrayIcons = services.TrayIcons,
            };
            var window = new BarWindow(barServices, monitor.DeviceName);
            _bars.Add(monitor.DeviceName, window);
            window.Show();
        }
    }

    private void ApplyBehavior()
    {
        var behavior = _settings!.Current.Behavior;
        _taskbar!.Apply(behavior.TaskbarMode);
        StartupRegistration.Sync(behavior.StartWithWindows);
    }

    internal void OpenSettings()
    {
        if (_services is null)
            return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new Settings.SettingsWindow(_services);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private void CreateTrayIcon()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("BarContextMenu") };
        _updateTrayItem = TrayItem("Restart to update", "", RestartToUpdate);
        menu.Items.Add(_updateTrayItem);
        menu.Items.Add(TrayItem("Customize Tempo…", "\uE713", OpenSettings));
        menu.Items.Add(TrayItem("Restore Windows taskbar", "\uE7F4", () =>
            _settings!.Update(s => s.Behavior.TaskbarMode = Core.Settings.TaskbarMode.None)));
        menu.Items.Add(new Separator { Style = (Style)FindResource("BarMenuSeparator") });
        menu.Items.Add(TrayItem("Exit Tempo", "\uE7E8", Shutdown));

        _tray = new TaskbarIcon
        {
            ToolTipText = "Tempo",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Tempo;component/Assets/Tempo.ico")),
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        _tray.TrayLeftMouseUp += (_, _) => OpenSettings();
        _updates!.PropertyChanged += OnUpdatesChanged;
        OnUpdatesChanged(null, new PropertyChangedEventArgs(null));
        // Efficiency mode would throttle the bar's UI thread, so it stays off.
        _tray.ForceCreate(enablesEfficiencyMode: false);
    }

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        var ready = _updates!.Pending;
        _updateTrayItem!.Visibility = ready is null ? Visibility.Collapsed : Visibility.Visible;
        _updateTrayItem.Header = ready is null ? "Restart to update" : $"Restart to update to {ready.Version}";
        _tray!.ToolTipText = ready is null ? "Tempo" : $"Tempo — update {ready.Version} ready";
    }

    internal void RestartToUpdate()
    {
        _updates?.ApplyAfterExit(restart: true);
        Shutdown();
    }

    private MenuItem TrayItem(string header, string glyph, Action action)
    {
        var item = new MenuItem { Header = header, Tag = glyph, Style = (Style)FindResource("BarMenuItem") };
        item.Click += (_, _) => action();
        return item;
    }

    private void ListenForSignals()
    {
        WaitHandle[] signals = [_openSettingsSignal!, _exitSignal!];
        var thread = new Thread(() =>
        {
            while (true)
            {
                var which = WaitHandle.WaitAny(signals);
                // Exit requests come from --restore-taskbar or the uninstaller, so no update is installed.
                if (which == 1)
                    _skipUpdateOnExit = true;
                Dispatcher.BeginInvoke(which == 0 ? OpenSettings : Shutdown);
                if (which == 1)
                    return;
            }
        })
        { IsBackground = true, Name = "Tempo signal listener" };
        thread.Start();
    }

    private static void RestoreTaskbar() => new TaskbarHider(Path.Combine(DataDirectory, "taskbar-state.json")).Restore();

    /// <summary>Runs in a short-lived process launched by the uninstaller: stop Tempo and undo its changes to Windows.</summary>
    internal static void PrepareForUninstall()
    {
        Signal(ExitEventName);
        try
        {
            using var instance = new Mutex(false, MutexName);
            if (instance.WaitOne(TimeSpan.FromSeconds(10)))
                instance.ReleaseMutex();
        }
        catch (AbandonedMutexException)
        {
        }
        RestoreTaskbar();
        StartupRegistration.Sync(false);
    }

    private static void Signal(string name)
    {
        if (EventWaitHandle.TryOpenExisting(name, out var handle))
        {
            handle.Set();
            handle.Dispose();
        }
    }

    /// <summary>Whatever happens, the Windows taskbar must come back.</summary>
    private void InstallSafetyNets()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _taskbar?.Restore();
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Fatal unhandled exception", args.ExceptionObject as Exception);
            _taskbar?.Restore();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            // Keep the bar alive: a bug in one widget shouldn't take the desktop down with it.
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        SessionEnding += (_, _) =>
        {
            // Too late to start the updater; a downloaded update installs at the next launch instead.
            _skipUpdateOnExit = true;
            _taskbar?.Restore();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbarWatch?.Dispose();
        _updateWatch?.Dispose();
        _settingsWindow?.Close();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        if (_settings is not null)
            _settings.Changed -= ReconcileBars;
        foreach (var bar in _bars.Values)
            bar.Close();
        _bars.Clear();
        _services?.TrayIcons.Dispose();
        _tray?.Dispose();
        _settings?.Dispose();
        _taskbar?.Restore();
        if (!_skipUpdateOnExit)
            _updates?.ApplyAfterExit(restart: false);
        if (_instanceMutex is not null)
        {
            _instanceMutex.Dispose();
            Log.Info("Tempo exited.");
        }
        base.OnExit(e);
    }
}
