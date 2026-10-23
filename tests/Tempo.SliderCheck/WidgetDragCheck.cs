using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Tempo.Core.Layout;
using Tempo.Core.Settings;

internal static partial class Program
{
    private static int CheckWidgetDrag(Application app, Assembly assembly, object services, object settings)
    {
        var servicesType = services.GetType();
        var settingsType = settings.GetType();
        var current = (TempoSettings)settingsType.GetProperty("Current")!.GetValue(settings)!;
        current.Layout = new LayoutSettings();
        var first = current.Layout.Add(WidgetTypes.Separator, WidgetZone.Left);
        var second = current.Layout.Add(WidgetTypes.Spacer, WidgetZone.Left);
        current.Bar.ReserveSpaceInPill = false;
        current.Bar.Mode = BarMode.Pill;
        current.Appearance.Backdrop = BackdropKind.None;
        assembly.GetType("Tempo.App.Widgets.BuiltInWidgets", true)!.GetMethod("RegisterAll")!.Invoke(null,
            [servicesType.GetProperty("Widgets")!.GetValue(services)]);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Tempo;component/Widgets/WidgetTemplates.xaml")
        });
        var bar = (Window)Activator.CreateInstance(assembly.GetType("Tempo.App.Bar.BarWindow", true)!, [services])!;
        try
        {
            bar.Show();
            Pump();
            var zones = (Grid)bar.FindName("Zones");
            if (Descendants<Border>(zones).Any(b => b.ToolTip as string == "Drag to move widget"))
                throw new Exception("Widget drag grips must not be rendered.");
            if (!Descendants<ContentControl>(zones).Any(c => Descendants<Border>(c).Any()))
                throw new Exception("Widget content templates were lost.");

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var leftHost = (ItemsControl)bar.FindName("LeftZone");
            var dragged = leftHost.Items[0];
            var neighbor = leftHost.ItemContainerGenerator.ContainerFromIndex(1);
            dragged.GetType().GetProperty("IsDragging")!.SetValue(dragged, true);
            bar.GetType().GetField("_draggedWidget", flags)!.SetValue(bar, dragged);
            bar.GetType().GetField("_widgetPressOffset", flags)!.SetValue(bar, 1d);
            var pointer = leftHost.TranslatePoint(new Point(leftHost.ActualWidth - 1, leftHost.ActualHeight / 2), zones);
            bar.GetType().GetMethod("PreviewWidgetDrag", flags)!.Invoke(bar, [pointer]);
            if (!ReferenceEquals(leftHost.Items[1], dragged) || current.Layout.Left[0].InstanceId != first.InstanceId)
                throw new Exception("Live reorder must preview without saving intermediate positions.");
            var draggedContainer = (FrameworkElement)leftHost.ItemContainerGenerator.ContainerFromItem(dragged);
            var visibleX = draggedContainer.TranslatePoint(new Point(), zones).X;
            if (Math.Abs(visibleX - (pointer.X - 1)) > 0.5)
                throw new Exception("Dragged widget does not follow the pointer.");
            if (((FrameworkElement)neighbor).RenderTransform is not System.Windows.Media.TranslateTransform { HasAnimatedProperties: true })
                throw new Exception("Neighbor did not animate into its new slot.");
            bar.GetType().GetMethod("FinishWidgetDrag", flags)!.Invoke(bar, null);
            if (!ReferenceEquals(leftHost.Items[0], dragged))
                throw new Exception("Cancel did not restore the original widget order.");

            void Drop(string id, string hostName, bool atEnd, DragDropEffects expected)
            {
                var host = (ItemsControl)bar.FindName(hostName);
                var point = host.TranslatePoint(new Point(atEnd ? host.ActualWidth - 1 : 1, host.ActualHeight / 2), zones);
                var ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(c => c.GetParameters().Length == 5);
                var e = (DragEventArgs)ctor.Invoke([new DataObject("Tempo.BarWidget", id), DragDropKeyStates.LeftMouseButton,
                    DragDropEffects.Move, zones, point]);
                e.RoutedEvent = DragDrop.DropEvent;
                if (expected == DragDropEffects.Move)
                {
                    var moving = new[] { "LeftZone", "CenterZone", "RightZone" }
                        .SelectMany(name => ((ItemsControl)bar.FindName(name)).Items.Cast<object>())
                        .Single(item => (string)item.GetType().GetProperty("InstanceId")!.GetValue(item)! == id);
                    moving.GetType().GetProperty("IsDragging")!.SetValue(moving, true);
                    bar.GetType().GetField("_draggedWidget", flags)!.SetValue(bar, moving);
                }
                zones.RaiseEvent(e);
                bar.GetType().GetMethod("FinishWidgetDrag", flags)!.Invoke(bar, null);
                Pump();
                if (e.Effects != expected)
                    throw new Exception($"Unexpected drop effect: {e.Effects} instead of {expected}.");
            }

            Drop(first.InstanceId, "LeftZone", true, DragDropEffects.Move);
            if (current.Layout.Left[1].InstanceId != first.InstanceId)
                throw new Exception("Widget did not reorder within its zone.");
            Drop(first.InstanceId, "RightZone", false, DragDropEffects.Move);
            if (current.Layout.Right.Single().InstanceId != first.InstanceId)
                throw new Exception("Widget did not move into an empty zone.");
            settingsType.GetMethod("Update")!.Invoke(settings, [(Action<TempoSettings>)(s => s.Bar.IsLocked = true)]);
            Pump();

            Drop(first.InstanceId, "LeftZone", false, DragDropEffects.None);
            if (current.Layout.Right.Single().InstanceId != first.InstanceId)
                throw new Exception("Locked bar accepted a widget move.");
            settingsType.GetMethod("Update")!.Invoke(settings, [(Action<TempoSettings>)(s => s.Bar.IsLocked = false)]);
            Pump();
            Drop(first.InstanceId, "CenterZone", false, DragDropEffects.Move);
            if (current.Layout.Center.Single().InstanceId != first.InstanceId)
                throw new Exception("Unlocking did not restore widget movement.");
            settingsType.GetMethod("Flush")!.Invoke(settings, null);
            var restored = SettingsSerializer.Deserialize(System.IO.File.ReadAllText((string)settingsType.GetProperty("FilePath")!.GetValue(settings)!));
            if (restored.Layout.Center.Single().InstanceId != first.InstanceId)
                throw new Exception("Widget movement was not persisted.");
            Console.WriteLine("PASS: pointer tracking, animated neighbors, cancellation, widget content without grips, same-zone reorder, empty-zone drop, lock rejection, unlock, persistence.");
            return 0;
        }
        finally { bar.Close(); }
    }
}
