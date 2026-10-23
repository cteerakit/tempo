using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using ManagedShell.WindowsTray;

internal static partial class Program
{
    private static int CheckTray(Application app, Assembly assembly, object services)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Tempo;component/Widgets/WidgetTemplates.xaml") });
        var context = Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.WidgetContext", true)!,
            ["tray-check", services, (Action<object>)(_ => { })])!;
        var type = assembly.GetType("Tempo.App.Widgets.HiddenIconsWidget", true)!;
        var widget = Activator.CreateInstance(type, [context])!;
        var host = new ContentControl { Content = widget };
        var window = new Window { Content = host, Width = 300, Height = 80, Left = 100, Top = 400, ShowActivated = false };
        try
        {
            type.GetMethod("Start")!.Invoke(widget, null);
            window.Show();
            type.GetProperty("IsFlyoutOpen")!.SetValue(widget, true);
            using var fixture = new TaskbarIcon
            {
                ToolTipText = "Tempo tray integration check",
                IconSource = new BitmapImage(new Uri("pack://application:,,,/Tempo;component/Assets/Tempo.ico")),
                NoLeftClickDelay = true,
            };
            var left = 0;
            var right = 0;
            fixture.TrayLeftMouseUp += (_, _) => left++;
            fixture.TrayRightMouseUp += (_, _) => right++;
            fixture.ForceCreate(enablesEfficiencyMode: false);
            void WaitFor(Func<bool> check, string failure)
            {
                var until = DateTime.UtcNow.AddSeconds(5);
                while (!check() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(25); }
                if (!check()) { Console.WriteLine("Status: " + type.GetProperty("Status")!.GetValue(widget)); Console.WriteLine("Icons: " + string.Join(" | ", Icons().Select(i => i.Title))); throw new Exception(failure); }
            }
            IEnumerable<NotifyIcon> Icons() => ((IEnumerable)type.GetProperty("Icons")!.GetValue(widget)!).Cast<NotifyIcon>();
            WaitFor(() => Icons().Any(i => i.Title == fixture.ToolTipText), "Custom tray did not receive a real Shell_NotifyIcon registration.");
            var icon = Icons().Single(i => i.Title == fixture.ToolTipText);
            Pump();
            var popup = Descendants<Popup>(host).Single(p => p.IsOpen);
            var button = Descendants<Button>(popup.Child).Single(b => ReferenceEquals(b.DataContext, icon));
            if (Descendants<Image>(button).Single().Source is null) throw new Exception("Tray image missing.");
            var templates = new Tempo.App.Widgets.WidgetTemplates();

            void SendMouse(string handler, MouseButton mouse)
            {
                var evt = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, mouse) { RoutedEvent = UIElement.PreviewMouseDownEvent };
                templates.GetType().GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(templates, [button, evt]);
            }
            
            // Keyboard/UI Automation activation uses the same callback protocol as a physical click.
            templates.GetType().GetMethod("TrayIcon_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(templates, [button, new RoutedEventArgs()]);
            WaitFor(() => left > 0, "Tray left-click did not reach the owning application.");
            SendMouse("TrayIcon_MouseDown", MouseButton.Right);
            // The harness has no physical pointer over the button, so call the icon's release protocol directly.
            icon.IconMouseUp(MouseButton.Right, 0, 500);
            button.ReleaseMouseCapture();
            WaitFor(() => right > 0, "Tray right-click did not reach the owning application.");
            fixture.ToolTipText = "Tempo tray updated";
            WaitFor(() => icon.Title == "Tempo tray updated", "Tray tooltip update was not reflected.");
            var root = (FrameworkElement)popup.Child;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right), (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tempo-tray-check.png");
            using (var stream = System.IO.File.Create(output)) encoder.Save(stream);
            fixture.TrayIcon.Remove();
            WaitFor(() => !Icons().Contains(icon), "Removed tray icon remained visible.");
            var trays = (IEnumerable<nint>)assembly.GetType("Tempo.App.Shell.TaskbarHider", true)!.GetMethod("GetTrayWindows")!.Invoke(null, null)!;
            foreach (var tray in trays)
            {
                ManagedShell.Interop.NativeMethods.GetWindowThreadProcessId(tray, out var pid);
                if (pid == Environment.ProcessId) throw new Exception("Taskbar hider includes Tempo's tray listener.");
            }
            var second = Activator.CreateInstance(type, [context])!;
            try
            {
                type.GetMethod("Start")!.Invoke(second, null);
                if (!ReferenceEquals(type.GetProperty("Icons")!.GetValue(widget), type.GetProperty("Icons")!.GetValue(second)))
                    throw new Exception("Widgets on different bars do not share the tray listener.");
                ((IDisposable)widget).Dispose();
                fixture.TrayIcon.Create();
                WaitFor(() => Icons().Any(i => i.Title == fixture.ToolTipText), "Removing one widget stopped another widget's listener.");
            }
            finally { ((IDisposable)second).Dispose(); }
            Console.WriteLine("PASS: real tray registration, custom popup image, left/right callbacks, tooltip updates, GUID removal, taskbar listener exclusion, shared listener lifetime.");
            Console.WriteLine("Preview: " + output);
            return 0;
        }
        finally
        {
            ((IDisposable)widget).Dispose();
            window.Close();
        }
    }
}








