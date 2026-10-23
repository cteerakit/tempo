using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

internal static partial class Program
{
    private static int CheckPomodoro(Application app, Assembly assembly, object services)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Tempo;component/Widgets/WidgetTemplates.xaml"),
        });
        var context = Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.WidgetContext", true)!,
            ["pomodoro-check", services, (Action<object>)(_ => { })])!;
        var settings = Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.PomodoroSettings", true)!, true)!;
        var type = assembly.GetType("Tempo.App.Widgets.PomodoroWidget", true)!;
        var widget = Activator.CreateInstance(type, [context, settings])!;
        var host = new ContentControl { Content = widget };
        var window = new Window { Content = host, Width = 400, Height = 100, ShowActivated = false };
        try
        {
            type.GetMethod("Start")!.Invoke(widget, null);
            window.Show();
            Pump();
            Descendants<ToggleButton>(host).Single().IsChecked = true;
            Pump();
            var popup = Descendants<Popup>(host).Single();
            if (!popup.IsOpen) throw new Exception("Pomodoro flyout did not open.");
            void Click(string label)
            {
                var button = Descendants<Button>(popup.Child).Single(b => b.Content as string == label);
                if (!button.Command.CanExecute(null)) throw new Exception($"{label} is disabled.");
                button.Command.Execute(null);
                Pump();
            }
            string Read(string name) => (string)type.GetProperty(name)!.GetValue(widget)!;
            if (Read("Countdown") != "25:00") throw new Exception("Initial countdown is incorrect.");
            Click("Start");
            if (Read("ActionLabel") != "Pause") throw new Exception("Start did not update the action.");
            Click("Pause");
            Click("Reset");
            if (Read("Countdown") != "25:00") throw new Exception("Reset failed.");
            Click("Skip");
            if (Read("Phase") != "Short break" || Read("Countdown") != "05:00")
                throw new Exception("Skip did not show the short break.");
            Click("Skip");
            if (Read("Phase") != "Focus") throw new Exception("Skip did not return to focus.");
            Console.WriteLine("PASS: Pomodoro template, flyout, start/pause, reset, and skip.");
            return 0;
        }
        finally
        {
            type.GetProperty("IsFlyoutOpen")!.SetValue(widget, false);
            ((IDisposable)widget).Dispose();
            window.Close();
        }
    }
}
