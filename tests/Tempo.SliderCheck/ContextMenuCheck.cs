using System.Windows;
using System.Windows.Controls;
using Tempo.App.Controls;

internal static partial class Program
{
    private static int CheckBarContextMenu(Application app, System.Reflection.Assembly assembly, object services)
    {
        var bar = (Window)Activator.CreateInstance(assembly.GetType("Tempo.App.Bar.BarWindow", true)!, [services])!;
        var root = (Border)bar.FindName("Root");
        var menu = new ContextMenu();
        var label = new TextBlock { Text = "App" };
        var button = new Button { Content = label, ContextMenu = menu };
        button.ContextMenuOpening += (_, _) =>
        {
            menu.Items.Clear();
            menu.Items.Add(new MenuItem { Header = "Open" });
        };
        var template = new DataTemplate();
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding());
        template.VisualTree = presenter;
        root.Child = new ContentControl { Content = button, ContentTemplate = template };
        root.Measure(new Size(300, 50));
        root.Arrange(new Rect(0, 0, 300, 50));
        root.UpdateLayout();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var opening = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, [label, true], null)!;
            label.RaiseEvent(opening);
            var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header as string).ToArray();
            Console.WriteLine($"App menu opening {attempt + 1}: {string.Join(", ", headers)}");
            if (headers.Count(header => header == "Open") != 1 ||
                headers.Count(header => header == "Customize Tempo\u2026") != 1 ||
                headers.Count(header => header == "Exit Tempo") != 1)
                return 1;
        }
        bar.Close();
        return 0;
    }

    private static int CheckContextMenuHighlight(Application app)
    {
        var first = new Button { Style = (Style)app.FindResource("WidgetButton"), Content = "First" };
        var second = new Button { Style = first.Style, Content = "Second" };
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Action" });
        var panel = new StackPanel { ContextMenu = menu };
        panel.Children.Add(first);
        panel.Children.Add(second);
        var window = new Window { Content = panel, Width = 240, Height = 140 };
        window.Show();
        window.UpdateLayout();
        var passed = true;
        foreach (var ownMenu in new[] { false, true })
        {
            first.ContextMenu = ownMenu ? menu : null;
            var opening = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, [first, true], null)!;
            first.RaiseEvent(opening);
            menu.PlacementTarget = first;
            menu.IsOpen = true;
            app.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var surface = (Border)first.Template.FindName("Surface", first);
            var open = ContextMenuHighlight.GetIsOpen(first) && !ContextMenuHighlight.GetIsOpen(second)
                && Equals(surface.Background, first.FindResource("Widget.HoverBackground"));
            menu.IsOpen = false;
            WaitForClose();
            var closed = !ContextMenuHighlight.GetIsOpen(first);
            menu.PlacementTarget = second;
            menu.IsOpen = true;
            app.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            closed &= !ContextMenuHighlight.GetIsOpen(first);
            menu.IsOpen = false;
            WaitForClose();
            Console.WriteLine($"{(ownMenu ? "Own" : "Shared")} menu: highlighted={open}, cleared={closed}");
            passed &= open && closed;
        }
        window.Close();
        return passed ? 0 : 1;

        static void WaitForClose()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }
}
