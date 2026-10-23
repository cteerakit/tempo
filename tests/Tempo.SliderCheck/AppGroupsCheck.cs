using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

internal static partial class Program
{
    private static int CheckAppGroups(Application app, Assembly assembly, object services)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Tempo;component/Widgets/WidgetTemplates.xaml") });
        var context = Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.WidgetContext", true)!, ["group-check", services, (Action<object>)(_ => { })])!;
        var settingsType = assembly.GetType("Tempo.App.Widgets.AppsSettings", true)!;
        var options = (JsonSerializerOptions)assembly.GetType("Tempo.App.Widgets.WidgetJson", true)!.GetField("Options")!.GetValue(null)!;
        var settings = JsonSerializer.Deserialize("{}", settingsType, options)!;
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        Require((bool)settingsType.GetProperty("ShowSeparator")!.GetValue(settings)!, "Separator must default on for existing settings.");
        var appsType = assembly.GetType("Tempo.App.Widgets.AppsWidget", true)!;
        var apps = Activator.CreateInstance(appsType, [context, settings])!;
        var entryType = assembly.GetType("Tempo.App.Widgets.AppEntry", true)!;
        var entries = (IList)appsType.GetProperty("Entries")!.GetValue(apps)!;
        object Entry(string key, bool pinned)
        {
            var entry = Activator.CreateInstance(entryType, [key, apps])!;
            if (pinned)
                entryType.GetProperty("Pin")!.SetValue(entry, Activator.CreateInstance(assembly.GetType("Tempo.App.Shell.PinnedApp", true)!, [key, key, key, null, null, null]));
            entries.Add(entry);
            return entry;
        }
        var a = Entry("pin:a", true);
        var b = Entry("pin:b", true);
        var c = Entry("exe:c", false);
        var d = Entry("exe:d", false);
        void Update() => appsType.GetMethod("UpdateSeparator", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(apps, null);
        void Move(object entry, int index)
        {
            appsType.GetMethod("BeginReorder")!.Invoke(apps, [entry]);
            appsType.GetMethod("MoveDragged")!.Invoke(apps, [index]);
            appsType.GetMethod("EndReorder")!.Invoke(apps, null);
        }
        var host = new ContentControl { Content = apps };
        var window = new Window { Content = host, Width = 350, Height = 90, ShowActivated = false };
        try
        {
            Update();
            window.Show();
            Pump();
            int Separators() => Descendants<Border>(host).Count(x => x.Width == 1 && x.Margin == new Thickness(6, 11, 6, 11) && x.IsVisible);
            Require(Separators() == 1, "Expected one rendered separator.");
            Move(a, 99);
            Require(entries[0] == b && entries[1] == a && entries[2] == c, "Pinned drag crossed groups or failed to reorder.");
            Move(d, -99);
            Require(entries[2] == d && entries[3] == c, "Unpinned drag crossed groups or failed to reorder.");
            Pump();
            Require(Separators() == 1, "Separator lost after reorder.");
            settingsType.GetProperty("ShowSeparator")!.SetValue(settings, false);
            Update();
            Pump();
            Require(Separators() == 0, "Disabled separator still visible.");
            var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, settingsType, options), settingsType, options)!;
            Require(!(bool)settingsType.GetProperty("ShowSeparator")!.GetValue(restored)!, "Separator preference did not persist.");
            settingsType.GetProperty("ShowSeparator")!.SetValue(settings, true);
            entries.Remove(a);
            entries.Remove(b);
            Update();
            Pump();
            Require(Separators() == 0, "Separator visible with only unpinned apps.");
            Console.WriteLine("PASS: group drag boundaries, within-group reorder, rendered separator, toggle, default, persistence, single-group hiding.");
            return 0;
        }
        finally { window.Close(); ((IDisposable)apps).Dispose(); }
    }
}
