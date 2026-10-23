using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
using Tempo.Core.Settings;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new CheckApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Tempo;component/Themes/Base.xaml") });
        var assembly = Assembly.Load("Tempo");
        if (args.Contains("--native-apps")) return CheckNativeApps(assembly);
        if (args.Contains("--pwa-icons"))
        {
            var pins = assembly.GetType("Tempo.App.Shell.PinnedApps", true)!;
            var read = pins.GetMethod("ReadShortcut", BindingFlags.NonPublic | BindingFlags.Static)!;
            var icons = assembly.GetType("Tempo.App.Shell.ShellIcons", true)!;
            var forPath = icons.GetMethod("ForPath")!;
            var count = 0;
            var missing = 0;
            foreach (var path in System.IO.Directory.EnumerateFiles(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), "*.lnk", System.IO.SearchOption.AllDirectories))
            {
                var shortcut = ((string Target, string Arguments, string Aumid))read.Invoke(null, [path])!;
                if (!Tempo.Core.Shell.AppGrouping.IsBrowserWebApp(shortcut.Aumid))
                    continue;
                count++;
                var resolved = pins.GetMethod("FindWebAppShortcut")!.Invoke(null, [shortcut.Aumid]);
                var expected = resolved is string link ? forPath.Invoke(null, [link, 24]) : null;
                var image = icons.GetMethod("ForWebApp")!.Invoke(null, [shortcut.Aumid, 24, (nint)0]);
                if (expected is null || !ReferenceEquals(expected, image)) missing++;
            }
            Console.WriteLine($"PWA icons: {count - missing}/{count} use installed shortcut icons");
            return count > 0 && missing == 0 ? 0 : 1;
        }
        var themeType = assembly.GetType("Tempo.App.Services.ThemeService", true)!;
        var theme = Activator.CreateInstance(themeType, true)!;
        var appearance = new AppearanceSettings();
        void Apply() => themeType.GetMethod("Apply")!.Invoke(theme, [appearance]);
        Apply();
        if (args.Contains("--context-menu"))
            return CheckContextMenuHighlight(app);
        var taskbarType = assembly.GetType("Tempo.App.Shell.TaskbarHider", true)!;
        var taskbar = Activator.CreateInstance(taskbarType, [System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tempo-slider-check-state.json")])!;
        void ApplyBehavior() => taskbarType.GetMethod("Apply")!.Invoke(taskbar, [TaskbarMode.None]);
        ApplyBehavior();
        var directory = System.IO.Directory.CreateTempSubdirectory("tempo-slider-");
        var settingsType = assembly.GetType("Tempo.App.Services.SettingsService", true)!;
        var settings = Activator.CreateInstance(settingsType, [System.IO.Path.Combine(directory.FullName, "settings.json"), app.Dispatcher])!;
        var behavior = args.Contains("--gap");
        var blurStrengthCheck = args.Contains("--blur-strength");
        if (blurStrengthCheck)
        {
            var blurSettings = (TempoSettings)settingsType.GetProperty("Current")!.GetValue(settings)!;
            blurSettings.Appearance.ThemeId = "Glass";
            blurSettings.Bar.Mode = BarMode.Pill;
            blurSettings.Behavior.HideOnFullscreen = false;
        }
        if (args.Contains("--gap"))
            ((TempoSettings)settingsType.GetProperty("Current")!.GetValue(settings)!).Bar.Mode = BarMode.Pill;
        var servicesType = assembly.GetType("Tempo.App.Widgets.AppServices", true)!;
        var services = Activator.CreateInstance(servicesType, true)!;
        servicesType.GetProperty("Settings")!.SetValue(services, settings);
        servicesType.GetProperty("Theme")!.SetValue(services, theme);
        servicesType.GetProperty("Scheduler")!.SetValue(services, Activator.CreateInstance(assembly.GetType("Tempo.App.Services.PollingScheduler", true)!, [app.Dispatcher]));
        servicesType.GetProperty("Taskbar")!.SetValue(services, taskbar);
        servicesType.GetProperty("Bar")!.SetValue(services, Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.BarState", true)!, true));
        servicesType.GetProperty("Widgets")!.SetValue(services, Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.WidgetRegistry", true)!, true));
        if (args.Contains("--pill"))
            assembly.GetType("Tempo.App.Widgets.BuiltInWidgets", true)!.GetMethod("RegisterAll")!.Invoke(null,
                [servicesType.GetProperty("Widgets")!.GetValue(services)]);
        settingsType.GetEvent("Changed")!.AddEventHandler(settings, (Action)(() => {
            ApplyBehavior();
        }));
        if (args.Contains("--reservation"))
        {
            try { return CheckReservation(assembly, services, settings); }
            finally { ((IDisposable)settings).Dispose(); directory.Delete(true); }
        }
        if (args.Contains("--pomodoro"))
        {
            try { return CheckPomodoro(app, assembly, services); }
            finally
            {
                ((IDisposable)settings).Dispose();
                directory.Delete(true);
            }
        }
        if (args.Contains("--bar-context-menu"))
        {
            try { return CheckBarContextMenu(app, assembly, services); }
            finally
            {
                ((IDisposable)settings).Dispose();
                directory.Delete(true);
            }
        }
        if (args.Contains("--widget-drag"))
        {
            try { return CheckWidgetDrag(app, assembly, services, settings); }
            finally
            {
                ((IDisposable)settings).Dispose();
                directory.Delete(true);
            }
        }
        if (args.Contains("--chrome-icons"))
        {
            try { return CheckChromeIcons(assembly, services); }
            finally
            {
                ((IDisposable)settings).Dispose();
                directory.Delete(true);
            }
        }
        if (args.Contains("--app-groups"))
        {
            try { return CheckAppGroups(app, assembly, services); }
            finally
            {
                ((IDisposable)settings).Dispose();
                directory.Delete(true);
            }
        }
        if (args.Contains("--pin-sets"))
        {
            try { return CheckPinSets(app, assembly, services, settings); }
            finally
            {
                ((IDisposable)settings).Dispose();
                directory.Delete(true);
            }
        }
        if (args.Contains("--tray"))
        {
            try { return CheckTray(app, assembly, services); }
            finally
            {
                ((IDisposable)servicesType.GetProperty("TrayIcons")!.GetValue(services)!).Dispose();
                ((IDisposable)settings).Dispose();
                directory.Delete(true);
            }
        }
        if (args.Contains("--flyouts"))
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Tempo;component/Widgets/WidgetTemplates.xaml") });
            var contextType = assembly.GetType("Tempo.App.Widgets.WidgetContext", true)!;
            var context = Activator.CreateInstance(contextType, ["flyout-check", services, (Action<object>)(_ => { })])!;
            object Create(string name, string settingsName = null)
            {
                var type = assembly.GetType("Tempo.App.Widgets." + name, true)!;
                return settingsName is null ? Activator.CreateInstance(type, [context])!
                    : Activator.CreateInstance(type, [context, Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets." + settingsName, true)!, true)])!;
            }
            var network = Create("NetworkWidget", "NetworkSettings");
            var clock = Create("ClockWidget", "ClockSettings");
            var notifications = Create("NotificationsWidget");
            network.GetType().GetMethod("Start")!.Invoke(network, null);
            clock.GetType().GetMethod("Start")!.Invoke(clock, null);
            notifications.GetType().GetMethod("Start")!.Invoke(notifications, null);
            var host = new StackPanel { Orientation = Orientation.Horizontal };
            host.Children.Add(new ContentControl { Content = network });
            host.Children.Add(new ContentControl { Content = clock });
            host.Children.Add(new ContentControl { Content = notifications });
            var flyoutWindow = new Window { Content = host, Width = 500, Height = 100, Left = 100, Top = 100, ShowActivated = false };
            flyoutWindow.Show();
            Pump();
            var networkToggle = Descendants<ToggleButton>(host).First();
            networkToggle.IsChecked = true;
            Pump();
            var networkPopup = Descendants<Popup>(host).First(p => p.IsOpen);
            if (!Descendants<TextBlock>(networkPopup.Child).Any(t => t.Text == "Network"))
                throw new Exception("Network panel did not open.");
            var clockType = clock.GetType();
            clockType.GetProperty("IsFlyoutOpen")!.SetValue(clock, true);
            Pump();
            if (networkPopup.IsOpen) throw new Exception("Opening calendar did not close network.");
            var calendarPopup = Descendants<Popup>(host).Single(p => p.IsOpen);
            if (!Descendants<TextBlock>(calendarPopup.Child).Any(t => t.Text == "Network") &&
                Descendants<TextBlock>(calendarPopup.Child).Any(t => t.Text == "Notifications"))
                throw new Exception("Calendar still contains notifications.");
            Descendants<ToggleButton>(host).Last().IsChecked = true;
            for (var i = 0; i < 20; i++) { Pump(); Thread.Sleep(50); }
            var popup = Descendants<Popup>(host).Single(p => p.IsOpen);
            if (!Descendants<TextBlock>(popup.Child).Any(t => t.Text == "Notifications"))
                throw new Exception("Notifications widget did not open its panel.");
            if (calendarPopup.IsOpen) throw new Exception("Opening notifications did not close calendar.");
            if (!Descendants<Button>(popup.Child).Any(b => b.Content as string == "Open Windows notifications"))
                throw new Exception("Notification fallback is missing.");
            ((IDisposable)notifications).Dispose();
            ((IDisposable)clock).Dispose();
            ((IDisposable)network).Dispose();
            flyoutWindow.Close();
            ((IDisposable)settings).Dispose();
            directory.Delete(true);
            Console.WriteLine("PASS: custom network panel, independent calendar and notifications widgets, exclusive flyouts, notification fallback.");
            return 0;
        }
        if (args.Contains("--app-reorder"))
        {
            assembly.GetType("Tempo.App.Widgets.BuiltInWidgets", true)!.GetMethod("RegisterAll")!.Invoke(null,
                [servicesType.GetProperty("Widgets")!.GetValue(services)]);
            var reorderSettings = (TempoSettings)settingsType.GetProperty("Current")!.GetValue(settings)!;
            reorderSettings.Layout.Left.Clear();
            reorderSettings.Layout.Center.Clear();
            reorderSettings.Layout.Right.Clear();
            var instance = reorderSettings.Layout.Add(Tempo.Core.Layout.WidgetTypes.Apps, Tempo.Core.Layout.WidgetZone.Center,
                settings: new System.Text.Json.Nodes.JsonObject { ["showTaskbarPins"] = false });
            var modelType = assembly.GetType("Tempo.App.Bar.BarViewModel", true)!;
            var model = Activator.CreateInstance(modelType, [services])!;
            void Reconcile() => modelType.GetMethod("Reconcile")!.Invoke(model, [reorderSettings.Layout]);
            settingsType.GetEvent("Changed")!.AddEventHandler(settings, (Action)Reconcile);
            Reconcile();
            var center = (System.Collections.IList)modelType.GetProperty("Center")!.GetValue(model)!;
            var widget = center[0]!;
            var widgetType = widget.GetType();
            var entries = (System.Collections.IList)widgetType.GetProperty("Entries")!.GetValue(widget)!;
            entries.Clear();
            var entryType = assembly.GetType("Tempo.App.Widgets.AppEntry", true)!;
            var first = Activator.CreateInstance(entryType, ["test:first", widget])!;
            var second = Activator.CreateInstance(entryType, ["test:second", widget])!;
            entries.Add(first);
            entries.Add(second);
            var replacements = 0;
            ((System.Collections.Specialized.INotifyCollectionChanged)center).CollectionChanged += (_, _) => replacements++;
            widgetType.GetMethod("BeginReorder")!.Invoke(widget, [first]);
            widgetType.GetMethod("MoveDragged")!.Invoke(widget, [1]);
            widgetType.GetMethod("EndReorder")!.Invoke(widget, null);
            var retained = ReferenceEquals(center[0], widget) && replacements == 0
                && ReferenceEquals(entries[0], second) && ReferenceEquals(entries[1], first);
            var order = instance.Settings["order"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
            var orderSaved = order.SequenceEqual(new[] { "test:second", "test:first" });
            Console.WriteLine($"{(retained ? "PASS" : "FAIL")}: dropping an icon retains the live row and entries ({replacements} row collection changes)");
            Console.WriteLine($"{(orderSaved ? "PASS" : "FAIL")}: dropped icon order is persisted");
            // External settings still need to replace the widget to apply the new configuration.
            settingsType.GetMethod("Update")!.Invoke(settings, [(Action<TempoSettings>)(_ => instance.Settings["showLabels"] = true)]);
            var externalApplied = !ReferenceEquals(center[0], widget);
            Console.WriteLine($"{(externalApplied ? "PASS" : "FAIL")}: external widget settings still apply");
            ((IDisposable)model).Dispose();
            ((IDisposable)settings).Dispose();
            directory.Delete(true);
            return retained && orderSaved && externalApplied ? 0 : 1;
        }
        if (args.Contains("--monitors"))
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var appType = typeof(Tempo.App.App);
            appType.GetField("_settings", flags)!.SetValue(app, settings);
            appType.GetField("_services", flags)!.SetValue(app, services);
            var currentSettings = (TempoSettings)settingsType.GetProperty("Current")!.GetValue(settings)!;
            currentSettings.Bar.Monitor = BarSettings.AllMonitors;
            var reconcile = appType.GetMethod("ReconcileBars", flags)!;
            reconcile.Invoke(app, null);
            var bars = (System.Collections.IDictionary)appType.GetField("_bars", flags)!.GetValue(app)!;
            var monitorsType = assembly.GetType("Tempo.App.Shell.Monitors", true)!;
            var monitors = ((System.Collections.IEnumerable)monitorsType.GetMethod("GetAll")!.Invoke(null, null)!).Cast<object>().ToArray();
            var monitorsPassed = bars.Count == monitors.Length && bars.Count > 0;
            foreach (var monitor in monitors)
            {
                var name = (string)monitor.GetType().GetProperty("DeviceName")!.GetValue(monitor)!;
                var windowForMonitor = (Window)bars[name]!;
                monitorsPassed &= windowForMonitor.IsVisible;
                monitorsPassed &= (string)windowForMonitor.GetType().GetField("_monitor", flags)!.GetValue(windowForMonitor) == name;
            }
            Console.WriteLine($"{(monitorsPassed ? "PASS" : "FAIL")}: All monitors creates {bars.Count} bars for {monitors.Length} connected displays");
            currentSettings.Bar.Monitor = null;
            reconcile.Invoke(app, null);
            monitorsPassed &= bars.Count == 1;
            Console.WriteLine($"{(bars.Count == 1 ? "PASS" : "FAIL")}: switching to primary leaves one bar");
            foreach (Window liveBar in bars.Values)
                liveBar.Close();
            bars.Clear();
            ((IDisposable)settings).Dispose();
            directory.Delete(true);
            return monitorsPassed ? 0 : 1;
        }
        var bar = (Window)Activator.CreateInstance(assembly.GetType("Tempo.App.Bar.BarWindow", true)!, [services])!;
        if (args.Contains("--pill"))
        {
            var monitorsType = assembly.GetType("Tempo.App.Shell.Monitors", true)!;
            var monitors = ((System.Collections.IEnumerable)monitorsType.GetMethod("GetAll")!.Invoke(null, null)!).Cast<object>();
            var monitor = args.Contains("--secondary")
                ? monitors.First(m => !(bool)m.GetType().GetProperty("IsPrimary")!.GetValue(m)!)
                : monitors.First(m => (bool)m.GetType().GetProperty("IsPrimary")!.GetValue(m)!);
            var monitorName = (string)monitor.GetType().GetProperty("DeviceName")!.GetValue(monitor)!;
            bar.Show();
            void SetMode(BarMode mode) => settingsType.GetMethod("Update")!.Invoke(settings,
                [(Action<TempoSettings>)(s => { s.Behavior.HideOnFullscreen = false; s.Bar.Mode = mode; s.Bar.Margin = 32; s.Bar.ReserveSpaceInPill = false; s.Appearance.ThemeId = "Glass"; s.Appearance.Backdrop = BackdropKind.None; s.Bar.Monitor = monitorName; })]);
            void Render()
            {
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.ApplicationIdle,
                    (_, _) => frame.Continue = false, app.Dispatcher);
                Dispatcher.PushFrame(frame);
                timer.Stop();
                DwmFlush();
            }
            var cornersPassed = true;
            for (var round = 0; round < 3; round++)
            {
                SetMode(BarMode.Full);
                Render();
                SetMode(BarMode.Pill);
                Render();
                var hwnd = new System.Windows.Interop.WindowInteropHelper(bar).Handle;
                GetWindowRect(hwnd, out var rect);
                var points = new[] { (rect.Left + 1, rect.Top + 1), (rect.Right - 2, rect.Top + 1),
                    (rect.Left + 1, rect.Bottom - 2), (rect.Right - 2, rect.Bottom - 2) };
                uint[] Sample()
                {
                    var screen = GetDC(0);
                    try { return points.Select(p => GetPixel(screen, p.Item1, p.Item2)).ToArray(); }
                    finally { ReleaseDC(0, screen); }
                }
                var actual = Sample();
                bar.Hide();
                Render();
                var expected = Sample();
                bar.Show();
                Render();
                for (var index = 0; index < points.Length; index++)
                {
                    var (x, y) = points[index];
                    var color = actual[index];
                    var clear = color != 0xFFFFFFFF && color == expected[index];
                    cornersPassed &= clear;
                    Console.WriteLine($"{(clear ? "PASS" : "FAIL")}: pill switch {round + 1}, corner ({x},{y}) = #{color:X6}, expected #{expected[index]:X6} behind bar");
                }
            }
            settingsType.GetMethod("Update")!.Invoke(settings,
                [(Action<TempoSettings>)(s => s.Appearance.Backdrop = BackdropKind.Acrylic)]);
            Render();
            var behind = new Window
            {
                WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
                Left = bar.Left - 80, Top = bar.Top - 80, Width = bar.ActualWidth + 160,
                Height = bar.ActualHeight + 160, Background = System.Windows.Media.Brushes.Red, Topmost = true,
            };
            behind.Show();
            bar.GetType().GetMethod("UpdatePlacement", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, null);
            Render();
            GetWindowRect(new System.Windows.Interop.WindowInteropHelper(bar).Handle, out var liveRect);
            uint ReadSurface()
            {
                var dc = GetDC(0);
                try { return GetPixel(dc, (liveRect.Left + liveRect.Right) / 2, liveRect.Top + 2); }
                finally { ReleaseDC(0, dc); }
            }
            var red = ReadSurface();
            var cornerDc = GetDC(0);
            uint glassCorner;
            try { glassCorner = GetPixel(cornerDc, liveRect.Left + 1, liveRect.Top + 1); }
            finally { ReleaseDC(0, cornerDc); }
            var roundedGlass = (glassCorner & 255) > 150 && ((glassCorner >> 8) & 255) < 5
                && ((glassCorner >> 16) & 255) < 5;
            Console.WriteLine($"{(roundedGlass ? "PASS" : "FAIL")}: glass corner shows unobscured red background: #{glassCorner:X6}");
            behind.Background = System.Windows.Media.Brushes.Blue;
            Render();
            var blue = ReadSurface();
            var live = red != blue && (red & 255) > (blue & 255) && ((blue >> 16) & 255) > ((red >> 16) & 255);
            Console.WriteLine($"{(live ? "PASS" : "FAIL")}: live content behind pill changes surface: red #{red:X6}, blue #{blue:X6}");
            var stripes = new System.Windows.Media.DrawingBrush
            {
                TileMode = System.Windows.Media.TileMode.Tile,
                ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 8, 8),
                Drawing = new System.Windows.Media.DrawingGroup
                {
                    Children =
                    {
                        new System.Windows.Media.GeometryDrawing(System.Windows.Media.Brushes.White, null,
                            new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 8, 8))),
                        new System.Windows.Media.GeometryDrawing(System.Windows.Media.Brushes.Black, null,
                            new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 4, 8))),
                    },
                },
            };
            behind.Background = stripes;
            Render();
            double Contrast()
            {
                var dc = GetDC(0);
                try
                {
                    var values = Enumerable.Range(0, 96).Select(i =>
                        (double)(GetPixel(dc, (liveRect.Left + liveRect.Right) / 2 - 48 + i, liveRect.Top + 2) & 255)).ToArray();
                    return values.Max() - values.Min();
                }
                finally { ReleaseDC(0, dc); }
            }
            var blurredContrast = Contrast();
            void SetStrength(double value) => settingsType.GetMethod("Update")!.Invoke(settings,
                [(Action<TempoSettings>)(s => s.Appearance.BlurStrength = value)]);
            SetStrength(50);
            Render();
            var mediumContrast = Contrast();
            SetStrength(0);
            Render();
            var zeroContrast = Contrast();
            SetStrength(100);
            Render();
            var strengthPassed = mediumContrast > blurredContrast + 10 && zeroContrast > mediumContrast + 10;
            Console.WriteLine($"{(strengthPassed ? "PASS" : "FAIL")}: blur strength stripe contrast: 100% {blurredContrast}, 50% {mediumContrast}, 0% {zeroContrast}");
            settingsType.GetMethod("Update")!.Invoke(settings,
                [(Action<TempoSettings>)(s => s.Appearance.Backdrop = BackdropKind.None)]);
            Render();
            var sharpContrast = Contrast();
            var blurred = sharpContrast > 50 && blurredContrast < sharpContrast * 0.4;
            Console.WriteLine($"{(blurred ? "PASS" : "FAIL")}: live stripe contrast: glass {blurredContrast}, transparent {sharpContrast}");
            settingsType.GetMethod("Update")!.Invoke(settings,
                [(Action<TempoSettings>)(s => { s.Bar.Mode = BarMode.Full; s.Appearance.Backdrop = BackdropKind.Acrylic; })]);
            Render();
            GetWindowRect(new System.Windows.Interop.WindowInteropHelper(bar).Handle, out liveRect);
            MoveWindow(new System.Windows.Interop.WindowInteropHelper(behind).Handle,
                liveRect.Left - 80, liveRect.Top - 160,
                liveRect.Right - liveRect.Left + 160, liveRect.Bottom - liveRect.Top + 240, true);
            Render();
            var fullContrast = Contrast();
            SetStrength(0);
            Render();
            var fullSharpContrast = Contrast();
            var fullStrengthPassed = fullContrast < fullSharpContrast * 0.4 && fullSharpContrast > 50;
            Console.WriteLine($"{(fullStrengthPassed ? "PASS" : "FAIL")}: full-width blur strength: 100% {fullContrast}, 0% {fullSharpContrast}");
            settingsType.GetMethod("Update")!.Invoke(settings,
                [(Action<TempoSettings>)(s => { s.Bar.Mode = BarMode.Pill; s.Appearance.BlurStrength = 100; })]);
            Render();
            var radiiPassed = true;
            var monitorScale = (double)monitor.GetType().GetProperty("Scale")!.GetValue(monitor)!;
            foreach (var configuredRadius in new[] { 0d, 6d, 14d, 36d })
            {
                void SetGlass(bool enabled) => settingsType.GetMethod("Update")!.Invoke(settings,
                    [(Action<TempoSettings>)(s =>
                    {
                        s.Appearance.CornerRadius = configuredRadius;
                        s.Appearance.Backdrop = enabled ? BackdropKind.Acrylic : BackdropKind.None;
                    })]);
                SetGlass(false);
                Render();
                GetWindowRect(new System.Windows.Interop.WindowInteropHelper(bar).Handle, out var radiusRect);
                var effectiveRadius = Math.Min(configuredRadius, bar.Height / 2) * monitorScale;
                var extent = (int)Math.Ceiling(effectiveRadius) + 6;
                var samples = (from corner in Enumerable.Range(0, 4)
                    from y in Enumerable.Range(0, extent).Where(n => n % 3 == 0)
                    from x in Enumerable.Range(0, extent).Where(n => n % 3 == 0)
                    select (X: (corner & 1) == 0 ? radiusRect.Left + x : radiusRect.Right - 1 - x,
                        Y: (corner & 2) == 0 ? radiusRect.Top + y : radiusRect.Bottom - 1 - y,
                        LocalX: x + 0.5, LocalY: y + 0.5)).ToArray();
                uint[] Pixels()
                {
                    var dc = GetDC(0);
                    try { return samples.Select(p => GetPixel(dc, p.X, p.Y)).ToArray(); }
                    finally { ReleaseDC(0, dc); }
                }
                var transparentPixels = Pixels();
                SetGlass(true);
                Render();
                var glassPixels = Pixels();
                var outsideCount = 0;
                var leaks = 0;
                var insideDifferences = new List<double>();
                for (var i = 0; i < samples.Length; i++)
                {
                    var p = samples[i];
                    var distance = Math.Sqrt(Math.Pow(effectiveRadius - p.LocalX, 2) + Math.Pow(effectiveRadius - p.LocalY, 2));
                    var outside = p.LocalX < effectiveRadius && p.LocalY < effectiveRadius && distance > effectiveRadius + 2;
                    var difference = Math.Abs((int)(glassPixels[i] & 255) - (int)(transparentPixels[i] & 255));
                    if (outside)
                    {
                        outsideCount++;
                        if (difference > 3) leaks++;
                    }
                    if (p.LocalX > 3 && p.LocalY > 3 &&
                        (effectiveRadius == 0 || distance < effectiveRadius - 3))
                        insideDifferences.Add(difference);
                }
                var radiusPassed = leaks == 0 && insideDifferences.Count > 0 && insideDifferences.Average() > 10;
                radiiPassed &= radiusPassed;
                Console.WriteLine($"{(radiusPassed ? "PASS" : "FAIL")}: radius {configuredRadius}: {leaks}/{outsideCount} outside pixels affected; interior blur difference {insideDifferences.DefaultIfEmpty().Average():F1}");
            }
            behind.Close();
            bar.Close();
            ((IDisposable)settings).Dispose();
            directory.Delete(true);
            return cornersPassed && roundedGlass && live && blurred && strengthPassed && fullStrengthPassed && radiiPassed ? 0 : 1;
        }
        bar.Show();
        var window = (Window)Activator.CreateInstance(assembly.GetType("Tempo.App.Settings.SettingsWindow", true)!, [services])!;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Show();
        var tabs = Find<TabControl>(window);
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item =>
            AutomationProperties.GetAutomationId(item) == (behavior ? "TabBehavior" : "TabAppearance"));
        window.UpdateLayout();
        app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var slider = Find<Slider>(window, behavior ? "SldMargin" : blurStrengthCheck ? "SldBlurStrength" : "SldOpacity");
        slider.BringIntoView();
        slider.ApplyTemplate();
        window.UpdateLayout();
        app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var track = (Track)slider.Template.FindName("PART_Track", slider);
        var thumb = track.Thumb;
        // A synthetic drag has no physical button held. Suppress mouse moves so
        // Thumb does not interpret the host's released button as a drag ending.
        PreProcessInputEventHandler ignoreMouseMoves = (_, e) =>
        {
            if (e.StagingItem.Input is MouseEventArgs input && input.RoutedEvent == Mouse.MouseMoveEvent)
                e.Cancel();
        };
        InputManager.Current.PreProcessInput += ignoreMouseMoves;
        thumb.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
        var startValue = slider.Value;
        for (var step = 0; step < 5; step++)
        {
            thumb.RaiseEvent(new DragDeltaEventArgs(-10, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            window.UpdateLayout();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
        var current = ((Track)slider.Template.FindName("PART_Track", slider)).Thumb;
        var saved = (TempoSettings)settingsType.GetProperty("Current")!.GetValue(settings)!;
        var storedValue = behavior ? saved.Bar.Margin : blurStrengthCheck ? saved.Appearance.BlurStrength : saved.Appearance.TintOpacity!.Value;
        var passed = ReferenceEquals(thumb, current) && ReferenceEquals(Mouse.Captured, thumb) && thumb.IsDragging && slider.Value < startValue - .1
            && Math.Abs(storedValue - slider.Value) < .011;
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {AutomationProperties.GetAutomationId(slider)} five drag movements {startValue:0.00} → {slider.Value:0.00}; thumb retained={ReferenceEquals(thumb, current)}, mouse capture retained={ReferenceEquals(Mouse.Captured, thumb)}");
        Mouse.Capture(null);
        app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var deferred = (bool)bar.GetType().GetField("_placementDeferred", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;
        var applied = !deferred && Math.Abs(bar.ActualHeight - saved.Bar.Height) < 1;
        Console.WriteLine($"{(applied ? "PASS" : "FAIL")}: bar placement applied after release; height={bar.ActualHeight:0.00}");
        passed &= applied;
        InputManager.Current.PreProcessInput -= ignoreMouseMoves;
        window.Close();
        bar.Close();
        ((IDisposable)settings).Dispose();
        directory.Delete(true);
        return passed ? 0 : 1;
    }

    private static void Pump() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool MoveWindow(nint hwnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out ScreenRect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();

    private sealed class CheckApp : Tempo.App.App
    {
        protected override void OnStartup(StartupEventArgs e) { }
    }

    private static T Find<T>(DependencyObject root, string id = null) where T : DependencyObject
    {
        if (root is T match && (id is null || AutomationProperties.GetAutomationId(root) == id)) return match;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Find<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i), id);
            if (child is not null) return child;
        }
        return null!;
    }
}
