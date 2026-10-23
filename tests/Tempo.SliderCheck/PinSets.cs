using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tempo.Core.Settings;

internal static partial class Program
{
    private static int CheckPinSets(Application app, Assembly assembly, object services, object settings)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Tempo;component/Widgets/WidgetTemplates.xaml") });
        var contextType = assembly.GetType("Tempo.App.Widgets.WidgetContext", true)!;
        var context = Activator.CreateInstance(contextType, ["pin-check", services, (Action<object>)(_ => { })])!;
        var switcher = Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.PinSetSwitcherWidget", true)!, [context])!;
        var appsSettings = Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.AppsSettings", true)!, true)!;
        var apps = Activator.CreateInstance(assembly.GetType("Tempo.App.Widgets.AppsWidget", true)!, [context, appsSettings])!;
        var settingsWindow = (Window)Activator.CreateInstance(assembly.GetType("Tempo.App.Settings.SettingsWindow", true)!, [services])!;
        var management = settingsWindow.DataContext;
        var type = management.GetType();
        var current = (TempoSettings)settings.GetType().GetProperty("Current")!.GetValue(settings)!;
        List<string> List(string name) => (List<string>)apps.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(apps)!;
        void Command(string name) => ((ICommand)type.GetProperty(name + "Command")!.GetValue(management)!).Execute(null);
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        var defaultPins = List("Pins");
        defaultPins.Add("default.exe");
        List("Order").Add("pin:default.exe");
        List("HiddenPins").Add("hidden.lnk");
        var host = new ContentControl { Content = switcher };
        var window = new Window { Content = host, Width = 340, Height = 100, ShowActivated = false };
        try
        {
            settingsWindow.Show();
            Descendants<TabControl>(settingsWindow).Single().SelectedIndex = 1;
            Pump();
            Require(Descendants<TextBox>(settingsWindow).Any(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "TxtPinSetName"), "Pin set settings did not render.");
            window.Show();
            Pump();
            var switchButton = Descendants<Button>(host).Single();
            var initialId = current.PinSets.ActiveId;
            switchButton.Command.Execute(null);
            Pump();
            Require(current.PinSets.ActiveId == initialId, "Cycling a single set changed the active set.");
            Require(!Descendants<Popup>(host).Any(), "Switcher still contains a popup.");
            type.GetProperty("SetName")!.SetValue(management, "Work");
            Command("Create");
            Pump();
            Require(current.PinSets.Sets.Count == 2 && List("Pins").Count == 0, "New set must start empty.");
            var workId = current.PinSets.ActiveId;
            var iconPicker = Descendants<ListBox>(settingsWindow).Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "LstPinSetIcon");
            for (var index = 0; index < iconPicker.Items.Count; index++)
            {
                var item = (ListBoxItem)iconPicker.ItemContainerGenerator.ContainerFromIndex(index);
                var center = item.TranslatePoint(new Point(item.ActualWidth / 2, item.ActualHeight / 2), settingsWindow);
                var hit = settingsWindow.InputHitTest(center) as DependencyObject;
                while (hit is not null && hit != item)
                    hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
                Require(hit == item, $"Icon tile {index} is not reachable by mouse hit testing.");
                var peer = new System.Windows.Automation.Peers.ListBoxItemAutomationPeer(iconPicker.Items[index], new System.Windows.Automation.Peers.ListBoxAutomationPeer(iconPicker));
                ((System.Windows.Automation.Provider.ISelectionItemProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem)).Select();
                Pump();
                var expectedIcon = (string)iconPicker.Items[index].GetType().GetProperty("Value")!.GetValue(iconPicker.Items[index])!;
                Require(current.PinSets.Sets.Last().Icon == expectedIcon && (string)iconPicker.SelectedValue == expectedIcon,
                    $"Icon tile {index} selection did not update the active set.");
            }
            iconPicker.SelectedValue = "\uE821";
            var iconOnly = Descendants<CheckBox>(settingsWindow).Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "ChkPinSetIconOnly");
            iconOnly.IsChecked = true;
            Pump();
            Require(current.PinSets.Sets.Last().Icon == "\uE821" && current.PinSets.Sets.Last().IconOnly, "Icon controls did not update the active set.");
            var toggle = switchButton;
            Require(Descendants<TextBlock>(toggle).Any(t => t.Text == "\uE821" && t.IsVisible), "Selected icon did not render.");
            Require(Descendants<TextBlock>(toggle).Single(t => t.Text == "Work").Visibility == Visibility.Collapsed, "Icon-only mode still shows the name.");
            iconOnly.IsChecked = false;
            Pump();
            Require(Descendants<TextBlock>(toggle).Single(t => t.Text == "Work").IsVisible, "Icon with text did not restore the name.");
            List("Pins").Add("work.exe");
            List("Order").Add("pin:work.exe");
            Require(List("HiddenPins").Count == 0, "Hidden pins leaked between sets.");
            type.GetProperty("SetName")!.SetValue(management, "Office");
            Command("Rename");
            Require(current.PinSets.ActiveId == workId && current.PinSets.Sets.Last().Name == "Office", "Rename changed set identity.");
            type.GetProperty("SetName")!.SetValue(management, "office");
            Command("Create");
            Require(current.PinSets.Sets.Count == 2, "Duplicate set name accepted.");
            type.GetProperty("SelectedSet")!.SetValue(management, current.PinSets.Sets.First());
            Require((string)iconPicker.SelectedValue == "\uE8F1" && iconOnly.IsChecked == false, "Icon settings leaked between sets.");
            Require(List("Pins").SequenceEqual(["default.exe"]) && List("Order").SequenceEqual(["pin:default.exe"]) && List("HiddenPins").SequenceEqual(["hidden.lnk"]), "Default pins/order were lost.");
            switchButton.Command.Execute(null);
            Pump();
            Require(current.PinSets.ActiveId == workId, "Cycling did not advance to the next set.");
            switchButton.Command.Execute(null);
            Pump();
            Require(current.PinSets.ActiveId == initialId, "Cycling did not wrap to the first set.");
            switchButton.Command.Execute(null);
            Pump();
            Require(List("Pins").SequenceEqual(["work.exe"]) && List("Order").SequenceEqual(["pin:work.exe"]), "Work pins/order were lost.");
            var descriptorType = assembly.GetType("Tempo.App.Widgets.WidgetJson", true)!;
            var options = (System.Text.Json.JsonSerializerOptions)descriptorType.GetField("Options")!.GetValue(null)!;
            var json = System.Text.Json.JsonSerializer.Serialize(appsSettings, appsSettings.GetType(), options);
            var restored = System.Text.Json.JsonSerializer.Deserialize(json, appsSettings.GetType(), options)!;
            var restoredApps = Activator.CreateInstance(apps.GetType(), [context, restored])!;
            Require(((List<string>)apps.GetType().GetProperty("Pins", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(restoredApps)!).SequenceEqual(["work.exe"]), "Pins did not survive serialization.");
            ((IDisposable)restoredApps).Dispose();
            var back = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(current));
            Require(back.PinSets.ActiveId == workId && back.PinSets.Sets.Last().Name == "Office", "Catalog did not survive serialization.");
            Require(back.PinSets.Sets.Last().Icon == "\uE821" && !back.PinSets.Sets.Last().IconOnly, "Icon settings did not survive serialization.");
            Console.WriteLine("PASS: settings page editing, click to cycle, wraparound, single-set behavior, create, rename, duplicate validation, isolated pins/order, persistence.");
            return 0;
        }
        finally
        {
            window.Close();
            settingsWindow.Close();
            ((IDisposable)switcher).Dispose();
            ((IDisposable)apps).Dispose();
        }
    }
}
